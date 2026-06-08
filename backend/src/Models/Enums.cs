namespace ChillerPlantOptimization.Models;

public enum DeviceType
{
    CentrifugalChiller = 1,
    ScrewChiller = 2,
    CoolingTower = 3,
    ChilledWaterPump = 4,
    CoolingWaterPump = 5,
    IceStorageTank = 6,
    IceMakingChiller = 7
}

public enum IceStorageMode
{
    IceMaking = 1,
    IceMelting = 2,
    ChillerOnly = 3,
    Combined = 4,
    Standby = 5
}

public enum ElectricityPriceTier
{
    Valley = 1,
    Flat = 2,
    Peak = 3,
    CriticalPeak = 4
}

public enum DRRequestType
{
    LoadReduction = 1,
    LoadShifting = 2,
    PeakShaving = 3,
    EmergencyDR = 4
}

public enum DRRequestStatus
{
    Received = 1,
    Executing = 2,
    Completed = 3,
    Cancelled = 4
}

public enum FaultSeverity
{
    Minor = 1,
    Moderate = 2,
    Severe = 3
}

public enum DeviationType
{
    High = 1,
    Low = 2,
    Pattern = 3
}

public enum BayesianNodeType
{
    Fault = 1,
    Symptom = 2
}

public enum BuildingType
{
    Office = 1,
    Mall = 2,
    Hotel = 3,
    Complex = 4,
    Hospital = 5,
    School = 6
}

public enum StatisticsPeriod
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}

public enum DeviceStatus
{
    Stopped = 0,
    Running = 1,
    Fault = 2,
    Standby = 3
}

public enum EfficiencyStatus
{
    High = 0,
    Normal = 1,
    Low = 2,
    Fault = 3
}

public enum AlarmLevel
{
    Level1 = 1,
    Level2 = 2
}

public enum AlarmType
{
    ParameterExceedance = 1,
    LowEfficiency = 2,
    SystemFault = 3,
    CommunicationError = 4
}

public enum AlarmStatus
{
    Active = 0,
    Acknowledged = 1,
    Resolved = 2,
    Cleared = 3
}

public enum WorkOrderStatus
{
    Pending = 0,
    Assigned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

public enum RecommendationStatus
{
    New = 0,
    Applied = 1,
    Rejected = 2,
    Expired = 3
}

public enum UserRole
{
    Administrator = 0,
    Engineer = 1,
    Manager = 2
}
