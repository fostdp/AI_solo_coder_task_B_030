using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Services;

public interface IFaultDiagnoser
{
    Task<List<DiagnosisResult>> DiagnoseDeviceAsync(string deviceId);
    Task<List<DiagnosisResult>> DiagnoseAllAsync();
    Task ConfirmDiagnosisAsync(long diagnosisId, string confirmedBy);
    Task<AnomalyReport> DetectAnomaliesAsync(string deviceId);
    Task<List<FaultType>> GetFaultTypesForDeviceTypeAsync(DeviceType deviceType);
    Task<DiagnosisStatistic> GetDailyStatisticsAsync(DateTime date);
    Task InitializeFaultKnowledgeBaseAsync();
}

public class DiagnosisResult
{
    public long DiagnosisId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string FaultCode { get; set; } = string.Empty;
    public string FaultName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public decimal BayesProbability { get; set; }
    public string MatchingSymptoms { get; set; } = string.Empty;
    public string DeviationDetails { get; set; } = string.Empty;
    public string MaintenanceRecommendation { get; set; } = string.Empty;
    public decimal? EstimatedDowntimeHours { get; set; }
    public decimal? EstimatedRepairCost { get; set; }
    public bool IsConfirmed { get; set; }
    public bool IsUnknownFault { get; set; }
    public decimal? AnomalyScore { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AnomalyReport
{
    public string DeviceId { get; set; } = string.Empty;
    public bool IsAnomaly { get; set; }
    public double AnomalyScore { get; set; }
    public List<string> AnomalousParameters { get; set; } = new();
    public Dictionary<string, double> ZScores { get; set; } = new();
    public DateTime ReportTime { get; set; }
}

public class FaultDiagnoser : IFaultDiagnoser
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FaultDiagnoser> _logger;
    private readonly IBayesianInferenceService _bayesianInference;
    private readonly SemaphoreSlim _diagnosisSemaphore = new(1, 1);

    public FaultDiagnoser(
        AppDbContext dbContext,
        ILogger<FaultDiagnoser> logger,
        IBayesianInferenceService bayesianInference)
    {
        _dbContext = dbContext;
        _logger = logger;
        _bayesianInference = bayesianInference;
    }

    public async Task InitializeFaultKnowledgeBaseAsync()
    {
        await _bayesianInference.InitializeNetworkAsync();
    }

    public async Task<List<DiagnosisResult>> DiagnoseDeviceAsync(string deviceId)
    {
        await InitializeFaultKnowledgeBaseAsync();

        var device = await _dbContext.Devices
            .Include(d => d.DeviceData)
            .FirstOrDefaultAsync(d => d.Id == deviceId);

        if (device == null)
            throw new KeyNotFoundException($"Device {deviceId} not found");

        var recentData = await _dbContext.DeviceData
            .Where(d => d.DeviceId == deviceId)
            .OrderByDescending(d => d.Timestamp)
            .Take(30)
            .ToListAsync();

        if (!recentData.Any())
            return new List<DiagnosisResult>();

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var historicalData = await _dbContext.DeviceData
            .Where(d => d.DeviceId == deviceId
                && d.Timestamp >= sevenDaysAgo
                && d.Timestamp < DateTime.UtcNow)
            .OrderByDescending(d => d.Timestamp)
            .Take(300)
            .ToListAsync();

        var anomalyResult = _bayesianInference.DetectAnomalies(device, recentData, historicalData);

        var faultTypes = await GetFaultTypesForDeviceTypeAsync(device.DeviceTypeId);
        var results = new List<InferenceResult>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var resultsLock = new object();

        Parallel.ForEach(faultTypes, options, faultType =>
        {
            var result = _bayesianInference.InferFaultAsync(device, recentData, faultType).GetAwaiter().GetResult();
            if (result != null && result.Confidence > 0.6)
            {
                lock (resultsLock)
                {
                    results.Add(result);
                }
            }
        });

        var diagnosisResults = new List<DiagnosisResult>();
        foreach (var result in results.OrderByDescending(r => r.Confidence).Take(3))
        {
            var faultType = faultTypes.First(f => f.FaultCode == result.FaultCode);
            var diagnosis = new FaultDiagnosisResult
            {
                DeviceId = deviceId,
                FaultTypeId = faultType.Id,
                FaultType = faultType,
                Timestamp = DateTime.UtcNow,
                Confidence = (decimal)result.Confidence,
                BayesProbability = (decimal)result.PosteriorProbability,
                MatchingSymptoms = string.Join(",", result.MatchingSymptoms),
                DeviationDetails = result.DeviationDetails,
                MaintenanceRecommendation = result.Recommendation,
                EstimatedDowntimeHours = faultType.EstimatedRepairHours,
                EstimatedRepairCost = faultType.EstimatedRepairHours * 500,
                IsConfirmed = false,
                AnomalyScore = (decimal)anomalyResult.AnomalyScore
            };

            diagnosisResults.Add(new DiagnosisResult
            {
                DiagnosisId = diagnosis.Id,
                DeviceId = deviceId,
                FaultCode = result.FaultCode,
                FaultName = result.FaultName,
                Confidence = diagnosis.Confidence,
                BayesProbability = diagnosis.BayesProbability,
                MatchingSymptoms = diagnosis.MatchingSymptoms,
                DeviationDetails = diagnosis.DeviationDetails,
                MaintenanceRecommendation = diagnosis.MaintenanceRecommendation,
                EstimatedDowntimeHours = diagnosis.EstimatedDowntimeHours,
                EstimatedRepairCost = diagnosis.EstimatedRepairCost,
                IsConfirmed = diagnosis.IsConfirmed,
                AnomalyScore = diagnosis.AnomalyScore,
                Timestamp = diagnosis.Timestamp
            });

            await _dbContext.FaultDiagnosisResults.AddAsync(diagnosis);
        }

        if (anomalyResult.IsAnomaly && !diagnosisResults.Any())
        {
            var unknownFault = new FaultDiagnosisResult
            {
                DeviceId = deviceId,
                FaultTypeId = -1,
                FaultType = new FaultType
                {
                    Id = -1,
                    FaultCode = "UNKNOWN-001",
                    FaultName = "未知异常模式",
                    Severity = anomalyResult.AnomalyScore > 0.7 ? FaultSeverity.Severe :
                               anomalyResult.AnomalyScore > 0.4 ? FaultSeverity.Moderate : FaultSeverity.Minor,
                    Description = "设备运行参数出现异常偏离，但不符合已知故障模式",
                    TypicalCauses = "可能原因：新型故障模式、传感器漂移、控制逻辑异常、外部干扰",
                    TypicalSolution = "建议：1. 检查传感器校准 2. 分析历史趋势 3. 联系厂家技术支持 4. 持续监测运行数据"
                },
                Timestamp = DateTime.UtcNow,
                Confidence = (decimal)(anomalyResult.AnomalyScore * 0.9),
                BayesProbability = 0,
                MatchingSymptoms = $"异常参数: {string.Join(",", anomalyResult.AnomalousParameters)}",
                DeviationDetails = $"Z-score详情: {string.Join(",", anomalyResult.ZScores.Select(kv => $"{kv.Key}={kv.Value:F2}"))}",
                MaintenanceRecommendation = "⚠️ 检测到未知异常模式！详细偏离情况：" +
                    $"综合异常评分 {anomalyResult.AnomalyScore:P0}。" +
                    $"异常参数: {string.Join("、", anomalyResult.AnomalousParameters)}。" +
                    $"建议立即进行人工复核，采集更多数据进行深度分析。",
                EstimatedDowntimeHours = 4,
                EstimatedRepairCost = 2000,
                IsConfirmed = false,
                IsUnknownFault = true,
                AnomalyScore = (decimal)anomalyResult.AnomalyScore
            };

            diagnosisResults.Insert(0, new DiagnosisResult
            {
                DiagnosisId = unknownFault.Id,
                DeviceId = deviceId,
                FaultCode = "UNKNOWN-001",
                FaultName = "未知异常模式",
                Confidence = unknownFault.Confidence,
                BayesProbability = unknownFault.BayesProbability,
                MatchingSymptoms = unknownFault.MatchingSymptoms,
                DeviationDetails = unknownFault.DeviationDetails,
                MaintenanceRecommendation = unknownFault.MaintenanceRecommendation,
                EstimatedDowntimeHours = unknownFault.EstimatedDowntimeHours,
                EstimatedRepairCost = unknownFault.EstimatedRepairCost,
                IsConfirmed = unknownFault.IsConfirmed,
                IsUnknownFault = true,
                AnomalyScore = unknownFault.AnomalyScore,
                Timestamp = unknownFault.Timestamp
            });

            await _dbContext.FaultDiagnosisResults.AddAsync(unknownFault);

            _logger.LogWarning(
                "设备 {DeviceId} 检测到未知异常: 评分={Score:P0}, 异常参数={Params}",
                deviceId, anomalyResult.AnomalyScore, string.Join(",", anomalyResult.AnomalousParameters));
        }

        await _dbContext.SaveChangesAsync();

        if (diagnosisResults.Any())
        {
            _logger.LogWarning(
                "设备 {DeviceId} 诊断出 {Count} 个潜在故障，最高置信度: {Confidence:P0}",
                deviceId, diagnosisResults.Count, diagnosisResults.Max(d => d.Confidence));
        }

        return diagnosisResults;
    }

    public async Task<List<DiagnosisResult>> DiagnoseAllAsync()
    {
        var devices = await _dbContext.Devices
            .Where(d => d.Status == DeviceStatus.Running)
            .ToListAsync();

        var allResults = new List<DiagnosisResult>();

        foreach (var device in devices)
        {
            try
            {
                var results = await DiagnoseDeviceAsync(device.Id);
                allResults.AddRange(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "诊断设备 {DeviceId} 时出错", device.Id);
            }
        }

        return allResults;
    }

    public async Task<List<FaultType>> GetFaultTypesForDeviceTypeAsync(DeviceType deviceType)
    {
        return await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .Where(f => f.DeviceTypeId == deviceType)
            .ToListAsync();
    }

    public async Task ConfirmDiagnosisAsync(long diagnosisId, string confirmedBy)
    {
        var diagnosis = await _dbContext.FaultDiagnosisResults.FindAsync(diagnosisId);
        if (diagnosis == null)
            throw new KeyNotFoundException($"Diagnosis {diagnosisId} not found");

        diagnosis.IsConfirmed = true;
        diagnosis.ConfirmedBy = confirmedBy;
        diagnosis.ConfirmedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "诊断结果 {Id} 已由 {User} 确认，故障: {FaultType}",
            diagnosisId, confirmedBy, diagnosis.FaultTypeId);
    }

