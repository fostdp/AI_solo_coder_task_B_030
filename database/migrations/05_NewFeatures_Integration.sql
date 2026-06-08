-- =============================================
-- 功能迭代：冰蓄冷、需求响应、故障诊断、多楼宇对标
-- 数据库迁移脚本 v2.0
-- =============================================

USE ChillerPlantOptimization;
GO

-- =============================================
-- 一、冰蓄冷系统 (Ice Storage System)
-- =============================================

-- 1.1 设备类型扩展：添加冰蓄冷设备类型
IF NOT EXISTS (SELECT * FROM DeviceTypes WHERE Id = 6)
BEGIN
    INSERT INTO DeviceTypes (Id, Name, Description) VALUES
    (6, 'IceStorageTank', '冰蓄冷装置'),
    (7, 'IceMakingChiller', '制冰专用冷水机组');
END
GO

-- 1.2 蓄冰模式枚举表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IceStorageModes')
BEGIN
    CREATE TABLE IceStorageModes (
        Id INT PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL,
        Description NVARCHAR(200) NULL
    );
    
    INSERT INTO IceStorageModes (Id, Name, Description) VALUES
    (1, 'IceMaking', '蓄冰模式（夜间低谷电价）'),
    (2, 'IceMelting', '融冰供冷模式（白天高峰电价）'),
    (3, 'ChillerOnly', '主机单独供冷'),
    (4, 'Combined', '主机+融冰联合供冷'),
    (5, 'Standby', '待机');
END
GO

-- 1.3 冰蓄冷装置表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IceStorageTanks')
BEGIN
    CREATE TABLE IceStorageTanks (
        Id NVARCHAR(50) PRIMARY KEY FOREIGN KEY REFERENCES Devices(Id),
        MaxIceCapacity DECIMAL(18,4) NOT NULL,        -- 最大蓄冰量 (kWh)
        CurrentIceAmount DECIMAL(18,4) NOT NULL DEFAULT 0, -- 当前蓄冰量 (kWh)
        IceMakingRate DECIMAL(18,4) NOT NULL,         -- 制冰速率 (kW)
        IceMeltingRateMax DECIMAL(18,4) NOT NULL,     -- 最大融冰速率 (kW)
        CurrentMeltingRate DECIMAL(18,4) NOT NULL DEFAULT 0,
        IceMakingCOP DECIMAL(18,4) NOT NULL,          -- 制冰COP
        IceMeltingEfficiency DECIMAL(18,4) NOT NULL,  -- 融冰效率
        CurrentMode INT NOT NULL DEFAULT 5 FOREIGN KEY REFERENCES IceStorageModes(Id),
        TankTemperature DECIMAL(18,4) NULL,
        BrineConcentration DECIMAL(18,4) NULL
    );
END
GO

-- 1.4 峰谷电价时段表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectricityPriceTiers')
BEGIN
    CREATE TABLE ElectricityPriceTiers (
        Id INT PRIMARY KEY IDENTITY(1,1),
        TierName NVARCHAR(50) NOT NULL,               -- 尖峰/高峰/平段/低谷
        PricePerKWh DECIMAL(18,4) NOT NULL,           -- 电价 (元/kWh)
        StartHour INT NOT NULL,
        EndHour INT NOT NULL,
        Color NVARCHAR(20) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );
    
    INSERT INTO ElectricityPriceTiers (TierName, PricePerKWh, StartHour, EndHour, Color) VALUES
    ('尖峰', 1.6800, 10, 12, '#DC2626'),
    ('尖峰', 1.6800, 19, 21, '#DC2626'),
    ('高峰', 1.2600, 8, 10, '#F97316'),
    ('高峰', 1.2600, 12, 14, '#F97316'),
    ('高峰', 1.2600, 17, 19, '#F97316'),
    ('高峰', 1.2600, 21, 23, '#F97316'),
    ('平段', 0.8400, 6, 8, '#EAB308'),
    ('平段', 0.8400, 14, 17, '#EAB308'),
    ('平段', 0.8400, 23, 24, '#EAB308'),
    ('低谷', 0.4200, 0, 6, '#22C55E');
END
GO

