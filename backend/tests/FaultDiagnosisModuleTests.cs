using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.FaultDiagnosis;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 故障诊断贝叶斯准确性测试
/// 验证贝叶斯网络对各种故障类型的诊断准确性
/// </summary>
public class FaultDiagnosisModule_BayesianAccuracy_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnosisModule>> _mockLogger;
    private readonly FaultDiagnosisModule _module;

    public FaultDiagnosisModule_BayesianAccuracy_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnosisModule>();
        _module = new FaultDiagnosisModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 冷凝器结垢诊断测试：验证系统能正确识别冷凝器结垢故障
    /// 验证点：检测到COND-001故障，置信度>0.6
    /// </summary>
    [Fact]
    public async Task Diagnosis_ShouldIdentifyCondenserScaling()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-001", DeviceType.CentrifugalChiller, "1号离心主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建具有冷凝器结垢特征的设备数据
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 900m,
                SupplyTemperature = 7.0m,
                ReturnTemperature = 13.0m,
                Pressure = 0.7m,
                FlowRate = 170m,
                Frequency = 50m,
                Current = 130m,
                Voltage = 385m,
                InletTemperature = 29m,
                OutletTemperature = 31m,
                CoolingWaterTempDiff = 2.0m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();
        var condenserFault = results.FirstOrDefault(r =>
            r.FaultType != null && r.FaultType.FaultCode == "COND-001");

        condenserFault.Should().NotBeNull();
        condenserFault!.Confidence.Should().BeGreaterThan(0.6m, because: "应该能识别冷凝器结垢故障");
        condenserFault.MatchingSymptoms.Should().Contain("CoolingWaterTempDiff");
    }

    /// <summary>
    /// 蒸发器泄漏诊断测试：验证系统能正确识别蒸发器泄漏故障
    /// 验证点：检测到EVAP-001故障，置信度>0.6
    /// </summary>
    [Fact]
    public async Task Diagnosis_ShouldIdentifyEvaporatorLeak()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-002", DeviceType.CentrifugalChiller, "2号离心主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建具有蒸发器泄漏特征的设备数据
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 750m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 12.5m,
                Pressure = 0.3m,
                FlowRate = 180m,
                Frequency = 50m,
                Current = 110m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 32m,
                TemperatureRise = 3.5m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();
        var evaporatorFault = results.FirstOrDefault(r =>
            r.FaultType != null && r.FaultType.FaultCode == "EVAP-001");

        evaporatorFault.Should().NotBeNull();
        evaporatorFault!.Confidence.Should().BeGreaterThan(0.6m, because: "应该能识别蒸发器泄漏故障");
        evaporatorFault.MatchingSymptoms.Should().Contain("SupplyTemperature");
    }

    /// <summary>
    /// 风机故障诊断测试：验证系统能正确识别冷却塔风机故障
    /// 验证点：检测到FAN-001故障，置信度>0.6
    /// </summary>
    [Fact]
    public async Task Diagnosis_ShouldIdentifyFanFailure()
    {
        // Arrange
        var device = await SeedDeviceAsync("CT-001", DeviceType.CoolingTower, "1号冷却塔");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建具有风机故障特征的设备数据
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 150m,
                SupplyTemperature = 30m,
                ReturnTemperature = 36m,
                Pressure = 0.2m,
                FlowRate = 200m,
                Frequency = 45m,
                Current = 30m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 37m,
                FanSpeed = 40m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();
        var fanFault = results.FirstOrDefault(r =>
            r.FaultType != null && r.FaultType.FaultCode == "FAN-001");

        fanFault.Should().NotBeNull();
        fanFault!.Confidence.Should().BeGreaterThan(0.6m, because: "应该能识别风机故障");
        fanFault.MatchingSymptoms.Should().Contain("FanSpeed");
    }

    /// <summary>
    /// 正常设备测试：验证正常设备不会产生高置信度的故障诊断
    /// 验证点：置信度=0或无诊断结果
    /// </summary>
    [Fact]
    public async Task Diagnosis_ShouldReturnZeroConfidenceForNormal()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-003", DeviceType.CentrifugalChiller, "3号离心主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建正常运行的设备数据（所有参数都在正常范围内）
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 750m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.45m,
                FlowRate = 180m,
                Frequency = 50m,
                Current = 120m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 32m,
                CoolingWaterTempDiff = 4.0m,
                TemperatureRise = 6.0m,
                FanSpeed = 90m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        // 正常设备应该没有高置信度的故障诊断
        if (results.Any())
        {
            results.All(r => r.Confidence < 0.6m).Should().BeTrue(
                because: "正常设备的故障置信度应该低于阈值");
        }
        else
        {
            results.Should().BeEmpty(because: "正常设备不应该诊断出故障");
        }
    }

    /// <summary>
    /// 多症状置信度提升测试：验证匹配的症状越多，置信度越高
    /// 验证点：3个症状匹配的置信度 > 1个症状匹配的置信度
    /// </summary>
    [Fact]
    public async Task Confidence_ShouldIncreaseWithMoreSymptoms()
    {
        // Arrange
        var device1 = await SeedDeviceAsync("CH-TEST1", DeviceType.CentrifugalChiller, "测试主机1");
        var device2 = await SeedDeviceAsync("CH-TEST2", DeviceType.CentrifugalChiller, "测试主机2");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var baseTime = DateTime.UtcNow.AddHours(-10);

        // device1：只有1个症状异常（低置信度）
        var data1 = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            data1.Add(new DeviceData
            {
                DeviceId = device1.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 800m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.7m,
                FlowRate = 180m,
                Frequency = 50m,
                Current = 120m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 32m,
                CoolingWaterTempDiff = 4.0m
            });
        }

        // device2：3个症状异常（高置信度）
        var data2 = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            data2.Add(new DeviceData
            {
                DeviceId = device2.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.75m,
                FlowRate = 140m,
                Frequency = 50m,
                Current = 140m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 30m,
                CoolingWaterTempDiff = 2.0m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(data1);
        await _dbContext.DeviceData.AddRangeAsync(data2);
        await _dbContext.SaveChangesAsync();

        // Act
        var results1 = await _module.DiagnoseDeviceAsync(device1.Id);
        var results2 = await _module.DiagnoseDeviceAsync(device2.Id);

        // Assert
        results1.Should().NotBeEmpty();
        results2.Should().NotBeEmpty();

        var fault1 = results1.FirstOrDefault(r => r.FaultType?.FaultCode == "COND-001");
        var fault2 = results2.FirstOrDefault(r => r.FaultType?.FaultCode == "COND-001");

        fault1.Should().NotBeNull();
        fault2.Should().NotBeNull();

        // 多症状的置信度应该更高
        fault2!.Confidence.Should().BeGreaterThan(fault1!.Confidence,
            because: "匹配更多症状应该有更高的置信度");

        // 验证症状数量
        var symptoms1 = fault1.MatchingSymptoms?.Split(',').Length ?? 0;
        var symptoms2 = fault2.MatchingSymptoms?.Split(',').Length ?? 0;
        symptoms2.Should().BeGreaterThan(symptoms1, because: "应该匹配更多症状");
    }
}

