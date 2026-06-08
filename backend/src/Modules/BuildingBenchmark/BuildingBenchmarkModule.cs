using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

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
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BuildingBenchmarkModule> _logger;

    public BuildingBenchmarkModule(
        AppDbContext dbContext,
        ILogger<BuildingBenchmarkModule> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<Building>> GetBuildingsAsync()
    {
        return await _dbContext.Buildings
            .Where(b => b.Status == 1)
            .OrderBy(b => b.BuildingName)
            .ToListAsync();
    }

    public async Task<Building> AddBuildingAsync(Building building)
    {
        building.Id = string.IsNullOrEmpty(building.Id)
            ? $"BLD-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : building.Id;
        building.CreatedAt = DateTime.UtcNow;
        building.UpdatedAt = DateTime.UtcNow;
        building.Status = 1;

        await _dbContext.Buildings.AddAsync(building);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("新增楼宇: {Id} - {Name}", building.Id, building.BuildingName);
        return building;
    }

    public async Task<Building> UpdateBuildingAsync(string buildingId, Building building)
    {
        var existing = await _dbContext.Buildings.FindAsync(buildingId);
        if (existing == null)
            throw new KeyNotFoundException($"Building {buildingId} not found");

        existing.BuildingName = building.BuildingName;
        existing.BuildingType = building.BuildingType;
        existing.Address = building.Address;
        existing.GrossFloorArea = building.GrossFloorArea;
        existing.CoolingArea = building.CoolingArea;
        existing.NumberOfFloors = building.NumberOfFloors;
        existing.YearBuilt = building.YearBuilt;
        existing.DesignCoolingLoad = building.DesignCoolingLoad;
        existing.PeakCoolingLoad = building.PeakCoolingLoad;
        existing.ContactPerson = building.ContactPerson;
        existing.ContactPhone = building.ContactPhone;
        existing.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteBuildingAsync(string buildingId)
    {
        var building = await _dbContext.Buildings.FindAsync(buildingId);
        if (building == null) return false;

        building.Status = 0;
        building.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("删除楼宇: {Id} - {Name}", buildingId, building.BuildingName);
        return true;
    }

    public async Task<List<BuildingEfficiencyMetric>> CalculateBuildingMetricsAsync(
        string buildingId,
        DateTime startDate,
        DateTime endDate)
    {
        var building = await _dbContext.Buildings.FindAsync(buildingId);
        if (building == null)
            throw new KeyNotFoundException($"Building {buildingId} not found");

        var deviceIds = await _dbContext.BuildingDevices
            .Where(bd => bd.BuildingId == buildingId)
            .Select(bd => bd.DeviceId)
            .ToListAsync();

        if (!deviceIds.Any())
            throw new InvalidOperationException($"No devices associated with building {buildingId}");

        var results = new List<BuildingEfficiencyMetric>();
        var currentDate = startDate.Date;

        while (currentDate <= endDate.Date)
        {
            var dayStart = currentDate;
            var dayEnd = currentDate.AddDays(1);

            var deviceData = await _dbContext.DeviceData
                .Where(d => deviceIds.Contains(d.DeviceId)
                    && d.Timestamp >= dayStart
                    && d.Timestamp < dayEnd)
                .ToListAsync();

            if (deviceData.Any())
            {
                var metric = await CalculateDailyMetric(building, deviceIds, deviceData, currentDate);
                results.Add(metric);
            }

            currentDate = currentDate.AddDays(1);
        }

        return results;
    }

    public async Task<List<BuildingEfficiencyMetric>> GetBuildingMetricsAsync(
        string buildingId,
        StatisticsPeriod period,
        DateTime startDate)
    {
        var periodStr = period.ToString();
        var query = _dbContext.BuildingEfficiencyMetrics
            .Where(m => m.BuildingId == buildingId
                && m.StatisticsPeriod == periodStr
                && m.StatisticsDate >= startDate.Date)
            .OrderBy(m => m.StatisticsDate);

        return await query.ToListAsync();
    }

    public async Task<BenchmarkReport> GenerateBenchmarkReportAsync(
        List<string> buildingIds,
        string reportName,
        DateTime startDate,
        DateTime endDate)
    {
        if (buildingIds == null || buildingIds.Count < 2)
            throw new ArgumentException("At least 2 buildings are required for benchmarking");

        var buildings = await _dbContext.Buildings
            .Where(b => buildingIds.Contains(b.Id))
            .ToListAsync();

        var period = endDate.AddDays(1) - startDate;
        var periodStr = period.TotalDays <= 1 ? "Daily"
            : period.TotalDays <= 7 ? "Weekly"
            : period.TotalDays <= 31 ? "Monthly" : "Yearly";

        var metrics = new List<(string BuildingId, List<BuildingEfficiencyMetric> Metrics)>();
        foreach (var buildingId in buildingIds)
        {
            var buildingMetrics = await _dbContext.BuildingEfficiencyMetrics
                .Where(m => m.BuildingId == buildingId
                    && m.StatisticsPeriod == periodStr
                    && m.StatisticsDate >= startDate.Date
                    && m.StatisticsDate <= endDate.Date)
                .ToListAsync();

            if (!buildingMetrics.Any())
            {
                buildingMetrics = await CalculateBuildingMetricsAsync(buildingId, startDate, endDate);
            }

            metrics.Add((buildingId, buildingMetrics));
        }

        var copRanking = RankBuildings(metrics, m => m.COP ?? 0, true);
        var energyRanking = RankBuildings(metrics, m => m.EnergyPerUnitArea ?? 9999, false);
        var costRanking = RankBuildings(metrics, m => m.CostPerUnitArea ?? 9999, false);
        var loadRanking = RankBuildings(metrics, m => m.LoadFactor ?? 0, true);
        var eerRanking = RankBuildings(metrics, m => m.EER ?? 0, true);

        var bestPractices = GenerateBestPractices(metrics, buildings);
        var suggestions = GenerateImprovementSuggestions(metrics, buildings);

        var overallScores = buildingIds.ToDictionary(
            id => id,
            id => CalculateOverallScore(id, copRanking, energyRanking, costRanking, loadRanking, eerRanking));

        var report = new BenchmarkReport
        {
            ReportName = reportName,
            ReportPeriod = periodStr,
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            BuildingIds = string.Join(",", buildingIds),
            COPRanking = SerializeRanking(copRanking),
            EnergyPerAreaRanking = SerializeRanking(energyRanking),
            CostPerAreaRanking = SerializeRanking(costRanking),
            LoadFactorRanking = SerializeRanking(loadRanking),
            EERRanking = SerializeRanking(eerRanking),
            BestPractices = string.Join("|", bestPractices),
            ImprovementSuggestions = string.Join("|", suggestions),
            OverallScore = overallScores.Values.Average(),
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.BenchmarkReports.AddAsync(report);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "生成对标报告: {ReportName}, 楼宇数量: {Count}, 周期: {Start} ~ {End}",
            reportName, buildingIds.Count, startDate, endDate);

        return report;
    }

    public async Task<List<BenchmarkReport>> GetBenchmarkReportsAsync(int page = 1, int pageSize = 20)
    {
        return await _dbContext.BenchmarkReports
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<Dictionary<string, Dictionary<string, decimal>>> GetRadarChartDataAsync(
        List<string> buildingIds,
        StatisticsPeriod period,
        DateTime date)
    {
        var periodStr = period.ToString();
        var result = new Dictionary<string, Dictionary<string, decimal>>();

        foreach (var buildingId in buildingIds)
        {
            var building = await _dbContext.Buildings.FindAsync(buildingId);
            if (building == null) continue;

            var metric = await _dbContext.BuildingEfficiencyMetrics
                .Where(m => m.BuildingId == buildingId
                    && m.StatisticsPeriod == periodStr
                    && m.StatisticsDate == date.Date)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (metric != null)
            {
                result[buildingId] = new Dictionary<string, decimal>
                {
                    ["COP"] = Normalize(metric.COP ?? 0, 0, 6),
                    ["EER"] = Normalize(metric.EER ?? 0, 0, 5),
                    ["EnergyPerUnitArea"] = 100 - Normalize(metric.EnergyPerUnitArea ?? 0, 0, 200),
                    ["LoadFactor"] = Normalize(metric.LoadFactor ?? 0, 0, 1) * 100,
                    ["CostPerUnitArea"] = 100 - Normalize(metric.CostPerUnitArea ?? 0, 0, 100),
                    ["PUE"] = 100 - Normalize(metric.PUE ?? 1, 1, 3) * 100
                };
            }
            else
            {
                result[buildingId] = new Dictionary<string, decimal>
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

    private async Task<BuildingEfficiencyMetric> CalculateDailyMetric(
        Building building,
        List<string> deviceIds,
        List<DeviceData> deviceData,
        DateTime date)
    {
        var totalPower = deviceData.Sum(d => d.Power) / 120;
        var avgSupplyTemp = deviceData.Average(d => d.SupplyTemperature);
        var avgReturnTemp = deviceData.Average(d => d.ReturnTemperature);
        var avgFlowRate = deviceData.Average(d => d.FlowRate);

        var totalCoolingCapacity = avgFlowRate * 4.186m * (avgReturnTemp - avgSupplyTemp) * 24 / 3600;
        var operatingHours = deviceData
            .GroupBy(d => d.Timestamp.Hour)
            .Count(g => g.Any(d => d.Power > d.Power * 0.1m));

        var peakDemand = deviceData.Max(d => d.Power);
        var avgPower = deviceData.Average(d => d.Power);
        var loadFactor = peakDemand > 0 ? avgPower / peakDemand : 0;

        var cop = totalPower > 0 ? totalCoolingCapacity / (totalPower / 1000) : 0;
        var eer = totalPower > 0 ? totalCoolingCapacity / totalPower : 0;
        var pue = building.DesignCoolingLoad > 0 ? totalPower / (building.DesignCoolingLoad * 24 / 1000) : 1;

        var energyPerUnitArea = building.CoolingArea > 0 ? totalPower / building.CoolingArea : 0;
        var coolingPerUnitArea = building.CoolingArea > 0 ? totalCoolingCapacity / building.CoolingArea : 0;

        var priceTier = await _dbContext.ElectricityPriceTiers
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.PricePerKWh)
            .FirstAsync();

        var totalCost = totalPower * priceTier.PricePerKWh;
        var costPerUnitArea = building.CoolingArea > 0 ? totalCost / building.CoolingArea : 0;
        var costPerCooling = totalCoolingCapacity > 0 ? totalCost / totalCoolingCapacity : 0;

        var metric = new BuildingEfficiencyMetric
        {
            BuildingId = building.Id,
            StatisticsDate = date.Date,
            StatisticsPeriod = "Daily",
            EER = eer,
            COP = cop,
            EnergyPerUnitArea = energyPerUnitArea,
            CoolingPerUnitArea = coolingPerUnitArea,
            PUE = pue,
            LoadFactor = loadFactor,
            TotalElectricityConsumption = totalPower,
            TotalCoolingCapacity = totalCoolingCapacity,
            PeakDemand = peakDemand,
            OperatingHours = operatingHours,
            TotalCost = totalCost,
            CostPerUnitArea = costPerUnitArea,
            CostPerCooling = costPerCooling,
            OutdoorAvgTemp = 25 + (decimal)(new Random().NextDouble() * 10),
            HDD = 0,
            CDD = Math.Max(0, (avgSupplyTemp - 26) * 24),
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
        await _dbContext.SaveChangesAsync();

        return metric;
    }

    private static List<(string BuildingId, int Rank, decimal Value)> RankBuildings(
        List<(string BuildingId, List<BuildingEfficiencyMetric> Metrics)> allMetrics,
        Func<BuildingEfficiencyMetric, decimal> selector,
        bool higherIsBetter)
    {
        var averages = allMetrics
            .Select(m => new
            {
                m.BuildingId,
                Average = m.Metrics.Any() ? m.Metrics.Average(selector) : 0
            })
            .ToList();

        var sorted = higherIsBetter
            ? averages.OrderByDescending(a => a.Average).ToList()
            : averages.OrderBy(a => a.Average).ToList();

        return sorted.Select((a, i) => (a.BuildingId, i + 1, a.Average)).ToList();
    }

    private static List<string> GenerateBestPractices(
        List<(string BuildingId, List<BuildingEfficiencyMetric> Metrics)> metrics,
        List<Building> buildings)
    {
        var practices = new List<string>();

        var bestCOP = metrics.MaxBy(m => m.Metrics.Average(x => x.COP) ?? 0);
        if (bestCOP.Metrics.Any() && bestCOP.Metrics.Average(x => x.COP) > 4)
        {
            var building = buildings.First(b => b.Id == bestCOP.BuildingId);
            practices.Add($"{building.BuildingName}的COP达到{bestCOP.Metrics.Average(x => x.COP):F2}，采用的主机群控策略可作为最佳实践推广");
        }

        var bestEnergy = metrics.MinBy(m => m.Metrics.Average(x => x.EnergyPerUnitArea) ?? 9999);
        if (bestEnergy.Metrics.Any() && bestEnergy.Metrics.Average(x => x.EnergyPerUnitArea) < 80)
        {
            var building = buildings.First(b => b.Id == bestEnergy.BuildingId);
            practices.Add($"{building.BuildingName}的单位面积能耗仅{bestEnergy.Metrics.Average(x => x.EnergyPerUnitArea):F1}kWh/m²，其运行时间表优化经验值得借鉴");
        }

        var bestLoad = metrics.MaxBy(m => m.Metrics.Average(x => x.LoadFactor) ?? 0);
        if (bestLoad.Metrics.Any() && bestLoad.Metrics.Average(x => x.LoadFactor) > 0.75)
        {
            var building = buildings.First(b => b.Id == bestLoad.BuildingId);
            practices.Add($"{building.BuildingName}的平均负荷率达到{bestLoad.Metrics.Average(x => x.LoadFactor):P0}，设备组合匹配合理");
        }

        return practices;
    }

    private static List<string> GenerateImprovementSuggestions(
        List<(string BuildingId, List<BuildingEfficiencyMetric> Metrics)> metrics,
        List<Building> buildings)
    {
        var suggestions = new List<string>();

        foreach (var (buildingId, buildingMetrics) in metrics)
        {
            var building = buildings.FirstOrDefault(b => b.Id == buildingId);
            if (building == null || !buildingMetrics.Any()) continue;

            var avgCOP = buildingMetrics.Average(x => x.COP) ?? 0;
            if (avgCOP < 3.5)
            {
                suggestions.Add($"{building.BuildingName}的COP偏低({avgCOP:F2})，建议检查主机性能曲线，考虑进行冷凝器清洗");
            }

            var avgLoadFactor = buildingMetrics.Average(x => x.LoadFactor) ?? 0;
            if (avgLoadFactor < 0.5)
            {
                suggestions.Add($"{building.BuildingName}的负荷率偏低({avgLoadFactor:P0})，建议优化设备运行组合，避免大马拉小车");
            }

            var avgPUE = buildingMetrics.Average(x => x.PUE) ?? 1;
            if (avgPUE > 1.5)
            {
                suggestions.Add($"{building.BuildingName}的PUE偏高({avgPUE:F2})，建议优化水泵和冷却塔的运行策略");
            }
        }

        return suggestions;
    }

    private static decimal CalculateOverallScore(
        string buildingId,
        List<(string BuildingId, int Rank, decimal Value)> copRanking,
        List<(string BuildingId, int Rank, decimal Value)> energyRanking,
        List<(string BuildingId, int Rank, decimal Value)> costRanking,
        List<(string BuildingId, int Rank, decimal Value)> loadRanking,
        List<(string BuildingId, int Rank, decimal Value)> eerRanking)
    {
        var totalBuildings = copRanking.Count;
        var ranks = new[]
        {
            copRanking.First(r => r.BuildingId == buildingId).Rank,
            energyRanking.First(r => r.BuildingId == buildingId).Rank,
            costRanking.First(r => r.BuildingId == buildingId).Rank,
            loadRanking.First(r => r.BuildingId == buildingId).Rank,
            eerRanking.First(r => r.BuildingId == buildingId).Rank
        };

        var avgRank = ranks.Average();
        var score = (1 - (avgRank - 1) / Math.Max(1, totalBuildings - 1)) * 100;

        return (decimal)score;
    }

    private static string SerializeRanking(List<(string BuildingId, int Rank, decimal Value)> ranking)
    {
        return string.Join(";", ranking.Select(r => $"{r.BuildingId}:{r.Rank}:{r.Value:F2}"));
    }

    private static decimal Normalize(decimal value, decimal min, decimal max)
    {
        if (max <= min) return 0;
        return Math.Max(0, Math.Min(1, (value - min) / (max - min)));
    }
}
