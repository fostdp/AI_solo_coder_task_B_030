using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

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

public class FaultDiagnosisModule : IFaultDiagnosisModule
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FaultDiagnosisModule> _logger;
    private readonly SemaphoreSlim _diagnosisSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, BayesianNode> _bayesianNetwork = new();
    private bool _knowledgeBaseInitialized;

    public FaultDiagnosisModule(
        AppDbContext dbContext,
        ILogger<FaultDiagnosisModule> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeFaultKnowledgeBaseAsync()
    {
        if (_knowledgeBaseInitialized) return;

        await _diagnosisSemaphore.WaitAsync();
        try
        {
            if (_knowledgeBaseInitialized) return;

            var faultTypes = await _dbContext.FaultTypes
                .Include(f => f.SymptomParameters)
                .ToListAsync();

            foreach (var faultType in faultTypes)
            {
                var faultNode = new BayesianNode
                {
                    Name = faultType.FaultCode,
                    Type = BayesianNodeType.Fault,
                    PriorProbability = (double)(faultType.SymptomParameters?.FirstOrDefault()?.PriorProbability ?? 0.01m)
                };

                foreach (var symptom in faultType.SymptomParameters ?? new())
                {
                    var symptomNode = new BayesianNode
                    {
                        Name = $"{faultType.FaultCode}_{symptom.ParameterName}",
                        Type = BayesianNodeType.Symptom,
                        PriorProbability = 0.1
                    };

                    symptomNode.Parents.Add(faultNode);

                    symptomNode.CPT[$"{faultType.FaultCode}=True"] = (double)symptom.ConditionalProbability;
                    symptomNode.CPT[$"{faultType.FaultCode}=False"] = 0.05;

                    _bayesianNetwork[$"{faultType.FaultCode}_{symptom.ParameterName}"] = symptomNode;
                }

                _bayesianNetwork[faultType.FaultCode] = faultNode;
            }

            _knowledgeBaseInitialized = true;
            _logger.LogInformation("故障诊断知识库初始化完成，共加载 {Count} 种故障模式", faultTypes.Count);
        }
        finally
        {
            _diagnosisSemaphore.Release();
        }
    }

    public async Task<List<FaultDiagnosisResult>> DiagnoseDeviceAsync(string deviceId)
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
            return new List<FaultDiagnosisResult>();

        var faultTypes = await GetFaultTypesForDeviceTypeAsync(device.DeviceTypeId);
        var results = new List<InferenceResult>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var resultsLock = new object();

        Parallel.ForEach(faultTypes, options, faultType =>
        {
            var result = PerformBayesianInference(device, recentData, faultType);
            if (result != null && result.Confidence > 0.6)
            {
                lock (resultsLock)
                {
                    results.Add(result);
                }
            }
        });

        var diagnosisResults = new List<FaultDiagnosisResult>();
        foreach (var result in results.OrderByDescending(r => r.Confidence).Take(3))
        {
            var faultType = faultTypes.First(f => f.FaultCode == result.FaultCode);
            var diagnosis = new FaultDiagnosisResult
            {
                DeviceId = deviceId,
                FaultTypeId = faultType.Id,
                Timestamp = DateTime.UtcNow,
                Confidence = (decimal)result.Confidence,
                BayesProbability = (decimal)result.PosteriorProbability,
                MatchingSymptoms = string.Join(",", result.MatchingSymptoms),
                DeviationDetails = result.DeviationDetails,
                MaintenanceRecommendation = result.Recommendation,
                EstimatedDowntimeHours = faultType.EstimatedRepairHours,
                EstimatedRepairCost = faultType.EstimatedRepairHours * 500,
                IsConfirmed = false
            };

            diagnosisResults.Add(diagnosis);
            await _dbContext.FaultDiagnosisResults.AddAsync(diagnosis);
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

    public async Task<List<FaultDiagnosisResult>> DiagnoseAllDevicesAsync()
    {
        var devices = await _dbContext.Devices
            .Where(d => d.Status == DeviceStatus.Running)
            .ToListAsync();

        var allResults = new List<FaultDiagnosisResult>();

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
        var falsePositive = diagnoses.Count(d => d.Confidence > 0.6 && !d.IsConfirmed);

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

    private static InferenceResult? PerformBayesianInference(
        Device device,
        List<DeviceData> recentData,
        FaultType faultType)
    {
        var symptoms = faultType.SymptomParameters?.ToList();
        if (symptoms == null || !symptoms.Any()) return null;

        var matchingSymptoms = new List<string>();
        var deviationDetails = new List<string>();
        var evidence = new Dictionary<string, bool>();

        foreach (var symptom in symptoms)
        {
            var (parameterValue, parameterName) = GetParameterValue(recentData, symptom.ParameterName);
            if (parameterValue == null) continue;

            bool isDeviating;
            double deviationPercent = 0;

            switch (symptom.DeviationType)
            {
                case "High":
                    isDeviating = parameterValue > symptom.ThresholdValue;
                    if (isDeviating)
                    {
                        deviationPercent = (double)((parameterValue.Value - symptom.ThresholdValue) / symptom.ThresholdValue * 100);
                    }
                    break;
                case "Low":
                    isDeviating = parameterValue < symptom.ThresholdValue;
                    if (isDeviating)
                    {
                        deviationPercent = (double)((symptom.ThresholdValue - parameterValue.Value) / symptom.ThresholdValue * 100);
                    }
                    break;
                case "Pattern":
                default:
                    var avg = recentData.Average(d => GetParamByName(d, symptom.ParameterName)) ?? 0;
                    var stdDev = Math.Sqrt(recentData.Average(d =>
                        Math.Pow((double)(GetParamByName(d, symptom.ParameterName) ?? 0) - (double)avg, 2)));
                    isDeviating = stdDev > (double)symptom.ThresholdValue;
                    deviationPercent = stdDev;
                    break;
            }

            evidence[$"{faultType.FaultCode}_{symptom.ParameterName}"] = isDeviating;

            if (isDeviating)
            {
                matchingSymptoms.Add(symptom.ParameterName);
                deviationDetails.Add(
                    $"{parameterName}: {parameterValue:F2}, 阈值: {symptom.ThresholdValue:F2}, " +
                    $"偏离: {deviationPercent:F1}%, 权重: {symptom.DeviationWeight:F2}");
            }
        }

        if (!matchingSymptoms.Any()) return null;

        var posteriorProbability = CalculatePosteriorProbability(
            faultType, symptoms, evidence, matchingSymptoms.Count);

        var weightedConfidence = matchingSymptoms.Count > 0
            ? matchingSymptoms.Sum(s =>
                (double)(symptoms.First(sp => sp.ParameterName == s).DeviationWeight)) / matchingSymptoms.Count
            : 0;

        var confidence = Math.Min(0.99, posteriorProbability * 0.6 + weightedConfidence * 0.4);

        if (confidence < 0.6) return null;

        return new InferenceResult
        {
            FaultCode = faultType.FaultCode,
            FaultName = faultType.FaultName,
            PosteriorProbability = posteriorProbability,
            Confidence = confidence,
            MatchingSymptoms = matchingSymptoms,
            DeviationDetails = string.Join("; ", deviationDetails),
            Recommendation = faultType.TypicalSolution ?? "建议联系专业技术人员检修"
        };
    }

    private static double CalculatePosteriorProbability(
        FaultType faultType,
        List<FaultSymptomParameter> symptoms,
        Dictionary<string, bool> evidence,
        int matchedCount)
    {
        var prior = (double)(symptoms.FirstOrDefault()?.PriorProbability ?? 0.01m);

        double likelihood = 1.0;
        double evidenceProbability = 1.0;

        foreach (var symptom in symptoms)
        {
            var key = $"{faultType.FaultCode}_{symptom.ParameterName}";
            var isPresent = evidence.TryGetValue(key, out var val) && val;
            var cp = (double)symptom.ConditionalProbability;

            if (isPresent)
            {
                likelihood *= cp;
                evidenceProbability *= cp * prior + 0.05 * (1 - prior);
            }
            else
            {
                likelihood *= (1 - cp);
                evidenceProbability *= (1 - cp) * prior + 0.95 * (1 - prior);
            }
        }

        var matchBonus = Math.Pow(1.2, matchedCount - 1);
        var posterior = (likelihood * prior) / Math.Max(0.0001, evidenceProbability);

        return Math.Min(0.99, posterior * matchBonus);
    }

    private static (decimal? Value, string Name) GetParameterValue(List<DeviceData> data, string parameterName)
    {
        if (!data.Any()) return (null, parameterName);

        var avg = data.Average(d => GetParamByName(d, parameterName));
        return (avg, parameterName);
    }

    private static decimal? GetParamByName(DeviceData data, string paramName)
    {
        return paramName.ToLower() switch
        {
            "power" => data.Power,
            "supplytemperature" => data.SupplyTemperature,
            "returntemperature" => data.ReturnTemperature,
            "pressure" => data.Pressure,
            "flowrate" => data.FlowRate,
            "frequency" => data.Frequency,
            "current" => data.Current,
            "voltage" => data.Voltage,
            "inlettemperature" => data.InletTemperature,
            "outlettemperature" => data.OutletTemperature,
            "fanspeed" => data.FanSpeed,
            "temperaturerise" => data.ReturnTemperature - data.SupplyTemperature,
            "coolingwatertempdiff" => data.OutletTemperature - data.InletTemperature,
            _ => null
        };
    }
}
