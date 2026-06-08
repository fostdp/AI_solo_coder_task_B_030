import React, { useState, useEffect, useMemo } from 'react';
import ReactECharts from 'echarts-for-react';
import type { IceStorageSchedule, ElectricityPriceTier } from '@/types';
import { iceStorageApi } from '@/services/api';
import { IceStorageMode } from '@/types';

interface IceStorageGanttChartProps {
  date?: string;
}

const modeColors: Record<number, string> = {
  [IceStorageMode.IceMaking]: '#22C55E',
  [IceStorageMode.IceMelting]: '#DC2626',
  [IceStorageMode.ChillerOnly]: '#3B82F6',
  [IceStorageMode.Combined]: '#F97316',
  [IceStorageMode.Standby]: '#64748B',
};

const modeNames: Record<number, string> = {
  [IceStorageMode.IceMaking]: '蓄冰',
  [IceStorageMode.IceMelting]: '融冰',
  [IceStorageMode.ChillerOnly]: '主机供冷',
  [IceStorageMode.Combined]: '联合供冷',
  [IceStorageMode.Standby]: '待机',
};

export const IceStorageGanttChart: React.FC<IceStorageGanttChartProps> = ({ date }) => {
  const [schedules, setSchedules] = useState<IceStorageSchedule[]>([]);
  const [priceTiers, setPriceTiers] = useState<ElectricityPriceTier[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedHour, setSelectedHour] = useState<number | null>(null);

  const currentDate = useMemo(() => {
    return date || new Date().toISOString().split('T')[0];
  }, [date]);

  useEffect(() => {
    const fetchData = async () => {
      try {
        setLoading(true);
        const [scheduleRes, priceRes] = await Promise.all([
          iceStorageApi.getSchedule(currentDate),
          iceStorageApi.getElectricityPrices(),
        ]);
        setSchedules(scheduleRes.data);
        setPriceTiers(priceRes.data);
      } catch (error) {
        console.error('获取蓄冰计划数据失败:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchData();
  }, [currentDate]);

  const getPriceTierByHour = (hour: number): ElectricityPriceTier | undefined => {
    return priceTiers.find((tier) => {
      if (tier.startHour <= tier.endHour) {
        return hour >= tier.startHour && hour < tier.endHour;
      }
      return hour >= tier.startHour || hour < tier.endHour;
    });
  };

  const stats = useMemo(() => {
    const totalExpectedCost = schedules.reduce((sum, s) => sum + s.expectedCost, 0);
    const totalBaselineCost = schedules.reduce((sum, s) => sum + s.baselineCost, 0);
    const totalSaving = schedules.reduce((sum, s) => sum + s.costSaving, 0);
    const iceMakingHours = schedules.filter((s) => s.mode === IceStorageMode.IceMaking).length;
    const iceMeltingHours = schedules.filter((s) => s.mode === IceStorageMode.IceMelting || s.mode === IceStorageMode.Combined).length;

    return {
      totalExpectedCost,
      totalBaselineCost,
      totalSaving,
      savingPercent: totalBaselineCost > 0 ? ((totalBaselineCost - totalExpectedCost) / totalBaselineCost) * 100 : 0,
      iceMakingHours,
      iceMeltingHours,
    };
  }, [schedules]);

  const getChartOption = () => {
    const hours = Array.from({ length: 24 }, (_, i) => i);
    const maxIceAmount = Math.max(...schedules.map((s) => s.targetIceAmount), 100);

    const priceBgColors = hours.map((hour) => {
      const tier = getPriceTierByHour(hour);
      return tier?.color || '#1E293B';
    });

    const modeData = hours.map((hour) => {
      const schedule = schedules.find((s) => s.hourOfDay === hour);
      return schedule ? [hour, schedule.mode] : [hour, IceStorageMode.Standby];
    });

    const iceAmountData = hours.map((hour) => {
      const schedule = schedules.find((s) => s.hourOfDay === hour);
      return [hour, schedule?.targetIceAmount || 0];
    });

    const chillerLoadData = hours.map((hour) => {
      const schedule = schedules.find((s) => s.hourOfDay === hour);
      return [hour, schedule ? schedule.chillerLoadRatio * 100 : 0];
    });

    const iceLoadData = hours.map((hour) => {
      const schedule = schedules.find((s) => s.hourOfDay === hour);
      return [hour, schedule ? schedule.iceLoadRatio * 100 : 0];
    });

    const meltingRateData = hours.map((hour) => {
      const schedule = schedules.find((s) => s.hourOfDay === hour);
      return [hour, schedule?.targetMeltingRate || 0];
    });

    return {
      backgroundColor: 'transparent',
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        backgroundColor: 'rgba(15, 23, 42, 0.95)',
        borderColor: '#334155',
        textStyle: { color: '#F1F5F9' },
        formatter: (params: unknown) => {
          const p = params as Array<{ axisValue: number; seriesName: string; value: number[] }>;
          if (!p.length) return '';
          const hour = p[0].axisValue;
          const schedule = schedules.find((s) => s.hourOfDay === hour);
          const tier = getPriceTierByHour(hour);
          let html = `<div class="font-semibold mb-2">${hour}:00 - ${hour + 1}:00</div>`;
          if (tier) {
            html += `<div class="text-xs mb-1">电价时段: <span style="color:${tier.color}">${tier.tierName}</span> (${tier.pricePerKWh.toFixed(2)}元/kWh)</div>`;
          }
          if (schedule) {
            html += `<div class="text-xs mb-1">运行模式: <span style="color:${modeColors[schedule.mode]}">${modeNames[schedule.mode]}</span></div>`;
            html += `<div class="text-xs mb-1">蓄冰量: ${schedule.targetIceAmount.toFixed(1)} kWh</div>`;
            html += `<div class="text-xs mb-1">主机负荷: ${(schedule.chillerLoadRatio * 100).toFixed(0)}%</div>`;
            html += `<div class="text-xs mb-1">融冰负荷: ${(schedule.iceLoadRatio * 100).toFixed(0)}%</div>`;
            html += `<div class="text-xs mb-1">融冰速率: ${schedule.targetMeltingRate.toFixed(1)} kWh/h</div>`;
            html += `<div class="text-xs mt-2 pt-2 border-t border-slate-600">预期电费: <span class="text-yellow-400">¥${schedule.expectedCost.toFixed(2)}</span></div>`;
            html += `<div class="text-xs">节省电费: <span class="text-green-400">¥${schedule.costSaving.toFixed(2)}</span></div>`;
          }
          return html;
        },
      },
      grid: [
        { left: 60, right: 60, top: 40, height: 40 },
        { left: 60, right: 60, top: 100, height: 40 },
        { left: 60, right: 60, top: 160, height: 100 },
        { left: 60, right: 60, top: 280, height: 80 },
      ],
      xAxis: [
        {
          type: 'category',
          data: hours,
          gridIndex: 0,
          axisLabel: { show: false },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
        },
        {
          type: 'category',
          data: hours,
          gridIndex: 1,
          axisLabel: { show: false },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
        },
        {
          type: 'category',
          data: hours,
          gridIndex: 2,
          axisLabel: { show: false },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
        },
        {
          type: 'category',
          data: hours.map((h) => `${h}:00`),
          gridIndex: 3,
          axisLabel: { color: '#94A3B8', fontSize: 10, rotate: 45 },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { lineStyle: { color: '#1E293B' } },
        },
      ],
      yAxis: [
        {
          type: 'category',
          data: ['运行模式'],
          gridIndex: 0,
          axisLabel: { color: '#94A3B8', fontSize: 11 },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
        },
        {
          type: 'category',
          data: ['电价'],
          gridIndex: 1,
          axisLabel: { color: '#94A3B8', fontSize: 11 },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
        },
        {
          type: 'value',
          name: '蓄冰量 (kWh)',
          max: maxIceAmount * 1.2,
          gridIndex: 2,
          axisLabel: { color: '#94A3B8', fontSize: 10 },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { lineStyle: { color: '#1E293B' } },
          nameTextStyle: { color: '#94A3B8', fontSize: 10 },
        },
        {
          type: 'value',
          name: '负荷率 (%)',
          max: 100,
          gridIndex: 3,
          axisLabel: { color: '#94A3B8', fontSize: 10 },
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { lineStyle: { color: '#1E293B' } },
          nameTextStyle: { color: '#94A3B8', fontSize: 10 },
        },
      ],
      series: [
        {
          type: 'bar',
          xAxisIndex: 0,
          yAxisIndex: 0,
          data: modeData.map((d) => ({
            value: [d[0], 0],
            itemStyle: { color: modeColors[d[1] as number] || '#64748B' },
          })),
          barWidth: '95%',
        },
        {
          type: 'bar',
          xAxisIndex: 1,
          yAxisIndex: 1,
          data: priceBgColors.map((color, index) => ({
            value: [index, 0],
            itemStyle: { color },
          })),
          barWidth: '95%',
        },
        {
          name: '蓄冰量',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: iceAmountData,
          smooth: true,
          showSymbol: false,
          lineStyle: { width: 2, color: '#06B6D4' },
          areaStyle: {
            color: {
              type: 'linear',
              x: 0, y: 0, x2: 0, y2: 1,
              colorStops: [
                { offset: 0, color: 'rgba(6, 182, 212, 0.4)' },
                { offset: 1, color: 'rgba(6, 182, 212, 0)' },
              ],
            },
          },
        },
        {
          name: '融冰速率',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: meltingRateData,
          smooth: true,
          showSymbol: false,
          lineStyle: { width: 2, color: '#F59E0B', type: 'dashed' },
        },
        {
          name: '主机负荷',
          type: 'bar',
          xAxisIndex: 3,
          yAxisIndex: 3,
          data: chillerLoadData,
          stack: 'load',
          itemStyle: { color: '#3B82F6' },
          barWidth: '70%',
        },
        {
          name: '融冰负荷',
          type: 'bar',
          xAxisIndex: 3,
          yAxisIndex: 3,
          data: iceLoadData,
          stack: 'load',
          itemStyle: { color: '#DC2626' },
          barWidth: '70%',
        },
      ],
    };
  };

  const handleChartClick = (params: unknown) => {
    const p = params as { componentType: string; name?: string; dataIndex?: number };
    if (p.componentType === 'xAxis' || p.dataIndex !== undefined) {
      const hour = p.dataIndex ?? parseInt(p.name || '0');
      setSelectedHour(hour === selectedHour ? null : hour);
    }
  };

  const selectedSchedule = selectedHour !== null
    ? schedules.find((s) => s.hourOfDay === selectedHour)
    : null;

  if (loading) {
    return (
      <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4">
        <h3 className="text-white font-semibold mb-4">蓄融冰计划甘特图</h3>
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full" />
        </div>
      </div>
    );
  }

  return (
    <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-white font-semibold">蓄融冰计划甘特图</h3>
        <div className="text-xs text-slate-400">{currentDate}</div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <div className="bg-slate-700/30 rounded-lg p-3">
          <div className="text-slate-400 text-xs mb-1">预期电费</div>
          <div className="text-xl font-bold text-yellow-400">¥{stats.totalExpectedCost.toFixed(2)}</div>
        </div>
        <div className="bg-slate-700/30 rounded-lg p-3">
          <div className="text-slate-400 text-xs mb-1">节省电费</div>
          <div className="text-xl font-bold text-green-400">¥{stats.totalSaving.toFixed(2)}</div>
        </div>
        <div className="bg-slate-700/30 rounded-lg p-3">
          <div className="text-slate-400 text-xs mb-1">节能率</div>
          <div className="text-xl font-bold text-emerald-400">{stats.savingPercent.toFixed(1)}%</div>
        </div>
        <div className="bg-slate-700/30 rounded-lg p-3">
          <div className="text-slate-400 text-xs mb-1">蓄/融冰时长</div>
          <div className="text-xl font-bold text-cyan-400">
            {stats.iceMakingHours}h / {stats.iceMeltingHours}h
          </div>
        </div>
      </div>

      <div className="flex flex-wrap gap-3 mb-4">
        {Object.entries(modeNames).map(([mode, name]) => (
          <div key={mode} className="flex items-center gap-2 text-xs">
            <span
              className="w-3 h-3 rounded"
              style={{ backgroundColor: modeColors[parseInt(mode)] }}
            />
            <span className="text-slate-400">{name}</span>
          </div>
        ))}
      </div>

      <div className="h-96">
        <ReactECharts
          option={getChartOption()}
          style={{ height: '100%', width: '100%' }}
          onEvents={{ click: handleChartClick }}
        />
      </div>

      {selectedSchedule && selectedHour !== null && (
        <div className="mt-4 p-4 bg-slate-700/30 border border-slate-600 rounded-lg">
          <div className="flex items-center justify-between mb-3">
            <h4 className="text-white font-medium">
              {selectedHour}:00 - {selectedHour + 1}:00 详细信息
            </h4>
            <button
              onClick={() => setSelectedHour(null)}
              className="text-slate-400 hover:text-white text-sm"
            >
              ✕
            </button>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
            <div>
              <span className="text-slate-400">运行模式: </span>
              <span style={{ color: modeColors[selectedSchedule.mode] }}>
                {modeNames[selectedSchedule.mode]}
              </span>
            </div>
            <div>
              <span className="text-slate-400">目标蓄冰量: </span>
              <span className="text-cyan-400">{selectedSchedule.targetIceAmount.toFixed(1)} kWh</span>
            </div>
            <div>
              <span className="text-slate-400">目标融冰速率: </span>
              <span className="text-orange-400">{selectedSchedule.targetMeltingRate.toFixed(1)} kWh/h</span>
            </div>
            <div>
              <span className="text-slate-400">是否优化: </span>
              <span className={selectedSchedule.isOptimized ? 'text-green-400' : 'text-slate-400'}>
                {selectedSchedule.isOptimized ? '是' : '否'}
              </span>
            </div>
            <div>
              <span className="text-slate-400">主机负荷率: </span>
              <span className="text-blue-400">{(selectedSchedule.chillerLoadRatio * 100).toFixed(0)}%</span>
            </div>
            <div>
              <span className="text-slate-400">融冰负荷率: </span>
              <span className="text-red-400">{(selectedSchedule.iceLoadRatio * 100).toFixed(0)}%</span>
            </div>
            <div>
              <span className="text-slate-400">预期电费: </span>
              <span className="text-yellow-400">¥{selectedSchedule.expectedCost.toFixed(2)}</span>
            </div>
            <div>
              <span className="text-slate-400">节省电费: </span>
              <span className="text-green-400">¥{selectedSchedule.costSaving.toFixed(2)}</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
