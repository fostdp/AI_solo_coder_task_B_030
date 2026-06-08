import { useState, useCallback, useEffect } from 'react';
import { buildingBenchmarkApi } from '@/services/api';
import type {
  Building,
  BuildingEfficiencyMetric,
  BenchmarkReport,
} from '@/types';

export function useBuildingBenchmark() {
  const [buildings, setBuildings] = useState<Building[]>([]);
  const [selectedBuildings, setSelectedBuildings] = useState<string[]>([]);
  const [buildingMetrics, setBuildingMetrics] = useState<Record<string, BuildingEfficiencyMetric[]>>({});
  const [radarData, setRadarData] = useState<Record<string, Record<string, number>>>({});
  const [reports, setReports] = useState<BenchmarkReport[]>([]);
  const [isGeneratingReport, setIsGeneratingReport] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchBuildings = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.getBuildings();
      setBuildings(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取楼宇列表失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const addBuilding = useCallback(async (data: Partial<Building>) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.addBuilding(data);
      await fetchBuildings();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '添加楼宇失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [fetchBuildings]);

  const updateBuilding = useCallback(async (id: string, data: Partial<Building>) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.updateBuilding(id, data);
      await fetchBuildings();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '更新楼宇失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [fetchBuildings]);

  const deleteBuilding = useCallback(async (id: string) => {
    setLoading(true);
    setError(null);
    try {
      const result = await buildingBenchmarkApi.deleteBuilding(id);
      await fetchBuildings();
      return result.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '删除楼宇失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [fetchBuildings]);

  const calculateMetrics = useCallback(async (buildingId: string, startDate: Date, endDate: Date) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.calculateMetrics(
        buildingId,
        startDate.toISOString(),
        endDate.toISOString()
      );
      setBuildingMetrics(prev => ({
        ...prev,
        [buildingId]: response.data,
      }));
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '计算楼宇指标失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchBuildingMetrics = useCallback(async (buildingId: string, period: number, startDate: Date) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.getBuildingMetrics(
        buildingId,
        period,
        startDate.toISOString()
      );
      setBuildingMetrics(prev => ({
        ...prev,
        [buildingId]: response.data,
      }));
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取楼宇指标失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchRadarData = useCallback(async (buildingIds: string[], period: number, date: Date) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.getRadarData(
        buildingIds,
        period,
        date.toISOString()
      );
      setRadarData(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取雷达图数据失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const generateReport = useCallback(async (
    buildingIds: string[],
    reportName: string,
    startDate: Date,
    endDate: Date
  ) => {
    setIsGeneratingReport(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.generateReport({
        buildingIds,
        reportName,
        startDate: startDate.toISOString(),
        endDate: endDate.toISOString(),
      });
      await fetchReports();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '生成对标报告失败');
      throw err;
    } finally {
      setIsGeneratingReport(false);
    }
  }, []);

  const fetchReports = useCallback(async (page = 1, pageSize = 20) => {
    setLoading(true);
    setError(null);
    try {
      const response = await buildingBenchmarkApi.getReports(page, pageSize);
      setReports(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取报告列表失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const toggleBuildingSelection = useCallback((buildingId: string) => {
    setSelectedBuildings(prev =>
      prev.includes(buildingId)
        ? prev.filter(id => id !== buildingId)
        : [...prev, buildingId]
    );
  }, []);

  const getBuildingById = useCallback((buildingId: string) => {
    return buildings.find(b => b.id === buildingId);
  }, [buildings]);

  const getAverageMetric = useCallback((buildingId: string, metricKey: keyof BuildingEfficiencyMetric) => {
    const metrics = buildingMetrics[buildingId];
    if (!metrics || metrics.length === 0) return 0;
    
    const values = metrics
      .map(m => m[metricKey])
      .filter((v): v is number => typeof v === 'number');
    
    return values.length > 0 ? values.reduce((sum, v) => sum + v, 0) / values.length : 0;
  }, [buildingMetrics]);

  const getRankingData = useCallback(() => {
    if (Object.keys(radarData).length === 0) return [];
    
    return Object.entries(radarData).map(([buildingId, data]) => ({
      buildingId,
      buildingName: getBuildingById(buildingId)?.buildingName || '未知',
      overallScore: (
        (data.COP || 0) +
        (data.EER || 0) +
        (data.EnergyPerUnitArea || 0) +
        (data.LoadFactor || 0) +
        (data.CostPerUnitArea || 0) +
        (data.PUE || 0)
      ) / 6,
      ...data,
    })).sort((a, b) => b.overallScore - a.overallScore);
  }, [radarData, getBuildingById]);

  useEffect(() => {
    fetchBuildings();
    fetchReports();
  }, [fetchBuildings, fetchReports]);

  return {
    buildings,
    selectedBuildings,
    buildingMetrics,
    radarData,
    reports,
    isGeneratingReport,
    loading,
    error,
    fetchBuildings,
    addBuilding,
    updateBuilding,
    deleteBuilding,
    calculateMetrics,
    fetchBuildingMetrics,
    fetchRadarData,
    generateReport,
    fetchReports,
    toggleBuildingSelection,
    getBuildingById,
    getAverageMetric,
    getRankingData,
  };
}