-- 1.5 次日负荷预测表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoadForecasts')
BEGIN
    CREATE TABLE LoadForecasts (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ForecastDate DATE NOT NULL,
        HourOfDay INT NOT NULL,
        PredictedLoad DECIMAL(18,4) NOT NULL,         -- 预测负荷 (kW)
        ActualLoad DECIMAL(18,4) NULL,                -- 实际负荷
        PredictionModel NVARCHAR(50) NULL,
        Confidence DECIMAL(18,4) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UNIQUE (ForecastDate, HourOfDay)
    );
    
    CREATE INDEX IX_LoadForecasts_ForecastDate ON LoadForecasts (ForecastDate);
END
GO

-- 1.6 蓄融冰策略计划表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IceStorageSchedules')
BEGIN
    CREATE TABLE IceStorageSchedules (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ScheduleDate DATE NOT NULL,
        HourOfDay INT NOT NULL,
        Mode INT NOT NULL FOREIGN KEY REFERENCES IceStorageModes(Id),
        IceTankId NVARCHAR(50) NULL FOREIGN KEY REFERENCES IceStorageTanks(Id),
        TargetIceAmount DECIMAL(18,4) NOT NULL,       -- 目标蓄冰量 (kWh)
        TargetMeltingRate DECIMAL(18,4) NOT NULL,     -- 目标融冰速率 (kW)
        ChillerLoadRatio DECIMAL(18,4) NOT NULL,      -- 主机承担负荷比例
        IceLoadRatio DECIMAL(18,4) NOT NULL,          -- 融冰承担负荷比例
        ExpectedCost DECIMAL(18,4) NOT NULL,          -- 预期电费 (元)
        BaselineCost DECIMAL(18,4) NOT NULL,          -- 基准电费 (元)
        CostSaving DECIMAL(18,4) NOT NULL,            -- 预计节省 (元)
        IsOptimized BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UNIQUE (ScheduleDate, HourOfDay)
    );
    
    CREATE INDEX IX_IceStorageSchedules_Date ON IceStorageSchedules (ScheduleDate);
END
GO

-- 1.7 冰蓄冷运行记录表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IceStorageOperations')
BEGIN
    CREATE TABLE IceStorageOperations (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        IceTankId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES IceStorageTanks(Id),
        Timestamp DATETIME2 NOT NULL,
        Mode INT NOT NULL FOREIGN KEY REFERENCES IceStorageModes(Id),
        IceAmountBefore DECIMAL(18,4) NOT NULL,
        IceAmountAfter DECIMAL(18,4) NOT NULL,
        IceAmountDelta DECIMAL(18,4) NOT NULL,        -- 正为蓄冰，负为融冰
        MeltingRate DECIMAL(18,4) NOT NULL,
        PowerConsumption DECIMAL(18,4) NOT NULL,
        CoolingProvided DECIMAL(18,4) NOT NULL,
        ElectricityPrice DECIMAL(18,4) NOT NULL,
        Cost DECIMAL(18,4) NOT NULL
    );
    
    CREATE CLUSTERED INDEX IX_IceStorageOperations_Timestamp ON IceStorageOperations (Timestamp DESC);
    CREATE INDEX IX_IceStorageOperations_Tank ON IceStorageOperations (IceTankId, Timestamp DESC);
END
GO

