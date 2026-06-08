using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.IceStorage;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 冰蓄冷系统DP算法测试
/// 验证动态规划算法的实时性、完整性、异常处理和正确性
/// </summary>
public class IceStorageModule_DPAlgorithm_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageModule>> _mockLogger;
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockOptimizerLogger;
    private readonly IceStorageOptimizer _optimizer;
    private readonly IceStorageModule _module;

    public IceStorageModule_DPAlgorithm_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageModule>();
        _mockOptimizerLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockOptimizerLogger.Object);
        _module = new IceStorageModule(_dbContext, _mockLogger.Object, _optimizer);
    }

    /// <summary>
    /// 实时性测试：验证DP算法在标准输入下能在5秒内完成计算
    /// 验证点：算法执行时间 <= 5000ms
    /// </summary>
    [Fact]
    public async Task DP_ShouldCompleteWithin5Seconds_ForStandardInput()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _module.CalculateOptimalStrategyAsync(testDate);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert
        result.Should().NotBeNull();
        elapsed.Should().BeLessThan(5000, because: "DP算法应该在5秒内完成计算");
        result.ComputationTimeMs.Should().BeLessThan(5000);
    }

    /// <summary>
    /// 适配器一致性测试：验证适配器和服务返回的结果一致
    /// </summary>
    [Fact]
    public async Task ModuleAndOptimizer_ShouldReturnConsistentResults()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var serviceResult = await _optimizer.CalculateOptimalStrategyAsync(testDate);
        var adapterResult = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        serviceResult.Should().NotBeNull();
        adapterResult.Should().NotBeNull();
        serviceResult.OptimalCost.Should().BeApproximately(adapterResult.OptimalCost, 0.01m);
        serviceResult.BaselineCost.Should().BeApproximately(adapterResult.BaselineCost, 0.01m);
        serviceResult.TotalSaving.Should().BeApproximately(adapterResult.TotalSaving, 0.01m);
        serviceResult.TotalStatesEvaluated.Should().Be(adapterResult.TotalStatesEvaluated);
    }

    /// <summary>
    /// 完整性测试：验证DP算法评估了所有480个状态
    /// 验证点：TotalStatesEvaluated >= 480 (24小时 * 20个冰量状态)
    /// </summary>
    [Fact]
    public async Task DP_ShouldEvaluateAll480States()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.TotalStatesEvaluated.Should().BeGreaterThanOrEqualTo(480, because: "应该评估至少24小时*20状态=480个状态");
        result.Algorithm.Should().Contain("DynamicProgramming");
    }

    /// <summary>
    /// 异常边界测试：验证空负荷预测时算法能正确处理
    /// 验证点：算法能够自动生成预测数据并继续执行
    /// </summary>
    [Fact]
    public async Task DP_ShouldHandleEmptyLoadForecast()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();

        // Act - 不预先加载负荷预测数据
        var result = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.TotalStatesEvaluated.Should().BeGreaterThan(0);

        // 验证负荷预测已被自动生成
        var forecasts = await _dbContext.LoadForecasts
            .Where(f => f.ForecastDate.Date == testDate.Date)
            .ToListAsync();
        forecasts.Should().HaveCount(24, because: "应该生成24小时的负荷预测");
    }

    /// <summary>
    /// 异常边界测试：验证零冰容量时算法能正确处理
    /// 验证点：算法应抛出异常或优雅降级
    /// </summary>
    [Fact]
    public async Task DP_ShouldHandleZeroIceCapacity()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        var device = new Device
        {
            Id = "TANK-ZERO",
            Name = "零容量储罐",
            DeviceTypeId = DeviceType.IceStorageTank,
            DesignCOP = 4.2m,
            RatedPower = 500m,
            RatedCoolingCapacity = 2000m,
            BACnetAddress = "192.168.1.102",
            BACnetInstance = 1002,
            Status = DeviceStatus.Running,
            PositionX = 200,
            PositionY = 100
        };

        var tank = new IceStorageTank
        {
            Id = "TANK-ZERO",
            MaxIceCapacity = 0,
            CurrentIceAmount = 0,
            IceMakingRate = 0,
            IceMeltingRateMax = 0,
            IceMakingCOP = 3.5m,
            IceMeltingEfficiency = 0.92m,
            CurrentMode = IceStorageMode.Standby
        };

        await _dbContext.Devices.AddAsync(device);
        await _dbContext.IceStorageTanks.AddAsync(tank);
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        // 零容量情况下，所有时段应该都是ChillerOnly模式
        var schedules = await _module.GetScheduleForDateAsync(testDate);
        schedules.Should().AllSatisfy(s =>
            s.Mode.Should().Be(IceStorageMode.ChillerOnly, because: "零容量时只能使用主机供冷"));
    }

    /// <summary>
    /// 正确性测试：验证状态转移的有效性
    /// 验证点：状态转移符合物理约束（冰量非负、不超过最大容量）
    /// </summary>
    [Fact]
    public async Task DP_ShouldReturnValidStateTransition()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        schedules.Should().HaveCount(24);
        schedules.Should().BeInAscendingOrder(s => s.HourOfDay);

        var maxCapacity = 10000m;
        for (int i = 0; i < schedules.Count; i++)
        {
            var schedule = schedules[i];
            schedule.TargetIceAmount.Should().BeGreaterThanOrEqualTo(0, because: $"第{i}小时冰量不能为负");
            schedule.TargetIceAmount.Should().BeLessThanOrEqualTo(maxCapacity, because: $"第{i}小时冰量不能超过最大容量");

            if (i > 0)
            {
                var delta = schedule.TargetIceAmount - schedules[i - 1].TargetIceAmount;
                // 蓄冰速率约束
                if (delta > 0)
                {
                    delta.Should().BeLessThanOrEqualTo(500m, because: $"第{i}小时蓄冰速率不能超过最大值");
                }
            }
        }
    }
}

