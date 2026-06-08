import React, { useMemo } from 'react';
import type { Building, RadarChartData } from '@/types';

interface RankingTableProps {
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

const indicatorNames: Record<string, string> = {
  COP: 'COP',
  EER: 'EER',
  EnergyPerUnitArea: '单位面积能耗',
  LoadFactor: '负荷率',
  CostPerUnitArea: '单位面积费用',
  PUE: 'PUE',
};

const indicatorUnits: Record<string, string> = {
  COP: '',
  EER: '',
  EnergyPerUnitArea: 'kWh/m²',
  LoadFactor: '%',
  CostPerUnitArea: '元/m²',
  PUE: '',
};

const higherIsBetter: Record<string, boolean> = {
  COP: true,
  EER: true,
  EnergyPerUnitArea: false,
  LoadFactor: true,
  CostPerUnitArea: false,
  PUE: false,
};

const bestPractices = [
  '优化冷水机组运行台数组合，保持机组在高效区间运行',
  '根据室外气象条件动态调整冷冻水供水温度设定值',
  '实施水泵变频控制，按需调节流量，减少输送能耗',
  '定期清洗换热器，保持换热效率，降低污垢热阻',
  '优化冷却塔运行控制，降低冷凝温度，提高机组效率',
  '建立设备预防性维护计划，延长设备使用寿命',
  '实施分时电价策略，利用冰蓄冷系统移峰填谷',
  '加强楼宇自动化系统监控，实时分析能耗数据',
];

interface IndicatorRanking {
  name: string;
  values: { buildingId: string; buildingName: string; value: number; rank: number }[];
}

export const RankingTable: React.FC<RankingTableProps> = ({ radarData, buildings }) => {
  const getBuildingColor = (index: number) => {
    return buildingColors[index % buildingColors.length];
  };

  const rankings = useMemo((): IndicatorRanking[] => {
    const indicators = ['COP', 'EER', 'EnergyPerUnitArea', 'LoadFactor', 'CostPerUnitArea', 'PUE'];

    return indicators.map((indicator) => {
      const values = radarData.map((data) => {
        const building = buildings.find((b) => b.id === data.buildingId);
        const value = data.indicators[indicator as keyof RadarChartData['indicators']] || 0;
        return {
          buildingId: data.buildingId,
          buildingName: building?.buildingName || data.buildingId,
          value,
          rank: 0,
        };
      });

      const sorted = [...values].sort((a, b) => {
        if (higherIsBetter[indicator]) {
          return b.value - a.value;
        }
        return a.value - b.value;
      });

      sorted.forEach((item, index) => {
        const original = values.find((v) => v.buildingId === item.buildingId);
        if (original) {
          original.rank = index + 1;
        }
      });

      return {
        name: indicator,
        values,
      };
    });
  }, [radarData, buildings]);

  const overallScores = useMemo(() => {
    return radarData.map((data) => {
      const building = buildings.find((b) => b.id === data.buildingId);
      let totalRank = 0;
      rankings.forEach((ranking) => {
        const item = ranking.values.find((v) => v.buildingId === data.buildingId);
        if (item) {
          totalRank += item.rank;
        }
      });
      const avgRank = totalRank / rankings.length;
      const score = Math.max(0, Math.min(100, 100 - (avgRank - 1) * (100 / Math.max(radarData.length, 1))));
      return {
        buildingId: data.buildingId,
        buildingName: building?.buildingName || data.buildingId,
        score,
        avgRank,
      };
    }).sort((a, b) => a.avgRank - b.avgRank);
  }, [radarData, buildings, rankings]);

  const getRankBadgeColor = (rank: number, total: number) => {
    if (rank === 1) return 'bg-yellow-500 text-yellow-900';
    if (rank === 2) return 'bg-slate-400 text-slate-900';
    if (rank === 3) return 'bg-amber-700 text-amber-100';
    return 'bg-slate-600 text-slate-300';
  };

  return (
    <>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4">
        <RadarChart radarData={radarData} buildings={buildings} />

        <div className="bg-slate-700/20 rounded-xl p-4">
          <h4 className="text-white font-medium mb-3">综合评分</h4>
          <div className="space-y-3">
            {overallScores.map((item, index) => {
              const colorIndex = radarData.findIndex((d) => d.buildingId === item.buildingId);
              const color = getBuildingColor(colorIndex);
              return (
                <div key={item.buildingId} className="flex items-center gap-3">
                  <span
                    className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${
                      index === 0 ? 'bg-yellow-500 text-yellow-900' : 'bg-slate-600 text-white'
                    }`}
                  >
                    {index + 1}
                  </span>
                  <div className="flex-1">
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-white text-sm">{item.buildingName}</span>
                      <span className="text-white font-bold" style={{ color: color.main }}>
                        {item.score.toFixed(1)}
                      </span>
                    </div>
                    <div className="h-2 bg-slate-600 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all"
                        style={{ width: `${item.score}%`, backgroundColor: color.main }}
                      />
                    </div>
                  </div>
                </div>
              );
            })}
          </div>

          <div className="mt-4 p-3 bg-blue-500/10 border border-blue-500/30 rounded-lg">
            <div className="flex items-center gap-2 mb-2">
              <span className="text-blue-400">💡</span>
              <span className="text-blue-400 text-sm font-medium">最佳实践建议</span>
            </div>
            <div className="text-xs text-slate-300 space-y-1">
              {bestPractices.slice(0, 3).map((practice, index) => (
                <div key={index} className="flex items-start gap-1.5">
                  <span className="text-blue-400 mt-0.5">•</span>
                  <span>{practice}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="bg-slate-700/20 rounded-xl p-4">
        <h4 className="text-white font-medium mb-3">指标排名</h4>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-slate-400 text-xs border-b border-slate-600">
                <th className="text-left py-2 px-3 font-medium">指标</th>
                {radarData.map((data) => {
                  const building = buildings.find((b) => b.id === data.buildingId);
                  const colorIndex = radarData.findIndex((d) => d.buildingId === data.buildingId);
                  const color = getBuildingColor(colorIndex);
                  return (
                    <th
                      key={data.buildingId}
                      className="text-center py-2 px-3 font-medium"
                      style={{ color: color.main }}
                    >
                      {building?.buildingName || data.buildingId}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {rankings.map((ranking) => (
                <tr key={ranking.name} className="border-b border-slate-700/50">
                  <td className="py-2 px-3 text-slate-300 whitespace-nowrap">
                    {indicatorNames[ranking.name]}
                  </td>
                  {ranking.values.map((item) => {
                    const colorIndex = radarData.findIndex((d) => d.buildingId === item.buildingId);
                    return (
                      <td key={item.buildingId} className="py-2 px-3 text-center">
                        <div className="flex flex-col items-center gap-1">
                          <span className="text-white font-medium">
                            {item.value.toFixed(2)}
                            <span className="text-xs text-slate-400 ml-1">
                              {indicatorUnits[ranking.name]}
                            </span>
                          </span>
                          <span
                            className={`text-xs px-2 py-0.5 rounded-full font-medium ${getRankBadgeColor(
                              item.rank,
                              radarData.length
                            )}`}
                          >
                            #{item.rank}
                          </span>
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};