-- 1.8 动态规划策略计算记录表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DPStrategyRecords')
BEGIN
    CREATE TABLE DPStrategyRecords (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ScheduleDate DATE NOT NULL UNIQUE,
        TotalStatesEvaluated INT NOT NULL,
        OptimalCost DECIMAL(18,4) NOT NULL,
        BaselineCost DECIMAL(18,4) NOT NULL,
        TotalSaving DECIMAL(18,4) NOT NULL,
        ComputationTimeMs INT NOT NULL,
        Algorithm NVARCHAR(50) NOT NULL DEFAULT 'DynamicProgramming',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

-- =============================================
-- 二、需求响应联动 (Demand Response)
-- =============================================

-- 2.1 需求响应事件类型表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DRRequestTypes')
BEGIN
    CREATE TABLE DRRequestTypes (
        Id INT PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL,
        Description NVARCHAR(200) NULL
    );
    
    INSERT INTO DRRequestTypes (Id, Name, Description) VALUES
    (1, 'LoadReduction', '削峰减载'),
    (2, 'LoadShifting', '负荷转移'),
    (3, 'PeakShaving', '削峰填谷'),
    (4, 'EmergencyDR', '紧急需求响应');
END
GO

-- 2.2 需求响应状态表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DRRequestStatuses')
BEGIN
    CREATE TABLE DRRequestStatuses (
        Id INT PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL
    );
    
    INSERT INTO DRRequestStatuses (Id, Name) VALUES
    (1, 'Received'),
    (2, 'Executing'),
    (3, 'Completed'),
    (4, 'Cancelled');
END
GO

-- 2.3 需求响应指令表（模拟电力平台下发）
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DemandResponseRequests')
BEGIN
    CREATE TABLE DemandResponseRequests (
        Id NVARCHAR(50) PRIMARY KEY,
        RequestType INT NOT NULL FOREIGN KEY REFERENCES DRRequestTypes(Id),
        Status INT NOT NULL DEFAULT 1 FOREIGN KEY REFERENCES DRRequestStatuses(Id),
        SourcePlatform NVARCHAR(100) NOT NULL DEFAULT 'Simulation',
        RequestedLoadReduction DECIMAL(18,4) NOT NULL,  -- 请求减载量 (kW)
        StartTime DATETIME2 NOT NULL,
        EndTime DATETIME2 NOT NULL,
        IncentivePerKWh DECIMAL(18,4) NOT NULL,        -- 补贴电价 (元/kWh)
        MaxChillerOutputLimit DECIMAL(18,4) NULL,      -- 主机出力上限比例 (0~1)
        MinIceMeltingRate DECIMAL(18,4) NULL,          -- 最小融冰速率 (kW)
        Priority INT NOT NULL DEFAULT 1,
        ReceivedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ResponseRequired BIT NOT NULL DEFAULT 1
    );
    
    CREATE INDEX IX_DRRequests_Time ON DemandResponseRequests (StartTime DESC);
    CREATE INDEX IX_DRRequests_Status ON DemandResponseRequests (Status);
END
GO

-- 2.4 需求响应执行记录表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DRExecutionLogs')
BEGIN
    CREATE TABLE DRExecutionLogs (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DRRequestId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES DemandResponseRequests(Id),
        Timestamp DATETIME2 NOT NULL,
        BaselineLoad DECIMAL(18,4) NOT NULL,           -- 基准负荷 (kW)
        ActualLoad DECIMAL(18,4) NOT NULL,             -- 实际负荷 (kW)
        AchievedReduction DECIMAL(18,4) NOT NULL,      -- 实际减载量 (kW)
        TargetReduction DECIMAL(18,4) NOT NULL,        -- 目标减载量 (kW)
        ChillerOutputLimit DECIMAL(18,4) NOT NULL,     -- 主机出力上限
        IceMeltingRateApplied DECIMAL(18,4) NOT NULL,  -- 应用的融冰速率
        ElectricitySaved DECIMAL(18,4) NOT NULL,       -- 节省电量 (kWh)
        IncentiveEarned DECIMAL(18,4) NOT NULL,        -- 获得补贴 (元)
        CostSaving DECIMAL(18,4) NOT NULL,             -- 电费节省 (元)
        TotalBenefit DECIMAL(18,4) NOT NULL            -- 总收益
    );
    
    CREATE CLUSTERED INDEX IX_DRExecution_Timestamp ON DRExecutionLogs (Timestamp DESC);
    CREATE INDEX IX_DRExecution_Request ON DRExecutionLogs (DRRequestId);
END
GO

-- 2.5 需求响应效果汇总表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DRResponseSummaries')
BEGIN
    CREATE TABLE DRResponseSummaries (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DRRequestId NVARCHAR(50) NOT NULL UNIQUE FOREIGN KEY REFERENCES DemandResponseRequests(Id),
        TotalDurationMinutes INT NOT NULL,
        TotalRequestedReduction DECIMAL(18,4) NOT NULL,
        TotalActualReduction DECIMAL(18,4) NOT NULL,
        AverageComplianceRate DECIMAL(18,4) NOT NULL,   -- 平均响应达标率
        TotalElectricitySaved DECIMAL(18,4) NOT NULL,
        TotalIncentiveEarned DECIMAL(18,4) NOT NULL,
        TotalCostSaving DECIMAL(18,4) NOT NULL,
        TotalBenefit DECIMAL(18,4) NOT NULL,
        UserSatisfactionScore INT NULL,
        CompletedAt DATETIME2 NULL
    );
END
GO

-- =============================================
-- 三、故障诊断专家系统 (Fault Diagnosis)
-- =============================================

-- 3.1 故障类型表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FaultTypes')
BEGIN
    CREATE TABLE FaultTypes (
        Id INT PRIMARY KEY IDENTITY(1,1),
        FaultCode NVARCHAR(50) NOT NULL UNIQUE,
        FaultName NVARCHAR(100) NOT NULL,
        DeviceTypeId INT NOT NULL FOREIGN KEY REFERENCES DeviceTypes(Id),
        Severity INT NOT NULL,                           -- 1:轻微, 2:中等, 3:严重
        Description NVARCHAR(500) NULL,
        TypicalCauses NVARCHAR(500) NULL,
        TypicalSolution NVARCHAR(1000) NULL,
        EstimatedRepairHours DECIMAL(18,2) NULL
    );
    
    INSERT INTO FaultTypes (FaultCode, FaultName, DeviceTypeId, Severity, Description, TypicalCauses, TypicalSolution, EstimatedRepairHours) VALUES
    ('F-C-001', '冷凝器结垢', 1, 2, '冷凝器换热管表面结垢导致换热效率下降', '冷却水水质不良、长期未清洗、水处理不当', '化学清洗除垢、改善水质处理、定期清洗', 8.0),
    ('F-C-002', '蒸发器制冷剂泄漏', 1, 3, '蒸发器内制冷剂不足导致制冷能力下降', '焊缝泄漏、法兰密封不良、腐蚀穿孔', '检漏补焊、抽真空、充注制冷剂', 6.0),
    ('F-C-003', '压缩机吸气压力过低', 1, 2, '压缩机吸气压力低于正常范围', '制冷剂不足、蒸发器结霜、膨胀阀故障', '检查制冷剂充注量、除霜、检修膨胀阀', 4.0),
    ('F-C-004', '压缩机排气温度过高', 1, 3, '压缩机排气温度超过安全值', '冷凝器散热不良、制冷剂不足、压缩机故障', '清洗冷凝器、补充制冷剂、检修压缩机', 6.0),
    ('F-C-005', '油压过低', 1, 2, '压缩机润滑油压力不足', '油位过低、油泵故障、油过滤器堵塞', '补加润滑油、检修油泵、更换油过滤器', 3.0),
    ('F-T-001', '冷却塔风机故障', 3, 2, '冷却塔风机不转或转速不足', '电机烧毁、皮带断裂、轴承损坏', '更换电机、更换皮带、更换轴承', 4.0),
    ('F-T-002', '冷却塔填料堵塞', 3, 1, '冷却塔填料结垢堵塞影响散热', '水质差、藻类滋生、灰尘积累', '清洗或更换填料、改善水质处理', 6.0),
    ('F-P-001', '水泵气蚀', 4, 2, '水泵吸入端压力过低产生气泡', '吸入管阻力过大、液位过低、水温过高', '检查吸入管路、提高液位、检查水温', 3.0),
    ('F-P-002', '水泵轴承过热', 4, 2, '水泵轴承温度超过正常范围', '润滑不良、轴承磨损、轴不对中', '更换润滑油、更换轴承、找正', 3.0),
    ('F-P-003', '水泵流量不足', 4, 1, '水泵出水量低于额定值', '叶轮磨损、入口堵塞、转速不足', '更换叶轮、清理入口、检查电机', 4.0);
END
GO

-- 3.2 故障症状参数表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FaultSymptomParameters')
BEGIN
    CREATE TABLE FaultSymptomParameters (
        Id INT PRIMARY KEY IDENTITY(1,1),
        FaultTypeId INT NOT NULL FOREIGN KEY REFERENCES FaultTypes(Id),
        ParameterName NVARCHAR(100) NOT NULL,            -- 参数名称
        DeviationType NVARCHAR(20) NOT NULL,             -- High/Low/Pattern
        ThresholdValue DECIMAL(18,4) NOT NULL,           -- 阈值
        DeviationWeight DECIMAL(18,4) NOT NULL,          -- 权重 (0~1)
        PriorProbability DECIMAL(18,4) NOT NULL,         -- 先验概率
        ConditionalProbability DECIMAL(18,4) NOT NULL    -- 条件概率 P(症状|故障)
    );
    
    CREATE INDEX IX_FaultSymptoms_Fault ON FaultSymptomParameters (FaultTypeId);
END
GO

-- 3.3 贝叶斯网络节点表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BayesianNetworkNodes')
BEGIN
    CREATE TABLE BayesianNetworkNodes (
        Id INT PRIMARY KEY IDENTITY(1,1),
        NodeName NVARCHAR(50) NOT NULL,
        NodeType NVARCHAR(20) NOT NULL,                   -- Fault/Symptom
        DeviceTypeId INT NULL FOREIGN KEY REFERENCES DeviceTypes(Id),
        Description NVARCHAR(200) NULL,
        ParentNodes NVARCHAR(200) NULL                    -- 父节点列表，逗号分隔
    );
END
GO

-- 3.4 诊断结果表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FaultDiagnosisResults')
BEGIN
    CREATE TABLE FaultDiagnosisResults (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DeviceId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES Devices(Id),
        FaultTypeId INT NOT NULL FOREIGN KEY REFERENCES FaultTypes(Id),
        Timestamp DATETIME2 NOT NULL,
        Confidence DECIMAL(18,4) NOT NULL,                -- 置信度 (0~1)
        BayesProbability DECIMAL(18,4) NOT NULL,          -- 贝叶斯后验概率
        MatchingSymptoms NVARCHAR(500) NULL,              -- 匹配的症状列表
        DeviationDetails NVARCHAR(1000) NULL,             -- 参数偏离详情
        MaintenanceRecommendation NVARCHAR(1000) NULL,    -- 检修建议
        EstimatedDowntimeHours DECIMAL(18,2) NULL,
        EstimatedRepairCost DECIMAL(18,2) NULL,
        IsConfirmed BIT NOT NULL DEFAULT 0,
        ConfirmedBy NVARCHAR(50) NULL,
        ConfirmedAt DATETIME2 NULL
    );
    
    CREATE CLUSTERED INDEX IX_Diagnosis_Timestamp ON FaultDiagnosisResults (Timestamp DESC);
    CREATE INDEX IX_Diagnosis_Device ON FaultDiagnosisResults (DeviceId, Timestamp DESC);
    CREATE INDEX IX_Diagnosis_Confidence ON FaultDiagnosisResults (Confidence DESC);
END
GO

-- 3.5 诊断历史统计表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DiagnosisStatistics')
BEGIN
    CREATE TABLE DiagnosisStatistics (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        StatisticsDate DATE NOT NULL UNIQUE,
        TotalDiagnosisCount INT NOT NULL,
        ConfirmedFaultCount INT NOT NULL,
        FalsePositiveCount INT NOT NULL,
        AverageConfidence DECIMAL(18,4) NOT NULL,
        TopFaultTypes NVARCHAR(500) NULL,
        AccuracyRate DECIMAL(18,4) NULL
    );
END
GO

-- =============================================
-- 四、多楼宇能效对标 (Multi-Building Benchmarking)
-- =============================================

-- 4.1 楼宇信息表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Buildings')
BEGIN
    CREATE TABLE Buildings (
        Id NVARCHAR(50) PRIMARY KEY,
        BuildingName NVARCHAR(100) NOT NULL,
        BuildingType NVARCHAR(50) NOT NULL,               -- 写字楼/商场/酒店/综合体
        Address NVARCHAR(200) NULL,
        GrossFloorArea DECIMAL(18,4) NOT NULL,            -- 总建筑面积 (m²)
        CoolingArea DECIMAL(18,4) NOT NULL,               -- 空调面积 (m²)
        NumberOfFloors INT NULL,
        YearBuilt INT NULL,
        DesignCoolingLoad DECIMAL(18,4) NOT NULL,         -- 设计冷负荷 (kW)
        PeakCoolingLoad DECIMAL(18,4) NULL,               -- 峰值冷负荷 (kW)
        ContactPerson NVARCHAR(50) NULL,
        ContactPhone NVARCHAR(20) NULL,
        Status INT NOT NULL DEFAULT 1,                     -- 0:停用, 1:启用
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

-- 4.2 楼宇能效指标表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BuildingEfficiencyMetrics')
BEGIN
    CREATE TABLE BuildingEfficiencyMetrics (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        BuildingId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES Buildings(Id),
        StatisticsDate DATE NOT NULL,
        StatisticsPeriod NVARCHAR(20) NOT NULL,            -- Daily/Weekly/Monthly/Yearly
        -- 能效指标
        EER DECIMAL(18,4) NULL,                            -- 能效比
        COP DECIMAL(18,4) NULL,                            -- 性能系数
        EnergyPerUnitArea DECIMAL(18,4) NULL,              -- 单位面积能耗 (kWh/m²)
        CoolingPerUnitArea DECIMAL(18,4) NULL,             -- 单位面积冷量 (kWh/m²)
        PUE DECIMAL(18,4) NULL,                            -- 电源使用效率
        LoadFactor DECIMAL(18,4) NULL,                     -- 负荷率
        -- 能耗数据
        TotalElectricityConsumption DECIMAL(18,4) NULL,    -- 总耗电量 (kWh)
        TotalCoolingCapacity DECIMAL(18,4) NULL,           -- 总制冷量 (kWh)
        PeakDemand DECIMAL(18,4) NULL,                     -- 峰值需求 (kW)
        OperatingHours DECIMAL(18,4) NULL,                 -- 运行小时数
        -- 经济指标
        TotalCost DECIMAL(18,4) NULL,                      -- 总费用 (元)
        CostPerUnitArea DECIMAL(18,4) NULL,                -- 单位面积费用 (元/m²)
        CostPerCooling DECIMAL(18,4) NULL,                 -- 单位冷量费用 (元/kWh)
        -- 其他
        OutdoorAvgTemp DECIMAL(18,4) NULL,                 -- 室外平均温度
        HDD DECIMAL(18,4) NULL,                            -- 度日数
        CDD DECIMAL(18,4) NULL,                            -- 供冷度日数
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UNIQUE (BuildingId, StatisticsDate, StatisticsPeriod)
    );
    
    CREATE INDEX IX_BuildingMetrics_Building ON BuildingEfficiencyMetrics (BuildingId, StatisticsDate DESC);
END
GO

-- 4.3 对标分析报告表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BenchmarkReports')
BEGIN
    CREATE TABLE BenchmarkReports (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ReportName NVARCHAR(200) NOT NULL,
        ReportPeriod NVARCHAR(20) NOT NULL,
        StartDate DATE NOT NULL,
        EndDate DATE NOT NULL,
        BuildingIds NVARCHAR(500) NOT NULL,                -- 参与对标的楼宇ID列表
        -- 各指标排名（JSON格式存储排名详情）
        COPRanking NVARCHAR(1000) NULL,
        EnergyPerAreaRanking NVARCHAR(1000) NULL,
        CostPerAreaRanking NVARCHAR(1000) NULL,
        LoadFactorRanking NVARCHAR(1000) NULL,
        EERRanking NVARCHAR(1000) NULL,
        -- 最佳实践与建议
        BestPractices NVARCHAR(2000) NULL,
        ImprovementSuggestions NVARCHAR(2000) NULL,
        OverallScore DECIMAL(18,4) NULL,                   -- 综合评分
        GeneratedBy NVARCHAR(50) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    CREATE INDEX IX_BenchmarkReports_Date ON BenchmarkReports (StartDate DESC);
END
GO

-- 4.4 楼宇设备关联表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BuildingDevices')
BEGIN
    CREATE TABLE BuildingDevices (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        BuildingId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES Buildings(Id),
        DeviceId NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES Devices(Id),
        InstallationDate DATE NULL,
        Notes NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UNIQUE (BuildingId, DeviceId)
    );
END
GO

-- =============================================
-- 五、索引优化
-- =============================================

-- 冰蓄冷查询优化
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_IceStorageOperations_DateMode')
BEGIN
    CREATE INDEX IX_IceStorageOperations_DateMode 
    ON IceStorageOperations (Timestamp DESC, Mode)
    INCLUDE (IceAmountDelta, PowerConsumption, CoolingProvided, Cost);
END
GO

-- 故障诊断查询优化
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_FaultDiagnosis_DeviceConfidence')
BEGIN
    CREATE INDEX IX_FaultDiagnosis_DeviceConfidence
    ON FaultDiagnosisResults (DeviceId, Confidence DESC, Timestamp DESC)
    INCLUDE (FaultTypeId, BayesProbability, MaintenanceRecommendation);
END
GO

-- 楼宇对标查询优化
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_BuildingMetrics_PeriodDate')
BEGIN
    CREATE INDEX IX_BuildingMetrics_PeriodDate
    ON BuildingEfficiencyMetrics (StatisticsPeriod, StatisticsDate DESC)
    INCLUDE (BuildingId, COP, EnergyPerUnitArea, CostPerUnitArea, LoadFactor);
END
GO

-- 需求响应查询优化
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_DRExecution_RequestTime')
BEGIN
    CREATE INDEX IX_DRExecution_RequestTime
    ON DRExecutionLogs (DRRequestId, Timestamp DESC)
    INCLUDE (AchievedReduction, TotalBenefit, ComplianceRate);
END
GO

PRINT '新功能数据库表创建完成';
GO
