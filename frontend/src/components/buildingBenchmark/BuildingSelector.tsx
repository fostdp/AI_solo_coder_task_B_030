import React from 'react';
import type { Building } from '@/types';

interface BuildingSelectorProps {
  buildings: Building[];
  selectedBuildingIds: string[];
  period: number;
  date: string;
  onBuildingToggle: (buildingId: string) => void;
  onSelectAll: () => void;
  onPeriodChange: (period: number) => void;
  onDateChange: (date: string) => void;
  onExportReport: () => void;
  isGeneratingReport: boolean;
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

export const BuildingSelector: React.FC<BuildingSelectorProps> = ({
  buildings,
  selectedBuildingIds,
  period,
  date,
  onBuildingToggle,
  onSelectAll,
  onPeriodChange,
  onDateChange,
  onExportReport,
  isGeneratingReport,
}) => {
  const getBuildingColor = (index: number) => {
    return buildingColors[index % buildingColors.length];
  };

  return (
    <>
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-white font-semibold">楼宇能效雷达图</h3>
        <button
          onClick={onExportReport}
          disabled={selectedBuildingIds.length < 2 || isGeneratingReport}
          className="px-3 py-1.5 bg-emerald-500 hover:bg-emerald-600 disabled:bg-slate-600 disabled:cursor-not-allowed text-white text-sm rounded-lg transition-colors flex items-center gap-2"
        >
          <span>📊</span>
          {isGeneratingReport ? '生成中...' : '导出对标报告'}
        </button>
      </div>

      <div className="flex flex-wrap gap-4 mb-4">
        <div className="flex items-center gap-2">
          <label className="text-slate-400 text-sm">统计周期:</label>
          <select
            value={period}
            onChange={(e) => onPeriodChange(parseInt(e.target.value))}
            className="bg-slate-700 border border-slate-600 text-white text-sm rounded-lg px-3 py-1.5 focus:outline-none focus:border-blue-500"
          >
            <option value={0}>日</option>
            <option value={1}>周</option>
            <option value={2}>月</option>
            <option value={3}>年</option>
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label className="text-slate-400 text-sm">日期:</label>
          <input
            type="date"
            value={date}
            onChange={(e) => onDateChange(e.target.value)}
            className="bg-slate-700 border border-slate-600 text-white text-sm rounded-lg px-3 py-1.5 focus:outline-none focus:border-blue-500"
          />
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={onSelectAll}
            className="text-xs text-blue-400 hover:text-blue-300 transition-colors"
          >
            {selectedBuildingIds.length === buildings.length ? '取消全选' : '全选'}
          </button>
        </div>
      </div>

      <div className="flex flex-wrap gap-2 mb-4">
        {buildings.map((building, index) => {
          const color = getBuildingColor(index);
          const isSelected = selectedBuildingIds.includes(building.id);

          return (
            <button
              key={building.id}
              onClick={() => onBuildingToggle(building.id)}
              className={`px-3 py-1.5 rounded-lg text-sm transition-all flex items-center gap-2 border ${
                isSelected
                  ? 'bg-opacity-20 border-opacity-50 text-white'
                  : 'bg-slate-700/30 border-transparent text-slate-400 hover:bg-slate-700/50'
              }`}
              style={{
                backgroundColor: isSelected ? color.light : undefined,
                borderColor: isSelected ? color.main : undefined,
              }}
            >
              <span
                className="w-3 h-3 rounded-full"
                style={{ backgroundColor: isSelected ? color.main : '#64748B' }}
              />
              <span className="truncate max-w-32">{building.buildingName}</span>
            </button>
          );
        })}
      </div>
    </>
  );
};
