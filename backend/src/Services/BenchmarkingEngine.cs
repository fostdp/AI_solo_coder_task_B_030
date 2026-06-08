using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Services;

public interface IBenchmarkingEngine
{
    Task<BuildingMetrics> CalculateBuildingMetricsAsync(string buildingId, DateTime startDate, DateTime endDate);
    Task<BenchmarkComparison> CompareBuildingsAsync(List<string> buildingIds, DateTime date);
    Task<RadarChartDataset> GetRadarDataAsync(List<string> buildingIds, DateTime date);
    Task<BenchmarkReport> GenerateReportAsync(List<string> buildingIds, string reportName, DateTime startDate, DateTime endDate);
    Task<List<Building>> GetBuildingsAsync();
    Task<Building> AddBuildingAsync(Building building);
    Task<Building> UpdateBuildingAsync(string buildingId, Building building);
    Task<bool> DeleteBuildingAsync(string buildingId);
    Task<List<BuildingEfficiencyMetric>> GetBuildingMetricsAsync(string buildingId, StatisticsPeriod period, DateTime startDate);
    Task<List<BenchmarkReport>> GetBenchmarkReportsAsync(int page = 1, int pageSize = 20);
}

public class BuildingMetrics
{
    public string BuildingId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<BuildingEfficiencyMetric> DailyMetrics { get; set; } = new();
    public decimal AverageCOP { get; set; }
    public decimal AverageEER { get; set; }
    public decimal AverageEnergyPerUnitArea { get; set; }
    public decimal AverageLoadFactor { get; set; }
    public decimal AveragePUE { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalElectricityConsumption { get; set; }
}

public class BenchmarkComparison
{
    public DateTime ComparisonDate { get; set; }
    public List<BuildingRanking> Rankings { get; set; } = new();
    public Dictionary<string, decimal> CategoryAdjustments { get; set; } = new();
    public string BestPracticeSummary { get; set; } = string.Empty;
    public string ImprovementSuggestions { get; set; } = string.Empty;
}

public class BuildingRanking
{
    public string BuildingId { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public BuildingType BuildingType { get; set; }
    public int OverallRank { get; set; }
    public decimal OverallScore { get; set; }
    public Dictionary<string, int> CategoryRanks { get; set; } = new();
    public Dictionary<string, decimal> CategoryValues { get; set; } = new();
}

public class RadarChartDataset
{
    public List<string> Labels { get; set; } = new();
    public List<RadarSeries> Datasets { get; set; } = new();
    public Dictionary<string, decimal> AdjustmentFactors { get; set; } = new();
}

public class RadarSeries
{
    public string BuildingId { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public List<decimal> Data { get; set; } = new();
    public string Color { get; set; } = string.Empty;
}

public class BenchmarkingEngine : IBenchmarkingEngine
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BenchmarkingEngine> _logger;

    private readonly Dictionary<BuildingType, decimal> _functionTypeCoefficients = new()
    {
        { BuildingType.Office, 1.00m },
        { BuildingType.Mall, 1.35m },
        { BuildingType.Hotel, 1.25m },
        { BuildingType.Complex, 1.15m },
        { BuildingType.Hospital, 1.50m },
        { BuildingType.School, 0.90m }
    };

    private readonly Dictionary<BuildingType, string> _functionTypeNames = new()
    {
        { BuildingType.Office, "办公楼" },
        { BuildingType.Mall, "商场" },
        { BuildingType.Hotel, "酒店" },
        { BuildingType.Complex, "商业综合体" },
        { BuildingType.Hospital, "医院" },
        { BuildingType.School, "学校" }
    };

    private readonly string[] _indicatorNames = new[] { "COP", "EER", "EnergyPerUnitArea", "LoadFactor", "CostPerUnitArea", "PUE" };

    public BenchmarkingEngine(
        AppDbContext dbContext,
        ILogger<BenchmarkingEngine> logger)
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

    public async Task<BuildingMetrics> CalculateBuildingMetricsAsync(string buildingId, DateTime startDate, DateTime endDate)
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

