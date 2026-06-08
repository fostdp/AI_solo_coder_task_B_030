import React from 'react';
import type { IceStorageSchedule } from '@/types';
import { IceStorageMode } from '@/types';

interface StatisticsCardsProps {
  schedules: IceStorageSchedule[];
}

export const StatisticsCards: React.FC<StatisticsCardsProps> = ({ schedules }) => {
  const totalExpectedCost = schedules.reduce((sum, s) => sum + s.expectedCost, 0);
  const totalBaselineCost = schedules.reduce((sum, s) => sum + s.baselineCost, 0);
  const totalSaving = schedules.reduce((sum, s) => sum + s.costSaving, 0);
  const iceMakingHours = schedules.filter((s) => s.mode === IceStorageMode.IceMaking).length;
  const iceMeltingHours = schedules.filter((s) => s.mode === IceStorageMode.IceMelting || s.mode === IceStorageMode.Combined).length;
  const savingPercent = totalBaselineCost > 0 ? ((totalBaselineCost - totalExpectedCost) / totalBaselineCost) * 100 : 0;

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
      <div className="bg-slate-700/30 rounded-lg p-3">
        <div className="text-slate-400 text-xs mb-1">预期电费</div>
        <div className="text-xl font-bold text-yellow-400">¥{totalExpectedCost.toFixed(2)}</div>
      </div>
      <div className="bg-slate-700/30 rounded-lg p-3">
        <div className="text-slate-400 text-xs mb-1">节省电费</div>
        <div className="text-xl font-bold text-green-400">¥{totalSaving.toFixed(2)}</div>
      </div>
      <div className="bg-slate-700/30 rounded-lg p-3">
        <div className="text-slate-400 text-xs mb-1">节能率</div>
        <div className="text-xl font-bold text-emerald-400">{savingPercent.toFixed(1)}%</div>
      </div>
      <div className="bg-slate-700/30 rounded-lg p-3">
        <div className="text-slate-400 text-xs mb-1">蓄/融冰时长</div>
        <div className="text-xl font-bold text-cyan-400">
          {iceMakingHours}h / {iceMeltingHours}h
        </div>
      </div>
    </div>
  );
};
