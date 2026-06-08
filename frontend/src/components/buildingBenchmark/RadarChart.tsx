import React from 'react';
import ReactECharts from 'echarts-for-react';
import type { Building, RadarChartData } from '@/types';

interface RadarChartProps {
  radarData: RadarChartData[];
  buildings: Building[];
}

const buildingColors = [
  { main: '#3B82F6', light: 'rgba(59, 130, 246, 0.3)' },
  { main: '#10B981', light: 'rgba(16, 185, 129, 0.3)' },
  { main: '#F59E0B', light: 'rgba(245, 158, 11, 0.3)' },
  { main: '#EF4444', light: 'rgba(239, 68, 68, 0.3)' },
  { main: '#8B5CF6', light: 'rgba(139, 92, 246, 0.3)' },
  { main: '#EC4899', light: 'rgba(236, 72, 153, 0.3)' },
  { main: '#06B6D4', light: 'rgba(6, 182, 212, 0.3)' },
  { main: '#F97316', light: 'rgba(249, 115, 22, 0.3)' },
];

export const RadarChart: React.FC<RadarChartProps> = ({ radarData, buildings }) => {
  const getBuildingColor = (index: number) => {
    return buildingColors[index % buildingColors.length];
  };

  const getChartOption = () => {
    const indicators = [
      { name: 'COP', max: 7 },
      { name: 'EER', max: 10 },
      { name: '单位面积能耗', max: 150 },
      { name: '负荷率', max: 100 },
      { name: '单位面积费用', max: 100 },
      { name: 'PUE', max: 2 },
    ];

    const seriesData = radarData.map((data, index) => {
      const color = getBuildingColor(index);
      const indicatorValues = [
        data.indicators.COP || 0,
        data.indicators.EER || 0,
        data.indicators.EnergyPerUnitArea || 0,
        data.indicators.LoadFactor || 0,
        data.indicators.CostPerUnitArea || 0,
        data.indicators.PUE || 0,
      ];

      return {
        name: data.buildingName,
        type: 'radar',
        data: [
          {
            value: indicatorValues,
            name: data.buildingName,
            symbol: 'circle',
            symbolSize: 6,
            lineStyle: { width: 2, color: color.main },
            areaStyle: { color: color.light },
            itemStyle: { color: color.main },
          },
        ],
      };
    });

    return {
      backgroundColor: 'transparent',
      tooltip: {
        trigger: 'item',
        backgroundColor: 'rgba(15, 23, 42, 0.95)',
        borderColor: '#334155',
        textStyle: { color: '#F1F5F9' },
      },
      legend: {
        data: radarData.map((d) => d.buildingName),
        textStyle: { color: '#94A3B8' },
        top: 10,
        type: 'scroll',
      },
      radar: {
        indicator,
        center: ['50%', '55%'],
        radius: '65%',
        splitNumber: 4,
        axisName: {
          color: '#94A3B8',
          fontSize: 11,
        },
        splitLine: {
          lineStyle: { color: '#334155' },
        },
        splitArea: {
          show: true,
          areaStyle: {
            color: ['rgba(30, 41, 59, 0.3)', 'rgba(30, 41, 59, 0.1)'],
          },
        },
        axisLine: {
          lineStyle: { color: '#475569' },
        },
      },
      series: seriesData,
    };
  };

  return (
    <div className="bg-slate-700/20 rounded-xl p-4">
      <h4 className="text-white font-medium mb-3">能效雷达图</h4>
      <div className="h-80">
        <ReactECharts
          option={getChartOption()}
          style={{ height: '100%', width: '100%' }}
        />
      </div>
    </div>
  );
};