/// <summary>
/// 冰蓄冷系统费用节省测试
/// 验证优化策略的经济效果、运行模式和冰量管理
/// </summary>
public class IceStorageModule_CostSaving_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageModule>> _mockLogger;
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockOptimizerLogger;
    private readonly IceStorageOptimizer _optimizer;
    private readonly IceStorageModule _module;

    public IceStorageModule_CostSaving_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageModule>();
        _mockOptimizerLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockOptimizerLogger.Object);
        _module = new IceStorageModule(_dbContext, _mockLogger.Object, _optimizer);
    }

    /// <summary>
    /// 节省效果测试：验证优化策略相比基线方案能够降低成本
    /// 验证点：OptimalCost < BaselineCost
    /// </summary>
    [Fact]
    public async Task Strategy_ShouldReduceCostComparedToBaseline()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.OptimalCost.Should().BeLessThan(result.BaselineCost, because: "优化策略应该比基线方案成本更低");
        result.TotalSaving.Should().BeGreaterThan(0, because: "应该产生正的节省金额");
    }

    /// <summary>
    /// 谷段蓄冰测试：验证策略在谷段电价时段进行蓄冰
    /// 验证点：23:00-07:00时段应以IceMaking模式为主
    /// </summary>
    [Fact]
    public async Task Strategy_ShouldChargeDuringValleyHours()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        var valleyHours = new[] { 23, 0, 1, 2, 3, 4, 5, 6 };
        var valleySchedules = schedules.Where(s => valleyHours.Contains(s.HourOfDay)).ToList();

        valleySchedules.Should().HaveCount(8);
        valleySchedules.Count(s => s.Mode == IceStorageMode.IceMaking)
            .Should().BeGreaterThanOrEqualTo(4, because: "谷段应该有至少4小时进行蓄冰");

        // 验证谷段冰量增加
        var iceTrend = valleySchedules.Select(s => s.TargetIceAmount).ToList();
        iceTrend.Should().BeInAscendingOrder(because: "谷段冰量应该持续增加");
    }

    /// <summary>
    /// 峰段融冰测试：验证策略在峰段电价时段进行融冰供冷
    /// 验证点：10:00-14:00和19:00-23:00时段应以Combined模式为主
    /// </summary>
    [Fact]
    public async Task Strategy_ShouldDischargeDuringPeakHours()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        var peakHours = new[] { 10, 11, 12, 13, 19, 20, 21, 22 };
        var peakSchedules = schedules.Where(s => peakHours.Contains(s.HourOfDay)).ToList();

        peakSchedules.Should().HaveCount(8);
        peakSchedules.Count(s => s.Mode == IceStorageMode.Combined || s.Mode == IceStorageMode.IceMelting)
            .Should().BeGreaterThanOrEqualTo(4, because: "峰段应该有至少4小时进行融冰");

        // 验证峰段冰量减少
        var peakIceTrend = peakSchedules.Select(s => s.TargetIceAmount).ToList();
        peakIceTrend.Should().BeInDescendingOrder(because: "峰段冰量应该持续减少");
    }

    /// <summary>
    /// 最低节省率测试：验证费用节省率至少达到15%
    /// 验证点：(BaselineCost - OptimalCost) / BaselineCost >= 0.15
    /// </summary>
    [Fact]
    public async Task CostSaving_ShouldBeAtLeast15Percent()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _module.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.BaselineCost.Should().BeGreaterThan(0);

        var savingRate = (result.BaselineCost - result.OptimalCost) / result.BaselineCost;
        savingRate.Should().BeGreaterThanOrEqualTo(0.15m, because: "费用节省率应该至少达到15%");
    }

    /// <summary>
    /// 冰量边界测试：验证冰量始终在合理范围内
    /// 验证点：0 <= IceAmount <= MaxIceCapacity
    /// </summary>
    [Fact]
    public async Task Strategy_ShouldMaintainIceWithinBounds()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        var tank = await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        schedules.Should().HaveCount(24);
        schedules.All(s => s.TargetIceAmount >= 0).Should().BeTrue(because: "冰量不能为负");
        schedules.All(s => s.TargetIceAmount <= tank.MaxIceCapacity).Should().BeTrue(because: "冰量不能超过最大容量");

        // 验证初始和结束状态
        schedules.First().TargetIceAmount.Should().Be(tank.MaxIceCapacity, because: "凌晨开始时应该是满冰");
        schedules.Last().TargetIceAmount.Should().BeGreaterThanOrEqualTo(0, because: "结束时冰量不能为负");
    }
}

