import React, { useState, useMemo, useEffect } from 'react';
import { useBuildingBenchmark } from '@/hooks/useBuildingBenchmark';
import { BuildingSelector } from './buildingBenchmark/BuildingSelector';
import { RankingTable } from './buildingBenchmark/RankingTable';
import { ReportGenerator } from './buildingBenchmark/ReportGenerator';
import { StatisticsPeriod } from '@/types';

interface BuildingRadarChartProps {
  defaultPeriod?: number;
  defaultDate?: string;
}

export const BuildingRadarChart: React.FC<BuildingRadarChartProps> = ({
  defaultPeriod = StatisticsPeriod.Monthly,
  defaultDate,
}) => {
  const [period, setPeriod] = useState(defaultPeriod);
  const [date, setDate] = useState(defaultDate || new Date().toISOString().split('T')[0]);
  const [selectedBuildingIds, setSelectedBuildingIds] = useState<string[]>([]);
  const [report, setReport] = useState<any>(null);

  const {
    buildings,
    radarData,
    loading,
    isGeneratingReport,
    fetchBuildings,
    fetchRadarData,
    generateReport,
  } = useBuildingBenchmark();

  const radarDataArray = useMemo(() => {
    return Object.entries(radarData).map(([buildingId, indicators]) => {
      const building = buildings.find((b) => b.id === buildingId);
      return {
        buildingId,
        buildingName: building?.buildingName || buildingId,
        indicators: indicators as any,
      };
    });
  }, [radarData, buildings]);

  useEffect(() => {
    const init = async () => {
      const buildingsData = await fetchBuildings();
      if (buildingsData && buildingsData.length > 0 && selectedBuildingIds.length === 0) {
        const defaultSelected = buildingsData.slice(0, Math.min(3, buildingsData.length)).map((b) => b.id);
        setSelectedBuildingIds(defaultSelected);
      }
    };
    init();
  }, [fetchBuildings]);

  useEffect(() => {
    if (selectedBuildingIds.length === 0) return;
    fetchRadarData(selectedBuildingIds, period, new Date(date));
  }, [selectedBuildingIds, period, date, fetchRadarData]);

  const handleBuildingToggle = (buildingId: string) => {
    setSelectedBuildingIds((prev) =>
      prev.includes(buildingId)
        ? prev.filter((id) => id !== buildingId)
        : [...prev, buildingId]
    );
  };

  const handleSelectAll = () => {
    if (selectedBuildingIds.length === buildings.length) {
      setSelectedBuildingIds([]);
    } else {
      setSelectedBuildingIds(buildings.map((b) => b.id));
    }
  };

  const handleExportReport = async () => {
    if (selectedBuildingIds.length < 2) {
      alert('请至少选择2个楼宇进行对标');
      return;
    }

    const endDate = date;
    const startDateObj = new Date(date);
    if (period === StatisticsPeriod.Monthly) {
      startDateObj.setMonth(startDateObj.getMonth() - 1);
    } else if (period === StatisticsPeriod.Weekly) {
      startDateObj.setDate(startDateObj.getDate() - 7);
    } else if (period === StatisticsPeriod.Yearly) {
      startDateObj.setFullYear(startDateObj.getFullYear() - 1);
    } else {
      startDateObj.setDate(startDateObj.getDate() - 1);
    }
    const startDate = startDateObj.toISOString().split('T')[0];

    const reportData = await generateReport(
      selectedBuildingIds,
      `楼宇对标报告_${date}`,
      new Date(startDate),
      new Date(endDate)
    );
    setReport(reportData);
    alert('对标报告生成成功！');
  };

  if (loading && !buildings.length) {
    return (
      <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4">
        <h3 className="text-white font-semibold mb-4">楼宇能效雷达图</h3>
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full" />
        </div>
      </div>
    );
  }

  return (
    <div className="bg-slate-800/50 border border-slate-700 rounded-xl p-4">
      <BuildingSelector
        buildings={buildings}
        selectedBuildingIds={selectedBuildingIds}
        period={period}
        date={date}
        onBuildingToggle={handleBuildingToggle}
        onSelectAll={handleSelectAll}
        onPeriodChange={setPeriod}
        onDateChange={setDate}
        onExportReport={handleExportReport}
        isGeneratingReport={isGeneratingReport}
      />

      {selectedBuildingIds.length === 0 ? (
        <div className="text-center py-16 text-slate-400">
          请选择至少一个楼宇进行展示
        </div>
      ) : (
        <>
          <RankingTable
            radarData={radarDataArray}
            buildings={buildings}
          />

          <ReportGenerator
            report={report}
            onClose={() => setReport(null)}
          />
        </>
      )}
    </div>
  );
};
