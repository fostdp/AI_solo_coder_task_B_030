import React from 'react';
import type { Device } from '@/types';
import { DeviceStatus } from '@/types';

interface DeviceSelectorProps {
  devices: Device[];
  selectedDevice: Device | null;
  onDeviceSelect: (device: Device) => void;
}

const getStatusColor = (status: number) => {
  switch (status) {
    case DeviceStatus.Running:
      return 'bg-green-500';
    case DeviceStatus.Standby:
      return 'bg-blue-500';
    case DeviceStatus.Fault:
      return 'bg-red-500';
    case DeviceStatus.Maintenance:
      return 'bg-yellow-500';
    default:
      return 'bg-slate-500';
  }
};

const getStatusText = (status: number) => {
  return ['待机', '运行', '故障', '维护'][status] || '未知';
};

export const DeviceSelector: React.FC<DeviceSelectorProps> = ({
  devices,
  selectedDevice,
  onDeviceSelect,
}) => {
  return (
    <div className="w-64 flex-shrink-0 border-r border-slate-700 pr-4">
      <div className="text-slate-400 text-xs mb-2 font-medium">设备列表</div>
      <div className="space-y-2 overflow-y-auto max-h-full">
        {devices.map((device) => (
          <div
            key={device.id}
            onClick={() => onDeviceSelect(device)}
            className={`p-3 rounded-lg cursor-pointer transition-all ${
              selectedDevice?.id === device.id
                ? 'bg-blue-500/20 border border-blue-500/50'
                : 'bg-slate-700/30 border border-transparent hover:bg-slate-700/50'
            }`}
          >
            <div className="flex items-center gap-2 mb-1">
              <span className={`w-2 h-2 rounded-full ${getStatusColor(device.status)}`} />
              <span className="text-white text-sm font-medium truncate">{device.name}</span>
            </div>
            <div className="text-xs text-slate-400">{device.deviceTypeName}</div>
          </div>
        ))}
      </div>
    </div>
  );
};