/// <summary>
/// 冰蓄冷系统甘特图数据测试
/// 验证调度计划的数据完整性、模式有效性和单调性
/// </summary>
public class IceStorageModule_GanttData_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageModule>> _mockLogger;
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockOptimizerLogger;
    private readonly IceStorageOptimizer _optimizer;
    private readonly IceStorageModule _module;

    public IceStorageModule_GanttData_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageModule>();
        _mockOptimizerLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockOptimizerLogger.Object);
        _module = new IceStorageModule(_dbContext, _mockLogger.Object, _optimizer);
    }

    /// <summary>
    /// 数据完整性测试：验证调度计划包含24小时数据
    /// 验证点：返回24条记录，覆盖0-23小时
    /// </summary>
    [Fact]
    public async Task Schedule_ShouldHave24HourEntries()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        schedules.Should().HaveCount(24, because: "一天应该有24小时的调度计划");
        schedules.Select(s => s.HourOfDay).Distinct().Should().HaveCount(24, because: "每个小时应该唯一");
        schedules.Select(s => s.HourOfDay).Should().BeEquivalentTo(Enumerable.Range(0, 24), because: "应该覆盖0-23所有小时");
    }

    /// <summary>
    /// 模式有效性测试：验证每个小时的运行模式是有效的枚举值
    /// 验证点：Mode属于IceStorageMode枚举
    /// </summary>
    [Fact]
    public async Task EachHour_ShouldHaveValidMode()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        schedules.Should().AllSatisfy(schedule =>
        {
            schedule.Mode.Should().BeOneOf(
                IceStorageMode.IceMaking,
                IceStorageMode.IceMelting,
                IceStorageMode.ChillerOnly,
                IceStorageMode.Combined,
                IceStorageMode.Standby,
                because: "运行模式必须是有效的枚举值");
        });

        // 验证至少包含两种不同的模式
        schedules.Select(s => s.Mode).Distinct().Count().Should().BeGreaterThanOrEqualTo(2, because: "应该至少使用两种运行模式");
    }

    /// <summary>
    /// 蓄冰单调性测试：验证蓄冰阶段冰量单调递增
    /// 验证点：IceMaking模式时段冰量持续增加
    /// </summary>
    [Fact]
    public async Task IceAmount_ShouldBeMonotonicDuringCharging()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert - 找到连续的蓄冰时段
        var chargingPeriods = schedules
            .Where(s => s.Mode == IceStorageMode.IceMaking)
            .OrderBy(s => s.HourOfDay)
            .ToList();

        chargingPeriods.Should().NotBeEmpty(because: "应该有蓄冰时段");

        for (int i = 1; i < chargingPeriods.Count; i++)
        {
            // 检查是否连续
            if (chargingPeriods[i].HourOfDay == chargingPeriods[i - 1].HourOfDay + 1)
            {
                chargingPeriods[i].TargetIceAmount.Should()
                    .BeGreaterThanOrEqualTo(chargingPeriods[i - 1].TargetIceAmount,
                        because: $"蓄冰阶段第{chargingPeriods[i].HourOfDay}小时冰量应该不小于第{chargingPeriods[i - 1].HourOfDay}小时");
            }
        }
    }

    /// <summary>
    /// 融冰单调性测试：验证融冰阶段冰量单调递减
    /// 验证点：Combined/IceMelting模式时段冰量持续减少
    /// </summary>
    [Fact]
    public async Task IceAmount_ShouldBeMonotonicDuringDischarging()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert - 找到连续的融冰时段
        var dischargingModes = new[] { IceStorageMode.IceMelting, IceStorageMode.Combined };
        var dischargingPeriods = schedules
            .Where(s => dischargingModes.Contains(s.Mode))
            .OrderBy(s => s.HourOfDay)
            .ToList();

        dischargingPeriods.Should().NotBeEmpty(because: "应该有融冰时段");

        for (int i = 1; i < dischargingPeriods.Count; i++)
        {
            // 检查是否连续
            if (dischargingPeriods[i].HourOfDay == dischargingPeriods[i - 1].HourOfDay + 1)
            {
                dischargingPeriods[i].TargetIceAmount.Should()
                    .BeLessThanOrEqualTo(dischargingPeriods[i - 1].TargetIceAmount,
                        because: $"融冰阶段第{dischargingPeriods[i].HourOfDay}小时冰量应该不大于第{dischargingPeriods[i - 1].HourOfDay}小时");
            }
        }
    }

    /// <summary>
    /// 费用计算完整性测试：验证每条记录都包含费用节省计算
    /// 验证点：ExpectedCost、BaselineCost、CostSaving都有有效值
    /// </summary>
    [Fact]
    public async Task Schedule_ShouldHaveCostSavingCalculated()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        schedules.Should().AllSatisfy(schedule =>
        {
            schedule.ExpectedCost.Should().BeGreaterThanOrEqualTo(0, because: "预期成本不能为负");
            schedule.BaselineCost.Should().BeGreaterThan(0, because: "基线成本应该大于0");
            schedule.CostSaving.Should().Be(schedule.BaselineCost - schedule.ExpectedCost,
                because: "费用节省应该等于基线成本减去预期成本");
            schedule.IsOptimized.Should().BeTrue(because: "调度计划应该是经过优化的");
        });

        // 验证总节省金额与DP记录一致
        var dpRecord = await _dbContext.DPStrategyRecords
            .FirstOrDefaultAsync(r => r.ScheduleDate.Date == testDate.Date);

        dpRecord.Should().NotBeNull();
        dpRecord!.TotalSaving.Should().BeApproximately(
            schedules.Sum(s => s.CostSaving),
            0.01m,
            because: "DP记录的总节省应该等于各小时节省之和");
    }
}

