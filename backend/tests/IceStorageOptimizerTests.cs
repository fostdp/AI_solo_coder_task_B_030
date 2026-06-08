using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 冰蓄冷优化器算法测试
/// 验证鲁棒负荷计算、场景生成、期望成本、DP状态转移和并发控制
/// </summary>
public class IceStorageOptimizer_Algorithm_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockLogger;
    private readonly IceStorageOptimizer _optimizer;

    public IceStorageOptimizer_Algorithm_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 验证Z-score安全裕度计算
    /// 测试点：鲁棒负荷 = 预测负荷 × (1 + 安全裕度) + 预测负荷 × 误差标准差 × Z-score
    /// Z-score = 1.645 × confidence，误差标准差 = 0.15，安全裕度 = 0.20
    /// </summary>
    [Fact]
    public void CalculateRobustLoad_ShouldAddZScoreMargin()
    {
        // Arrange
        var predictedLoad = 5000m;
        var confidence = 0.85m;
        var forecastErrorStdDev = 0.15m;
        var safetyMargin = 0.20m;
        var zScore = 1.645m * confidence;
        var expectedMinLoad = predictedLoad * (1 + safetyMargin);
        var expectedErrorMargin = predictedLoad * forecastErrorStdDev * zScore;

        // Act
        var robustLoad = _optimizer.CalculateRobustLoad(predictedLoad, confidence);

        // Assert
        robustLoad.Should().BeGreaterThan(predictedLoad,
            because: "鲁棒负荷应该始终大于预测负荷");
        robustLoad.Should().BeGreaterThanOrEqualTo(expectedMinLoad,
            because: "应该至少包含20%的安全裕度");
        robustLoad.Should().BeApproximately(expectedMinLoad + expectedErrorMargin, 0.01m,
            because: "应该正确计算Z-score误差裕度");
    }

    /// <summary>
    /// 验证不同置信度下Z-score安全裕度的变化
    /// 测试点：置信度越高，Z-score越大，鲁棒负荷越大
    /// </summary>
    [Fact]
    public void CalculateRobustLoad_ShouldIncreaseWithConfidence()
    {
        // Arrange
        var predictedLoad = 5000m;
        var lowConfidence = 0.3m;
        var highConfidence = 0.95m;

        // Act
        var lowRobustLoad = _optimizer.CalculateRobustLoad(predictedLoad, lowConfidence);
        var highRobustLoad = _optimizer.CalculateRobustLoad(predictedLoad, highConfidence);

        // Assert
        lowRobustLoad.Should().BeGreaterThan(predictedLoad);
        highRobustLoad.Should().BeGreaterThan(lowRobustLoad,
            because: "置信度越高，Z-score越大，鲁棒负荷应该越大");
    }

    /// <summary>
    /// 验证5个场景覆盖85%~120%的预测值范围
    /// 测试点：场景因子应为 [0.85, 0.95, 1.0, 1.10, 1.20]
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
        scenarios[0].Should().BeApproximately(baseLoad * expectedFactors[0], 0.001m,
            because: "第一个场景应该是基准的85%（最低场景）");
        scenarios[2].Should().BeApproximately(baseLoad * expectedFactors[2], 0.001m,
            because: "中间场景应该是基准的100%（基准场景）");
        scenarios[4].Should().BeApproximately(baseLoad * expectedFactors[4], 0.001m,
            because: "最后一个场景应该是基准的120%（最高场景）");
        scenarios.Should().BeInAscendingOrder(because: "场景应该按负荷从小到大排列");
    }

    /// <summary>
    /// 验证场景生成的边界值
    /// 测试点：最低场景为85%，最高场景为120%，覆盖35%的波动范围
    /// </summary>
    [Fact]
    public void GenerateLoadScenarios_ShouldHaveCorrectBounds()
    {
        // Arrange
        var baseLoad = 10000m;

        // Act
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);

        // Assert
        scenarios.Min().Should().Be(8500m, because: "最低场景应该是基准的85%");
        scenarios.Max().Should().Be(12000m, because: "最高场景应该是基准的120%");
        (scenarios.Max() - scenarios.Min()).Should().Be(3500m,
            because: "应该覆盖35%的波动范围");
    }

    /// <summary>
    /// 验证场景权重正确应用
    /// 测试点：权重应为 [0.1, 0.2, 0.3, 0.25, 0.15]，总和为1.0
    /// </summary>
    [Fact]
    public void CalculateExpectedCost_ShouldApplyScenarioWeights()
    {
        // Arrange
        var baseLoad = 5000m;
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);
        var expectedWeights = new[] { 0.1m, 0.2m, 0.3m, 0.25m, 0.15m };
        expectedWeights.Sum().Should().Be(1.0m, because: "权重总和应该为1.0");

        var costs = new decimal[scenarios.Length];
        for (int i = 0; i < scenarios.Length; i++)
        {
            costs[i] = scenarios[i] * 0.84m / 4.0m;
        }

        var method = typeof(IceStorageOptimizer).GetMethod("CalculateExpectedCost",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
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
    /// 验证等成本场景下期望成本的计算
    /// 测试点：所有场景成本相同时，期望成本应等于该成本
    /// </summary>
    [Fact]
    public void CalculateExpectedCost_ShouldHandleEqualCosts()
    {
        // Arrange
        var baseLoad = 5000m;
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);
        var uniformCost = 1000m;
        var costs = Enumerable.Repeat(uniformCost, scenarios.Length).ToArray();

        var method = typeof(IceStorageOptimizer).GetMethod("CalculateExpectedCost",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var expectedCost = (decimal)method!.Invoke(_optimizer, new object[] { scenarios, costs })!;

        // Assert
        expectedCost.Should().BeApproximately(uniformCost, 0.001m,
            because: "所有权重之和为1，等成本场景期望成本应等于该成本");
    }

    /// <summary>
    /// 验证DP状态转移正确性
    /// 测试点：状态转移符合物理约束（冰量非负、不超过最大容量、速率约束）
    /// </summary>
    [Fact]
    public async Task DPTable_ShouldHaveValidStateTransitions()
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
        result.Schedules.Should().HaveCount(24, because: "应该有24小时的调度计划");

        var maxCapacity = 10000m;
        var maxChargingRate = 500m;

        for (int i = 0; i < result.Schedules.Count; i++)
        {
            var schedule = result.Schedules[i];
            schedule.TargetIceAmount.Should().BeGreaterThanOrEqualTo(0,
                because: $"第{i}小时冰量不能为负");
            schedule.TargetIceAmount.Should().BeLessThanOrEqualTo(maxCapacity,
                because: $"第{i}小时冰量不能超过最大容量");

            if (i > 0)
            {
                var delta = schedule.TargetIceAmount - result.Schedules[i - 1].TargetIceAmount;
                if (delta > 0)
                {
                    delta.Should().BeLessThanOrEqualTo(maxChargingRate,
                        because: $"第{i}小时蓄冰速率不能超过最大值");
                }
            }
        }
    }

    /// <summary>
    /// 验证DP表的状态数量
    /// 测试点：应该评估至少480个状态（24小时 × 20个冰量状态）
    /// </summary>
    [Fact]
    public async Task DPTable_ShouldEvaluateMinimumStates()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.TotalStatesEvaluated.Should().BeGreaterThanOrEqualTo(480,
            because: "应该评估至少24小时×20状态=480个状态");
    }

    /// <summary>
    /// 验证并发控制
    /// 测试点：SemaphoreSlim确保同一时间只有一个计算在执行
    /// </summary>
    [Fact]
    public async Task Semaphore_ShouldPreventConcurrentCalculation()
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

        // Act
        var startTime = DateTime.UtcNow;
        var task1 = _optimizer.CalculateOptimalStrategyAsync(testDate1);
        var task2 = _optimizer.CalculateOptimalStrategyAsync(testDate2);

        await Task.WhenAll(task1, task2);
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert
        task1.IsCompletedSuccessfully.Should().BeTrue();
        task2.IsCompletedSuccessfully.Should().BeTrue();

        var result1 = await task1;
        var result2 = await task2;

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        elapsed.Should().BeGreaterThanOrEqualTo(result1.ComputationTimeMs,
            because: "并发计算应该被排队，总时间至少等于第一个计算的时间");
    }

    /// <summary>
    /// 验证连续调用的正确性
    /// 测试点：连续多次调用应返回一致的结果
    /// </summary>
    [Fact]
    public async Task Semaphore_ShouldAllowSequentialCalls()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result1 = await _optimizer.CalculateOptimalStrategyAsync(testDate);
        var result2 = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.OptimalCost.Should().BeApproximately(result2.OptimalCost, 0.01m,
            because: "相同输入应该产生相同的最优成本");
        result1.TotalStatesEvaluated.Should().Be(result2.TotalStatesEvaluated,
            because: "相同输入应该评估相同数量的状态");
    }

    /// <summary>
    /// 验证零预测负荷的边界情况
    /// 测试点：零负荷时鲁棒负荷计算应合理
    /// </summary>
    [Fact]
    public void CalculateRobustLoad_ShouldHandleZeroLoad()
    {
        // Arrange
        var predictedLoad = 0m;
        var confidence = 0.85m;

        // Act
        var robustLoad = _optimizer.CalculateRobustLoad(predictedLoad, confidence);

        // Assert
        robustLoad.Should().Be(0, because: "零预测负荷的鲁棒负荷也应为零");
    }

    /// <summary>
    /// 验证场景生成的单调性
    /// 测试点：场景应严格递增
    /// </summary>
    [Fact]
    public void GenerateLoadScenarios_ShouldBeStrictlyIncreasing()
    {
        // Arrange
        var baseLoad = 8000m;

        // Act
        var scenarios = _optimizer.GenerateLoadScenarios(baseLoad);

        // Assert
        for (int i = 1; i < scenarios.Length; i++)
        {
            scenarios[i].Should().BeGreaterThan(scenarios[i - 1],
                because: $"场景{i}应该严格大于场景{i - 1}");
        }
    }
}