    public async Task<DiagnosisStatistic> GetDailyStatisticsAsync(DateTime date)
    {
        var existing = await _dbContext.DiagnosisStatistics
            .FirstOrDefaultAsync(s => s.StatisticsDate.Date == date.Date);

        if (existing != null) return existing;

        var diagnoses = await _dbContext.FaultDiagnosisResults
            .Where(d => d.Timestamp.Date == date.Date)
            .ToListAsync();

        var confirmed = diagnoses.Count(d => d.IsConfirmed);
        var falsePositive = diagnoses.Count(d => d.Confidence > 0.6m && !d.IsConfirmed);

        var topFaults = diagnoses
            .GroupBy(d => d.FaultTypeId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"故障{g.Key}:{g.Count()}次")
            .ToList();

        var statistic = new DiagnosisStatistic
        {
            StatisticsDate = date.Date,
            TotalDiagnosisCount = diagnoses.Count,
            ConfirmedFaultCount = confirmed,
            FalsePositiveCount = falsePositive,
            AverageConfidence = diagnoses.Any() ? diagnoses.Average(d => d.Confidence) : 0,
            TopFaultTypes = string.Join(";", topFaults),
            AccuracyRate = diagnoses.Count > 0 ? (decimal)confirmed / diagnoses.Count : null
        };

        await _dbContext.DiagnosisStatistics.AddAsync(statistic);
        await _dbContext.SaveChangesAsync();

        return statistic;
    }

    public async Task<AnomalyReport> DetectAnomaliesAsync(string deviceId)
    {
        var device = await _dbContext.Devices.FindAsync(deviceId);
        if (device == null)
            throw new KeyNotFoundException($"Device {deviceId} not found");

        var recentData = await _dbContext.DeviceData
            .Where(d => d.DeviceId == deviceId)
            .OrderByDescending(d => d.Timestamp)
            .Take(30)
            .ToListAsync();

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var historicalData = await _dbContext.DeviceData
            .Where(d => d.DeviceId == deviceId
                && d.Timestamp >= sevenDaysAgo
                && d.Timestamp < DateTime.UtcNow)
            .OrderByDescending(d => d.Timestamp)
            .Take(300)
            .ToListAsync();

        var result = _bayesianInference.DetectAnomalies(device, recentData, historicalData);

        return new AnomalyReport
        {
            DeviceId = deviceId,
            IsAnomaly = result.IsAnomaly,
            AnomalyScore = result.AnomalyScore,
            AnomalousParameters = result.AnomalousParameters,
            ZScores = result.ZScores,
            ReportTime = DateTime.UtcNow
        };
    }
}