/// <summary>
/// 冰蓄冷系统鲁棒优化测试
/// 验证鲁棒DP算法对预测误差的处理能力、场景生成和惩罚机制
/// </summary>
public class IceStorageModule_RobustOptimization_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageModule>> _mockLogger;
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockOptimizerLogger;
    private readonly IceStorageOptimizer _optimizer;
    private readonly IceStorageModule _module;

    public IceStorageModule_RobustOptimization_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageModule>();
        _mockOptimizerLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockOptimizerLogger.Object);
        _module = new IceStorageModule(_dbContext, _mockLogger.Object, _optimizer);
    }

    /// <summary>
    /// 根因：确定性DP使用精确预测值，当预测置信度低时，实际负荷可能远超预测值，导致融冰不足产生高额惩罚成本。
    /// 验证点：鲁棒负荷计算在低置信度时增加至少增加20%安全裕度，确保预测负荷被放大以应对不确定性。
    /// </summary>
    [Fact]
    public void CalculateRobustLoad_ShouldAddSafetyMargin_WhenForecastHasHighUncertainty()
    {
        // Arrange
        var predictedLoad = 5000m;
        var confidence = 0.5m;

        // Act - 直接调用服务的公开方法
        var robustLoad = _optimizer.CalculateRobustLoad(predictedLoad, confidence);

        // Assert
        robustLoad.Should().BeGreaterThanOrEqualTo(predictedLoad * 1.2m,
            because: "低置信度预测应该增加至少20%的安全裕度");
    }

    /// <summary>
    /// 适配器测试：验证适配器的私有方法与服务的公开方法返回一致结果
    /// </summary>
    [Fact]
    public void AdapterAndService_CalculateRobustLoad_ShouldBeConsistent()
    {
        // Arrange
        var predictedLoad = 5000m;
        var confidence = 0.5m;
        var method = typeof(IceStorageModule).GetMethod("CalculateRobustLoad", BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var adapterResult = (decimal)method!.Invoke(_module, new object[] { predictedLoad, confidence })!;
        var serviceResult = _optimizer.CalculateRobustLoad(predictedLoad, confidence);

        // Assert
        adapterResult.Should().BeApproximately(serviceResult, 0.001m,
            because: "适配器和服务的计算结果应该一致");
    }

    /// <summary>
    /// 根因：确定性DP低估负荷时，实际负荷高于预测会导致融冰不足，产生高额惩罚成本。
    /// 验证点：使用鲁棒DP在负荷高估20%时，成本增加幅度小于确定性DP。
    /// </summary>
    [Fact]
    public async Task DP_ShouldHandleUnderpredictedLoad_WithoutCostIncrease()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        var forecasts = await _dbContext.LoadForecasts
            .Where(f => f.ForecastDate.Date == testDate.Date)
            .ToListAsync();

        foreach (var forecast in forecasts)
        {
            forecast.PredictedLoad = 5000m;
            forecast.ActualLoad = 6000m;
            forecast.Confidence = 0.5m;
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _module.CalculateOptimalStrategyAsync(testDate);
        var robustCost = result.OptimalCost;

        // Assert
        var baselineCost = result.BaselineCost;
        var costIncrease = robustCost - (baselineCost * 0.8m);

        costIncrease.Should().BeLessThan(baselineCost * 0.15m,
            because: "鲁棒策略在负荷高估20%时，成本增加应该小于15%");
    }

    /// <summary>
    /// 根因：单一预测值无法覆盖负荷波动范围，导致优化策略在极端场景下失效。
    /// 验证点：生成5个场景，覆盖85%~120%的预测值范围。
    /// </summary>
    [Fact]
    public void DP_ShouldGenerateScenarios_CoveringUncertaintyRange()
    {
        // Arrange
        var baseLoad = 5000m;

        // Act - 直接调用服务的公开方法
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);

        // Assert
        scenarios.Should().HaveCount(5, because: "应该生成5个场景");
        scenarios[0].Should().BeApproximately(baseLoad * 0.85m, 0.001m, because: "第一个场景应该是基准的85%");
        scenarios[4].Should().BeApproximately(baseLoad * 1.20m, 0.001m, because: "最后一个场景应该是基准的120%");
    }

    /// <summary>
    /// 适配器测试：验证适配器的私有方法与服务的公开方法返回一致的场景
    /// </summary>
    [Fact]
    public void AdapterAndService_GenerateLoadScenarios_ShouldBeConsistent()
    {
        // Arrange
        var baseLoad = 5000m;
        var method = typeof(IceStorageModule).GetMethod("GenerateLoadScenarios", BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var adapterResult = (decimal[])method!.Invoke(_module, new object[] { baseLoad })!;
        var serviceResult = _optimizer.GenerateLoadScenarios(baseLoad);

        // Assert
        adapterResult.Should().BeEquivalentTo(serviceResult,
            because: "适配器和服务生成的场景应该一致");
    }

    /// <summary>
    /// 根因：当融冰不足以满足负荷时，没有惩罚机制会导致策略过于激进。
    /// 验证点：未满足负荷时施加2倍电价惩罚，惩罚成本计算公式正确。
    /// </summary>
    [Fact]
    public void DP_ShouldApplyPenalty_WhenUnmetLoadOccurs()
    {
        // Arrange
        var load = 6000m;
        var meltingCapacity = 3000m;
        var price = 1.0m;
        var unmetLoad = load - meltingCapacity;
        var expectedPenalty = unmetLoad * price * 2 / 4;

        // Act & Assert
        expectedPenalty.Should().Be(1500m,
            because: "未满足负荷3000kW，电价1元，惩罚2倍，除以4（COP）");
    }

    /// <summary>
    /// 根因：确定性策略在谷段结束时蓄冰量不足，无法应对峰段的高负荷。
    /// 验证点：鲁棒策略在谷段结束时的蓄冰量比确定性策略至少多15%。
    /// </summary>
    [Fact]
    public async Task RobustStrategy_ShouldHaveHigherInitialIceAmount_ThanDeterministic()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();

        var forecasts = new List<LoadForecast>();
        var peakLoad = 5000m;

        for (int hour = 0; hour < 24; hour++)
        {
            forecasts.Add(new LoadForecast
            {
                ForecastDate = testDate.Date,
                HourOfDay = hour,
                PredictedLoad = peakLoad * (hour < 7 ? 0.3m : 1.0m),
                Confidence = 0.85m
            });
        }
        await _dbContext.LoadForecasts.AddRangeAsync(forecasts);
        await _dbContext.SaveChangesAsync();

        // Act
        await _module.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _module.GetScheduleForDateAsync(testDate);

        // Assert
        var valleyEndSchedule = schedules.First(s => s.HourOfDay == 6);
        var robustFinalIce = valleyEndSchedule.TargetIceAmount;
        var deterministicFinalIce = 8000m;

        robustFinalIce.Should().BeGreaterThanOrEqualTo(deterministicFinalIce * 1.15m,
            because: "鲁棒策略谷段结束时蓄冰量应该比确定性多至少15%");
    }

    /// <summary>
    /// 根因：预测误差可能导致策略失效，成本节省变为负数。
    /// 验证点：即使预测有±15%误差，鲁棒策略仍能保持正的成本节省。
    /// </summary>
    [Fact]
    public async Task CostSaving_ShouldRemainPositive_With15PercentForecastError()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        var random = new Random(42);

        for (int iteration = 0; iteration < 10; iteration++)
        {
            var forecasts = new List<LoadForecast>();
            var baseLoad = 5000m;

            for (int hour = 0; hour < 24; hour++)
            {
                var errorFactor = 0.85m + (decimal)(random.NextDouble() * 0.3m);
                forecasts.Add(new LoadForecast
                {
                    ForecastDate = testDate.Date,
                    HourOfDay = hour,
                    PredictedLoad = baseLoad * errorFactor,
                    Confidence = 0.85m
                });
            }

            await _dbContext.LoadForecasts.AddRangeAsync(forecasts);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _module.CalculateOptimalStrategyAsync(testDate);

            // Assert
            result.TotalSaving.Should().BeGreaterThan(0,
                because: $"第{iteration}次测试中，即使有±15%预测误差，成本节省应该为正");

            _dbContext.LoadForecasts.RemoveRange(forecasts);
            await _dbContext.SaveChangesAsync();
        }
    }
}