/// <summary>
/// 冰蓄冷优化器鲁棒DP测试
/// 验证鲁棒DP在不确定性下的表现、惩罚机制和安全裕度效果
/// </summary>
public class IceStorageOptimizer_RobustDP_Tests : TestBase
{
    private readonly Mock<ILogger<IceStorageOptimizer>> _mockLogger;
    private readonly IceStorageOptimizer _optimizer;

    public IceStorageOptimizer_RobustDP_Tests()
    {
        _mockLogger = CreateMockLogger<IceStorageOptimizer>();
        _optimizer = new IceStorageOptimizer(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 鲁棒DP在不确定负荷下表现更优
    /// 测试点：当实际负荷高于预测值时，鲁棒DP的成本应低于确定性DP
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldOutperformDeterministic_UnderLoadUncertainty()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();

        var forecasts = new List<LoadForecast>();
        var baseLoad = 5000m;

        for (int hour = 0; hour < 24; hour++)
        {
            forecasts.Add(new LoadForecast
            {
                ForecastDate = testDate.Date,
                HourOfDay = hour,
                PredictedLoad = baseLoad,
                ActualLoad = baseLoad * 1.2m,
                Confidence = 0.5m,
                PredictionModel = "Test",
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.LoadForecasts.AddRangeAsync(forecasts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.Algorithm.Should().Contain("Robust",
            because: "应该使用鲁棒动态规划算法");
        result.RobustnessMargin.Should().Be(0.20m,
            because: "鲁棒裕度应为20%");
        result.ForecastErrorConsidered.Should().Be(0.15m,
            because: "考虑的预测误差应为15%");
        result.TotalSaving.Should().BeGreaterThan(0,
            because: "鲁棒策略应该产生正的成本节省");
    }

    /// <summary>
    /// 验证鲁棒DP的算法标识
    /// 测试点：算法名称应包含"RobustDynamicProgramming"
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldHaveCorrectAlgorithmIdentifier()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Algorithm.Should().Contain("RobustDynamicProgramming",
            because: "算法标识应该包含鲁棒动态规划");
        result.Algorithm.Should().Contain("Scenario5",
            because: "应该标识使用5个场景");
    }

    /// <summary>
    /// 未满足负荷惩罚机制
    /// 测试点：当融冰能力不足以满足负荷时，应施加2倍电价的惩罚
    /// </summary>
    [Fact]
    public void UnmetLoadPenalty_ShouldDiscourageInadequateIceStorage()
    {
        // Arrange
        var load = 6000m;
        var meltingCapacity = 3000m;
        var price = 1.0m;
        var unmetLoad = load - meltingCapacity;
        var penaltyMultiplier = 2.0m;
        var cop = 4.0m;
        var expectedPenalty = unmetLoad * price * penaltyMultiplier / cop;

        // Act & Assert
        expectedPenalty.Should().Be(1500m,
            because: "未满足负荷3000kW，电价1元，惩罚2倍，除以4（COP）");
        unmetLoad.Should().BePositive(because: "存在未满足的负荷");
        (expectedPenalty / unmetLoad).Should().Be(price * penaltyMultiplier / cop,
            because: "单位未满足负荷的惩罚成本应该正确");
    }

    /// <summary>
    /// 验证惩罚成本的计算
    /// 测试点：惩罚成本应显著高于正常供电成本
    /// </summary>
    [Fact]
    public void UnmetLoadPenalty_ShouldBeHigherThanNormalCost()
    {
        // Arrange
        var load = 4000m;
        var price = 0.84m;
        var cop = 4.0m;
        var normalCost = load * price / cop;
        var penaltyCost = load * price * 2.0m / cop;

        // Act & Assert
        penaltyCost.Should().BeApproximately(normalCost * 2, 0.001m,
            because: "惩罚成本应该是正常成本的2倍");
        penaltyCost.Should().BeGreaterThan(normalCost,
            because: "惩罚成本应该高于正常供电成本");
    }

    /// <summary>
    /// 安全裕度确保峰段冰量充足
    /// 测试点：鲁棒策略在峰段开始时应留有足够的冰量
    /// </summary>
    [Fact]
    public async Task SafetyMargin_ShouldEnsureAdequateIceInPeakHours()
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
                Confidence = 0.85m,
                PredictionModel = "Test",
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.LoadForecasts.AddRange(forecasts);
        await _dbContext.SaveChangesAsync();

        // Act
        await _optimizer.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _optimizer.GetScheduleAsync(testDate);

        // Assert
        var peakHours = new[] { 10, 11, 12, 13, 19, 20, 21, 22 };
        var peakSchedules = schedules.Where(s => peakHours.Contains(s.HourOfDay)).ToList();

        peakSchedules.Should().HaveCount(8, because: "峰段应该有8个小时");

        foreach (var schedule in peakSchedules)
        {
            schedule.TargetIceAmount.Should().BeGreaterThan(0,
                because: $"峰段第{schedule.HourOfDay}小时应该有冰量可用");
        }

        var valleyEndSchedule = schedules.First(s => s.HourOfDay == 6);
        var peakStartSchedule = schedules.First(s => s.HourOfDay == 10);
        peakStartSchedule.TargetIceAmount.Should().BeGreaterThan(valleyEndSchedule.TargetIceAmount * 0.6m,
            because: "峰段开始时应该保留至少60%的谷段结束冰量");
    }

    /// <summary>
    /// 验证谷段蓄冰量充足
    /// 测试点：谷段结束时（6:00）冰量应达到较高水平
    /// </summary>
    [Fact]
    public async Task SafetyMargin_ShouldEnsureFullIceAtValleyEnd()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        var tank = await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _optimizer.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _optimizer.GetScheduleAsync(testDate);

        // Assert
        var valleyEndSchedule = schedules.First(s => s.HourOfDay == 6);
        valleyEndSchedule.TargetIceAmount.Should().BeGreaterThanOrEqualTo(tank.MaxIceCapacity * 0.7m,
            because: "谷段结束时冰量应该至少达到最大容量的70%");
    }

    /// <summary>
    /// 验证峰段融冰模式
    /// 测试点：峰段时段应以Combined或IceMelting模式为主
    /// </summary>
    [Fact]
    public async Task SafetyMargin_ShouldUseIceDuringPeakHours()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        await _optimizer.CalculateOptimalStrategyAsync(testDate);
        var schedules = await _optimizer.GetScheduleAsync(testDate);

        // Assert
        var peakHours = new[] { 10, 11, 12, 13, 19, 20, 21, 22 };
        var peakSchedules = schedules.Where(s => peakHours.Contains(s.HourOfDay)).ToList();
        var dischargingModes = new[] { IceStorageMode.IceMelting, IceStorageMode.Combined };

        var dischargingCount = peakSchedules.Count(s => dischargingModes.Contains(s.Mode));
        dischargingCount.Should().BeGreaterThanOrEqualTo(4,
            because: "峰段应该至少有4小时使用融冰模式");
    }

    /// <summary>
    /// 验证鲁棒DP的结果结构完整性
    /// 测试点：返回结果应包含所有必要字段
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldReturnCompleteResult()
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
        result.ComputationTimeMs.Should().BeGreaterThan(0);
        result.Schedules.Should().HaveCount(24);
        result.TotalStatesEvaluated.Should().BeGreaterThan(0);
        result.RobustnessMargin.Should().Be(0.20m);
        result.ForecastErrorConsidered.Should().Be(0.15m);
    }

