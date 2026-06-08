import React, { useState, useEffect, useMemo } from 'react';
import ReactECharts from 'echarts-for-react';
import type { Building, RadarChartData, BenchmarkReport } from '@/types';
import { buildingBenchmarkApi } from '@/services/api';
import { StatisticsPeriod } from '@/types';

interface BuildingRadarChartProps {
  defaultPeriod?: number;
  defaultDate?: string;
}

interface IndicatorRanking {
  name: string;
  values: { buildingId: string; buildingName: string; value: number; rank: number }[];
}

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

export const BuildingRadarChart: React.FC<BuildingRadarChartProps> = ({
  defaultPeriod = StatisticsPeriod.Monthly,
  defaultDate,
}) => {
  const [buildings, setBuildings] = useState<Building[]>([]);
  const [selectedBuildingIds, setSelectedBuildingIds] = useState<string[]>([]);
  const [radarData, setRadarData] = useState<RadarChartData[]>([]);
  const [period, setPeriod] = useState(defaultPeriod);
  const [date, setDate] = useState(defaultDate || new Date().toISOString().split('T')[0]);
  const [loading, setLoading] = useState(true);
  const [generatingReport, setGeneratingReport] = useState(false);
  const [report, setReport] = useState<BenchmarkReport | null>(null);

  useEffect(() => {
    const fetchBuildings = async () => {
      try {
        const res = await buildingBenchmarkApi.getBuildings();
        setBuildings(res.data);
        if (res.data.length > 0) {
          const defaultSelected = res.data.slice(0, Math.min(3, res.data.length)).map((b) => b.id);
          setSelectedBuildingIds(defaultSelected);
        }
      } catch (error) {
        console.error('获取楼宇列表失败:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchBuildings();
  }, []);

  useEffect(() => {
    if (selectedBuildingIds.length === 0) return;

    const fetchRadarData = async () => {
      try {
        setLoading(true);
        const res = await buildingBenchmarkApi.getRadarData(selectedBuildingIds, period, date);
        const dataArray: RadarChartData[] = Object.entries(res.data).map(([buildingId, indicators]) => {
          const building = buildings.find((b) => b.id === buildingId);
          return {
            buildingId,
            buildingName: building?.buildingName || buildingId,
            indicators: indicators as RadarChartData['indicators'],
          };
        });
        setRadarData(dataArray);
      } catch (error) {
        console.error('获取雷达图数据失败:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchRadarData();
  }, [selectedBuildingIds, period, date, buildings]);

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
      const color = buildingColors[index % buildingColors.length];
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

  const handleExportReport = async () => {
    if (selectedBuildingIds.length < 2) {
      alert('请至少选择2个楼宇进行对标');
      return;
    }

    try {
      setGeneratingReport(true);
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

      const res = await buildingBenchmarkApi.generateReport({
        buildingIds: selectedBuildingIds,
        reportName: `楼宇对标报告_${date}`,
        startDate,
        endDate,
      });

      setReport(res.data);
      alert('对标报告生成成功！');
    } catch (error) {
      console.error('生成对标报告失败:', error);
      alert('生成对标报告失败');
    } finally {
      setGeneratingReport(false);
    }
  };

  const getRankBadgeColor = (rank: number, total: number) => {
    if (rank === 1) return 'bg-yellow-500 text-yellow-900';
    if (rank === 2) return 'bg-slate-400 text-slate-900';
    if (rank === 3) return 'bg-amber-700 text-amber-100';
    return 'bg-slate-600 text-slate-300';
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
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-white font-semibold">楼宇能效雷达图</h3>
        <button
          onClick={handleExportReport}
          disabled={selectedBuildingIds.length < 2 || generatingReport}
          className="px-3 py-1.5 bg-emerald-500 hover:bg-emerald-600 disabled:bg-slate-600 disabled:cursor-not-allowed text-white text-sm rounded-lg transition-colors flex items-center gap-2"
        >
          <span>📊</span>
          {generatingReport ? '生成中...' : '导出对标报告'}
        </button>
      </div>

      <div className="flex flex-wrap gap-4 mb-4">
        <div className="flex items-center gap-2">
          <label className="text-slate-400 text-sm">统计周期:</label>
          <select
            value={period}
            onChange={(e) => setPeriod(parseInt(e.target.value))}
            className="bg-slate-700 border border-slate-600 text-white text-sm rounded-lg px-3 py-1.5 focus:outline-none focus:border-blue-500"
          >
            <option value={StatisticsPeriod.Daily}>日</option>
            <option value={StatisticsPeriod.Weekly}>周</option>
            <option value={StatisticsPeriod.Monthly}>月</option>
            <option value={StatisticsPeriod.Yearly}>年</option>
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label className="text-slate-400 text-sm">日期:</label>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="bg-slate-700 border border-slate-600 text-white text-sm rounded-lg px-3 py-1.5 focus:outline-none focus:border-blue-500"
          />
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={handleSelectAll}
            className="text-xs text-blue-400 hover:text-blue-300 transition-colors"
          >
            {selectedBuildingIds.length === buildings.length ? '取消全选' : '全选'}
          </button>
        </div>
      </div>

      <div className="flex flex-wrap gap-2 mb-4">
        {buildings.map((building, index) => {
          const color = buildingColors[index % buildingColors.length];
          const isSelected = selectedBuildingIds.includes(building.id);

          return (
            <button
              key={building.id}
              onClick={() => handleBuildingToggle(building.id)}
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

      {selectedBuildingIds.length === 0 ? (
        <div className="text-center py-16 text-slate-400">
          请选择至少一个楼宇进行展示
        </div>
      ) : (
        <>
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4">
            <div className="bg-slate-700/20 rounded-xl p-4">
              <h4 className="text-white font-medium mb-3">能效雷达图</h4>
              <div className="h-80">
                <ReactECharts
                  option={getChartOption()}
                  style={{ height: '100%', width: '100%' }}
                />
              </div>
            </div>

            <div className="bg-slate-700/20 rounded-xl p-4">
              <h4 className="text-white font-medium mb-3">综合评分</h4>
              <div className="space-y-3">
                {overallScores.map((item, index) => {
                  const color = buildingColors[
                    radarData.findIndex((d) => d.buildingId === item.buildingId) % buildingColors.length
                  ];
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
                      const color = buildingColors[colorIndex % buildingColors.length];
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
                        const color = buildingColors[colorIndex % buildingColors.length];
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

          {report && (
            <div className="mt-4 p-4 bg-emerald-500/10 border border-emerald-500/30 rounded-lg">
              <div className="flex items-center justify-between mb-2">
                <div className="flex items-center gap-2">
                  <span className="text-emerald-400">✅</span>
                  <span className="text-emerald-400 font-medium">报告生成成功</span>
                </div>
                <button
                  onClick={() => setReport(null)}
                  className="text-slate-400 hover:text-white text-sm"
                >
                  ✕
                </button>
              </div>
              <div className="text-sm text-slate-300 space-y-1">
                <div>报告名称: <span className="text-white">{report.reportName}</span></div>
                <div>报告周期: <span className="text-white">{report.reportPeriod}</span></div>
                <div>综合评分: <span className="text-emerald-400 font-bold">{report.overallScore?.toFixed(1) || '-'}</span></div>
                {report.bestPractices && (
                  <div className="mt-2 pt-2 border-t border-slate-600/50">
                    <span className="text-slate-400">最佳实践: </span>
                    <span className="text-white">{report.bestPractices}</span>
                  </div>
                )}
                {report.improvementSuggestions && (
                  <div className="mt-2">
                    <span className="text-slate-400">改进建议: </span>
                    <span className="text-white">{report.improvementSuggestions}</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
};
