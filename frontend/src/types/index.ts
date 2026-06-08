export interface Device {
  id: string;
  name: string;
  deviceType: number;
  deviceTypeName: string;
  designCOP: number;
  ratedPower: number;
  status: number;
  efficiencyStatus: number;
  currentCOP?: number;
  positionX: number;
  positionY: number;
  operatingHours: number;
}

export interface DeviceRealtimeData {
  deviceId: string;
  timestamp: string;
  power: number;
  supplyTemperature: number;
  returnTemperature: number;
  pressure: number;
  flowRate: number;
  frequency?: number;
  current?: number;
  voltage?: number;
  inletTemperature?: number;
  outletTemperature?: number;
  fanSpeed?: number;
}

export interface TrendDataPoint {
  timestamp: string;
  value: number;
}

export interface DeviceTrendData {
  deviceId: string;
  parameterName: string;
  dataPoints: TrendDataPoint[];
}

export interface SystemMetrics {
  timestamp: string;
  dailyEnergy: number;
  realtimeCOP: number;
  energySaving: number;
  peakPower: number;
  runningDeviceCount: number;
  totalDeviceCount: number;
}

export interface EfficiencyRecord {
  timestamp: string;
  systemCOP: number;
  designCOP: number;
  designCOPRatio: number;
  totalPower: number;
  totalCoolingCapacity: number;
  chilledWaterSupplyTemp: number;
  chilledWaterReturnTemp: number;
  coolingWaterSupplyTemp: number;
  coolingWaterReturnTemp: number;
  loadRate: number;
  dailyEnergyConsumption: number;
  energySaving: number;
}

export interface Alarm {
  id: number;
  deviceId?: string;
  deviceName?: string;
  alarmLevel: number;
  alarmType: number;
  message: string;
  startTime: string;
  endTime?: string;
  status: number;
  durationMinutes: number;
  parameterName?: string;
  parameterValue?: number;
  thresholdValue?: number;
}

export interface WorkOrder {
  id: number;
  workOrderNo: string;
  alarmId?: number;
  title: string;
  description: string;
  assignee?: string;
  status: number;
  priority: number;
  createdAt: string;
  completedAt?: string;
  completedBy?: string;
  resolution?: string;
}

export interface OptimizationRecommendation {
  id: number;
  generatedAt: string;
  deviceCombination: string;
  runningChillers: string;
  runningPumps: string;
  runningTowers: string;
  predictedCOP: number;
  predictedPower: number;
  chilledWaterSetpoint: number;
  expectedEnergySaving: number;
  expectedSavingPercent: number;
  loadRate: number;
  status: number;
}

export interface Pipeline {
  id: string;
  from: { x: number; y: number };
  to: { x: number; y: number };
  type: 'chilled' | 'cooling';
  flowDirection: 1 | -1;
}

export const DeviceType = {
  CentrifugalChiller: 1,
  ScrewChiller: 2,
  CoolingTower: 3,
  ChilledWaterPump: 4,
  CoolingWaterPump: 5,
};

export const DeviceStatus = {
  Standby: 0,
  Running: 1,
  Fault: 2,
  Maintenance: 3,
};

export const EfficiencyStatus = {
  High: 1,
  Medium: 2,
  Low: 3,
  Fault: 4,
};

export const AlarmLevel = {
  Level1: 1,
  Level2: 2,
};

export const AlarmStatus = {
  Active: 1,
  Acknowledged: 2,
  WorkOrderGenerated: 3,
  Resolved: 4,
  Cleared: 5,
};

export const WorkOrderStatus = {
  Created: 1,
  Assigned: 2,
  InProgress: 3,
  Completed: 4,
  Closed: 5,
};

export const RecommendationStatus = {
  Pending: 1,
  Applied: 2,
  Rejected: 3,
};

// ===== 冰蓄冷系统 =====
export interface IceStorageTank {
  id: string;
  device?: Device;
  maxIceCapacity: number;
  currentIceAmount: number;
  iceMakingRate: number;
  iceMeltingRateMax: number;
  currentMeltingRate: number;
  iceMakingCOP: number;
  iceMeltingEfficiency: number;
  currentMode: number;
  tankTemperature?: number;
  brineConcentration?: number;
}

export interface IceStorageSchedule {
  id: number;
  scheduleDate: string;
  hourOfDay: number;
  mode: number;
  iceTankId?: string;
  targetIceAmount: number;
  targetMeltingRate: number;
  chillerLoadRatio: number;
  iceLoadRatio: number;
  expectedCost: number;
  baselineCost: number;
  costSaving: number;
  isOptimized: boolean;
  createdAt: string;
}

export interface IceStorageOperation {
  id: number;
  iceTankId: string;
  timestamp: string;
  mode: number;
  iceAmountBefore: number;
  iceAmountAfter: number;
  iceAmountDelta: number;
  meltingRate: number;
  powerConsumption: number;
  coolingProvided: number;
  electricityPrice: number;
  cost: number;
}

export interface ElectricityPriceTier {
  id: number;
  tierName: string;
  pricePerKWh: number;
  startHour: number;
  endHour: number;
  color: string;
  isActive: boolean;
}

export interface LoadForecast {
  id: number;
  forecastDate: string;
  hourOfDay: number;
  predictedLoad: number;
  actualLoad?: number;
  predictionModel?: string;
  confidence?: number;
  createdAt: string;
}

export interface DPStrategyRecord {
  id: number;
  scheduleDate: string;
  totalStatesEvaluated: number;
  optimalCost: number;
  baselineCost: number;
  totalSaving: number;
  computationTimeMs: number;
  algorithm: string;
  createdAt: string;
}

