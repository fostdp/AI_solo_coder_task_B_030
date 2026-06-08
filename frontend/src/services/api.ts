import axios from 'axios';
import type {
  Device,
  DeviceRealtimeData,
  DeviceTrendData,
  SystemMetrics,
  EfficiencyRecord,
  Alarm,
  WorkOrder,
  OptimizationRecommendation,
  IceStorageTank,
  IceStorageSchedule,
  IceStorageOperation,
  ElectricityPriceTier,
  LoadForecast,
  DPStrategyRecord,
  DemandResponseRequest,
  DRExecutionLog,
  DRResponseSummary,
  DRLimits,
  FaultType,
  FaultDiagnosisResult,
  DiagnosisStatistic,
  Building,
  BuildingEfficiencyMetric,
  BenchmarkReport,
} from '@/types';

const api = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    console.error('API Error:', error);
    return Promise.reject(error);
  }
);

export const deviceApi = {
  getAll: () => api.get<Device[]>('/device'),
  getById: (id: string) => api.get<Device>(`/device/${id}`),
  getByType: (type: number) => api.get<Device[]>(`/device/type/${type}`),
  getRealtimeData: (id: string) => api.get<DeviceRealtimeData>(`/device/${id}/realtime`),
  getTrendData: (id: string, startTime?: string, endTime?: string) => {
    const params: Record<string, string> = {};
    if (startTime) params.startTime = startTime;
    if (endTime) params.endTime = endTime;
    return api.get<DeviceTrendData[]>(`/device/${id}/trend`, { params });
  },
  updateStatus: (id: string, status: number) => api.put(`/device/${id}/status/${status}`),
};

export const efficiencyApi = {
  getCurrentMetrics: () => api.get<SystemMetrics>('/efficiency/current'),
  getHistory: (startTime?: string, endTime?: string) => {
    const params: Record<string, string> = {};
    if (startTime) params.startTime = startTime;
    if (endTime) params.endTime = endTime;
    return api.get<EfficiencyRecord[]>('/efficiency/history', { params });
  },
  getToday: () => api.get<EfficiencyRecord[]>('/efficiency/today'),
  getReports: (page = 1, pageSize = 20) =>
    api.get('/efficiency/reports', { params: { page, pageSize } }),
  calculate: () => api.post('/efficiency/calculate'),
};

export const optimizationApi = {
  getCurrent: () => api.get<OptimizationRecommendation>('/optimization/current'),
  getHistory: (page = 1, pageSize = 20) =>
    api.get<OptimizationRecommendation[]>('/optimization/history', { params: { page, pageSize } }),
  generate: () => api.post<OptimizationRecommendation>('/optimization/generate'),
  train: () => api.post('/optimization/train'),
  apply: (recommendationId: number, appliedBy: string) =>
    api.post('/optimization/apply', { recommendationId, appliedBy }),
  reject: (id: number, appliedBy: string) =>
    api.post(`/optimization/${id}/reject`, { appliedBy }),
};

export const alarmApi = {
  getActive: () => api.get<Alarm[]>('/alarm/active'),
  getByLevel: (level: number) => api.get<Alarm[]>(`/alarm/level/${level}`),
  getHistory: (startTime?: string, endTime?: string, page = 1, pageSize = 50) => {
    const params: Record<string, unknown> = { page, pageSize };
    if (startTime) params.startTime = startTime;
    if (endTime) params.endTime = endTime;
    return api.get<Alarm[]>('/alarm/history', { params });
  },
  getById: (id: number) => api.get<Alarm>(`/alarm/${id}`),
  acknowledge: (id: number, acknowledgedBy: string) =>
    api.put(`/alarm/${id}/acknowledge`, { acknowledgedBy }),
  clear: (id: number, acknowledgedBy: string) =>
    api.put(`/alarm/${id}/clear`, { acknowledgedBy }),
  getThresholds: () => api.get('/alarm/thresholds'),
};

export const workOrderApi = {
  getAll: (status?: number, page = 1, pageSize = 20) => {
    const params: Record<string, unknown> = { page, pageSize };
    if (status !== undefined) params.status = status;
    return api.get<WorkOrder[]>('/workorder', { params });
  },
  getById: (id: number) => api.get<WorkOrder>(`/workorder/${id}`),
  getByAlarmId: (alarmId: number) => api.get<WorkOrder>(`/workorder/alarm/${alarmId}`),
  create: (data: Partial<WorkOrder>) => api.post<WorkOrder>('/workorder', data),
  assign: (id: number, processor: string) =>
    api.put(`/workorder/${id}/assign`, { processor }),
  start: (id: number, processor: string) =>
    api.put(`/workorder/${id}/start`, { processor }),
  complete: (id: number, processor: string, resolution: string) =>
    api.put(`/workorder/${id}/complete`, { processor, resolution }),
  close: (id: number, processor: string) =>
    api.put(`/workorder/${id}/close`, { processor }),
  getStats: () => api.get('/workorder/stats'),
};

