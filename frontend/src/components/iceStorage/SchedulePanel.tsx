import React from 'react';
import type { IceStorageSchedule } from '@/types';
import { IceStorageMode } from '@/types';

interface SchedulePanelProps {
  schedule: IceStorageSchedule;
  hour: number;
  onClose: () => void;
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

export const SchedulePanel: React.FC<SchedulePanelProps> = ({
  schedule,
  hour,
  onClose,
}) => {
  return (
    <div className="mt-4 p-4 bg-slate-700/30 border border-slate-600 rounded-lg">
      <div className="flex items-center justify-between mb-3">
        <h4 className="text-white font-medium">
          {hour}:00 - {hour + 1}:00 详细信息
        </h4>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-white text-sm"
        >
          ✕
        </button>
      </div>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
        <div>
          <span className="text-slate-400">运行模式: </span>
          <span style={{ color: modeColors[schedule.mode] }}>
            {modeNames[schedule.mode]}
          </span>
        </div>
        <div>
          <span className="text-slate-400">目标蓄冰量: </span>
          <span className="text-cyan-400">{schedule.targetIceAmount.toFixed(1)} kWh</span>
        </div>
        <div>
          <span className="text-slate-400">目标融冰速率: </span>
          <span className="text-orange-400">{schedule.targetMeltingRate.toFixed(1)} kWh/h</span>
        </div>
        <div>
          <span className="text-slate-400">是否优化: </span>
          <span className={schedule.isOptimized ? 'text-green-400' : 'text-slate-400'}>
            {schedule.isOptimized ? '是' : '否'}
          </span>
        </div>
        <div>
          <span className="text-slate-400">主机负荷率: </span>
          <span className="text-blue-400">{(schedule.chillerLoadRatio * 100).toFixed(0)}%</span>
        </div>
        <div>
          <span className="text-slate-400">融冰负荷率: </span>
          <span className="text-red-400">{(schedule.iceLoadRatio * 100).toFixed(0)}%</span>
        </div>
        <div>
          <span className="text-slate-400">预期电费: </span>
          <span className="text-yellow-400">¥{schedule.expectedCost.toFixed(2)}</span>
        </div>
        <div>
          <span className="text-slate-400">节省电费: </span>
          <span className="text-green-400">¥{schedule.costSaving.toFixed(2)}</span>
        </div>
      </div>
    </div>
  );
};