        return new BuildingMetrics
        {
            BuildingId = buildingId,
            StartDate = startDate,
            EndDate = endDate,
            DailyMetrics = results,
            AverageCOP = results.Any() ? results.Average(m => m.COP ?? 0) : 0,
            AverageEER = results.Any() ? results.Average(m => m.EER ?? 0) : 0,
            AverageEnergyPerUnitArea = results.Any() ? results.Average(m => m.EnergyPerUnitArea ?? 0) : 0,
            AverageLoadFactor = results.Any() ? results.Average(m => m.LoadFactor ?? 0) : 0,
            AveragePUE = results.Any() ? results.Average(m => m.PUE ?? 1) : 1,
            TotalCost = results.Sum(m => m.TotalCost ?? 0),
            TotalElectricityConsumption = results.Sum(m => m.TotalElectricityConsumption ?? 0)
        };
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

    public async Task<RadarChartDataset> GetRadarDataAsync(List<string> buildingIds, DateTime date)
    {
        var periodStr = StatisticsPeriod.Daily.ToString();
        var dataset = new RadarChartDataset
        {
            Labels = new List<string> { "COP", "EER", "单位面积能耗", "负荷率", "单位面积成本", "PUE" }
        };

        var colors = new[] { "#3498db", "#e74c3c", "#2ecc71", "#f39c12", "#9b59b6", "#1abc9c" };
        var colorIndex = 0;

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

            var series = new RadarSeries
            {
                BuildingId = buildingId,
                BuildingName = building.BuildingName,
                Color = colors[colorIndex % colors.Length]
            };

            var typeCoefficient = _functionTypeCoefficients.GetValueOrDefault(building.BuildingType, 1.0m);
            dataset.AdjustmentFactors[buildingId] = typeCoefficient;

            if (metric != null)
            {
                var adjustedEnergyPerUnitArea = (metric.EnergyPerUnitArea ?? 0) / typeCoefficient;
                var adjustedCostPerUnitArea = (metric.CostPerUnitArea ?? 0) / typeCoefficient;

                series.Data = new List<decimal>
                {
                    Normalize(metric.COP ?? 0, 0, 6) * 100,
                    Normalize(metric.EER ?? 0, 0, 5) * 100,
                    100 - Normalize(adjustedEnergyPerUnitArea, 0, 200) * 100,
                    Normalize(metric.LoadFactor ?? 0, 0, 1) * 100,
                    100 - Normalize(adjustedCostPerUnitArea, 0, 100) * 100,
                    100 - Normalize(metric.PUE ?? 1, 1, 3) * 100
                };
            }
            else
            {
                series.Data = new List<decimal> { 0, 0, 0, 0, 0, 0 };
            }

            dataset.Datasets.Add(series);
            colorIndex++;
        }

        return dataset;
    }

