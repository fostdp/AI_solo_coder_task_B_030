import React, { useMemo } from 'react';
import ReactECharts from 'echarts-for-react';
import type { IceStorageSchedule, ElectricityPriceTier } from '@/types';
import { IceStorageMode } from '@/types';

interface GanttChartProps {
  schedules: IceStorageSchedule[];
  priceTiers: ElectricityPriceTier[];
  onHourClick?: (hour: number) => void;
  selectedHour?: number | null;
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

export const GanttChart: React.FC<GanttChartProps> = ({
  schedules,
  priceTiers,
  onHourClick,
  selectedHour,
}) => {
  const getPriceTierByHour = (hour: number): ElectricityPriceTier | undefined => {
    return priceTiers.find((tier) => {
      if (tier.startHour <= tier.endHour) {
        return hour >= tier.startHour && hour < tier.endHour;
      }
      return hour >= tier.startHour || hour < tier.endHour;
    });
  };

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
    if (!onHourClick) return;
    const p = params as { componentType: string; name?: string; dataIndex?: number };
    if (p.componentType === 'xAxis' || p.dataIndex !== undefined) {
      const hour = p.dataIndex ?? parseInt(p.name || '0');
      onHourClick(hour === selectedHour ? null : hour);
    }
  };

  const legendItems = useMemo(() => {
    return Object.entries(modeNames).map(([mode, name]) => ({
      mode: parseInt(mode),
      name,
      color: modeColors[parseInt(mode)],
    }));
  }, []);

  return (
    <>
      <div className="flex flex-wrap gap-3 mb-4">
        {legendItems.map((item) => (
          <div key={item.mode} className="flex items-center gap-2 text-xs">
            <span
              className="w-3 h-3 rounded"
              style={{ backgroundColor: item.color }}
            />
            <span className="text-slate-400">{item.name}</span>
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
    </>
  );
};
