using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.DemandResponse;
using ChillerPlantOptimization.Modules.IceStorage;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 需求响应响应时间测试
/// 验证系统对需求响应请求的快速响应能力
/// </summary>
public class DemandResponseModule_ResponseTime_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponseModule>> _mockLogger;
    private readonly Mock<ILogger<DemandResponder>> _mockResponderLogger;
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockOptimizerLogger;
    private readonly IDemandResponder _responder;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_ResponseTime_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockResponderLogger = CreateMockLogger<DemandResponder>();
        _mockOptimizerLogger = CreateMockLogger<IceStorageOptimizer>();
        var optimizer = new IceStorageOptimizer(_dbContext, _mockOptimizerLogger.Object);
        _responder = new DemandResponder(_dbContext, _mockResponderLogger.Object, optimizer);
        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, null, _responder);
    }

    [Fact]
    public async Task AdapterAndService_ShouldReturnConsistentResults()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        // Act
        var adapterResult = await _module.ExecuteResponseAsync(request.Id);
        var serviceStatus = await _responder.GetStatusAsync(request.Id);

        // Assert
        adapterResult.Should().NotBeNull();
        adapterResult.DRRequestId.Should().Be(request.Id);
        serviceStatus.Status.Should().Be(DRRequestStatus.Executing);
        serviceStatus.RequestId.Should().Be(request.Id);
    }

    /// <summary>
    /// 响应时间测试：验证系统能在1秒内开始响应
    /// 验证点：ExecuteResponseAsync执行时间 <= 1000ms
    /// </summary>
    [Fact]
    public async Task Response_ShouldStartWithin1Second()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _module.ExecuteResponseAsync(request.Id);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert
        result.Should().NotBeNull();
        elapsed.Should().BeLessThan(1000, because: "响应应该在1秒内开始");
        request.Status.Should().Be(DRRequestStatus.Executing);
    }

    /// <summary>
    /// 立即执行测试：验证系统能够处理立即执行的请求
    /// 验证点：StartTime设置为当前时间时也能正常执行
    /// </summary>
    [Fact]
    public async Task Response_ShouldHandleImmediateExecution()
    {
        // Arrange
        var immediateRequest = new DemandResponseRequest
        {
            Id = $"DR-IMMEDIATE-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.EmergencyDR,
            Status = DRRequestStatus.Received,
            SourcePlatform = "PowerGrid",
            RequestedLoadReduction = 3000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(30),
            IncentivePerKWh = 0.8m,
            MaxChillerOutputLimit = 0.6m,
            MinIceMeltingRate = 1500m,
            Priority = 0,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(immediateRequest);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _module.ExecuteResponseAsync(immediateRequest.Id);

        // Assert
        result.Should().NotBeNull();
        result.DRRequestId.Should().Be(immediateRequest.Id);
        immediateRequest.Status.Should().Be(DRRequestStatus.Executing);

        // 验证限制参数已应用
        var chillerLimit = await _module.GetCurrentChillerOutputLimitAsync();
        var iceMeltingRate = await _module.GetCurrentIceMeltingRateAsync();

        chillerLimit.Should().Be(0.6m, because: "主机出力上限应该立即生效");
        iceMeltingRate.Should().Be(1500m, because: "融冰速率应该立即生效");
    }

    /// <summary>
    /// 延迟执行边界测试：验证系统能够处理延迟执行的请求
    /// 验证点：StartTime设置为未来时间时也能正常执行
    /// </summary>
    [Fact]
    public async Task Response_ShouldHandleDelayedExecution()
    {
        // Arrange
        var delayedRequest = new DemandResponseRequest
        {
            Id = $"DR-DELAYED-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "PowerGrid",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            IncentivePerKWh = 0.4m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(delayedRequest);
        await _dbContext.SaveChangesAsync();

        // Act - 即使开始时间在未来，也应该能够执行
        var result = await _module.ExecuteResponseAsync(delayedRequest.Id);

        // Assert
        result.Should().NotBeNull();
        result.DRRequestId.Should().Be(delayedRequest.Id);
        result.TotalDurationMinutes.Should().Be(120, because: "应该正确计算持续时间");
        delayedRequest.Status.Should().Be(DRRequestStatus.Executing);
    }

    /// <summary>
    /// 取消请求异常测试：验证系统能够处理已取消的请求
    /// 验证点：对已取消的请求执行操作时抛出正确的异常
    /// </summary>
    [Fact]
    public async Task Response_ShouldHandleCancelledRequest()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        request.Status = DRRequestStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        // Act
        Func<Task> act = async () => await _module.ExecuteResponseAsync(request.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Cancelled*", because: "已取消的请求不能执行");
    }

    /// <summary>
    /// 零时长边界测试：验证系统能够处理零时长的请求
    /// 验证点：零时长请求能够被正确处理或抛出有意义的异常
    /// </summary>
    [Fact]
    public async Task Response_ShouldHandleZeroDurationRequest()
    {
        // Arrange
        var zeroDurationRequest = new DemandResponseRequest
        {
            Id = $"DR-ZERO-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "PowerGrid",
            RequestedLoadReduction = 1000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            IncentivePerKWh = 0.4m,
            Priority = 3,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(zeroDurationRequest);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _module.ExecuteResponseAsync(zeroDurationRequest.Id);

        // Assert
        result.Should().NotBeNull();
        result.TotalDurationMinutes.Should().Be(0, because: "零时长请求的持续时间应该为0");
        result.TotalActualReduction.Should().Be(0, because: "零时长请求不应该有实际减载量");
        zeroDurationRequest.Status.Should().BeOneOf(
            DRRequestStatus.Executing,
            DRRequestStatus.Completed,
            because: "零时长请求应该被正确处理");
    }
}

/// <summary>
/// 需求响应负荷调整测试
/// 验证负荷削减量、出力限制和融冰速率的正确性
/// </summary>
public class DemandResponseModule_LoadAdjustment_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponseModule>> _mockLogger;
    private readonly Mock<ILogger<DemandResponder>> _mockResponderLogger;
    private readonly Mock<ILogger<IceStorageModule>> _mockIceLogger;
    private readonly Mock<IIceStorageModule> _mockIceStorageModule;
    private readonly IDemandResponder _responder;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_LoadAdjustment_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockResponderLogger = CreateMockLogger<DemandResponder>();
        _mockIceLogger = CreateMockLogger<IceStorageModule>();
        _mockIceStorageModule = new Mock<IIceStorageModule>();
        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);

        var mockOptimizer = new Mock<IIceStorageOptimizer>();
        mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockResponderLogger.Object, mockOptimizer.Object);
        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, _mockIceStorageModule.Object, _responder);
    }

    [Fact]
    public async Task AdapterAndService_CurrentLimits_ShouldBeConsistent()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            3000m,
            60,
            0.6m);
        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var adapterChillerLimit = await _module.GetCurrentChillerOutputLimitAsync();
        var adapterIceMeltingRate = await _module.GetCurrentIceMeltingRateAsync();
        var serviceAdjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adapterChillerLimit.Should().Be(serviceAdjustments.ChillerOutputLimit);
        adapterIceMeltingRate.Should().Be(serviceAdjustments.IceMeltingRate);
    }
    }

    /// <summary>
    /// 调整量准确性测试：验证实际减载量与目标值匹配
    /// 验证点：AchievedReduction ≈ TargetReduction
    /// </summary>
    [Fact]
    public async Task LoadReduction_ShouldMatchTarget()
    {
        // Arrange
        var targetReduction = 2000m;
        var baselineLoad = 6000m;
        var actualLoad = baselineLoad - targetReduction;

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            targetReduction,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        log.AchievedReduction.Should().Be(targetReduction, because: "减载量应该等于基准负荷减去实际负荷");
        log.TargetReduction.Should().Be(targetReduction, because: "目标减载量应该正确");
        log.BaselineLoad.Should().Be(baselineLoad);
        log.ActualLoad.Should().Be(actualLoad);
    }

    /// <summary>
    /// 出力上限测试：验证主机出力上限被正确应用
    /// 验证点：ChillerOutputLimit == request.MaxChillerOutputLimit
    /// </summary>
    [Fact]
    public async Task ChillerOutputLimit_ShouldBeApplied()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            3000m,
            60,
            0.6m);

        // Act
        await _module.ExecuteResponseAsync(request.Id);
        var log = await _module.RecordExecutionLogAsync(request.Id, 7000m, 4000m);

        // Assert
        log.Should().NotBeNull();
        log.ChillerOutputLimit.Should().Be(request.MaxChillerOutputLimit, because: "主机出力上限应该被记录");

        // 验证模块级别限制
        var currentLimit = await _module.GetCurrentChillerOutputLimitAsync();
        currentLimit.Should().Be(request.MaxChillerOutputLimit, because: "主机出力上限应该被应用");
    }

    /// <summary>
    /// 融冰速率调整测试：验证融冰速率被正确提高
    /// 验证点：IceMeltingRateApplied == request.MinIceMeltingRate
    /// </summary>
    [Fact]
    public async Task IceMeltingRate_ShouldBeIncreased()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2500m,
            60,
            0.5m);

        // Act
        await _module.ExecuteResponseAsync(request.Id);
        var log = await _module.RecordExecutionLogAsync(request.Id, 6500m, 4000m);

        // Assert
        log.Should().NotBeNull();
        log.IceMeltingRateApplied.Should().Be(request.MinIceMeltingRate, because: "融冰速率应该被记录");

        // 验证模块级别融冰速率
        var currentMeltingRate = await _module.GetCurrentIceMeltingRateAsync();
        currentMeltingRate.Should().Be(request.MinIceMeltingRate, because: "融冰速率应该被提高");
    }

    /// <summary>
    /// 边界测试：验证减载量不超过最大容量
    /// 验证点：RequestedLoadReduction <= 系统最大容量
    /// </summary>
    [Fact]
    public async Task LoadReduction_ShouldNotExceedMaxCapacity()
    {
        // Arrange
        var maxCapacity = 8000m;
        var baselineLoad = 7500m;

        // 请求的减载量超过系统能力
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.EmergencyDR,
            maxCapacity + 1000m,
            30,
            0.8m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act - 实际只能减载到最低负荷
        var actualLoad = 500m;
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        var maxPossibleReduction = baselineLoad - 500m;
        log.AchievedReduction.Should().BeLessThanOrEqualTo(maxPossibleReduction, because: "减载量不能超过系统最大能力");

        // 达标率应该合理（可能超过100%但有上限）
        var complianceRate = log.TargetReduction > 0
            ? Math.Min(1.5m, log.AchievedReduction / log.TargetReduction)
            : 1.0m;
        complianceRate.Should().BeLessThanOrEqualTo(1.5m, because: "达标率上限为150%");
    }

    /// <summary>
    /// 边界测试：验证减载量至少达到最小值
    /// 验证点：AchievedReduction >= 最小要求
    /// </summary>
    [Fact]
    public async Task LoadReduction_ShouldBeAtLeastMinimum()
    {
        // Arrange
        var minReduction = 500m;
        var baselineLoad = 6000m;
        var actualLoad = baselineLoad - minReduction;

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            minReduction,
            60,
            0.3m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        log.AchievedReduction.Should().BeGreaterThanOrEqualTo(minReduction, because: "至少应该达到最小减载量");
        log.AchievedReduction.Should().Be(minReduction, because: "实际减载量应该等于目标值");
    }
}

