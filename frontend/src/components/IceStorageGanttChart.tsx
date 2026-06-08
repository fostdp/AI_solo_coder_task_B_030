import React, { useState, useMemo } from 'react';
import { useIceStorage } from '@/hooks/useIceStorage';
import { StatisticsCards } from './iceStorage/StatisticsCards';
import { GanttChart } from './iceStorage/GanttChart';
import { SchedulePanel } from './iceStorage/SchedulePanel';

interface IceStorageGanttChartProps {
  date?: string;
}

export const IceStorageGanttChart: React.FC<IceStorageGanttChartProps> = ({ date }) => {
  const [selectedHour, setSelectedHour] = useState<number | null>(null);
  const { schedule, priceTiers, loading, fetchSchedule } = useIceStorage();

  const currentDate = useMemo(() => {
    return date || new Date().toISOString().split('T')[0];
  }, [date]);

  const selectedSchedule = selectedHour !== null
    ? schedule.find((s) => s.hourOfDay === selectedHour)
    : null;

  const handleHourClick = (hour: number | null) => {
    setSelectedHour(hour);
  };

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

      <StatisticsCards schedules={schedule} />

      <GanttChart
        schedules={schedule}
        priceTiers={priceTiers}
        onHourClick={handleHourClick}
        selectedHour={selectedHour}
      />

      {selectedSchedule && selectedHour !== null && (
        <SchedulePanel
          schedule={selectedSchedule}
          hour={selectedHour}
          onClose={() => setSelectedHour(null)}
        />
      )}
    </div>
  );
};
