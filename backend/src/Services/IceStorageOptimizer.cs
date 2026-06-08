using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Services;

public interface IIceStorageOptimizer
{
    Task<DPResult> CalculateOptimalStrategyAsync(DateTime scheduleDate);
    Task<DPState> GetCurrentStateAsync(string tankId);
    Task<List<IceStorageSchedule>> GetScheduleAsync(DateTime date);
    Task UpdateTankStateAsync(string tankId, IceStorageMode mode, decimal delta);
    Task<decimal> GetElectricityPriceForHourAsync(int hour);
    Task GenerateNextDayForecastAsync();
    decimal CalculateRobustLoad(decimal predictedLoad, decimal confidence);
    decimal[] GenerateLoadScenarios(decimal baseLoad);
}

public class DPResult
{
    public string StrategyId { get; set; } = string.Empty;
    public DateTime ScheduleDate { get; set; }
    public decimal OptimalCost { get; set; }
    public decimal BaselineCost { get; set; }
    public decimal TotalSaving { get; set; }
    public int ComputationTimeMs { get; set; }
    public bool IsRunning { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public List<IceStorageSchedule> Schedules { get; set; } = new();
    public int TotalStatesEvaluated { get; set; }
    public decimal? RobustnessMargin { get; set; }
    public decimal? ForecastErrorConsidered { get; set; }
}

public class DPState
{
    public int Hour { get; set; }
    public decimal IceAmount { get; set; }
    public decimal Cost { get; set; }
    public DPState? Previous { get; set; }
    public IceStorageMode Mode { get; set; }
    public decimal MeltingRate { get; set; }
    public decimal ChillerLoadRatio { get; set; }
    public decimal IceLoadRatio { get; set; }
}

public class RobustDPParameters
{
    public decimal ForecastErrorStdDev { get; set; } = 0.15m;
    public decimal SafetyMargin { get; set; } = 0.20m;
    public decimal ConfidenceLevel { get; set; } = 0.95m;
    public int ScenarioCount { get; set; } = 5;
}

public class DPProgress
{
    public int CurrentHour { get; set; }
    public int TotalHours { get; set; } = 24;
    public int StatesProcessed { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class IceStorageOptimizer : IIceStorageOptimizer
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<IceStorageOptimizer> _logger;
    private readonly SemaphoreSlim _dpSemaphore = new(1, 1);
    private readonly RobustDPParameters _robustParams = new();

    public IceStorageOptimizer(
        AppDbContext dbContext,
        ILogger<IceStorageOptimizer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public decimal CalculateRobustLoad(decimal predictedLoad, decimal confidence)
    {
        var zScore = 1.645m * confidence;
        var errorMargin = predictedLoad * _robustParams.ForecastErrorStdDev * zScore;
        var robustLoad = predictedLoad * (1 + _robustParams.SafetyMargin) + errorMargin;
        _logger.LogDebug("鲁棒负荷计算: 预测={Predicted:F0}, 误差裕度={Margin:F0}, 鲁棒值={Robust:F0}",
            predictedLoad, errorMargin, robustLoad);
        return robustLoad;
    }

    public decimal[] GenerateLoadScenarios(decimal baseLoad)
    {
        var scenarios = new decimal[_robustParams.ScenarioCount];
        var factors = new[] { 0.85m, 0.95m, 1.0m, 1.10m, 1.20m };
        for (int i = 0; i < _robustParams.ScenarioCount; i++)
        {
            scenarios[i] = baseLoad * factors[i];
        }
        return scenarios;
    }

    private decimal CalculateExpectedCost(decimal[] scenarios, decimal[] costs)
    {
        var weights = new[] { 0.1m, 0.2m, 0.3m, 0.25m, 0.15m };
        var expectedCost = 0m;
        for (int i = 0; i < scenarios.Length; i++)
        {
            expectedCost += costs[i] * weights[i];
        }
        return expectedCost;
    }

    public async Task<DPResult> CalculateOptimalStrategyAsync(DateTime scheduleDate)
    {
        await _dpSemaphore.WaitAsync();
        try
        {
            var startTime = DateTime.UtcNow;
            var iceTanks = await _dbContext.IceStorageTanks
                .Include(t => t.Device)
                .Where(t => t.Device != null && t.Device.Status == DeviceStatus.Running)
                .ToListAsync();

            if (!iceTanks.Any())
                throw new InvalidOperationException("No active ice storage tanks available");

            var mainTank = iceTanks.First();
            var maxIceCapacity = mainTank.MaxIceCapacity;
            var iceStep = Math.Max(100, maxIceCapacity / 20);
            var iceStates = Enumerable.Range(0, (int)(maxIceCapacity / iceStep) + 1)
                .Select(i => i * iceStep)
                .ToList();

            var priceTiers = await _dbContext.ElectricityPriceTiers
                .Where(p => p.IsActive)
                .ToListAsync();

            var forecasts = await _dbContext.LoadForecasts
                .Where(f => f.ForecastDate.Date == scheduleDate.Date)
                .OrderBy(f => f.HourOfDay)
                .ToListAsync();

            if (forecasts.Count < 24)
                await GenerateNextDayForecastAsync();

            forecasts = await _dbContext.LoadForecasts
                .Where(f => f.ForecastDate.Date == scheduleDate.Date)
                .OrderBy(f => f.HourOfDay)
                .ToListAsync();

            var baselineCost = await CalculateBaselineCostAsync(forecasts, priceTiers, mainTank);

            var dpTable = new ConcurrentDictionary<int, ConcurrentDictionary<decimal, DPState>>();
            dpTable[0] = new ConcurrentDictionary<decimal, DPState>();

            foreach (var ice in iceStates)
            {
                var initialIce = scheduleDate.Hour < 6 ? maxIceCapacity : 0;
                if (ice == initialIce)
                {
                    dpTable[0][ice] = new DPState { Hour = 0, IceAmount = ice, Cost = 0 };
                }
            }

            var totalStates = 0;
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            for (int hour = 0; hour < 24; hour++)
            {
                dpTable[hour + 1] = new ConcurrentDictionary<decimal, DPState>();
                var currentHour = hour;
                var currentForecast = forecasts.FirstOrDefault(f => f.HourOfDay == currentHour);
                var baseLoad = currentForecast?.PredictedLoad ?? 500;
                var forecastConfidence = currentForecast?.Confidence ?? 0.85m;
                var robustLoad = CalculateRobustLoad(baseLoad, forecastConfidence);
                var loadScenarios = GenerateLoadScenarios(baseLoad);
                var price = GetPriceForHour(priceTiers, currentHour);

                Parallel.ForEach(iceStates, options, currentIce =>
                {
                    if (!dpTable[currentHour].ContainsKey(currentIce)) return;

                    var currentState = dpTable[currentHour][currentIce];
                    var minIce = 0m;
                    var maxIce = Math.Min(currentIce + mainTank.IceMakingRate, maxIceCapacity);

                    foreach (var nextIce in iceStates.Where(i => i >= minIce && i <= maxIce))
                    {
                        var iceDelta = nextIce - currentIce;
                        IceStorageMode mode;
                        decimal chillerLoadRatio, iceLoadRatio, meltingRate, powerConsumption, cost;

                        decimal scenarioCost;
                        var scenarioCosts = new decimal[loadScenarios.Length];

                        for (int s = 0; s < loadScenarios.Length; s++)
                        {
                            var scenarioLoad = loadScenarios[s];
                            if (iceDelta > 0)
                            {
                                mode = IceStorageMode.IceMaking;
                                chillerLoadRatio = 1;
                                iceLoadRatio = 0;
                                meltingRate = 0;
                                powerConsumption = (iceDelta / mainTank.IceMakingCOP) + scenarioLoad * 0.1m;
                                scenarioCost = powerConsumption * price;
                            }
                            else if (iceDelta < 0)
                            {
                                mode = IceStorageMode.Combined;
                                meltingRate = -iceDelta;
                                var scenarioIceLoadRatio = Math.Min(1, meltingRate / Math.Max(1, scenarioLoad));
                                var scenarioChillerLoadRatio = 1 - scenarioIceLoadRatio;
                                powerConsumption = (scenarioLoad * scenarioChillerLoadRatio) / 4.0m;
                                scenarioCost = powerConsumption * price;

                                if (scenarioIceLoadRatio < 1 && scenarioLoad > meltingRate)
                                {
                                    var unmetLoad = scenarioLoad - meltingRate;
                                    var penaltyPrice = price * 2.0m;
                                    scenarioCost += unmetLoad * penaltyPrice / 4.0m;
                                }
                            }
                            else
                            {
                                mode = IceStorageMode.ChillerOnly;
                                chillerLoadRatio = 1;
                                iceLoadRatio = 0;
                                meltingRate = 0;
                                powerConsumption = scenarioLoad / 4.0m;
                                scenarioCost = powerConsumption * price;
                            }
                            scenarioCosts[s] = scenarioCost;
                        }

                        cost = CalculateExpectedCost(loadScenarios, scenarioCosts);

                        if (iceDelta < 0)
                        {
                            iceLoadRatio = Math.Min(1, meltingRate / Math.Max(1, robustLoad));
                            chillerLoadRatio = 1 - iceLoadRatio;
                            powerConsumption = (robustLoad * chillerLoadRatio) / 4.0m;
                        }
                        else
                        {
                            powerConsumption = iceDelta > 0
                                ? (iceDelta / mainTank.IceMakingCOP) + robustLoad * 0.1m
                                : robustLoad / 4.0m;
                        }

                        var totalCost = currentState.Cost + cost;
                        totalStates++;

                        if (!dpTable[currentHour + 1].ContainsKey(nextIce) ||
                            dpTable[currentHour + 1][nextIce].Cost > totalCost)
                        {
                            dpTable[currentHour + 1][nextIce] = new DPState
                            {
                                Hour = currentHour + 1,
                                IceAmount = nextIce,
                                Cost = totalCost,
                                Previous = currentState,
                                Mode = mode,
                                MeltingRate = meltingRate,
                                ChillerLoadRatio = chillerLoadRatio,
                                IceLoadRatio = iceLoadRatio
                            };
                        }
                    }
                });
            }

            var finalState = dpTable[24].Values.OrderBy(s => s.Cost).First();
            var optimalCost = finalState.Cost;

            var schedules = new List<IceStorageSchedule>();
            var current = finalState;
            var path = new List<DPState>();
            while (current != null)
            {
                path.Add(current);
                current = current.Previous;
            }
            path.Reverse();

            for (int i = 1; i < path.Count; i++)
            {
                var hour = i - 1;
                var forecast = forecasts.FirstOrDefault(f => f.HourOfDay == hour);
                var hourPrice = GetPriceForHour(priceTiers, hour);
                var hourLoad = forecast?.PredictedLoad ?? 500;
                var baselineHourCost = hourLoad * hourPrice / 4.0m;
                var optimizedHourCost = path[i].Cost - path[i - 1].Cost;

                schedules.Add(new IceStorageSchedule
                {
                    ScheduleDate = scheduleDate.Date,
                    HourOfDay = hour,
                    Mode = path[i].Mode,
                    IceTankId = mainTank.Id,
                    TargetIceAmount = path[i].IceAmount,
                    TargetMeltingRate = path[i].MeltingRate,
                    ChillerLoadRatio = path[i].ChillerLoadRatio,
                    IceLoadRatio = path[i].IceLoadRatio,
                    ExpectedCost = optimizedHourCost,
                    BaselineCost = baselineHourCost,
                    CostSaving = baselineHourCost - optimizedHourCost,
                    IsOptimized = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _dbContext.IceStorageSchedules
                .Where(s => s.ScheduleDate.Date == scheduleDate.Date)
                .ExecuteDeleteAsync();

            await _dbContext.IceStorageSchedules.AddRangeAsync(schedules);

            var dpRecord = new DPStrategyRecord
            {
                ScheduleDate = scheduleDate.Date,
                TotalStatesEvaluated = totalStates,
                OptimalCost = optimalCost,
                BaselineCost = baselineCost,
                TotalSaving = baselineCost - optimalCost,
                ComputationTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                Algorithm = "RobustDynamicProgramming-Scenario5",
                RobustnessMargin = _robustParams.SafetyMargin,
                ForecastErrorConsidered = _robustParams.ForecastErrorStdDev,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.DPStrategyRecords.AddAsync(dpRecord);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "DP策略计算完成: {Date}, 评估状态数: {States}, 最优成本: {Cost:C}, 预计节省: {Saving:C}, 耗时: {Ms}ms",
                scheduleDate.Date, totalStates, optimalCost, baselineCost - optimalCost,
                (DateTime.UtcNow - startTime).TotalMilliseconds);

            return new DPResult
            {
                StrategyId = dpRecord.Id.ToString(),
                ScheduleDate = scheduleDate.Date,
                OptimalCost = optimalCost,
                BaselineCost = baselineCost,
                TotalSaving = baselineCost - optimalCost,
                ComputationTimeMs = dpRecord.ComputationTimeMs,
                IsRunning = false,
                Algorithm = dpRecord.Algorithm,
                Schedules = schedules,
                TotalStatesEvaluated = totalStates,
                RobustnessMargin = dpRecord.RobustnessMargin,
                ForecastErrorConsidered = dpRecord.ForecastErrorConsidered
            };
        }
        finally
        {
            _dpSemaphore.Release();
        }
    }

    public async Task<DPState> GetCurrentStateAsync(string tankId)
    {
        var tank = await _dbContext.IceStorageTanks.FindAsync(tankId);
        if (tank == null)
            throw new KeyNotFoundException($"Tank {tankId} not found");

        var latestSchedule = await _dbContext.IceStorageSchedules
            .Where(s => s.IceTankId == tankId)
            .OrderByDescending(s => s.ScheduleDate)
            .ThenByDescending(s => s.HourOfDay)
            .FirstOrDefaultAsync();

        return new DPState
        {
            Hour = latestSchedule?.HourOfDay ?? 0,
            IceAmount = tank.CurrentIceAmount,
            Mode = tank.CurrentMode,
            MeltingRate = tank.CurrentMeltingRate,
            ChillerLoadRatio = latestSchedule?.ChillerLoadRatio ?? 0,
            IceLoadRatio = latestSchedule?.IceLoadRatio ?? 0
        };
    }

    public async Task<List<IceStorageSchedule>> GetScheduleAsync(DateTime date)
    {
        return await _dbContext.IceStorageSchedules
            .Where(s => s.ScheduleDate.Date == date.Date)
            .OrderBy(s => s.HourOfDay)
            .ToListAsync();
    }

    public async Task UpdateTankStateAsync(string tankId, IceStorageMode mode, decimal delta)
    {
        var tank = await _dbContext.IceStorageTanks.FindAsync(tankId);
        if (tank == null) return;

        var beforeAmount = tank.CurrentIceAmount;
        tank.CurrentIceAmount = Math.Max(0, Math.Min(tank.MaxIceCapacity, tank.CurrentIceAmount + delta));
        tank.CurrentMode = mode;
        tank.CurrentMeltingRate = mode == IceStorageMode.IceMelting || mode == IceStorageMode.Combined
            ? Math.Abs(delta)
            : 0;

        var price = await GetElectricityPriceForHourAsync(DateTime.Now.Hour);
        var coolingProvided = delta < 0 ? -delta * tank.IceMeltingEfficiency : 0;
        var powerConsumption = delta > 0 ? delta / tank.IceMakingCOP : 0;

        var operation = new IceStorageOperation
        {
            IceTankId = tankId,
            Timestamp = DateTime.UtcNow,
            Mode = mode,
            IceAmountBefore = beforeAmount,
            IceAmountAfter = tank.CurrentIceAmount,
            IceAmountDelta = delta,
            MeltingRate = tank.CurrentMeltingRate,
            PowerConsumption = powerConsumption,
            CoolingProvided = coolingProvided,
            ElectricityPrice = price,
            Cost = powerConsumption * price
        };

        await _dbContext.IceStorageOperations.AddAsync(operation);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<decimal> GetElectricityPriceForHourAsync(int hour)
    {
        var tiers = await _dbContext.ElectricityPriceTiers
            .Where(p => p.IsActive)
            .ToListAsync();

        return GetPriceForHour(tiers, hour);
    }

    public async Task GenerateNextDayForecastAsync()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        var existing = await _dbContext.LoadForecasts
            .AnyAsync(f => f.ForecastDate.Date == tomorrow);

        if (existing) return;

        var loadProfile = new[]
        {
            new { Hour = 0, Load = 0.3 },
            new { Hour = 1, Load = 0.25 },
            new { Hour = 2, Load = 0.22 },
            new { Hour = 3, Load = 0.2 },
            new { Hour = 4, Load = 0.2 },
            new { Hour = 5, Load = 0.22 },
            new { Hour = 6, Load = 0.35 },
            new { Hour = 7, Load = 0.55 },
            new { Hour = 8, Load = 0.75 },
            new { Hour = 9, Load = 0.88 },
            new { Hour = 10, Load = 0.95 },
            new { Hour = 11, Load = 0.98 },
            new { Hour = 12, Load = 1.0 },
            new { Hour = 13, Load = 0.98 },
            new { Hour = 14, Load = 0.97 },
            new { Hour = 15, Load = 0.95 },
            new { Hour = 16, Load = 0.92 },
            new { Hour = 17, Load = 0.88 },
            new { Hour = 18, Load = 0.82 },
            new { Hour = 19, Load = 0.78 },
            new { Hour = 20, Load = 0.7 },
            new { Hour = 21, Load = 0.6 },
            new { Hour = 22, Load = 0.5 },
            new { Hour = 23, Load = 0.4 }
        };

        var peakLoad = 8000m;

        foreach (var profile in loadProfile)
        {
            var randomFactor = 0.95m + (decimal)(new Random().NextDouble() * 0.1);
            await _dbContext.LoadForecasts.AddAsync(new LoadForecast
            {
                ForecastDate = tomorrow,
                HourOfDay = profile.Hour,
                PredictedLoad = peakLoad * (decimal)profile.Load * randomFactor,
                PredictionModel = "HistoricalProfile",
                Confidence = 0.85m,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    private static decimal GetPriceForHour(List<ElectricityPriceTier> tiers, int hour)
    {
        var tier = tiers.FirstOrDefault(t =>
            t.StartHour <= t.EndHour
                ? hour >= t.StartHour && hour < t.EndHour
                : hour >= t.StartHour || hour < t.EndHour);

        return tier?.PricePerKWh ?? 0.84m;
    }

    private async Task<decimal> CalculateBaselineCostAsync(
        List<LoadForecast> forecasts,
        List<ElectricityPriceTier> tiers,
        IceStorageTank tank)
    {
        decimal total = 0;
        foreach (var forecast in forecasts)
        {
            var price = GetPriceForHour(tiers, forecast.HourOfDay);
            var power = forecast.PredictedLoad / 4.0m;
            total += power * price;
        }
        return total;
    }
}
