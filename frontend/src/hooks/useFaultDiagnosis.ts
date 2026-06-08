import { useState, useCallback } from 'react';
import { faultDiagnosisApi } from '@/services/api';
import type {
  FaultDiagnosisResult,
  FaultType,
  DiagnosisStatistic,
} from '@/types';

export function useFaultDiagnosis() {
  const [diagnosisResults, setDiagnosisResults] = useState<FaultDiagnosisResult[]>([]);
  const [faultTypes, setFaultTypes] = useState<FaultType[]>([]);
  const [isDiagnosing, setIsDiagnosing] = useState(false);
  const [statistics, setStatistics] = useState<DiagnosisStatistic | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const diagnoseDevice = useCallback(async (deviceId: string) => {
    setIsDiagnosing(true);
    setError(null);
    try {
      const response = await faultDiagnosisApi.diagnoseDevice(deviceId);
      setDiagnosisResults(prev => {
        const filtered = prev.filter(r => r.deviceId !== deviceId);
        return [...response.data, ...filtered].slice(0, 100);
      });
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '设备诊断失败');
      throw err;
    } finally {
      setIsDiagnosing(false);
    }
  }, []);

  const diagnoseAll = useCallback(async () => {
    setIsDiagnosing(true);
    setError(null);
    try {
      const response = await faultDiagnosisApi.diagnoseAll();
      setDiagnosisResults(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '全设备诊断失败');
      throw err;
    } finally {
      setIsDiagnosing(false);
    }
  }, []);

  const fetchResults = useCallback(async (deviceId?: string, page = 1, pageSize = 20) => {
    setLoading(true);
    setError(null);
    try {
      const response = await faultDiagnosisApi.getResults(deviceId, page, pageSize);
      if (!deviceId) {
        setDiagnosisResults(response.data);
      }
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取诊断结果失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchFaultTypes = useCallback(async (deviceTypeId: number) => {
    setLoading(true);
    setError(null);
    try {
      const response = await faultDiagnosisApi.getFaultTypes(deviceTypeId);
      setFaultTypes(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取故障类型失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const confirmDiagnosis = useCallback(async (id: number, confirmedBy: string) => {
    setLoading(true);
    setError(null);
    try {
      await faultDiagnosisApi.confirmResult(id, confirmedBy);
      setDiagnosisResults(prev =>
        prev.map(r =>
          r.id === id
            ? { ...r, isConfirmed: true, confirmedBy, confirmedAt: new Date().toISOString() }
            : r
        )
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : '确认诊断失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchStatistics = useCallback(async (date: Date) => {
    setLoading(true);
    setError(null);
    try {
      const response = await faultDiagnosisApi.getStatistics(date.toISOString());
      setStatistics(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取统计数据失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const getConfirmedCount = useCallback(() => {
    return diagnosisResults.filter(r => r.isConfirmed).length;
  }, [diagnosisResults]);

  const getHighConfidenceCount = useCallback((threshold = 0.8) => {
    return diagnosisResults.filter(r => r.confidence >= threshold).length;
  }, [diagnosisResults]);

  const getFaultTypeDistribution = useCallback(() => {
    const distribution: Record<number, number> = {};
    diagnosisResults.forEach(r => {
      if (r.faultTypeId) {
        distribution[r.faultTypeId] = (distribution[r.faultTypeId] || 0) + 1;
      }
    });
    return distribution;
  }, [diagnosisResults]);

  const getAverageConfidence = useCallback(() => {
    if (diagnosisResults.length === 0) return 0;
    return diagnosisResults.reduce((sum, r) => sum + r.confidence, 0) / diagnosisResults.length;
  }, [diagnosisResults]);

  return {
    diagnosisResults,
    faultTypes,
    isDiagnosing,
    statistics,
    loading,
    error,
    diagnoseDevice,
    diagnoseAll,
    fetchResults,
    fetchFaultTypes,
    confirmDiagnosis,
    fetchStatistics,
    getConfirmedCount,
    getHighConfidenceCount,
    getFaultTypeDistribution,
    getAverageConfidence,
  };
}