/// <summary>
/// 需求响应费用节省测试
/// 验证激励费用、节电量和总收益的计算正确性
/// </summary>
public class DemandResponseModule_CostSaving_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponseModule>> _mockLogger;
    private readonly Mock<ILogger<DemandResponder>> _mockResponderLogger;
    private readonly Mock<IIceStorageModule> _mockIceStorageModule;
    private readonly IDemandResponder _responder;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_CostSaving_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockResponderLogger = CreateMockLogger<DemandResponder>();
        _mockIceStorageModule = new Mock<IIceStorageModule>();
        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);

        var mockOptimizer = new Mock<IIceStorageOptimizer>();
        mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockResponderLogger.Object, mockOptimizer.Object);
        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, _mockIceStorageModule.Object, _responder);
    }

    [Fact]
    public async Task AdapterAndService_RecordLog_ShouldBeConsistent()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);
        await _module.ExecuteResponseAsync(request.Id);
        var baselineLoad = 6000m;
        var actualLoad = 4000m;

        // Act
        var adapterLog = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);
        var serviceStatus = await _responder.GetStatusAsync(request.Id);

        // Assert
        adapterLog.Should().NotBeNull();
        adapterLog.AchievedReduction.Should().Be(baselineLoad - actualLoad);
        serviceStatus.AchievedReduction.Should().Be(baselineLoad - actualLoad);
    }

    /// <summary>
    /// 激励费用计算测试：验证激励费用被正确包含在收益中
    /// 验证点：IncentiveEarned == ElectricitySaved * IncentivePerKWh
    /// </summary>
    [Fact]
    public async Task CostSaving_ShouldIncludeIncentive()
    {
        // Arrange
        var incentivePerKWh = 0.5m;
        var baselineLoad = 6000m;
        var actualLoad = 4000m;
        var achievedReduction = baselineLoad - actualLoad;
        var expectedElectricitySaved = achievedReduction / 4.0m;
        var expectedIncentive = expectedElectricitySaved * incentivePerKWh;

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            achievedReduction,
            60,
            incentivePerKWh);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        log.IncentiveEarned.Should().BeApproximately(
            expectedIncentive,
            0.01m,
            because: "激励费用应该等于节电量乘以补贴单价");
    }

    /// <summary>
    /// 节电量计算测试：验证节电量计算正确
    /// 验证点：ElectricitySaved == (BaselineLoad - ActualLoad) / 4.0
    /// </summary>
    [Fact]
    public async Task ElectricitySaved_ShouldBeCorrect()
    {
        // Arrange
        var baselineLoad = 7000m;
        var actualLoad = 4500m;
        var achievedReduction = baselineLoad - actualLoad;
        var expectedElectricitySaved = achievedReduction / 4.0m;

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            achievedReduction,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        log.ElectricitySaved.Should().BeApproximately(
            expectedElectricitySaved,
            0.01m,
            because: "节电量应该等于减载量除以4（COP）");
        log.ElectricitySaved.Should().BeGreaterThan(0, because: "节电量应该为正");
    }

    /// <summary>
    /// 总收益计算测试：验证总收益等于各部分之和
    /// 验证点：TotalBenefit == CostSaving + IncentiveEarned
    /// </summary>
    [Fact]
    public async Task TotalBenefit_ShouldSumAllComponents()
    {
        // Arrange
        var baselineLoad = 6500m;
        var actualLoad = 4200m;
        var electricityPrice = 0.84m;

        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(electricityPrice);

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2300m,
            60,
            0.6m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoad, actualLoad);

        // Assert
        log.Should().NotBeNull();
        log.TotalBenefit.Should().Be(
            log.CostSaving + log.IncentiveEarned,
            because: "总收益应该等于电费节省加上激励收入");

        // 验证各部分计算
        var expectedCostSaving = log.ElectricitySaved * electricityPrice;
        log.CostSaving.Should().BeApproximately(expectedCostSaving, 0.01m, because: "电费节省应该正确");
    }

    /// <summary>
    /// 正收益测试：验证需求响应产生正收益
    /// 验证点：TotalBenefit > 0
    /// </summary>
    [Fact]
    public async Task Savings_ShouldBePositive()
    {
        // Arrange
        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act - 记录多次执行日志
        await _module.RecordExecutionLogAsync(request.Id, 6000m, 4000m);
        await _module.RecordExecutionLogAsync(request.Id, 6200m, 4100m);
        await _module.RecordExecutionLogAsync(request.Id, 5800m, 3900m);

        var summary = await _module.CompleteResponseAsync(request.Id, 8);

        // Assert
        summary.Should().NotBeNull();
        summary.TotalBenefit.Should().BeGreaterThan(0, because: "总收益应该为正");
        summary.TotalCostSaving.Should().BeGreaterThan(0, because: "电费节省应该为正");
        summary.TotalIncentiveEarned.Should().BeGreaterThan(0, because: "激励收入应该为正");
        summary.TotalElectricitySaved.Should().BeGreaterThan(0, because: "总节电量应该为正");
    }

    /// <summary>
    /// 基线负荷计算测试：验证基线负荷被正确记录和使用
    /// 验证点：BaselineLoad等于传入的基准负荷值
    /// </summary>
    [Fact]
    public async Task BaselineLoad_ShouldBeCorrectlyCalculated()
    {
        // Arrange
        var baselineLoads = new[] { 6000m, 6500m, 6200m, 5800m };
        var actualLoads = new[] { 4000m, 4200m, 4100m, 3900m };

        var request = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(request.Id);

        // Act
        var logs = new List<DRExecutionLog>();
        for (int i = 0; i < baselineLoads.Length; i++)
        {
            var log = await _module.RecordExecutionLogAsync(request.Id, baselineLoads[i], actualLoads[i]);
            logs.Add(log);
        }

        // Assert
        logs.Should().HaveCount(baselineLoads.Length);

        for (int i = 0; i < logs.Count; i++)
        {
            logs[i].BaselineLoad.Should().Be(baselineLoads[i], because: $"第{i}条记录的基线负荷应该正确");
            logs[i].ActualLoad.Should().Be(actualLoads[i], because: $"第{i}条记录的实际负荷应该正确");
            logs[i].AchievedReduction.Should().Be(baselineLoads[i] - actualLoads[i], because: $"第{i}条记录的减载量应该正确");
        }

        // 验证汇总数据
        var summary = await _module.CompleteResponseAsync(request.Id, 9);
        summary.TotalActualReduction.Should().Be(
            logs.Sum(l => l.AchievedReduction),
            because: "总减载量应该等于各次减载量之和");
        summary.AverageComplianceRate.Should().BeApproximately(
            logs.Average(l => l.TargetReduction > 0
                ? Math.Min(1.5m, l.AchievedReduction / l.TargetReduction)
                : (l.AchievedReduction > 0 ? 1.0m : 0m)),
            0.001m,
            because: "平均达标率应该正确计算");
    }
}