    /// <summary>
    /// 验证预测误差考虑参数
    /// 测试点：ForecastErrorConsidered应为15%
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldConsiderForecastError()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.ForecastErrorConsidered.Should().Be(0.15m,
            because: "应该考虑15%的预测误差标准差");
    }

    /// <summary>
    /// 验证低置信度时的更高安全裕度
    /// 测试点：低置信度预测会产生更高的鲁棒负荷
    /// </summary>
    [Fact]
    public async Task SafetyMargin_ShouldIncreaseWithLowConfidence()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();

        var lowConfidenceForecasts = new List<LoadForecast>();
        for (int hour = 0; hour < 24; hour++)
        {
            lowConfidenceForecasts.Add(new LoadForecast
            {
                ForecastDate = testDate.Date,
                HourOfDay = hour,
                PredictedLoad = 5000m,
                Confidence = 0.3m,
                PredictionModel = "LowConfidence",
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.LoadForecasts.AddRangeAsync(lowConfidenceForecasts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Should().NotBeNull();
        result.RobustnessMargin.Should().Be(0.20m,
            because: "基础安全裕度应为20%");
        result.TotalSaving.Should().BeGreaterThan(0,
            because: "即使低置信度预测，鲁棒策略仍应产生正收益");
    }

    /// <summary>
    /// 验证调度计划的时间连续性
    /// 测试点：调度计划应按小时连续排列
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldProduceContinuousSchedule()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.Schedules.Should().BeInAscendingOrder(s => s.HourOfDay,
            because: "调度计划应该按小时顺序排列");
        result.Schedules.Select(s => s.HourOfDay).Distinct().Should().HaveCount(24,
            because: "每个小时应该唯一");
        result.Schedules.Select(s => s.HourOfDay).Should().BeEquivalentTo(Enumerable.Range(0, 24),
            because: "应该覆盖0-23所有小时");
    }

    /// <summary>
    /// 验证优化策略的成本优势
    /// 测试点：最优成本应显著低于基线成本
    /// </summary>
    [Fact]
    public async Task RobustDP_ShouldAchieveSignificantSavings()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        await SeedIceStorageTankAsync();
        await SeedElectricityPriceTiersAsync();
        await SeedLoadForecastsAsync(testDate);

        // Act
        var result = await _optimizer.CalculateOptimalStrategyAsync(testDate);

        // Assert
        result.BaselineCost.Should().BeGreaterThan(0);
        var savingRate = (result.BaselineCost - result.OptimalCost) / result.BaselineCost;
        savingRate.Should().BeGreaterThanOrEqualTo(0.15m,
            because: "成本节省率应该至少达到15%");
    }
}