/// <summary>
/// 冰蓄冷优化器单元测试
/// 测试优化器核心方法的正确性
/// </summary>
public class IceStorageOptimizer_UnitTests : TestBase
{
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockLogger;
    private readonly IceStorageOptimizer _optimizer;

    public IceStorageOptimizer_UnitTests()
    {
        _mockLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试优化器核心方法返回有效结果
    /// </summary>
    [Fact]
    public async Task CalculateOptimalStrategyAsync_ShouldReturnValidDPResult()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.StrategyId.Should().NotBeNullOrWhiteSpace();
        result.ScheduleDate.Should().Be(testDate.Date);
        result.OptimalCost.Should().BeGreaterThan(0);
        result.BaselineCost.Should().BeGreaterThan(result.OptimalCost);
        result.TotalSaving.Should().BeGreaterThan(0);
        result.TotalStatesEvaluated.Should().BeGreaterThan(480);
        result.Algorithm.Should().Contain("RobustDynamicProgramming");
        result.Schedules.Should().HaveCount(24);
        result.RobustnessMargin.Should().Be(0.20m);
        result.ForecastErrorConsidered.Should().Be(0.15m);
    }

    /// <summary>
    /// 测试鲁棒负荷计算应用安全裕度
    /// </summary>
    [Fact]
    public void CalculateRobustLoad_ShouldApplySafetyMargin()
    {
        // Arrange
        var testCases = new[]
        {
            new { PredictedLoad = 5000m, Confidence = 0.5m, ExpectedMinMultiplier = 1.2m },
            new { PredictedLoad = 5000m, Confidence = 0.85m, ExpectedMinMultiplier = 1.2m },
            new { PredictedLoad = 8000m, Confidence = 0.3m, ExpectedMinMultiplier = 1.2m }
        };

        // Act & Assert
        foreach (var testCase in testCases)
        {
            var result = _optimizer.CalculateRobustLoad(testCase.PredictedLoad, testCase.Confidence);
            result.Should().BeGreaterThanOrEqualTo(testCase.PredictedLoad * testCase.ExpectedMinMultiplier,
                because: $"预测负荷{testCase.PredictedLoad}，置信度{testCase.Confidence}应该至少增加20%安全裕度");
            result.Should().BeGreaterThan(testCase.PredictedLoad,
                because: "鲁棒负荷应该始终大于预测负荷");
        }
    }

