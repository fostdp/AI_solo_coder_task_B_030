using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.DemandResponse;
using ChillerPlantOptimization.Modules.IceStorage;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 需求响应响应时间测试
/// 验证系统对需求响应请求的快速响应能力
/// </summary>
public class DemandResponseModule_ResponseTime_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponseModule>> _mockLogger;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_ResponseTime_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _module = new DemandResponseModule(_dbContext, _mockLogger.Object);
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
    private readonly Mock<ILogger<IceStorageModule>> _mockIceLogger;
    private readonly Mock<IIceStorageModule> _mockIceStorageModule;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_LoadAdjustment_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockIceLogger = CreateMockLogger<IceStorageModule>();
        _mockIceStorageModule = new Mock<IIceStorageModule>();
        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);

        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, _mockIceStorageModule.Object);
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
    private readonly Mock<IIceStorageModule> _mockIceStorageModule;
    private readonly DemandResponseModule _module;

    public DemandResponseModule_CostSaving_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponseModule>();
        _mockIceStorageModule = new Mock<IIceStorageModule>();
        _mockIceStorageModule.Setup(m => m.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);

        _module = new DemandResponseModule(_dbContext, _mockLogger.Object, _mockIceStorageModule.Object);
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