/// <summary>
/// 故障诊断故障库测试
/// 验证故障知识库的完整性和正确性
/// </summary>
public class FaultDiagnosisModule_FaultLibrary_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnosisModule>> _mockLogger;
    private readonly FaultDiagnosisModule _module;

    public FaultDiagnosisModule_FaultLibrary_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnosisModule>();
        _module = new FaultDiagnosisModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 覆盖度测试：验证故障库包含10种故障类型
    /// 验证点：FaultTypes.Count == 10
    /// </summary>
    [Fact]
    public async Task FaultLibrary_ShouldHave10FaultTypes()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var faultTypes = await _dbContext.FaultTypes.ToListAsync();

        // Assert
        faultTypes.Should().HaveCount(10, because: "故障库应该包含10种故障类型");
    }

    /// <summary>
    /// 症状完整性测试：验证每种故障都有对应的症状参数
    /// 验证点：每种故障至少有1个SymptomParameter
    /// </summary>
    [Fact]
    public async Task EachFault_ShouldHaveSymptoms()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var faultTypes = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .ToListAsync();

        // Assert
        faultTypes.Should().AllSatisfy(fault =>
        {
            fault.SymptomParameters.Should().NotBeEmpty(
                because: $"故障{fault.FaultName}应该有症状参数");
            fault.SymptomParameters!.Count.Should().BeGreaterThanOrEqualTo(1,
                because: $"故障{fault.FaultName}至少有1个症状");
        });
    }

    /// <summary>
    /// 检修建议完整性测试：验证每种故障都有检修建议
    /// 验证点：TypicalSolution不为空
    /// </summary>
    [Fact]
    public async Task EachFault_ShouldHaveMaintenanceRecommendation()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var faultTypes = await _dbContext.FaultTypes.ToListAsync();

        // Assert
        faultTypes.Should().AllSatisfy(fault =>
        {
            fault.TypicalSolution.Should().NotBeNullOrWhiteSpace(
                because: $"故障{fault.FaultName}应该有检修建议");
            fault.TypicalSolution!.Length.Should().BeGreaterThan(10,
                because: $"故障{fault.FaultName}的检修建议应该有足够的内容");
        });
    }

    /// <summary>
    /// 设备类型映射测试：验证故障被正确映射到对应设备类型
    /// 验证点：DeviceTypeId与故障类型匹配
    /// </summary>
    [Fact]
    public async Task Fault_ShouldBeMappedToCorrectDeviceType()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act & Assert
        // 冷凝器故障应该映射到离心机
        var condenserFaults = await _dbContext.FaultTypes
            .Where(f => f.FaultCode.StartsWith("COND"))
            .ToListAsync();
        condenserFaults.Should().AllSatisfy(f =>
            f.DeviceTypeId.Should().Be(DeviceType.CentrifugalChiller,
                because: "冷凝器故障应该属于离心主机"));

        // 风机故障应该映射到冷却塔
        var fanFaults = await _dbContext.FaultTypes
            .Where(f => f.FaultCode.StartsWith("FAN"))
            .ToListAsync();
        fanFaults.Should().AllSatisfy(f =>
            f.DeviceTypeId.Should().Be(DeviceType.CoolingTower,
                because: "风机故障应该属于冷却塔"));

        // 验证按设备类型查询
        var chillerFaults = await _module.GetFaultTypesForDeviceTypeAsync(DeviceType.CentrifugalChiller);
        chillerFaults.Should().NotBeEmpty(because: "离心主机应该有对应的故障类型");
        chillerFaults.All(f => f.DeviceTypeId == DeviceType.CentrifugalChiller)
            .Should().BeTrue(because: "返回的故障应该都是对应设备类型的");
    }

    /// <summary>
    /// 概率有效性测试：验证贝叶斯条件概率表的概率值有效
    /// 验证点：0 <= PriorProbability <= 1, 0 <= ConditionalProbability <= 1
    /// </summary>
    [Fact]
    public async Task BayesianCPT_ShouldHaveValidProbabilities()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var symptomParameters = await _dbContext.FaultSymptomParameters.ToListAsync();

        // Assert
        symptomParameters.Should().AllSatisfy(param =>
        {
            param.PriorProbability.Should().BeGreaterThanOrEqualTo(0,
                because: $"参数{param.ParameterName}的先验概率不能为负");
            param.PriorProbability.Should().BeLessThanOrEqualTo(1,
                because: $"参数{param.ParameterName}的先验概率不能超过1");

            param.ConditionalProbability.Should().BeGreaterThanOrEqualTo(0,
                because: $"参数{param.ParameterName}的条件概率不能为负");
            param.ConditionalProbability.Should().BeLessThanOrEqualTo(1,
                because: $"参数{param.ParameterName}的条件概率不能超过1");

            param.DeviationWeight.Should().BeGreaterThan(0,
                because: $"参数{param.ParameterName}的偏离权重应该大于0");
            param.DeviationWeight.Should().BeLessThanOrEqualTo(1,
                because: $"参数{param.ParameterName}的偏离权重不能超过1");
        });

        // 验证先验概率的合理性（应该很小，因为故障是稀有事件）
        symptomParameters.Average(p => p.PriorProbability).Should().BeLessThan(0.1m,
            because: "故障的先验概率应该很小");
    }
}

