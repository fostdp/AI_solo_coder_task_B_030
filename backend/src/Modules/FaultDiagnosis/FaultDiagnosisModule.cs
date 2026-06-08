using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Modules.FaultDiagnosis;

public interface IFaultDiagnosisModule
{
    Task<List<FaultDiagnosisResult>> DiagnoseDeviceAsync(string deviceId);
    Task<List<FaultDiagnosisResult>> DiagnoseAllDevicesAsync();
    Task<List<FaultType>> GetFaultTypesForDeviceTypeAsync(DeviceType deviceType);
    Task ConfirmDiagnosisAsync(long diagnosisId, string confirmedBy);
    Task<DiagnosisStatistic> GetDailyStatisticsAsync(DateTime date);
    Task InitializeFaultKnowledgeBaseAsync();
}

public class BayesianNode
{
    public string Name { get; set; } = string.Empty;
    public BayesianNodeType Type { get; set; }
    public List<BayesianNode> Parents { get; set; } = new();
    public Dictionary<string, double> CPT { get; set; } = new();
    public double PriorProbability { get; set; }
}

public class InferenceResult
{
    public string FaultCode { get; set; } = string.Empty;
    public string FaultName { get; set; } = string.Empty;
    public double PosteriorProbability { get; set; }
    public double Confidence { get; set; }
    public List<string> MatchingSymptoms { get; set; } = new();
    public string DeviationDetails { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class AnomalyDetectionResult
{
    public bool IsAnomaly { get; set; }
    public double AnomalyScore { get; set; }
    public List<string> AnomalousParameters { get; set; } = new();
    public Dictionary<string, double> ZScores { get; set; } = new();
    public double Threshold { get; set; } = 3.0;
}

public class FaultDiagnosisModule : IFaultDiagnosisModule
{
    private readonly IFaultDiagnoser _diagnoser;
    private readonly IBayesianInferenceService _bayesianInference;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FaultDiagnosisModule> _logger;

    public FaultDiagnosisModule(
        AppDbContext dbContext,
        ILogger<FaultDiagnosisModule> logger,
        IFaultDiagnoser? diagnoser = null,
        IBayesianInferenceService? bayesianInference = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        
        _bayesianInference = bayesianInference ?? BayesianInferenceService.Instance;
        _bayesianInference.Initialize(dbContext, 
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<BayesianInferenceService>());
        
        _diagnoser = diagnoser ?? new FaultDiagnoser(
            dbContext,
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<FaultDiagnoser>(),
            _bayesianInference);
    }

    public async Task<List<FaultDiagnosisResult>> DiagnoseDeviceAsync(string deviceId)
    {
        await _diagnoser.DiagnoseDeviceAsync(deviceId);
        
        return await _dbContext.FaultDiagnosisResults
            .Include(r => r.FaultType)
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.Timestamp)
            .Take(10)
            .ToListAsync();
    }

    public async Task<List<FaultDiagnosisResult>> DiagnoseAllDevicesAsync()
    {
        await _diagnoser.DiagnoseAllAsync();
        
        var since = DateTime.UtcNow.AddMinutes(-5);
        return await _dbContext.FaultDiagnosisResults
            .Include(r => r.FaultType)
            .Where(r => r.Timestamp >= since)
            .OrderByDescending(r => r.Confidence)
            .Take(50)
            .ToListAsync();
    }

    public async Task<List<FaultType>> GetFaultTypesForDeviceTypeAsync(DeviceType deviceType)
    {
        return await _diagnoser.GetFaultTypesForDeviceTypeAsync(deviceType);
    }

    public async Task ConfirmDiagnosisAsync(long diagnosisId, string confirmedBy)
    {
        await _diagnoser.ConfirmDiagnosisAsync(diagnosisId, confirmedBy);
    }

    public async Task<DiagnosisStatistic> GetDailyStatisticsAsync(DateTime date)
    {
        return await _diagnoser.GetDailyStatisticsAsync(date);
    }

    public async Task InitializeFaultKnowledgeBaseAsync()
    {
        await _diagnoser.InitializeFaultKnowledgeBaseAsync();
    }

    private AnomalyDetectionResult DetectAnomalies(
        Device device,
        List<DeviceData> recentData,
        List<DeviceData> historicalData)
    {
        return _bayesianInference.DetectAnomalies(device, recentData, historicalData);
    }
}
