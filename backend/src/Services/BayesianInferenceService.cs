using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Services;

public interface IBayesianInferenceService
{
    Task<InferenceResult> InferFaultAsync(
        Device device, List<DeviceData> recentData, FaultType faultType);
    Task InitializeNetworkAsync();
    void AddFaultNode(FaultType faultType);
    double CalculatePosterior(string faultCode, Dictionary<string, bool> symptoms);
    AnomalyDetectionResult DetectAnomalies(
        Device device, List<DeviceData> recentData, List<DeviceData> historicalData);
    void Initialize(AppDbContext dbContext, ILogger<BayesianInferenceService> logger);
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

public class BayesianInferenceService : IBayesianInferenceService
{
    private AppDbContext _dbContext;
    private ILogger<BayesianInferenceService> _logger;
    private readonly ConcurrentDictionary<string, BayesianNode> _bayesianNetwork = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _knowledgeBaseInitialized;
    private bool _isInitialized;

    private static readonly Lazy<BayesianInferenceService> _instance = new(() => new BayesianInferenceService());
    
    public static BayesianInferenceService Instance => _instance.Value;

    public BayesianInferenceService()
    {
        _dbContext = null!;
        _logger = null!;
    }

    public BayesianInferenceService(
        AppDbContext dbContext,
        ILogger<BayesianInferenceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _isInitialized = true;
    }

    public void Initialize(AppDbContext dbContext, ILogger<BayesianInferenceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _isInitialized = true;
    }

    public async Task InitializeNetworkAsync()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("BayesianInferenceService is not initialized. Call Initialize() first.");

        if (_knowledgeBaseInitialized) return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_knowledgeBaseInitialized) return;

            var faultTypes = await _dbContext.FaultTypes
                .Include(f => f.SymptomParameters)
                .ToListAsync();

            foreach (var faultType in faultTypes)
            {
                AddFaultNode(faultType);
            }

            _knowledgeBaseInitialized = true;
            _logger.LogInformation("贝叶斯网络初始化完成，共加载 {Count} 种故障模式", faultTypes.Count);
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    public void AddFaultNode(FaultType faultType)
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

    public double CalculatePosterior(string faultCode, Dictionary<string, bool> symptoms)
    {
        if (!_bayesianNetwork.TryGetValue(faultCode, out var faultNode))
            return 0.0;

        var prior = faultNode.PriorProbability;
        double likelihood = 1.0;
        double evidenceProbability = 1.0;

        foreach (var symptom in symptoms)
        {
            var key = $"{faultCode}_{symptom.Key}";
            var isPresent = symptom.Value;

            if (!_bayesianNetwork.TryGetValue(key, out var symptomNode))
                continue;

            var cp = symptomNode.CPT.TryGetValue($"{faultCode}=True", out var cpValue) ? cpValue : 0.8;

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

        var matchedCount = symptoms.Count(s => s.Value);
        var matchBonus = Math.Pow(1.2, matchedCount - 1);
        var posterior = (likelihood * prior) / Math.Max(0.0001, evidenceProbability);

        return Math.Min(0.99, posterior * matchBonus);
    }

    public async Task<InferenceResult> InferFaultAsync(
        Device device,
        List<DeviceData> recentData,
        FaultType faultType)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("BayesianInferenceService is not initialized. Call Initialize() first.");

        var symptoms = faultType.SymptomParameters?.ToList();
        if (symptoms == null || !symptoms.Any()) return null!;

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

            evidence[symptom.ParameterName] = isDeviating;

            if (isDeviating)
            {
                matchingSymptoms.Add(symptom.ParameterName);
                deviationDetails.Add(
                    $"{parameterName}: {parameterValue:F2}, 阈值: {symptom.ThresholdValue:F2}, " +
                    $"偏离: {deviationPercent:F1}%, 权重: {symptom.DeviationWeight:F2}");
            }
        }

        if (!matchingSymptoms.Any()) return null!;

        var posteriorProbability = CalculatePosterior(faultType.FaultCode, evidence);

        var weightedConfidence = matchingSymptoms.Count > 0
            ? matchingSymptoms.Sum(s =>
                (double)(symptoms.First(sp => sp.ParameterName == s).DeviationWeight)) / matchingSymptoms.Count
            : 0;

        var confidence = Math.Min(0.99, posteriorProbability * 0.6 + weightedConfidence * 0.4);

        if (confidence < 0.6) return null!;

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

    public AnomalyDetectionResult DetectAnomalies(
        Device device,
        List<DeviceData> recentData,
        List<DeviceData> historicalData)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("BayesianInferenceService is not initialized. Call Initialize() first.");

        var result = new AnomalyDetectionResult();

        if (!historicalData.Any() || !recentData.Any())
            return result;

        var parametersToCheck = new[]
        {
            "Power", "SupplyTemperature", "ReturnTemperature",
            "FlowRate", "Pressure", "Current", "Frequency",
            "CondenserTemp", "EvaporatorTemp", "Vibration"
        };

        foreach (var param in parametersToCheck)
        {
            var historicalValues = GetParameterValues(historicalData, param);
            var recentValues = GetParameterValues(recentData, param);

            if (!historicalValues.Any() || !recentValues.Any())
                continue;

            var mean = historicalValues.Average();
            var stdDev = Math.Sqrt(historicalValues.Average(v => Math.Pow(v - mean, 2)));

            if (stdDev < 0.001) continue;

            var recentMean = recentValues.Average();
            var zScore = Math.Abs(recentMean - mean) / stdDev;
            result.ZScores[param] = zScore;

            if (zScore > result.Threshold)
            {
                result.IsAnomaly = true;
                result.AnomalousParameters.Add(param);
                _logger.LogDebug(
                    "参数异常检测: {DeviceId} {Param}: Z-score={Z:F2}, 历史均值={Mean:F2}, 近期均值={Recent:F2}",
                    device.Id, param, zScore, mean, recentMean);
            }
        }

        if (result.ZScores.Any())
        {
            var maxZ = result.ZScores.Values.Max();
            var anomalyRatio = (double)result.AnomalousParameters.Count / parametersToCheck.Length;
            result.AnomalyScore = Math.Min(1.0, (maxZ / 5.0) * 0.6 + anomalyRatio * 0.4);
        }

        return result;
    }

    private List<double> GetParameterValues(List<DeviceData> data, string parameterName)
    {
        var values = new List<double>();
        foreach (var d in data)
        {
            double? val = parameterName switch
            {
                "Power" => (double?)d.Power,
                "SupplyTemperature" => (double?)d.SupplyTemperature,
                "ReturnTemperature" => (double?)d.ReturnTemperature,
                "FlowRate" => (double?)d.FlowRate,
                "Pressure" => (double?)d.Pressure,
                "Current" => (double?)d.Current,
                "Frequency" => (double?)d.Frequency,
                "CondenserTemp" => (double?)d.CondenserTemperature,
                "EvaporatorTemp" => (double?)d.EvaporatorTemperature,
                "Vibration" => (double?)d.Vibration,
                _ => null
            };
            if (val.HasValue)
                values.Add(val.Value);
        }
        return values;
    }
}
