#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
智能建筑中央空调冷站 BACnet/IP 设备模拟器
模拟37台设备每30秒上报运行数据
支持配置文件驱动的设备性能曲线
"""

import asyncio
import json
import random
import time
import logging
import os
from datetime import datetime, timezone
from typing import Dict, List, Optional
import aiohttp

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger('BACnetSimulator')


class PerformanceCurve:
    def __init__(self, curve_config: Dict):
        self.config = curve_config

    def calculate_cop(self, load_rate: float) -> float:
        coeffs = self.config.get('coefficients', {'a': 0, 'b': 0, 'c': 0})
        a, b, c = coeffs['a'], coeffs['b'], coeffs['c']
        cop = a * load_rate * load_rate + b * load_rate + c

        part_load = self.config.get('part_load_efficiency', {})
        if part_load:
            load_key = str(round(load_rate, 1))
            if load_key in part_load:
                cop *= part_load[load_key]
            else:
                keys = sorted([float(k) for k in part_load.keys()])
                lower = max([k for k in keys if k <= load_rate], default=keys[0])
                upper = min([k for k in keys if k >= load_rate], default=keys[-1])
                if lower != upper:
                    ratio = (load_rate - lower) / (upper - lower)
                    eff_lower = part_load[str(lower)]
                    eff_upper = part_load[str(upper)]
                    cop *= eff_lower + ratio * (eff_upper - eff_lower)

        return max(0.5, min(10.0, cop))

    def get_optimal_load_range(self) -> tuple:
        return tuple(self.config.get('optimal_load_range', [0.4, 0.85]))


class IceStorageTankSimulator:
    """
    冰蓄冷装置仿真器
    实现四种运行模式：蓄冰、融冰、联合供冷、待机
    支持根据时间和电价自动切换运行模式
    """

    def __init__(self, device_config: Dict, curve_config: Dict):
        self.device_config = device_config
        self.curve_config = curve_config

        self.max_ice_capacity = curve_config.get('max_ice_capacity_kwh', 10000)
        self.ice_making_rate = curve_config.get('ice_making_rate_kw', 800)
        self.ice_melting_rate_max = curve_config.get('ice_melting_rate_max_kw', 1500)
        self.ice_making_cop = curve_config.get('ice_making_cop', 3.5)
        self.ice_melting_efficiency = curve_config.get('ice_melting_efficiency', 0.92)
        self.performance_factor = curve_config.get('performance_factor', 1.0)

        self.operation_mode = 'standby'
        self.ice_amount = 0
        self.current_melting_rate = 0
        self.tank_temp = -1.5
        self.brine_concentration = 25.0

    def update(self, current_hour: float, price_tier: str, load_factor: float, collection_interval: int):
        """
        更新冰蓄冷装置状态
        :param current_hour: 当前小时（0-24）
        :param price_tier: 电价等级（valley/flat/peak/critical）
        :param load_factor: 当前系统负荷率
        :param collection_interval: 采集间隔（秒）
        """
        self._determine_operation_mode(current_hour, price_tier)

        hours_passed = collection_interval / 3600

        if self.operation_mode == 'charging':
            self._update_charging(hours_passed)
        elif self.operation_mode == 'discharging':
            self._update_discharging(hours_passed, load_factor)
        elif self.operation_mode == 'combined':
            self._update_combined(hours_passed, load_factor)
        else:
            self._update_standby(hours_passed)

        self._update_temperature_and_concentration()

    def _determine_operation_mode(self, current_hour: float, price_tier: str):
        """
        根据时间和电价确定运行模式
        - 低谷电价（0-6点）：优先蓄冰
        - 尖峰/高峰电价：优先融冰供冷
        - 平段电价：根据冰量和负荷决定
        """
        if 0 <= current_hour < 6 or price_tier == 'valley':
            if self.ice_amount < self.max_ice_capacity * 0.95:
                self.operation_mode = 'charging'
            else:
                self.operation_mode = 'standby'
        elif price_tier in ['peak', 'critical']:
            if self.ice_amount > self.max_ice_capacity * 0.05:
                self.operation_mode = 'discharging'
            else:
                self.operation_mode = 'standby'
        else:
            if self.ice_amount > self.max_ice_capacity * 0.5:
                self.operation_mode = 'combined'
            elif self.ice_amount < self.max_ice_capacity * 0.1 and current_hour < 22:
                self.operation_mode = 'charging'
            else:
                self.operation_mode = 'standby'

    def _update_charging(self, hours_passed: float):
        """蓄冰模式：冰量线性增加"""
        ice_added = self.ice_making_rate * hours_passed * self.performance_factor
        self.ice_amount = min(self.max_ice_capacity, self.ice_amount + ice_added)
        self.current_melting_rate = 0

    def _update_discharging(self, hours_passed: float, load_factor: float):
        """融冰模式：冰量线性减少"""
        melt_rate = min(
            self.ice_melting_rate_max * load_factor,
            self.ice_amount / max(hours_passed, 0.001)
        )
        ice_melted = melt_rate * hours_passed * self.ice_melting_efficiency
        self.ice_amount = max(0, self.ice_amount - ice_melted)
        self.current_melting_rate = melt_rate

    def _update_combined(self, hours_passed: float, load_factor: float):
        """联合供冷模式：同时蓄冰和融冰（或根据需求）"""
        if load_factor > 0.8:
            melt_rate = self.ice_melting_rate_max * 0.5 * load_factor
            ice_melted = melt_rate * hours_passed * self.ice_melting_efficiency
            self.ice_amount = max(0, self.ice_amount - ice_melted)
            self.current_melting_rate = melt_rate
        else:
            self._update_standby(hours_passed)

    def _update_standby(self, hours_passed: float):
        """待机模式：冰量自然损耗"""
        natural_loss = self.ice_amount * 0.001 * hours_passed
        self.ice_amount = max(0, self.ice_amount - natural_loss)
        self.current_melting_rate = 0

    def _update_temperature_and_concentration(self):
        """更新温度和浓度参数"""
        base_temp = -1.5
        if self.operation_mode == 'charging':
            base_temp = -5.0 + (self.ice_amount / self.max_ice_capacity) * 3.5
        elif self.operation_mode in ['discharging', 'combined']:
            base_temp = -1.0 + (1 - self.ice_amount / self.max_ice_capacity) * 2.0

        self.tank_temp = base_temp + random.uniform(-0.3, 0.3)
        self.brine_concentration = 25.0 + random.uniform(-0.5, 0.5)

    def get_power_consumption(self) -> float:
        """获取当前功耗"""
        if self.operation_mode == 'charging':
            return self.ice_making_rate / self.ice_making_cop * self.performance_factor
        elif self.operation_mode in ['discharging', 'combined']:
            return self.device_config.get('rated_power_kw', 200) * 0.3
        else:
            return self.device_config.get('rated_power_kw', 200) * 0.05

    def get_cooling_output(self) -> float:
        """获取当前供冷量"""
        if self.operation_mode == 'discharging':
            return self.current_melting_rate * self.ice_melting_efficiency
        elif self.operation_mode == 'combined':
            return self.current_melting_rate * self.ice_melting_efficiency
        else:
            return 0

    def get_status_values(self) -> Dict:
        """获取当前状态值字典"""
        return {
            'tank_temp': self.tank_temp,
            'brine_concentration': self.brine_concentration,
            'ice_amount': self.ice_amount,
            'melting_rate': self.current_melting_rate,
            'operation_mode': self.operation_mode,
            'state_of_charge': self.ice_amount / self.max_ice_capacity if self.max_ice_capacity > 0 else 0
        }


class BACnetDeviceSimulator:
    def __init__(self, config_path: str = 'config.json', api_base_url: str = None):
        self.config = self._load_config(config_path)
        self.api_base_url = api_base_url or self.config.get('api_url', 'http://localhost:5000')
        self.collection_interval = self.config.get('collection_interval_seconds', 30)
        self.log_level = self.config.get('log_level', 'INFO')

        self.devices: List[Dict] = []
        self.running = False
        self.performance_curves: Dict[str, PerformanceCurve] = {}
        self.ice_storage_simulators: Dict[str, IceStorageTankSimulator] = {}
        self.fault_modes_config: Dict = {}

        self._init_performance_curves()
        self._init_fault_modes()
        self._init_devices()

    def _load_config(self, config_path: str) -> Dict:
        try:
            with open(config_path, 'r', encoding='utf-8') as f:
                config = json.load(f)
            logger.info(f"配置文件已加载: {config_path}")
            return config
        except Exception as e:
            logger.warning(f"无法加载配置文件 {config_path}: {e}，使用默认配置")
            return self._get_default_config()

    def _get_default_config(self) -> Dict:
        return {
            "api_url": "http://localhost:5000",
            "collection_interval_seconds": 30,
            "log_level": "INFO",
            "device_performance_curves": {},
            "devices": {
                "centrifugal_chillers": {"count": 3, "prefix": "CEN-CH", "rated_power_kw": 800, "design_cop": 5.8},
                "screw_chillers": {"count": 2, "prefix": "SCR-CH", "rated_power_kw": 500, "design_cop": 5.2},
                "cooling_towers": {"count": 8, "prefix": "CT", "rated_power_kw": 75, "design_cop": 35.0},
                "chilled_water_pumps": {"count": 12, "prefix": "CHP", "rated_power_kw": 90, "design_cop": 20.0},
                "cooling_water_pumps": {"count": 12, "prefix": "CWP", "rated_power_kw": 75, "design_cop": 25.0}
            }
        }

    def _init_performance_curves(self):
        curves = self.config.get('device_performance_curves', {})
        for device_type, curve_config in curves.items():
            self.performance_curves[device_type] = PerformanceCurve(curve_config)
            logger.info(f"已加载性能曲线: {device_type}")

    def _init_fault_modes(self):
        """初始化故障模式配置"""
        fault_config = self.config.get('fault_simulation', {})
        self.fault_modes_config = fault_config.get('fault_modes', {})
        logger.info(f"已加载 {len(self.fault_modes_config)} 种设备类型的故障模式配置")

    def _get_electricity_price_tier(self, current_hour: int) -> str:
        """
        根据当前小时返回电价等级
        - valley: 低谷电价 (0-6点)
        - flat: 平段电价 (6-10点, 12-14点, 18-22点)
        - peak: 高峰电价 (10-12点, 14-18点)
        - critical: 尖峰电价 (22-24点)
        """
        if 0 <= current_hour < 6:
            return 'valley'
        elif (6 <= current_hour < 10) or (12 <= current_hour < 14) or (18 <= current_hour < 22):
            return 'flat'
        elif (10 <= current_hour < 12) or (14 <= current_hour < 18):
            return 'peak'
        else:
            return 'critical'

    def _get_device_type(self, device: Dict) -> str:
        """获取设备的简化类型名称，用于匹配故障模式"""
        curve_type = device.get('curve_type', '')
        if curve_type:
            return curve_type

        device_type = device.get('device_type', '')
        type_mapping = {
            'centrifugal_chillers': 'centrifugal_chiller',
            'screw_chillers': 'screw_chiller',
            'cooling_towers': 'cooling_tower',
            'chilled_water_pumps': 'chilled_water_pump',
            'cooling_water_pumps': 'cooling_water_pump',
            'ice_storage_tanks': 'ice_storage_tank'
        }
        return type_mapping.get(device_type, device_type)

    def _get_current_load_factor(self) -> float:
        scenarios = self.config.get('scenarios', {})
        if 'normal' not in scenarios:
            return 0.7

        load_profile = scenarios['normal'].get('load_profile', [])
        if not load_profile:
            return 0.7

        current_hour = datetime.now().hour + datetime.now().minute / 60

        load_profile.sort(key=lambda x: x['hour'])

        for i in range(len(load_profile)):
            if load_profile[i]['hour'] >= current_hour:
                if i == 0:
                    return load_profile[0]['load']
                prev = load_profile[i - 1]
                curr = load_profile[i]
                ratio = (current_hour - prev['hour']) / (curr['hour'] - prev['hour'])
                return prev['load'] + ratio * (curr['load'] - prev['load'])

        return load_profile[-1]['load']

    def _init_devices(self):
        device_id = 1
        devices_config = self.config.get('devices', {})
        fault_config = self.config.get('fault_simulation', {})

        device_type_map = {
            'centrifugal_chillers': ('centrifugal_chiller', 1),
            'screw_chillers': ('screw_chiller', 2),
            'cooling_towers': ('cooling_tower', 3),
            'chilled_water_pumps': ('chilled_water_pump', 4),
            'cooling_water_pumps': ('cooling_water_pump', 5),
            'ice_storage_tanks': ('ice_storage_tank', 6)
        }

        for config_key, device_config in devices_config.items():
            curve_type, type_id = device_type_map.get(config_key, (config_key, 0))
            count = device_config.get('count', 0)
            prefix = device_config.get('prefix', 'DEV')
            rated_power = device_config.get('rated_power_kw', 100)
            design_cop = device_config.get('design_cop', 5.0)
            base_values = device_config.get('base_values', {})
            position_layout = device_config.get('position_layout', {
                'base_x': 400, 'base_y': 400, 'x_spacing': 80, 'y_spacing': 100, 'per_row': 4
            })
            perf_factor = device_config.get('performance_factor', 1.0)

            for i in range(1, count + 1):
                device_id_str = f"{prefix}-{i:03d}"
                initial_status = 1 if i <= count * 0.7 else 0

                x_offset = (i - 1) % position_layout['per_row'] * position_layout['x_spacing']
                y_offset = (i - 1) // position_layout['per_row'] * position_layout['y_spacing']

                device = {
                    'id': device_id_str,
                    'name': device_id_str,
                    'device_type': config_key,
                    'curve_type': curve_type,
                    'device_type_id': type_id,
                    'design_cop': design_cop,
                    'rated_power': rated_power,
                    'capacity_kw': device_config.get('capacity_kw', rated_power * design_cop),
                    'performance_factor': perf_factor,
                    'status': initial_status,
                    'efficiency_status': 1,
                    'position_x': position_layout['base_x'] + x_offset,
                    'position_y': position_layout['base_y'] + y_offset,
                    'operating_hours': random.uniform(0, 5000),
                    'base_values': base_values,
                    'last_values': {},
                    'fault_probability': fault_config.get('fault_probability', 0.001),
                    'low_efficiency_probability': fault_config.get('low_efficiency_probability', 0.005),
                    'recovery_probability': fault_config.get('recovery_probability', 0.05),
                    'current_cop': design_cop * 0.8,
                    'active_faults': [],
                    'fault_parameters': {}
                }

                device_simple_type = self._get_device_type(device)
                if device_simple_type in self.fault_modes_config:
                    device['available_fault_modes'] = self.fault_modes_config[device_simple_type]
                else:
                    device['available_fault_modes'] = {}

                if config_key == 'ice_storage_tanks':
                    ice_curve = self.performance_curves.get('ice_storage_tank', {})
                    if hasattr(ice_curve, 'config'):
                        ice_curve_config = ice_curve.config
                    else:
                        ice_curve_config = {}
                    self.ice_storage_simulators[device_id_str] = IceStorageTankSimulator(
                        device_config, ice_curve_config
                    )

                for param, base_val in base_values.items():
                    device['last_values'][param] = base_val * (0.9 + random.random() * 0.2)

                if 'power' not in device['last_values']:
                    device['last_values']['power'] = rated_power * 0.5

                self.devices.append(device)
                device_id += 1

        total_count = len(self.devices)
        logger.info(f"已初始化 {total_count} 台模拟设备")
        self._print_device_summary()

    def _print_device_summary(self):
        summary = {}
        for device in self.devices:
            dtype = device['device_type']
            summary[dtype] = summary.get(dtype, 0) + 1
        for dtype, count in summary.items():
            logger.info(f"  {dtype}: {count}台")

    def _calculate_device_cop(self, device: Dict, load_factor: float) -> float:
        curve_type = device.get('curve_type')
        if curve_type in self.performance_curves:
            curve = self.performance_curves[curve_type]
            base_cop = curve.calculate_cop(load_factor)
            perf_factor = device.get('performance_factor', 1.0)
            return base_cop * perf_factor
        return device['design_cop'] * (0.8 + 0.4 * load_factor)

    def _check_and_trigger_fault_modes(self, device: Dict):
        """
        检查并触发故障模式
        根据概率触发特定故障模式，应用参数偏移
        """
        if device['status'] != 1:
            return

        available_faults = device.get('available_fault_modes', {})
        if not available_faults:
            return

        for fault_name, fault_config in available_faults.items():
            if fault_name in device['active_faults']:
                continue

            probability = fault_config.get('probability', 0.001)
            if random.random() < probability:
                device['active_faults'].append(fault_name)
                device['efficiency_status'] = 4

                parameters = fault_config.get('parameters', {})
                device['fault_parameters'][fault_name] = {}

                for param_name, param_config in parameters.items():
                    variance = param_config.get('variance', 0)
                    base_offset = param_config.get('increase', 0) - param_config.get('decrease', 0)
                    actual_offset = base_offset + random.uniform(-variance, variance)
                    device['fault_parameters'][fault_name][param_name] = actual_offset

                symptoms = fault_config.get('symptoms', [])
                logger.warning(
                    f"设备 {device['id']} 触发故障模式: {fault_name}, "
                    f"症状: {', '.join(symptoms)}"
                )

    def _apply_fault_parameters(self, device: Dict, param_name: str, current_value: float) -> float:
        """
        应用故障参数偏移
        :param device: 设备字典
        :param param_name: 参数名称
        :param current_value: 当前参数值
        :return: 应用故障偏移后的参数值
        """
        if not device.get('active_faults'):
            return current_value

        modified_value = current_value
        for fault_name in device['active_faults']:
            fault_params = device.get('fault_parameters', {}).get(fault_name, {})
            if param_name in fault_params:
                offset = fault_params[param_name]
                if 'temp' in param_name or 'vibration' in param_name:
                    modified_value += offset
                else:
                    modified_value *= (1 + offset)

        return modified_value

    def _check_fault_recovery(self, device: Dict):
        """
        检查故障恢复
        """
        if not device.get('active_faults'):
            return

        if random.random() < device['recovery_probability']:
            recovered_faults = []
            for fault_name in device['active_faults']:
                if random.random() < 0.5:
                    recovered_faults.append(fault_name)

            for fault_name in recovered_faults:
                device['active_faults'].remove(fault_name)
                if fault_name in device['fault_parameters']:
                    del device['fault_parameters'][fault_name]
                logger.info(f"设备 {device['id']} 故障模式恢复: {fault_name}")

            if not device['active_faults']:
                device['status'] = 0
                device['efficiency_status'] = 1
                logger.info(f"设备 {device['id']} 完全恢复，转入待机状态")

    def _update_ice_storage_tank(self, device: Dict):
        """
        更新冰蓄冷设备状态
        """
        device_id = device['id']
        if device_id not in self.ice_storage_simulators:
            return

        simulator = self.ice_storage_simulators[device_id]
        current_hour = datetime.now().hour + datetime.now().minute / 60
        hour_int = datetime.now().hour
        price_tier = self._get_electricity_price_tier(hour_int)
        load_factor = self._get_current_load_factor()

        simulator.update(current_hour, price_tier, load_factor, self.collection_interval)

        status_values = simulator.get_status_values()
        device['last_values']['tank_temp'] = status_values['tank_temp']
        device['last_values']['brine_concentration'] = status_values['brine_concentration']
        device['last_values']['ice_amount'] = status_values['ice_amount']
        device['last_values']['melting_rate'] = status_values['melting_rate']
        device['last_values']['state_of_charge'] = status_values['state_of_charge']
        device['operation_mode'] = status_values['operation_mode']

        device['last_values']['power'] = simulator.get_power_consumption()
        device['current_cop'] = simulator.ice_making_cop if simulator.operation_mode == 'charging' else 0

        mode_status_map = {
            'charging': 1,
            'discharging': 1,
            'combined': 1,
            'standby': 0
        }
        device['status'] = mode_status_map.get(simulator.operation_mode, 0)

        device['operating_hours'] += self.collection_interval / 3600

    def _update_device_values(self, device: Dict):
        if device['device_type'] == 'ice_storage_tanks':
            self._update_ice_storage_tank(device)
            return

        if device['status'] == 2 or device.get('active_faults'):
            self._check_fault_recovery(device)
            if device['status'] == 2 and not device.get('active_faults'):
                device['status'] = 0
                device['efficiency_status'] = 1
                logger.info(f"设备 {device['id']} 故障恢复")
                return
            if device.get('active_faults'):
                pass
            else:
                return

        if device['status'] == 0 and random.random() < 0.1:
            device['status'] = 1
            logger.info(f"设备 {device['id']} 启动")
        elif device['status'] == 1 and random.random() < 0.02:
            device['status'] = 0
            logger.info(f"设备 {device['id']} 停机")

        self._check_and_trigger_fault_modes(device)

        if not device.get('active_faults'):
            if random.random() < device['fault_probability'] and device['status'] == 1:
                device['status'] = 2
                device['efficiency_status'] = 4
                logger.warning(f"设备 {device['id']} 模拟故障触发")
                return

            if random.random() < device['low_efficiency_probability'] and device['status'] == 1:
                device['efficiency_status'] = 3
                logger.info(f"设备 {device['id']} 进入低效状态")
            elif device['efficiency_status'] == 3 and random.random() < 0.3:
                device['efficiency_status'] = 1
                logger.info(f"设备 {device['id']} 恢复高效状态")

        if device['status'] == 1:
            base_load = self._get_current_load_factor()
            load_factor = max(0.2, min(1.0, base_load * (0.95 + random.random() * 0.1)))

            device['current_cop'] = self._calculate_device_cop(device, load_factor)

            for param, base_val in device['base_values'].items():
                drift = random.uniform(-0.03, 0.03)
                last_val = device['last_values'].get(param, base_val)

                if device['efficiency_status'] == 3:
                    drift += random.uniform(-0.05, 0.1)
                    if 'temp' in param and 'supply' in param:
                        drift += 0.02

                target_val = base_val * load_factor
                new_val = last_val + (target_val - last_val) * 0.3 + base_val * drift
                min_val = base_val * 0.5
                max_val = base_val * 1.5

                if device['efficiency_status'] == 4 and not device.get('active_faults'):
                    new_val = new_val * (0.3 + random.random() * 0.4)

                new_val = max(min_val, min(max_val, new_val))

                if device.get('active_faults'):
                    new_val = self._apply_fault_parameters(device, param, new_val)

                device['last_values'][param] = new_val

            if 'power' not in device['base_values']:
                power_factor = load_factor * (0.9 + random.random() * 0.2)
                if device['efficiency_status'] == 3:
                    power_factor *= 1.15
                elif device['efficiency_status'] == 4 and not device.get('active_faults'):
                    power_factor *= 0.5
                power = device['rated_power'] * power_factor

                if device.get('active_faults'):
                    power = self._apply_fault_parameters(device, 'power', power)

                device['last_values']['power'] = power

            if device.get('active_faults'):
                device['current_cop'] = self._apply_fault_parameters(
                    device, 'cop', device['current_cop']
                )

            if device['efficiency_status'] == 1 and random.random() < 0.7:
                device['efficiency_status'] = 2

            device['operating_hours'] += self.collection_interval / 3600
        else:
            for param in device['last_values']:
                base_val = device['base_values'].get(param, 0)
                device['last_values'][param] = base_val * (0.01 + random.random() * 0.02)
            device['last_values']['power'] = device['rated_power'] * (0.01 + random.random() * 0.02)
            device['efficiency_status'] = 1
            device['current_cop'] = 0

    def _generate_device_data(self, device: Dict, timestamp: datetime) -> Dict:
        values = device['last_values']

        base_data = {
            'deviceId': device['id'],
            'timestamp': timestamp.isoformat(),
            'power': round(values.get('power', 0), 2),
            'supplyTemperature': round(values.get('supply_temp', values.get('outlet_temp', 0)), 2),
            'returnTemperature': round(values.get('return_temp', values.get('inlet_temp', 0)), 2),
            'pressure': round(values.get('pressure', 0), 3),
            'flowRate': round(values.get('flow_rate', 0), 2),
            'cop': round(device.get('current_cop', 0), 2),
            'loadRate': round(self._get_current_load_factor(), 3)
        }

        device_type = device['device_type']

        if device_type in ['centrifugal_chillers', 'screw_chillers', 'chilled_water_pumps', 'cooling_water_pumps']:
            base_data.update({
                'frequency': round(values.get('frequency', 0), 1),
                'current': round(values.get('current', 0), 1),
                'voltage': round(values.get('voltage', 0), 0)
            })

        if device_type == 'cooling_towers':
            base_data.update({
                'inletTemperature': round(values.get('inlet_temp', 0), 2),
                'outletTemperature': round(values.get('outlet_temp', 0), 2),
                'fanSpeed': round(values.get('fan_speed', 0), 1)
            })

        if device_type == 'ice_storage_tanks':
            base_data.update({
                'tankTemperature': round(values.get('tank_temp', 0), 2),
                'brineConcentration': round(values.get('brine_concentration', 0), 2),
                'iceAmount': round(values.get('ice_amount', 0), 2),
                'meltingRate': round(values.get('melting_rate', 0), 2),
                'stateOfCharge': round(values.get('state_of_charge', 0), 4),
                'operationMode': device.get('operation_mode', 'standby'),
                'current': round(values.get('current_draw', values.get('current', 0)), 1),
                'voltage': round(values.get('voltage', 0), 0)
            })

        if device.get('active_faults'):
            base_data['activeFaults'] = device['active_faults']

        return base_data

    async def _send_device_data(self, session: aiohttp.ClientSession, device: Dict, data: Dict):
        try:
            url = f"{self.api_base_url}/api/device/{device['id']}/data"
            async with session.post(url, json=data, timeout=10) as response:
                if response.status == 201:
                    logger.debug(f"设备 {device['id']} 数据上报成功")
                    return True
                else:
                    logger.warning(f"设备 {device['id']} 上报失败: {response.status}")
                    return False
        except Exception as e:
            logger.error(f"设备 {device['id']} 上报异常: {e}")
            return False

    async def _update_device_status(self, session: aiohttp.ClientSession, device: Dict):
        try:
            url = f"{self.api_base_url}/api/device/{device['id']}/status/{device['status']}"
            async with session.put(url, timeout=10) as response:
                return response.status == 204
        except Exception as e:
            logger.error(f"更新设备 {device['id']} 状态失败: {e}")
            return False

    async def _collect_and_send(self, session: aiohttp.ClientSession):
        timestamp = datetime.now(timezone.utc)
        logger.info(f"开始采集周期 - {timestamp.isoformat()}")

        tasks = []
        running_count = 0
        fault_count = 0

        for device in self.devices:
            self._update_device_values(device)

            if device['status'] == 1:
                running_count += 1
            elif device['status'] == 2:
                fault_count += 1

            data = self._generate_device_data(device, timestamp)

            task = self._send_device_data(session, device, data)
            tasks.append(task)

            if device['status'] != 1:
                status_task = self._update_device_status(session, device)
                tasks.append(status_task)

        results = await asyncio.gather(*tasks, return_exceptions=True)
        success_count = sum(1 for r in results if r is True)

        avg_cop = 0
        running_devices = [d for d in self.devices if d['status'] == 1]
        if running_devices:
            avg_cop = sum(d.get('current_cop', 0) for d in running_devices) / len(running_devices)

        logger.info(
            f"采集周期完成 - 运行: {running_count}, "
            f"故障: {fault_count}, 平均COP: {avg_cop:.2f}, "
            f"上报成功: {success_count}/{len(tasks)}"
        )

        return {
            'timestamp': timestamp.isoformat(),
            'running': running_count,
            'fault': fault_count,
            'avg_cop': avg_cop,
            'success': success_count,
            'total': len(tasks)
        }

    async def _register_devices(self, session: aiohttp.ClientSession):
        logger.info("检查设备注册状态...")
        try:
            url = f"{self.api_base_url}/api/device"
            async with session.get(url, timeout=10) as response:
                if response.status == 200:
                    existing_devices = await response.json()
                    existing_ids = {d['id'] for d in existing_devices}
                    logger.info(f"后端已有 {len(existing_ids)} 台设备")
                    return existing_ids
        except Exception as e:
            logger.warning(f"获取设备列表失败: {e}")
        return set()

    async def _check_api_connection(self, session: aiohttp.ClientSession) -> bool:
        try:
            url = f"{self.api_base_url}/health"
            async with session.get(url, timeout=5) as response:
                return response.status == 200
        except:
            return False

    async def start(self, collection_interval: int = None):
        interval = collection_interval or self.collection_interval
        self.running = True
        logger.info(f"BACnet模拟器启动，采集间隔: {interval}秒")
        logger.info(f"后端API地址: {self.api_base_url}")
        logger.info(f"性能曲线数量: {len(self.performance_curves)}")

        async with aiohttp.ClientSession() as session:
            logger.info("等待后端API就绪...")
            for i in range(120):
                if await self._check_api_connection(session):
                    logger.info("后端API连接成功")
                    break
                if i % 10 == 9:
                    logger.info(f"等待后端API... ({i + 1}/120)")
                await asyncio.sleep(1)
            else:
                logger.warning("后端API连接超时，将继续尝试发送数据")

            await self._register_devices(session)

            cycle_count = 0
            while self.running:
                try:
                    await self._collect_and_send(session)
                    cycle_count += 1

                    if cycle_count % 10 == 0:
                        await self._register_devices(session)

                except Exception as e:
                    logger.error(f"采集周期异常: {e}")

                await asyncio.sleep(interval)

    def stop(self):
        self.running = False
        logger.info("BACnet模拟器停止中...")


async def main():
    import argparse

    parser = argparse.ArgumentParser(description='BACnet/IP 设备模拟器')
    parser.add_argument(
        '--config',
        type=str,
        default=os.environ.get('SIMULATOR_CONFIG', 'config.json'),
        help='配置文件路径 (默认: config.json)'
    )
    parser.add_argument(
        '--api-url',
        type=str,
        default=os.environ.get('API_URL', None),
        help='后端API地址 (默认从配置文件读取)'
    )
    parser.add_argument(
        '--interval',
        type=int,
        default=None,
        help='数据采集间隔秒数 (默认从配置文件读取)'
    )
    parser.add_argument(
        '--log-level',
        type=str,
        default=None,
        choices=['DEBUG', 'INFO', 'WARNING', 'ERROR'],
        help='日志级别 (默认从配置文件读取)'
    )

    args = parser.parse_args()

    simulator = BACnetDeviceSimulator(config_path=args.config, api_base_url=args.api_url)

    if args.log_level:
        logging.getLogger().setLevel(getattr(logging, args.log_level))

    try:
        await simulator.start(collection_interval=args.interval)
    except KeyboardInterrupt:
        logger.info("收到停止信号")
        simulator.stop()
    except Exception as e:
        logger.error(f"模拟器异常退出: {e}")
        raise


if __name__ == '__main__':
    print("""
╔══════════════════════════════════════════════════════════════╗
║          智能建筑中央空调冷站 BACnet/IP 模拟器              ║
╠══════════════════════════════════════════════════════════════╣
║  设备配置:                                                  ║
║    - 离心式冷水机组: 3台                                    ║
║    - 螺杆式冷水机组: 2台                                    ║
║    - 冷却塔: 8台                                            ║
║    - 冷冻水泵: 12台                                         ║
║    - 冷却水泵: 12台                                         ║
║    - 冰蓄冷装置: 2台                                        ║
║  总计: 39台设备                                             ║
║                                                             ║
║  特性:                                                      ║
║    - 可配置设备性能曲线                                     ║
║    - 负荷率动态曲线                                         ║
║    - 详细故障模式模拟                                       ║
║    - 冰蓄冷移峰填谷优化                                     ║
║    - 电价时段智能调度                                       ║
║    - 30秒采集间隔 (可配置)                                  ║
╚══════════════════════════════════════════════════════════════╝
    """)
    asyncio.run(main())