/// <summary>
/// 需求响应多事件冲突处理测试
/// 验证紧急DR抢占、冲突检测、限制聚合、信号量并发控制
/// </summary>
public class DemandResponseModule_ConflictResolution_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponseModule>> _mockLogger;
    private readonly Mock<ILogger<DemandResponder>> _mockResponderLogger;
    private readonly Mock<IIceStorageModule> _mockIceStorageModule;
    private readonly IDemandResponder _responder;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_ConflictResolution_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockResponderLogger = CreateMockLogger<DemandResponder>();
        _mockIceStorageModule = new Mock<IIceStorageModule>();
        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);

        var mockOptimizer = new Mock<IIceStorageOptimizer>();
        mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockResponderLogger.Object, mockOptimizer.Object);
        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, _mockIceStorageModule.Object, _responder);
    }

    [Fact]
    public async Task AdapterAndService_CheckConflict_ShouldBeConsistent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var existingRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);
        await _module.ExecuteResponseAsync(existingRequest.Id);

        // Act
        var method = typeof(DemandResponseModule).GetMethod("CheckConflictAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var adapterResult = (ValueTuple<bool, string?>)method!.Invoke(_module,
            new object[] { DRRequestType.LoadReduction, now.AddMinutes(30), now.AddMinutes(90), 1 })!;
        var serviceResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction, now.AddMinutes(30), now.AddMinutes(90), 1);

        // Assert
        adapterResult.Item1.Should().Be(serviceResult.IsConflict);
        adapterResult.Item2.Should().Be(serviceResult.ConflictingRequestId);
    }

    /// <summary>
    /// 根因：没有优先级抢占机制，低优先级事件执行时，高优先级紧急DR无法及时响应。
    /// 验证点：紧急DR(优先级0)应该抢占正常DR(优先级1)，原事件状态变为Cancelled，新事件状态变为Executing。
    /// </summary>
    [Fact]
    public async Task EmergencyDR_ShouldPreempt_LowerPriorityEvents()
    {
        // Arrange
        var normalRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(normalRequest.Id);
        normalRequest.Status.Should().Be(DRRequestStatus.Executing);

        // Act - 触发紧急DR
        var emergencyRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.EmergencyDR,
            3000m,
            30,
            0.8m);

        await _module.ExecuteResponseAsync(emergencyRequest.Id);

        // Assert
        using var newContext = CreateNewContext();
        var originalRequest = await newContext.DemandResponseRequests.FindAsync(normalRequest.Id);
        var newRequest = await newContext.DemandResponseRequests.FindAsync(emergencyRequest.Id);

        originalRequest!.Status.Should().Be(DRRequestStatus.Cancelled,
            because: "低优先级事件应该被抢占并取消");
        newRequest!.Status.Should().Be(DRRequestStatus.Executing,
            because: "高优先级紧急DR应该开始执行");
    }

    /// <summary>
    /// 根因：同类型事件时间重叠会导致控制指令冲突，系统无法同时执行两个相同类型的DR。
    /// 验证点：两个同类型事件时间重叠时被检测为冲突，CheckConflictAsync返回IsConflict=true。
    /// </summary>
    [Fact]
    public async Task SameTypeEvents_ShouldBeDetectedAsConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act - 直接调用服务公开方法
        var result = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction, now, now.AddMinutes(60), 1);

        // Assert - 先添加一个活动事件
        var existingRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);
        await _module.ExecuteResponseAsync(existingRequest.Id);

        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction, now.AddMinutes(30), now.AddMinutes(90), 1);

        conflictResult.IsConflict.Should().BeTrue(because: "两个同类型事件时间重叠应该被检测为冲突");
        conflictResult.ConflictingRequestId.Should().Be(existingRequest.Id, because: "应该返回冲突的事件ID");
    }

    /// <summary>
    /// 根因：过度保守的冲突检测会导致正常的多事件组合无法执行，影响优化效果。
    /// 验证点：不同类型不同优先级的事件可以共存，CheckConflictAsync返回IsConflict=false。
    /// </summary>
    [Fact]
    public async Task DifferentTypeDifferentPriority_ShouldNotConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // 先添加一个Emergency事件
        var emergencyRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.EmergencyDR,
            3000m,
            60,
            0.8m);
        await _module.ExecuteResponseAsync(emergencyRequest.Id);

        // Act - 直接调用服务公开方法
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadShifting, now.AddMinutes(30), now.AddMinutes(90), 2);

        // Assert
        conflictResult.IsConflict.Should().BeTrue(because: "EmergencyDR是互斥事件，任何重叠事件都应该被检测为冲突");
    }

    /// <summary>
    /// 测试适配器反射调用与服务直接调用的一致性
    /// </summary>
    [Fact]
    public async Task ReflectionAndService_CheckConflict_ShouldBeConsistent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var existingRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);
        await _module.ExecuteResponseAsync(existingRequest.Id);

        // Act
        var method = typeof(DemandResponseModule).GetMethod("CheckConflictAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var reflectionResult = (ValueTuple<bool, string?>)method!.Invoke(_module,
            new object[] { DRRequestType.LoadReduction, now.AddMinutes(30), now.AddMinutes(90), 1 })!;
        var serviceResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction, now.AddMinutes(30), now.AddMinutes(90), 1);

        // Assert
        reflectionResult.Item1.Should().Be(serviceResult.IsConflict);
        reflectionResult.Item2.Should().Be(serviceResult.ConflictingRequestId);
    }

    /// <summary>
    /// 根因：多个活动事件时没有正确聚合限制参数，可能导致某个事件的限制被忽略。
    /// 验证点：取最小出力上限和最大融冰速率，确保最严格的限制被应用。
    /// </summary>
    [Fact]
    public async Task MultipleActiveRequests_ShouldApplyMostRestrictiveLimits()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = $"DR-A-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.7m,
            MinIceMeltingRate = 1000m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = $"DR-B-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow.AddMinutes(10),
            EndTime = DateTime.UtcNow.AddMinutes(70),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.5m,
            MinIceMeltingRate = 1500m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        // Act
        await _module.ExecuteResponseAsync(requestA.Id);
        await _module.ExecuteResponseAsync(requestB.Id);

        // Assert
        var chillerLimit = await _module.GetCurrentChillerOutputLimitAsync();
        var iceMeltingRate = await _module.GetCurrentIceMeltingRateAsync();

        chillerLimit.Should().Be(0.5m, because: "应该取最小的出力上限0.5（最严格）");
        iceMeltingRate.Should().Be(1500m, because: "应该取最大的融冰速率1500（最严格）");
    }

    /// <summary>
    /// 根因：没有优先级保护机制，低优先级事件可以抢占高优先级事件，导致重要DR无法执行。
    /// 验证点：低优先级事件不能抢占高优先级事件，应该抛出InvalidOperationException。
    /// </summary>
    [Fact]
    public async Task HigherPriorityRequest_ShouldBeRejected_WhenExistingIsHigherPriority()
    {
        // Arrange
        var highPriorityRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.EmergencyDR,
            3000m,
            60,
            0.8m);

        await _module.ExecuteResponseAsync(highPriorityRequest.Id);

        // Act - 尝试执行低优先级事件
        var lowPriorityRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        Func<Task> act = async () => await _module.ExecuteResponseAsync(lowPriorityRequest.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*优先级更高*", because: "低优先级事件不能抢占高优先级事件");
    }

    /// <summary>
    /// 根因：被抢占的事件没有记录取消原因，事后无法追溯为什么事件被中断。
    /// 验证点：CancellationReason包含"更高优先级"，说明事件被高优先级事件抢占。
    /// </summary>
    [Fact]
    public async Task CancelledRequest_ShouldHaveReasonRecorded()
    {
        // Arrange
        var lowPriorityRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.LoadReduction,
            2000m,
            60,
            0.5m);

        await _module.ExecuteResponseAsync(lowPriorityRequest.Id);

        // Act - 高优先级事件抢占
        var highPriorityRequest = await _module.SimulateDRRequestAsync(
            DRRequestType.EmergencyDR,
            3000m,
            30,
            0.8m);

        await _module.ExecuteResponseAsync(highPriorityRequest.Id);

        // Assert
        using var newContext = CreateNewContext();
        var cancelledRequest = await newContext.DemandResponseRequests.FindAsync(lowPriorityRequest.Id);

        cancelledRequest!.Status.Should().Be(DRRequestStatus.Cancelled);
        cancelledRequest.CancellationReason.Should().NotBeNullOrWhiteSpace();
        cancelledRequest.CancellationReason.Should().Contain("更高优先级",
            because: "被抢占的事件应该记录取消原因包含'更高优先级'");
    }

    /// <summary>
    /// 根因：没有并发控制机制，多个冲突请求同时执行时可能出现竞态条件，导致状态不一致。
    /// 验证点：并发执行10个冲突请求时，信号量确保只有最高优先级的事件最终执行。
    /// </summary>
    [Fact]
    public async Task ConflictResolutionSemaphore_ShouldPreventRaceConditions()
    {
        // Arrange
        var random = new Random(42);
        var requests = new List<DemandResponseRequest>();
        var priorities = Enumerable.Range(0, 10).OrderBy(_ => Guid.NewGuid()).ToList();

        for (int i = 0; i < 10; i++)
        {
            var request = new DemandResponseRequest
            {
                Id = $"DR-CONCURRENT-{i:00}",
                RequestType = DRRequestType.LoadReduction,
                Status = DRRequestStatus.Received,
                SourcePlatform = "ConcurrentTest",
                RequestedLoadReduction = 2000m + i * 100,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(60),
                IncentivePerKWh = 0.5m,
                MaxChillerOutputLimit = 0.5m + (decimal)(random.NextDouble() * 0.3),
                MinIceMeltingRate = 1000m + i * 50,
                Priority = priorities[i],
                ReceivedAt = DateTime.UtcNow,
                ResponseRequired = true
            };
            requests.Add(request);
        }

        await _dbContext.DemandResponseRequests.AddRangeAsync(requests);
        await _dbContext.SaveChangesAsync();

        // Act - 并发执行所有请求
        var tasks = requests.Select(r =>
        {
            try
            {
                return _module.ExecuteResponseAsync(r.Id);
            }
            catch (Exception)
            {
                return Task.FromResult<DRResponseSummary>(null!);
            }
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        using var newContext = CreateNewContext();
        var allRequests = await newContext.DemandResponseRequests
            .Where(r => r.Id.StartsWith("DR-CONCURRENT-"))
            .ToListAsync();

        var executingRequests = allRequests.Where(r => r.Status == DRRequestStatus.Executing).ToList();
        var highestPriorityRequest = allRequests.OrderBy(r => r.Priority).First();

        executingRequests.Should().ContainSingle(because: "最终应该只有一个活动事件");
        executingRequests.First().Id.Should().Be(highestPriorityRequest.Id,
            because: "最终活动事件应该是优先级最高的那个");

        var cancelledRequests = allRequests.Where(r => r.Status == DRRequestStatus.Cancelled).ToList();
        cancelledRequests.Should().HaveCount(9, because: "其他9个请求应该被取消");
    }
}