// ===== 需求响应 =====
export interface DemandResponseRequest {
  id: string;
  requestType: number;
  status: number;
  sourcePlatform: string;
  requestedLoadReduction: number;
  startTime: string;
  endTime: string;
  incentivePerKWh: number;
  maxChillerOutputLimit?: number;
  minIceMeltingRate?: number;
  priority: number;
  receivedAt: string;
  responseRequired: boolean;
}

export interface DRExecutionLog {
  id: number;
  drRequestId: string;
  timestamp: string;
  baselineLoad: number;
  actualLoad: number;
  achievedReduction: number;
  targetReduction: number;
  chillerOutputLimit: number;
  iceMeltingRateApplied: number;
  electricitySaved: number;
  incentiveEarned: number;
  costSaving: number;
  totalBenefit: number;
}

export interface DRResponseSummary {
  id: number;
  drRequestId: string;
  totalDurationMinutes: number;
  totalRequestedReduction: number;
  totalActualReduction: number;
  averageComplianceRate: number;
  totalElectricitySaved: number;
  totalIncentiveEarned: number;
  totalCostSaving: number;
  totalBenefit: number;
  userSatisfactionScore?: number;
  completedAt?: string;
}

export interface DRLimits {
  chillerOutputLimit: number;
  iceMeltingRate: number;
}

// ===== 故障诊断 =====
export interface FaultType {
  id: number;
  faultCode: string;
  faultName: string;
  deviceTypeId: number;
  severity: number;
  description?: string;
  typicalCauses?: string;
  typicalSolution?: string;
  estimatedRepairHours?: number;
  symptomParameters?: FaultSymptomParameter[];
}

export interface FaultSymptomParameter {
  id: number;
  faultTypeId: number;
  parameterName: string;
  deviationType: string;
  thresholdValue: number;
  deviationWeight: number;
  priorProbability: number;
  conditionalProbability: number;
}

export interface FaultDiagnosisResult {
  id: number;
  deviceId: string;
  deviceName?: string;
  faultTypeId: number;
  faultType?: FaultType;
  timestamp: string;
  confidence: number;
  bayesProbability: number;
  matchingSymptoms?: string;
  deviationDetails?: string;
  maintenanceRecommendation?: string;
  estimatedDowntimeHours?: number;
  estimatedRepairCost?: number;
  isConfirmed: boolean;
  confirmedBy?: string;
  confirmedAt?: string;
}

export interface DiagnosisStatistic {
  id: number;
  statisticsDate: string;
  totalDiagnosisCount: number;
  confirmedFaultCount: number;
  falsePositiveCount: number;
  averageConfidence: number;
  topFaultTypes?: string;
  accuracyRate?: number;
}

// ===== 多楼宇对标 =====
export interface Building {
  id: string;
  buildingName: string;
  buildingType: number;
  address?: string;
  grossFloorArea: number;
  coolingArea: number;
  numberOfFloors?: number;
  yearBuilt?: number;
  designCoolingLoad: number;
  peakCoolingLoad?: number;
  contactPerson?: string;
  contactPhone?: string;
  status: number;
  createdAt: string;
  updatedAt: string;
}

export interface BuildingEfficiencyMetric {
  id: number;
  buildingId: string;
  buildingName?: string;
  statisticsDate: string;
  statisticsPeriod: string;
  eer?: number;
  cop?: number;
  energyPerUnitArea?: number;
  coolingPerUnitArea?: number;
  pue?: number;
  loadFactor?: number;
  totalElectricityConsumption?: number;
  totalCoolingCapacity?: number;
  peakDemand?: number;
  operatingHours?: number;
  totalCost?: number;
  costPerUnitArea?: number;
  costPerCooling?: number;
  outdoorAvgTemp?: number;
  hdd?: number;
  cdd?: number;
  createdAt: string;
}

export interface BenchmarkReport {
  id: number;
  reportName: string;
  reportPeriod: string;
  startDate: string;
  endDate: string;
  buildingIds: string;
  copRanking?: string;
  energyPerAreaRanking?: string;
  costPerAreaRanking?: string;
  loadFactorRanking?: string;
  eerRanking?: string;
  bestPractices?: string;
  improvementSuggestions?: string;
  overallScore?: number;
  generatedBy?: string;
  createdAt: string;
}

export interface RadarChartData {
  buildingId: string;
  buildingName?: string;
  indicators: {
    COP: number;
    EER: number;
    EnergyPerUnitArea: number;
    LoadFactor: number;
    CostPerUnitArea: number;
    PUE: number;
  };
}

export const IceStorageMode = {
  IceMaking: 1,
  IceMelting: 2,
  ChillerOnly: 3,
  Combined: 4,
  Standby: 5,
};

export const DRRequestType = {
  LoadReduction: 1,
  LoadShifting: 2,
  PeakShaving: 3,
  EmergencyDR: 4,
};

export const DRRequestStatus = {
  Received: 1,
  Executing: 2,
  Completed: 3,
  Cancelled: 4,
};

export const FaultSeverity = {
  Minor: 1,
  Moderate: 2,
  Severe: 3,
};

export const BuildingType = {
  Office: 1,
  Mall: 2,
  Hotel: 3,
  Complex: 4,
  Hospital: 5,
  School: 6,
};

export const StatisticsPeriod = {
  Daily: 1,
  Weekly: 2,
  Monthly: 3,
  Yearly: 4,
};
