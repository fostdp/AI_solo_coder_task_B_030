import { useState, useCallback, useEffect } from 'react';
import { demandResponseApi } from '@/services/api';
import type {
  DemandResponseRequest,
  DRExecutionLog,
  DRResponseSummary,
  DRLimits,
} from '@/types';

export function useDemandResponse() {
  const [activeRequests, setActiveRequests] = useState<DemandResponseRequest[]>([]);
  const [currentLimits, setCurrentLimits] = useState<DRLimits>({
    chillerOutputLimit: 1.0,
    iceMeltingRate: 0,
  });
  const [executionLogs, setExecutionLogs] = useState<DRExecutionLog[]>([]);
  const [isExecuting, setIsExecuting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchActiveRequests = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await demandResponseApi.getActive();
      setActiveRequests(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取活动请求失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchCurrentLimits = useCallback(async () => {
    try {
      const response = await demandResponseApi.getCurrentLimits();
      setCurrentLimits(response.data);
      return response.data;
    } catch (err) {
      console.error('获取当前限制失败:', err);
    }
  }, []);

  const simulateRequest = useCallback(async (data: {
    type: number;
    loadReduction: number;
    durationMinutes: number;
    incentive: number;
  }) => {
    setLoading(true);
    setError(null);
    try {
      const response = await demandResponseApi.simulateRequest(data);
      await fetchActiveRequests();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '创建需求响应请求失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [fetchActiveRequests]);

  const executeRequest = useCallback(async (requestId: string) => {
    setIsExecuting(true);
    setError(null);
    try {
      const response = await demandResponseApi.executeRequest(requestId);
      await fetchActiveRequests();
      await fetchCurrentLimits();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '执行需求响应失败');
      throw err;
    } finally {
      setIsExecuting(false);
    }
  }, [fetchActiveRequests, fetchCurrentLimits]);

  const completeRequest = useCallback(async (requestId: string, satisfactionScore: number) => {
    setLoading(true);
    setError(null);
    try {
      const response = await demandResponseApi.completeRequest(requestId, satisfactionScore);
      await fetchActiveRequests();
      await fetchCurrentLimits();
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '完成需求响应失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, [fetchActiveRequests, fetchCurrentLimits]);

  const fetchExecutionLogs = useCallback(async (requestId: string) => {
    setLoading(true);
    setError(null);
    try {
      const response = await demandResponseApi.getLogs(requestId);
      setExecutionLogs(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取执行日志失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const recordExecutionLog = useCallback(async (
    requestId: string,
    baselineLoad: number,
    actualLoad: number
  ) => {
    try {
      const response = await demandResponseApi.getLogs(requestId);
      await fetchExecutionLogs(requestId);
      return response.data;
    } catch (err) {
      console.error('记录执行日志失败:', err);
      throw err;
    }
  }, [fetchExecutionLogs]);

  const getActiveRequestCount = useCallback(() => {
    return activeRequests.length;
  }, [activeRequests]);

  const getTotalRequestedReduction = useCallback(() => {
    return activeRequests.reduce((sum, r) => sum + r.requestedLoadReduction, 0);
  }, [activeRequests]);

  const getHighestPriority = useCallback(() => {
    if (activeRequests.length === 0) return null;
    return Math.min(...activeRequests.map(r => r.priority));
  }, [activeRequests]);

  useEffect(() => {
    fetchActiveRequests();
    fetchCurrentLimits();
  }, [fetchActiveRequests, fetchCurrentLimits]);

  return {
    activeRequests,
    currentLimits,
    executionLogs,
    isExecuting,
    loading,
    error,
    fetchActiveRequests,
    fetchCurrentLimits,
    simulateRequest,
    executeRequest,
    completeRequest,
    fetchExecutionLogs,
    recordExecutionLog,
    getActiveRequestCount,
    getTotalRequestedReduction,
    getHighestPriority,
  };
}