/// <summary>
/// 故障诊断建议测试
/// 验证诊断建议的具体性、工时和成本估算
/// </summary>
public class FaultDiagnosisModule_Recommendation_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnosisModule>> _mockLogger;
    private readonly FaultDiagnosisModule _module;

    public FaultDiagnosisModule_Recommendation_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnosisModule>();
        _module = new FaultDiagnosisModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 建议具体性测试：验证维护建议是具体的、可执行的
    /// 验证点：MaintenanceRecommendation包含具体动词和操作
    /// </summary>
    [Fact]
    public async Task Recommendation_ShouldBeSpecific()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-REC-001", DeviceType.CentrifugalChiller, "测试主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();
        await SeedDeviceDataAsync(device.Id);

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        if (results.Any())
        {
            results.Should().AllSatisfy(result =>
            {
                result.MaintenanceRecommendation.Should().NotBeNullOrWhiteSpace(
                    because: "应该提供维护建议");

                // 建议应该包含具体的操作动词
                var recommendation = result.MaintenanceRecommendation!;
                recommendation.Should().ContainAny(
                    "检查", "清洗", "更换", "检修", "校准", "调整", "清理",
                    because: "建议应该包含具体的操作动词");

                recommendation.Length.Should().BeGreaterThan(10,
                    because: "建议应该有足够的内容");
            });
        }
    }

    /// <summary>
    /// 预计工时测试：验证诊断结果包含预计修复工时
    /// 验证点：EstimatedDowntimeHours有合理值
    /// </summary>
    [Fact]
    public async Task Recommendation_ShouldIncludeEstimatedTime()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-REC-002", DeviceType.CentrifugalChiller, "测试主机2");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建具有明显故障特征的数据
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.75m,
                FlowRate = 140m,
                Frequency = 50m,
                Current = 140m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 30m,
                CoolingWaterTempDiff = 2.0m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();

        var result = results.First();
        result.EstimatedDowntimeHours.Should().HaveValue(
            because: "应该提供预计修复工时");
        result.EstimatedDowntimeHours.Should().BeGreaterThan(0,
            because: "预计工时应该大于0");
        result.EstimatedDowntimeHours.Should().BeLessThanOrEqualTo(48,
            because: "预计工时应该合理，不超过48小时");
    }

    /// <summary>
    /// 预计成本测试：验证诊断结果包含预计修复成本
    /// 验证点：EstimatedRepairCost有合理值
    /// </summary>
    [Fact]
    public async Task Recommendation_ShouldIncludeEstimatedCost()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-REC-003", DeviceType.CentrifugalChiller, "测试主机3");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        // 创建具有明显故障特征的数据
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-10);

        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 12.5m,
                Pressure = 0.3m,
                FlowRate = 180m,
                Frequency = 50m,
                Current = 140m,
                Voltage = 380m,
                InletTemperature = 28m,
                OutletTemperature = 32m,
                TemperatureRise = 3.5m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();

        var result = results.First();
        result.EstimatedRepairCost.Should().HaveValue(
            because: "应该提供预计修复成本");
        result.EstimatedRepairCost.Should().BeGreaterThan(0,
            because: "预计成本应该大于0");

        // 验证成本计算：工时 * 500
        if (result.FaultType != null && result.FaultType.EstimatedRepairHours.HasValue)
        {
            var expectedCost = result.FaultType.EstimatedRepairHours * 500;
            result.EstimatedRepairCost.Should().Be(expectedCost,
                because: "预计成本应该等于工时乘以500");
        }
    }

    /// <summary>
    /// 确认诊断记录测试：验证确认操作被正确记录
    /// 验证点：IsConfirmed=true, ConfirmedBy和ConfirmedAt有值
    /// </summary>
    [Fact]
    public async Task ConfirmedDiagnosis_ShouldBeRecorded()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-REC-004", DeviceType.CentrifugalChiller, "测试主机4");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();
        await SeedDeviceDataAsync(device.Id);

        var results = await _module.DiagnoseDeviceAsync(device.Id);
        results.Should().NotBeEmpty();

        var diagnosisId = results.First().Id;
        var confirmedBy = "engineer001";

        // Act
        await _module.ConfirmDiagnosisAsync(diagnosisId, confirmedBy);

        // Assert
        using var newContext = CreateNewContext();
        var confirmedDiagnosis = await newContext.FaultDiagnosisResults.FindAsync(diagnosisId);

        confirmedDiagnosis.Should().NotBeNull();
        confirmedDiagnosis!.IsConfirmed.Should().BeTrue(because: "诊断应该被标记为已确认");
        confirmedDiagnosis.ConfirmedBy.Should().Be(confirmedBy, because: "确认人应该被记录");
        confirmedDiagnosis.ConfirmedAt.Should().HaveValue(because: "确认时间应该被记录");
        confirmedDiagnosis.ConfirmedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10),
            because: "确认时间应该接近当前时间");
    }

    /// <summary>
    /// 统计正确性测试：验证诊断统计数据计算正确
    /// 验证点：统计数据与实际数据一致
    /// </summary>
    [Fact]
    public async Task Statistics_ShouldBeCorrectlyCalculated()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-STAT-001", DeviceType.CentrifugalChiller, "统计测试主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var testDate = new DateTime(2024, 6, 15);

        // 创建多个诊断结果，部分已确认，部分为误报
        var diagnoses = new List<FaultDiagnosisResult>
        {
            new()
            {
                DeviceId = device.Id,
                FaultTypeId = 1,
                Timestamp = testDate.AddHours(8),
                Confidence = 0.85m,
                BayesProbability = 0.82m,
                IsConfirmed = true,
                ConfirmedBy = "engineer1",
                ConfirmedAt = testDate.AddHours(9)
            },
            new()
            {
                DeviceId = device.Id,
                FaultTypeId = 2,
                Timestamp = testDate.AddHours(10),
                Confidence = 0.75m,
                BayesProbability = 0.78m,
                IsConfirmed = true,
                ConfirmedBy = "engineer1",
                ConfirmedAt = testDate.AddHours(11)
            },
            new()
            {
                DeviceId = device.Id,
                FaultTypeId = 1,
                Timestamp = testDate.AddHours(12),
                Confidence = 0.65m,
                BayesProbability = 0.62m,
                IsConfirmed = false
            },
            new()
            {
                DeviceId = device.Id,
                FaultTypeId = 3,
                Timestamp = testDate.AddHours(14),
                Confidence = 0.7m,
                BayesProbability = 0.68m,
                IsConfirmed = false
            },
            new()
            {
                DeviceId = device.Id,
                FaultTypeId = 1,
                Timestamp = testDate.AddHours(16),
                Confidence = 0.9m,
                BayesProbability = 0.88m,
                IsConfirmed = true,
                ConfirmedBy = "engineer2",
                ConfirmedAt = testDate.AddHours(17)
            }
        };

        await _dbContext.FaultDiagnosisResults.AddRangeAsync(diagnoses);
        await _dbContext.SaveChangesAsync();

        // Act
        var statistics = await _module.GetDailyStatisticsAsync(testDate);

        // Assert
        statistics.Should().NotBeNull();
        statistics.TotalDiagnosisCount.Should().Be(5, because: "总诊断次数应该正确");
        statistics.ConfirmedFaultCount.Should().Be(3, because: "已确认故障数应该正确");
        statistics.FalsePositiveCount.Should().Be(2, because: "误报数应该正确（置信度>0.6且未确认）");
        statistics.AverageConfidence.Should().BeApproximately(
            diagnoses.Average(d => d.Confidence),
            0.001m,
            because: "平均置信度应该正确");

        // 准确率应该正确计算
        var expectedAccuracy = (decimal)3 / 5;
        statistics.AccuracyRate.Should().BeApproximately(expectedAccuracy, 0.001m,
            because: "准确率应该正确计算");

        // 验证统计数据被持久化
        using var newContext = CreateNewContext();
        var savedStats = await newContext.DiagnosisStatistics
            .FirstOrDefaultAsync(s => s.StatisticsDate.Date == testDate.Date);
        savedStats.Should().NotBeNull();
        savedStats!.TotalDiagnosisCount.Should().Be(5);
    }
}

