using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Modules.BuildingBenchmark;

public interface IBuildingBenchmarkModule
{
    Task<List<Building>> GetBuildingsAsync();
    Task<Building> AddBuildingAsync(Building building);
    Task<Building> UpdateBuildingAsync(string buildingId, Building building);
    Task<bool> DeleteBuildingAsync(string buildingId);
    Task<List<BuildingEfficiencyMetric>> CalculateBuildingMetricsAsync(string buildingId, DateTime startDate, DateTime endDate);
    Task<List<BuildingEfficiencyMetric>> GetBuildingMetricsAsync(string buildingId, StatisticsPeriod period, DateTime startDate);
    Task<BenchmarkReport> GenerateBenchmarkReportAsync(List<string> buildingIds, string reportName, DateTime startDate, DateTime endDate);
    Task<List<BenchmarkReport>> GetBenchmarkReportsAsync(int page = 1, int pageSize = 20);
    Task<Dictionary<string, Dictionary<string, decimal>>> GetRadarChartDataAsync(List<string> buildingIds, StatisticsPeriod period, DateTime date);
}

public class BuildingBenchmarkModule : IBuildingBenchmarkModule
{
    private readonly IBenchmarkingEngine _benchmarkingEngine;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BuildingBenchmarkModule> _logger;

    public BuildingBenchmarkModule(
        AppDbContext dbContext,
        ILogger<BuildingBenchmarkModule> logger,
        IBenchmarkingEngine? benchmarkingEngine = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _benchmarkingEngine = benchmarkingEngine ?? new BenchmarkingEngine(
            dbContext,
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BenchmarkingEngine>());
    }

    public async Task<List<Building>> GetBuildingsAsync()
    {
        return await _benchmarkingEngine.GetBuildingsAsync();
    }

    public async Task<Building> AddBuildingAsync(Building building)
    {
        return await _benchmarkingEngine.AddBuildingAsync(building);
    }

    public async Task<Building> UpdateBuildingAsync(string buildingId, Building building)
    {
        return await _benchmarkingEngine.UpdateBuildingAsync(buildingId, building);
    }

    public async Task<bool> DeleteBuildingAsync(string buildingId)
    {
        return await _benchmarkingEngine.DeleteBuildingAsync(buildingId);
    }

    public async Task<List<BuildingEfficiencyMetric>> CalculateBuildingMetricsAsync(
        string buildingId,
        DateTime startDate,
        DateTime endDate)
    {
        var result = await _benchmarkingEngine.CalculateBuildingMetricsAsync(buildingId, startDate, endDate);
        return result.DailyMetrics;
    }

    public async Task<List<BuildingEfficiencyMetric>> GetBuildingMetricsAsync(
        string buildingId,
        StatisticsPeriod period,
        DateTime startDate)
    {
        return await _benchmarkingEngine.GetBuildingMetricsAsync(buildingId, period, startDate);
    }

    public async Task<BenchmarkReport> GenerateBenchmarkReportAsync(
        List<string> buildingIds,
        string reportName,
        DateTime startDate,
        DateTime endDate)
    {
        return await _benchmarkingEngine.GenerateReportAsync(buildingIds, reportName, startDate, endDate);
    }

    public async Task<List<BenchmarkReport>> GetBenchmarkReportsAsync(int page = 1, int pageSize = 20)
    {
        return await _benchmarkingEngine.GetBenchmarkReportsAsync(page, pageSize);
    }

    public async Task<Dictionary<string, Dictionary<string, decimal>>> GetRadarChartDataAsync(
        List<string> buildingIds,
        StatisticsPeriod period,
        DateTime date)
    {
        var radarData = await _benchmarkingEngine.GetRadarDataAsync(buildingIds, date);
        var result = new Dictionary<string, Dictionary<string, decimal>>();

        foreach (var series in radarData.Datasets)
        {
            var building = await _dbContext.Buildings.FindAsync(series.BuildingId);
            if (building == null) continue;

            var typeCoefficient = radarData.AdjustmentFactors.GetValueOrDefault(series.BuildingId, 1.0m);
            var metric = await _dbContext.BuildingEfficiencyMetrics
                .Where(m => m.BuildingId == series.BuildingId
                    && m.StatisticsPeriod == period.ToString()
                    && m.StatisticsDate == date.Date)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (metric != null)
            {
                var adjustedEnergyPerUnitArea = (metric.EnergyPerUnitArea ?? 0) / typeCoefficient;
                var adjustedCostPerUnitArea = (metric.CostPerUnitArea ?? 0) / typeCoefficient;

                result[series.BuildingId] = new Dictionary<string, decimal>
                {
                    ["COP"] = Normalize(metric.COP ?? 0, 0, 6) * 100,
                    ["EER"] = Normalize(metric.EER ?? 0, 0, 5) * 100,
                    ["EnergyPerUnitArea"] = 100 - Normalize(adjustedEnergyPerUnitArea, 0, 200) * 100,
                    ["LoadFactor"] = Normalize(metric.LoadFactor ?? 0, 0, 1) * 100,
                    ["CostPerUnitArea"] = 100 - Normalize(adjustedCostPerUnitArea, 0, 100) * 100,
                    ["PUE"] = 100 - Normalize(metric.PUE ?? 1, 1, 3) * 100,
                    ["RawEnergyPerUnitArea"] = metric.EnergyPerUnitArea ?? 0,
                    ["AdjustedEnergyPerUnitArea"] = adjustedEnergyPerUnitArea,
                    ["FunctionTypeCoefficient"] = typeCoefficient,
                    ["BuildingType"] = (int)building.BuildingType
                };
            }
            else
            {
                result[series.BuildingId] = new Dictionary<string, decimal>
                {
                    ["COP"] = 0,
                    ["EER"] = 0,
                    ["EnergyPerUnitArea"] = 0,
                    ["LoadFactor"] = 0,
                    ["CostPerUnitArea"] = 0,
                    ["PUE"] = 0
                };
            }
        }

        return result;
    }

    private static decimal Normalize(decimal value, decimal min, decimal max)
    {
        if (max <= min) return 0;
        return Math.Max(0, Math.Min(1, (value - min) / (max - min)));
    }
}