export const systemApi = {
  getConfig: () => api.get('/system/config'),
  getHealth: () => api.get('/system/health'),
  getDashboardSummary: () => api.get('/system/dashboard/summary'),
  getDeviceCount: () => api.get('/system/device-count'),
  getBACnetStatus: () => api.get('/system/bacnet/status'),
  getDesignCOP: () => api.get('/system/design/cop'),
};

// ===== 冰蓄冷系统API =====
export const iceStorageApi = {
  getSchedule: (date: string) => api.get<IceStorageSchedule[]>('/icestorage/schedule', { params: { date } }),
  calculateStrategy: (date: string) => api.post<DPStrategyRecord>('/icestorage/calculate', null, { params: { date } }),
  getTanks: () => api.get<IceStorageTank[]>('/icestorage/tanks'),
  getTankById: (id: string) => api.get<IceStorageTank>(`/icestorage/tanks/${id}`),
  getElectricityPrices: () => api.get<ElectricityPriceTier[]>('/icestorage/electricity-prices'),
  getForecasts: (date: string) => api.get<LoadForecast[]>('/icestorage/forecasts', { params: { date } }),
  generateForecasts: () => api.post('/icestorage/forecasts/generate'),
  getOperations: (tankId: string) => api.get<IceStorageOperation[]>('/icestorage/operations', { params: { tankId } }),
};

// ===== 需求响应API =====
export const demandResponseApi = {
  getActive: () => api.get<DemandResponseRequest[]>('/demandresponse/active'),
  simulateRequest: (data: { type: number; loadReduction: number; durationMinutes: number; incentive: number }) =>
    api.post<DemandResponseRequest>('/demandresponse/simulate', data),
  executeRequest: (requestId: string) => api.post<DRResponseSummary>(`/demandresponse/${requestId}/execute`),
  completeRequest: (requestId: string, satisfactionScore: number) =>
    api.post<DRResponseSummary>(`/demandresponse/${requestId}/complete`, null, { params: { satisfactionScore } }),
  getLogs: (requestId: string) => api.get<DRExecutionLog[]>(`/demandresponse/${requestId}/logs`),
  getHistory: (page = 1, pageSize = 20) =>
    api.get<DemandResponseRequest[]>('/demandresponse/history', { params: { page, pageSize } }),
  getCurrentLimits: () => api.get<DRLimits>('/demandresponse/current-limits'),
};

// ===== 故障诊断API =====
export const faultDiagnosisApi = {
  diagnoseDevice: (deviceId: string) => api.post<FaultDiagnosisResult[]>(`/faultdiagnosis/diagnose/${deviceId}`),
  diagnoseAll: () => api.post<FaultDiagnosisResult[]>('/faultdiagnosis/diagnose-all'),
  getResults: (deviceId?: string, page = 1, pageSize = 20) => {
    const params: Record<string, unknown> = { page, pageSize };
    if (deviceId) params.deviceId = deviceId;
    return api.get<FaultDiagnosisResult[]>('/faultdiagnosis/results', { params });
  },
  getResultById: (id: number) => api.get<FaultDiagnosisResult>(`/faultdiagnosis/results/${id}`),
  confirmResult: (id: number, confirmedBy: string) =>
    api.put(`/faultdiagnosis/results/${id}/confirm`, null, { params: { confirmedBy } }),
  getFaultTypes: (deviceTypeId: number) => api.get<FaultType[]>('/faultdiagnosis/fault-types', { params: { deviceTypeId } }),
  getStatistics: (date: string) => api.get<DiagnosisStatistic>('/faultdiagnosis/statistics', { params: { date } }),
};

// ===== 多楼宇对标API =====
export const buildingBenchmarkApi = {
  getBuildings: () => api.get<Building[]>('/buildingbenchmark/buildings'),
  addBuilding: (data: Partial<Building>) => api.post<Building>('/buildingbenchmark/buildings', data),
  updateBuilding: (id: string, data: Partial<Building>) =>
    api.put<Building>(`/buildingbenchmark/buildings/${id}`, data),
  deleteBuilding: (id: string) => api.delete(`/buildingbenchmark/buildings/${id}`),
  getBuildingMetrics: (buildingId: string, period: number, startDate: string) =>
    api.get<BuildingEfficiencyMetric[]>(`/buildingbenchmark/buildings/${buildingId}/metrics`, {
      params: { period, startDate },
    }),
  calculateMetrics: (buildingId: string, startDate: string, endDate: string) =>
    api.post<BuildingEfficiencyMetric[]>('/buildingbenchmark/calculate-metrics', null, {
      params: { buildingId, startDate, endDate },
    }),
  generateReport: (data: { buildingIds: string[]; reportName: string; startDate: string; endDate: string }) =>
    api.post<BenchmarkReport>('/buildingbenchmark/reports', data),
  getReports: (page = 1, pageSize = 20) =>
    api.get<BenchmarkReport[]>('/buildingbenchmark/reports', { params: { page, pageSize } }),
  getRadarData: (buildingIds: string[], period: number, date: string) =>
    api.get<Record<string, Record<string, number>>>('/buildingbenchmark/radar-data', {
      params: { buildingIds: buildingIds.join(','), period, date },
    }),
};

export default api;
