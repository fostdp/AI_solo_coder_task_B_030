import { useState, useCallback, useEffect } from 'react';
import { iceStorageApi } from '@/services/api';
import type { IceStorageSchedule, IceStorageTank, DPStrategyRecord } from '@/types';

export function useIceStorage() {
  const [schedule, setSchedule] = useState<IceStorageSchedule[]>([]);
  const [tanks, setTanks] = useState<IceStorageTank[]>([]);
  const [isCalculating, setIsCalculating] = useState(false);
  const [lastStrategy, setLastStrategy] = useState<DPStrategyRecord | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchSchedule = useCallback(async (date: Date) => {
    setLoading(true);
    setError(null);
    try {
      const response = await iceStorageApi.getSchedule(date.toISOString());
      setSchedule(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取调度计划失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchTanks = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await iceStorageApi.getTanks();
      setTanks(response.data);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '获取蓄冰罐状态失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const calculateStrategy = useCallback(async (date: Date) => {
    setIsCalculating(true);
    setError(null);
    try {
      const response = await iceStorageApi.calculateStrategy(date.toISOString());
      setLastStrategy(response.data);
      await fetchSchedule(date);
      return response.data;
    } catch (err) {
      setError(err instanceof Error ? err.message : '计算优化策略失败');
      throw err;
    } finally {
      setIsCalculating(false);
    }
  }, [fetchSchedule]);

  const generateForecasts = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      await iceStorageApi.generateForecasts();
    } catch (err) {
      setError(err instanceof Error ? err.message : '生成负荷预测失败');
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  const getCurrentIceAmount = useCallback(() => {
    if (tanks.length === 0) return 0;
    return tanks.reduce((sum, tank) => sum + tank.currentIceAmount, 0);
  }, [tanks]);

  const getTotalCapacity = useCallback(() => {
    if (tanks.length === 0) return 0;
    return tanks.reduce((sum, tank) => sum + tank.maxIceCapacity, 0);
  }, [tanks]);

  const getIcePercentage = useCallback(() => {
    const current = getCurrentIceAmount();
    const total = getTotalCapacity();
    return total > 0 ? (current / total) * 100 : 0;
  }, [getCurrentIceAmount, getTotalCapacity]);

  const getTodaySaving = useCallback(() => {
    if (schedule.length === 0) return 0;
    return schedule.reduce((sum, s) => sum + (s.costSaving || 0), 0);
  }, [schedule]);

  useEffect(() => {
    fetchTanks();
  }, [fetchTanks]);

  return {
    schedule,
    tanks,
    isCalculating,
    lastStrategy,
    loading,
    error,
    fetchSchedule,
    fetchTanks,
    calculateStrategy,
    generateForecasts,
    getCurrentIceAmount,
    getTotalCapacity,
    getIcePercentage,
    getTodaySaving,
  };
}