    public async Task<BenchmarkComparison> CompareBuildingsAsync(List<string> buildingIds, DateTime date)
    {
        var buildings = await _dbContext.Buildings
            .Where(b => buildingIds.Contains(b.Id))
            .ToListAsync();

        var metrics = new List<(string BuildingId, List<BuildingEfficiencyMetric> Metrics)>();
        foreach (var buildingId in buildingIds)
        {
            var buildingMetrics = await _dbContext.BuildingEfficiencyMetrics
                .Where(m => m.BuildingId == buildingId
                    && m.StatisticsDate == date.Date)
                .ToListAsync();

            metrics.Add((buildingId, buildingMetrics));
        }

        var buildingDict = buildings.ToDictionary(b => b.Id);
        var metricsWithBuildings = metrics
            .Select(m => (
                m.BuildingId,
                Building: buildingDict[m.BuildingId],
                m.Metrics))
            .ToList();

        var copRankings = RankByCategory(metricsWithBuildings, m => m.COP ?? 0, true);
        var energyRankings = RankByCategory(metricsWithBuildings, m => m.EnergyPerUnitArea ?? 9999, false);
        var costRankings = RankByCategory(metricsWithBuildings, m => m.CostPerUnitArea ?? 9999, false);
        var loadRankings = RankByCategory(metricsWithBuildings, m => m.LoadFactor ?? 0, true);
        var eerRankings = RankByCategory(metricsWithBuildings, m => m.EER ?? 0, true);

        var copRanking = copRankings.Last();
        var energyRanking = energyRankings.Last();
        var costRanking = costRankings.Last();
        var loadRanking = loadRankings.Last();
        var eerRanking = eerRankings.Last();

        var bestPractices = GenerateBestPractices(metrics, buildings);
        var suggestions = GenerateImprovementSuggestions(metrics, buildings);

        var comparison = new BenchmarkComparison
        {
            ComparisonDate = date,
            BestPracticeSummary = string.Join("|", bestPractices),
            ImprovementSuggestions = string.Join("|", suggestions)
        };

        foreach (var buildingId in buildingIds)
        {
            var building = buildingDict[buildingId];
            var overallScore = CalculateOverallScore(
                buildingId, copRanking, energyRanking, costRanking, loadRanking, eerRanking);

            var ranking = new BuildingRanking
            {
                BuildingId = buildingId,
                BuildingName = building.BuildingName,
                BuildingType = building.BuildingType,
                OverallScore = overallScore,
                CategoryRanks = new Dictionary<string, int>
                {
                    ["COP"] = copRanking.First(r => r.BuildingId == buildingId).Rank,
                    ["EnergyPerUnitArea"] = energyRanking.First(r => r.BuildingId == buildingId).Rank,
                    ["CostPerUnitArea"] = costRanking.First(r => r.BuildingId == buildingId).Rank,
                    ["LoadFactor"] = loadRanking.First(r => r.BuildingId == buildingId).Rank,
                    ["EER"] = eerRanking.First(r => r.BuildingId == buildingId).Rank
                },
                CategoryValues = new Dictionary<string, decimal>
                {
                    ["COP"] = copRanking.First(r => r.BuildingId == buildingId).Value,
                    ["EnergyPerUnitArea"] = energyRanking.First(r => r.BuildingId == buildingId).Value,
                    ["CostPerUnitArea"] = costRanking.First(r => r.BuildingId == buildingId).Value,
                    ["LoadFactor"] = loadRanking.First(r => r.BuildingId == buildingId).Value,
                    ["EER"] = eerRanking.First(r => r.BuildingId == buildingId).Value
                }
            };

            comparison.Rankings.Add(ranking);
            comparison.CategoryAdjustments[buildingId] =
                _functionTypeCoefficients.GetValueOrDefault(building.BuildingType, 1.0m);
        }

        comparison.Rankings = comparison.Rankings
            .OrderByDescending(r => r.OverallScore)
            .Select((r, i) => { r.OverallRank = i + 1; return r; })
            .ToList();

        return comparison;
    }

    public async Task<BenchmarkReport> GenerateReportAsync(
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
                var result = await CalculateBuildingMetricsAsync(buildingId, startDate, endDate);
                buildingMetrics = result.DailyMetrics;
            }

            metrics.Add((buildingId, buildingMetrics));
        }

        var buildingDict = buildings.ToDictionary(b => b.Id);
        var metricsWithBuildings = metrics
            .Select(m => (
                m.BuildingId,
                Building: buildingDict[m.BuildingId],
                m.Metrics))
            .ToList();

        var copRankings = RankByCategory(metricsWithBuildings, m => m.COP ?? 0, true);
        var energyRankings = RankByCategory(metricsWithBuildings, m => m.EnergyPerUnitArea ?? 9999, false);
        var costRankings = RankByCategory(metricsWithBuildings, m => m.CostPerUnitArea ?? 9999, false);
        var loadRankings = RankByCategory(metricsWithBuildings, m => m.LoadFactor ?? 0, true);
        var eerRankings = RankByCategory(metricsWithBuildings, m => m.EER ?? 0, true);

        var copRanking = copRankings.Last();
        var energyRanking = energyRankings.Last();
        var costRanking = costRankings.Last();
        var loadRanking = loadRankings.Last();
        var eerRanking = eerRankings.Last();