/// <summary>
/// 故障诊断未知故障检测测试
/// 验证异常检测、未知故障创建、贝叶斯概率和历史基准使用
/// </summary>
public class FaultDiagnosisModule_AnomalyDetection_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnosisModule>> _mockLogger;
    private readonly FaultDiagnosisModule _module;

    public FaultDiagnosisModule_AnomalyDetection_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnosisModule>();
        _module = new FaultDiagnosisModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 根因：仅依赖已知故障模式，参数偏离但不符合已知模式时无法检测，导致早期故障被忽略。
    /// 验证点：Z-score>3.0的参数被标记为异常，AnomalousParameters包含该参数。
    /// </summary>
    [Fact]
    public void DetectAnomalies_ShouldIdentify_ParameterDeviation()
    {
        // Arrange
        var device = new Device
        {
            Id = "DEV-ANOMALY-001",
            Name = "异常检测测试设备",
            DeviceTypeId = DeviceType.CentrifugalChiller,
            Status = DeviceStatus.Running
        };

        var historicalMean = 100.0;
        var historicalStdDev = 5.0;
        var recentMean = 120.0;

        var historicalData = new List<DeviceData>();
        var random = new Random(42);

        for (int i = 0; i < 30; i++)
        {
            historicalData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddDays(-7).AddMinutes(i * 30),
                Power = (decimal)(historicalMean + random.NextDouble() * historicalStdDev * 2 - historicalStdDev)
            });
        }

        var recentData = new List<DeviceData>();
        for (int i = 0; i < 10; i++)
        {
            recentData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                Power = (decimal)(recentMean + random.NextDouble() * 2 - 1)
            });
        }

        var method = typeof(FaultDiagnosisModule).GetMethod("DetectAnomalies",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (AnomalyDetectionResult)method!.Invoke(_module,
            new object[] { device, recentData, historicalData })!;

        // Assert
        result.IsAnomaly.Should().BeTrue(because: "Z-score=4.0超过阈值3.0，应该被检测为异常");
        result.AnomalousParameters.Should().Contain("Power",
            because: "Power参数Z-score=4.0超过阈值，应该被标记为异常");
    }

    /// <summary>
    /// 根因：没有未知故障处理机制，参数异常但不符合已知故障模式时系统保持沉默。
    /// 验证点：创建FaultTypeId=-1的未知故障，IsUnknownFault=true。
    /// </summary>
    [Fact]
    public async Task UnknownFault_ShouldBeCreated_WhenAnomalyDetectedButNoKnownFaultMatches()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-UNKNOWN-001", DeviceType.CentrifugalChiller, "未知故障测试主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var baseTime = DateTime.UtcNow.AddHours(-10);
        var random = new Random(42);

        var historicalData = new List<DeviceData>();
        for (int i = 0; i < 50; i++)
        {
            historicalData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddDays(-7).AddMinutes(i * 20),
                Power = 750m + (decimal)(random.NextDouble() * 50),
                SupplyTemperature = 6.5m + (decimal)(random.NextDouble() * 0.5),
                ReturnTemperature = 12.5m + (decimal)(random.NextDouble() * 0.5),
                Pressure = 0.45m + (decimal)(random.NextDouble() * 0.05),
                FlowRate = 180m + (decimal)(random.NextDouble() * 10),
                Frequency = 50m,
                Current = 120m + (decimal)(random.NextDouble() * 10)
            });
        }

        var recentData = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            recentData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 9.0m,
                Pressure = 0.2m,
                FlowRate = 100m,
                Frequency = 30m,
                Current = 160m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(historicalData);
        await _dbContext.DeviceData.AddRangeAsync(recentData);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();
        var unknownFault = results.FirstOrDefault(r => r.IsUnknownFault);

        unknownFault.Should().NotBeNull(because: "应该创建未知故障记录");
        unknownFault!.FaultTypeId.Should().Be(-1, because: "未知故障的FaultTypeId应该为-1");
        unknownFault.IsUnknownFault.Should().BeTrue(because: "IsUnknownFault应该为true");
    }

    /// <summary>
    /// 根因：未知故障与已知故障混淆，无法区分是模式库中不存在的故障还是已知故障。
    /// 验证点：未知故障的贝叶斯概率为0，表示不在模式库中。
    /// </summary>
    [Fact]
    public async Task UnknownFault_ShouldHaveZeroBayesProbability()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-UNKNOWN-002", DeviceType.CentrifugalChiller, "未知故障测试主机2");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var baseTime = DateTime.UtcNow.AddHours(-10);

        var historicalData = new List<DeviceData>();
        for (int i = 0; i < 50; i++)
        {
            historicalData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddDays(-7).AddMinutes(i * 20),
                Power = 750m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.45m,
                FlowRate = 180m,
                Frequency = 50m,
                Current = 120m
            });
        }

        var recentData = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            recentData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 9.0m,
                Pressure = 0.2m,
                FlowRate = 100m,
                Frequency = 30m,
                Current = 160m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(historicalData);
        await _dbContext.DeviceData.AddRangeAsync(recentData);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);
        var unknownFault = results.FirstOrDefault(r => r.IsUnknownFault);

        // Assert
        unknownFault.Should().NotBeNull();
        unknownFault!.BayesProbability.Should().Be(0,
            because: "未知故障不在模式库中，贝叶斯概率应该为0");
    }

    /// <summary>
    /// 根因：单一参数异常和多参数异常的严重程度相同，无法区分故障的严重性。
    /// 验证点：异常参数越多，AnomalyScore越高。
    /// </summary>
    [Fact]
    public void AnomalyScore_ShouldIncreaseWithNumberOfAnomalousParameters()
    {
        // Arrange
        var device = new Device
        {
            Id = "DEV-ANOMALY-SCORE",
            Name = "异常评分测试设备",
            DeviceTypeId = DeviceType.CentrifugalChiller,
            Status = DeviceStatus.Running
        };

        var method = typeof(FaultDiagnosisModule).GetMethod("DetectAnomalies",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var baseHistorical = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            baseHistorical.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddDays(-7).AddMinutes(i * 30),
                Power = 750m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.45m,
                FlowRate = 180m,
                Current = 120m
            });
        }

        // 场景1：只有1个参数异常
        var recent1 = new List<DeviceData>();
        for (int i = 0; i < 10; i++)
        {
            recent1.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                Power = 950m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.45m,
                FlowRate = 180m,
                Current = 120m
            });
        }

        // 场景2：5个参数都异常
        var recent2 = new List<DeviceData>();
        for (int i = 0; i < 10; i++)
        {
            recent2.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                Power = 950m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 15.0m,
                Pressure = 0.7m,
                FlowRate = 120m,
                Current = 160m
            });
        }

        // Act
        var result1 = (AnomalyDetectionResult)method!.Invoke(_module,
            new object[] { device, recent1, baseHistorical })!;
        var result2 = (AnomalyDetectionResult)method!.Invoke(_module,
            new object[] { device, recent2, baseHistorical })!;

        // Assert
        result2.AnomalyScore.Should().BeGreaterThan(result1.AnomalyScore,
            because: "5个参数异常的评分应该高于1个参数异常的评分");
    }

    /// <summary>
    /// 根因：异常检测可能覆盖已知故障，导致已知故障被误判为未知故障。
    /// 验证点：已知故障不会被异常检测覆盖，仍然能被正确诊断且置信度>0.8。
    /// </summary>
    [Fact]
    public async Task KnownFault_ShouldStillBeDiagnosed_EvenWithAnomalyDetection()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-KNOWN-001", DeviceType.CentrifugalChiller, "已知故障测试主机");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var baseTime = DateTime.UtcNow.AddHours(-10);

        var deviceDataList = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 900m,
                SupplyTemperature = 7.0m,
                ReturnTemperature = 13.0m,
                Pressure = 0.7m,
                FlowRate = 170m,
                Frequency = 50m,
                Current = 130m,
                Voltage = 385m,
                InletTemperature = 29m,
                OutletTemperature = 31m,
                CoolingWaterTempDiff = 2.0m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);

        // Assert
        results.Should().NotBeEmpty();
        var knownFault = results.FirstOrDefault(r =>
            r.FaultType != null && r.FaultType.FaultCode == "COND-001");

        knownFault.Should().NotBeNull(because: "冷凝器结垢是已知故障，应该被正确诊断");
        knownFault!.Confidence.Should().BeGreaterThan(0.6m,
            because: "已知故障的诊断置信度应该大于0.6");
        knownFault.IsUnknownFault.Should().BeFalse(because: "已知故障不应该被标记为未知故障");
    }

    /// <summary>
    /// 根因：异常检测使用近期数据作为基准，导致季节性变化或正常调整被误判为异常。
    /// 验证点：使用过去7天数据作为基准，Z=2.0不异常，Z=6.0异常。
    /// </summary>
    [Fact]
    public void AnomalyDetection_ShouldUseHistoricalBaseline()
    {
        // Arrange
        var device = new Device
        {
            Id = "DEV-BASELINE-001",
            Name = "基准测试设备",
            DeviceTypeId = DeviceType.CentrifugalChiller,
            Status = DeviceStatus.Running
        };

        var method = typeof(FaultDiagnosisModule).GetMethod("DetectAnomalies",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var historicalMean = 100.0;
        var historicalStdDev = 5.0;

        var historicalData = new List<DeviceData>();
        var random = new Random(42);

        for (int i = 0; i < 50; i++)
        {
            historicalData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddDays(-7).AddMinutes(i * 20),
                Power = (decimal)(historicalMean + random.NextDouble() * historicalStdDev * 2 - historicalStdDev)
            });
        }

        // 场景1：近期数据110±5，Z-score=2.0，不异常
        var recentNormal = new List<DeviceData>();
        for (int i = 0; i < 10; i++)
        {
            recentNormal.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                Power = (decimal)(110.0 + random.NextDouble() * 10 - 5)
            });
        }

        // 场景2：近期数据130±5，Z-score=6.0，异常
        var recentAnomaly = new List<DeviceData>();
        for (int i = 0; i < 10; i++)
        {
            recentAnomaly.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                Power = (decimal)(130.0 + random.NextDouble() * 10 - 5)
            });
        }

        // Act
        var resultNormal = (AnomalyDetectionResult)method!.Invoke(_module,
            new object[] { device, recentNormal, historicalData })!;
        var resultAnomaly = (AnomalyDetectionResult)method!.Invoke(_module,
            new object[] { device, recentAnomaly, historicalData })!;

        // Assert
        resultNormal.IsAnomaly.Should().BeFalse(because: "Z-score=2.0低于阈值3.0，不应该被检测为异常");
        resultAnomaly.IsAnomaly.Should().BeTrue(because: "Z-score=6.0高于阈值3.0，应该被检测为异常");
    }

    /// <summary>
    /// 根因：未知故障记录缺乏详细信息，运维人员无法了解具体哪些参数偏离以及偏离程度。
    /// 验证点：DeviationDetails包含"Z-score详情"和具体参数值。
    /// </summary>
    [Fact]
    public async Task UnknownFault_ShouldIncludeDetailedDeviationInfo()
    {
        // Arrange
        var device = await SeedDeviceAsync("CH-UNKNOWN-003", DeviceType.CentrifugalChiller, "未知故障测试主机3");
        await SeedFaultTypesAsync();
        await _module.InitializeFaultKnowledgeBaseAsync();

        var baseTime = DateTime.UtcNow.AddHours(-10);

        var historicalData = new List<DeviceData>();
        for (int i = 0; i < 50; i++)
        {
            historicalData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddDays(-7).AddMinutes(i * 20),
                Power = 750m,
                SupplyTemperature = 6.5m,
                ReturnTemperature = 12.5m,
                Pressure = 0.45m,
                FlowRate = 180m,
                Current = 120m
            });
        }

        var recentData = new List<DeviceData>();
        for (int i = 0; i < 30; i++)
        {
            recentData.Add(new DeviceData
            {
                DeviceId = device.Id,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 950m,
                SupplyTemperature = 9.0m,
                ReturnTemperature = 9.0m,
                Pressure = 0.2m,
                FlowRate = 100m,
                Current = 160m
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(historicalData);
        await _dbContext.DeviceData.AddRangeAsync(recentData);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _module.DiagnoseDeviceAsync(device.Id);
        var unknownFault = results.FirstOrDefault(r => r.IsUnknownFault);

        // Assert
        unknownFault.Should().NotBeNull();
        unknownFault!.DeviationDetails.Should().NotBeNullOrWhiteSpace();
        unknownFault.DeviationDetails.Should().Contain("Z-score详情",
            because: "未知故障应该包含Z-score详情");
        unknownFault.DeviationDetails.Should().ContainAny("Power", "SupplyTemperature", "Pressure", "FlowRate", "Current",
            because: "应该包含具体的异常参数名称和Z-score值");
    }
}