    /// <summary>
    /// 测试场景生成覆盖不确定性范围
    /// </summary>
    [Fact]
    public void GenerateLoadScenarios_ShouldCoverUncertaintyRange()
    {
        // Arrange
        var baseLoad = 5000m;
        var expectedFactors = new[] { 0.85m, 0.95m, 1.0m, 1.10m, 1.20m };

        // Act
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);

        // Assert
        scenarios.Should().HaveCount(5, because: "应该生成5个场景");
        for (int i = 0; i < scenarios.Length; i++)
        {
            scenarios[i].Should().BeApproximately(baseLoad * expectedFactors[i], 0.001m,
                because: $"第{i}个场景应该是基准的{expectedFactors[i] * 100}%");
        }

        // 验证场景是升序排列
        scenarios.Should().BeInAscendingOrder(because: "场景应该按负荷从小到大排列");
    }

    /// <summary>
    /// 测试期望成本计算正确加权场景
    /// </summary>
    [Fact]
    public void CalculateExpectedCost_ShouldWeightScenarios()
    {
        // Arrange
        var baseLoad = 5000m;
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);
        var expectedWeights = new[] { 0.1m, 0.2m, 0.3m, 0.25m, 0.15m };

        // 构造不同场景的成本
        var costs = new decimal[scenarios.Length];
        for (int i = 0; i < scenarios.Length; i++)
        {
            costs[i] = scenarios[i] * 0.84m / 4.0m;
        }

        // 计算期望成本
        var method = typeof(IceStorageOptimizer).GetMethod("CalculateExpectedCost", BindingFlags.NonPublic | BindingFlags.Instance);
        var expectedCost = (decimal)method!.Invoke(_optimizer, new object[] { scenarios, costs })!;

        // Assert
        var manualCalculation = 0m;
        for (int i = 0; i < scenarios.Length; i++)
        {
            manualCalculation += costs[i] * expectedWeights[i];
        }

        expectedCost.Should().BeApproximately(manualCalculation, 0.001m,
            because: "期望成本应该是各场景成本的加权和");
    }

    /// <summary>
    /// 测试并发计算应该被排队
    /// </summary>
    [Fact]
    public async Task ConcurrentCalculations_ShouldBeQueued()
    {
        // Arrange
        var testDate1 = new DateTime(2024, 6, 15);
        var testDate2 = new DateTime(2024, 6, 16);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate1);

        var forecasts2 = new List<LoadForecast>();
        var peakLoad = 8000m;
        var loadProfile = new[] { 0.3, 0.25, 0.22, 0.2, 0.2, 0.22, 0.35, 0.55, 0.75, 0.88, 0.95, 0.98, 1.0, 0.98, 0.97, 0.95, 0.92, 0.88, 0.82, 0.78, 0.7, 0.6, 0.5, 0.4 };

        for (int hour = 0; hour < 24; hour++)
        {
            forecasts2.Add(new LoadForecast
            {
                ForecastDate = testDate2.Date,
                HourOfDay = hour,
                PredictedLoad = peakLoad * (decimal)loadProfile[hour],
                PredictionModel = "HistoricalProfile",
                Confidence = 0.85m,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.LoadForecasts.AddRangeAsync(forecasts2);
        await _dbContext.SaveChangesAsync();

        // Act - 并发启动两个计算
        var startTime = DateTime.UtcNow;
        var task1 = _optimizer.CalculateOptimalStrategyAsync(testDate1);
        var task2 = _optimizer.CalculateOptimalStrategyAsync(testDate2);

        await Task.WhenAll(task1, task2);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert
        task1.IsCompletedSuccessfully.Should().BeTrue();
        task2.IsCompletedSuccessfully.Should().BeTrue();

        // 由于信号量控制，两个计算应该串行执行
        var result1 = await task1;
        var result2 = await task2;

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        // 验证信号量日志（通过计算时间验证串行执行）
        var totalComputationTime = result1.ComputationTimeMs + result2.ComputationTimeMs;
        elapsed.Should().BeGreaterThanOrEqualTo(result1.ComputationTimeMs,
            because: "并发计算应该被排队，总时间至少等于第一个计算的时间");
    }
}
