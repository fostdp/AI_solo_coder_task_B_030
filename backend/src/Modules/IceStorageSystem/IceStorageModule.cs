using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Modules.IceStorage;

public interface IIceStorageModule
{
    Task<DPStrategyRecord> CalculateOptimalStrategyAsync(DateTime scheduleDate);
    Task<List<IceStorageSchedule>> GetScheduleForDateAsync(DateTime date);
    Task<decimal> GetElectricityPriceForHourAsync(int hour);
    Task UpdateIceStorageStateAsync(string tankId, IceStorageMode mode, decimal iceAmountDelta);
    Task GenerateNextDayForecastAsync();
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

public class IceStorageModule : IIceStorageModule
{
    private readonly IIceStorageOptimizer _optimizer;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<IceStorageModule> _logger;

    public IceStorageModule(
        AppDbContext dbContext,
        ILogger<IceStorageModule> logger,
        IIceStorageOptimizer? optimizer = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _optimizer = optimizer ?? new IceStorageOptimizer(dbContext, logger);
    }

    public async Task<DPStrategyRecord> CalculateOptimalStrategyAsync(DateTime scheduleDate)
    {
        var result = await _optimizer.CalculateOptimalStrategyAsync(scheduleDate);
        
        var record = await _dbContext.DPStrategyRecords
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(r => r.ScheduleDate == scheduleDate.Date);

        if (record != null)
        {
            return record;
        }

        return new DPStrategyRecord
        {
            ScheduleDate = result.ScheduleDate,
            TotalStatesEvaluated = result.TotalStatesEvaluated,
            OptimalCost = result.OptimalCost,
            BaselineCost = result.BaselineCost,
            TotalSaving = result.TotalSaving,
            ComputationTimeMs = result.ComputationTimeMs,
            Algorithm = result.Algorithm,
            RobustnessMargin = result.RobustnessMargin,
            ForecastErrorConsidered = result.ForecastErrorConsidered,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<List<IceStorageSchedule>> GetScheduleForDateAsync(DateTime date)
    {
        return await _optimizer.GetScheduleAsync(date);
    }

    public async Task<decimal> GetElectricityPriceForHourAsync(int hour)
    {
        return await _optimizer.GetElectricityPriceForHourAsync(hour);
    }

    public async Task UpdateIceStorageStateAsync(string tankId, IceStorageMode mode, decimal iceAmountDelta)
    {
        await _optimizer.UpdateTankStateAsync(tankId, mode, iceAmountDelta);
    }

    public async Task GenerateNextDayForecastAsync()
    {
        await _optimizer.GenerateNextDayForecastAsync();
    }

    private decimal CalculateRobustLoad(decimal predictedLoad, decimal confidence)
    {
        return _optimizer.CalculateRobustLoad(predictedLoad, confidence);
    }

    private decimal[] GenerateLoadScenarios(decimal baseLoad)
    {
        return _optimizer.GenerateLoadScenarios(baseLoad);
    }
}