        var bestPractices = GenerateBestPractices(metrics, buildings);
        var suggestions = GenerateImprovementSuggestions(metrics, buildings);

        var overallScores = buildingIds.ToDictionary(
            id => id,
            id => CalculateOverallScore(id, copRanking, energyRanking, costRanking, loadRanking, eerRanking));

        var allRankings = new[] { copRankings, energyRankings, costRankings, loadRankings, eerRankings };

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
            CategoryRankings = SerializeCategoryRankings(allRankings),
            FunctionTypeAdjustmentApplied = true,
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

    private List<(string BuildingId, decimal AdjustedValue, decimal RawValue)> AdjustForBuildingType(
        List<(string BuildingId, Building Building, decimal Value)> data,
        Func<BuildingEfficiencyMetric, decimal?> selector)
    {
        var result = new List<(string, decimal, decimal)>();

        var groups = data
            .GroupBy(d => d.Building.BuildingType)
            .ToList();

        foreach (var group in groups)
        {
            var buildingType = group.Key;
            var coefficient = _functionTypeCoefficients.GetValueOrDefault(buildingType, 1.0m);

            foreach (var item in group)
            {
                var adjustedValue = item.Value / coefficient;
                result.Add((item.BuildingId, adjustedValue, item.Value));

                _logger.LogDebug(
                    "对标调整: {Building} 类型={Type}, 原值={Raw:F2}, 系数={Coeff:F2}, 调整后={Adjusted:F2}",
                    item.Building.BuildingName, _functionTypeNames[buildingType],
                    item.Value, coefficient, adjustedValue);
            }
        }

        return result;
    }

    private List<List<(string BuildingId, int Rank, decimal Value)>> RankByCategory(
        List<(string BuildingId, Building Building, List<BuildingEfficiencyMetric> Metrics)> allData,
        Func<BuildingEfficiencyMetric, decimal?> selector,
        bool higherIsBetter)
    {
        var results = new List<List<(string, int, decimal)>>();

        var byType = allData.GroupBy(d => d.Building.BuildingType);
        foreach (var typeGroup in byType)
        {
            var groupData = typeGroup
                .Select(d => new
                {
                    d.BuildingId,
                    Average = d.Metrics.Any() ? d.Metrics.Average(selector) ?? 0 : 0
                })
                .ToList();

            var sorted = higherIsBetter
                ? groupData.OrderByDescending(a => a.Average).ToList()
                : groupData.OrderBy(a => a.Average).ToList();

            var groupRank = sorted.Select((a, i) => (a.BuildingId, i + 1, a.Average)).ToList();
            results.Add(groupRank);
        }

        var adjustedData = allData
            .Select(d => (
                d.BuildingId,
                d.Building,
                Value: d.Metrics.Any() ? d.Metrics.Average(selector) ?? 0 : 0))
            .ToList();

        var adjusted = AdjustForBuildingType(adjustedData, selector);
        var adjustedSorted = higherIsBetter
            ? adjusted.OrderByDescending(a => a.AdjustedValue).ToList()
            : adjusted.OrderBy(a => a.AdjustedValue).ToList();

        var overallRank = adjustedSorted.Select((a, i) => (a.BuildingId, i + 1, a.AdjustedValue)).ToList();
        results.Add(overallRank);

        return results;
    }

    private string SerializeCategoryRankings(
        List<List<(string BuildingId, int Rank, decimal Value)>>[] allRankings)
    {
        var parts = new List<string>();
        var indicatorNames = new[] { "COP", "EnergyPerArea", "CostPerArea", "LoadFactor", "EER" };

        for (int i = 0; i < allRankings.Length; i++)
        {
            var indicator = indicatorNames[i];
            for (int j = 0; j < allRankings[i].Count; j++)
            {
                var rankType = j == allRankings[i].Count - 1 ? "Overall" : $"Type{j}";
                var serialized = SerializeRanking(allRankings[i][j]);
                parts.Add($"{indicator}:{rankType}:{serialized}");
            }
        }

        return string.Join("|", parts);
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
