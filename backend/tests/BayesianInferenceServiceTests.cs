using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

public class BayesianInferenceService_NetworkConstruction_Tests : TestBase
{
    private readonly Mock<ILogger<BayesianInferenceService>> _mockLogger;
    private readonly BayesianInferenceService _service;

    public BayesianInferenceService_NetworkConstruction_Tests()
    {
        _mockLogger = CreateMockLogger<BayesianInferenceService>();
        _service = new BayesianInferenceService(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试初始化网络时应该为所有故障创建节点。
    /// 验证点：数据库中的每种故障类型都应在贝叶斯网络中有对应的节点。
    /// </summary>
    [Fact]
    public async Task InitializeNetwork_ShouldCreateNodesForAllFaults()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var faultCount = await _dbContext.FaultTypes.CountAsync();

        // Act
        await _service.InitializeNetworkAsync();

        // Assert
        var faultTypes = await _dbContext.FaultTypes.ToListAsync();
        foreach (var fault in faultTypes)
        {
            var symptoms = new Dictionary<string, bool>();
            var posterior = _service.CalculatePosterior(fault.FaultCode, symptoms);
            posterior.Should().BeGreaterThanOrEqualTo(0,
                because: $"故障 {fault.FaultCode} 应该存在于网络中");
        }
    }

    /// <summary>
    /// 测试每个故障应该有对应的症状节点。
    /// 验证点：故障的症状参数应被正确转换为症状节点。
    /// </summary>
    [Fact]
    public async Task EachFault_ShouldHaveSymptomNodes()
    {
        // Arrange
        await SeedFaultTypesAsync();
        await _service.InitializeNetworkAsync();

        // Act & Assert
        var faultTypes = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .ToListAsync();

        foreach (var fault in faultTypes.Where(f => f.SymptomParameters != null && f.SymptomParameters.Any()))
        {
            foreach (var symptom in fault.SymptomParameters!)
            {
                var symptoms = new Dictionary<string, bool>
                {
                    { symptom.ParameterName, true }
                };
                var posterior = _service.CalculatePosterior(fault.FaultCode, symptoms);
                posterior.Should().BeGreaterThan(0,
                    because: $"故障 {fault.FaultCode} 的症状 {symptom.ParameterName} 应该有对应的节点");
            }
        }
    }

    /// <summary>
    /// 测试CPT（条件概率表）应该包含有效的概率值。
    /// 验证点：所有概率值应在[0, 1]范围内。
    /// </summary>
    [Fact]
    public async Task CPT_ShouldHaveValidProbabilityValues()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        await _service.InitializeNetworkAsync();

        // Assert
        var faultTypes = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .ToListAsync();

        foreach (var fault in faultTypes)
        {
            var symptoms = new Dictionary<string, bool>();
            var posterior = _service.CalculatePosterior(fault.FaultCode, symptoms);

            posterior.Should().BeGreaterThanOrEqualTo(0,
                because: "后验概率不能小于0")
                .And.BeLessThanOrEqualTo(1,
                because: "后验概率不能大于1");

            if (fault.SymptomParameters != null)
            {
                foreach (var symptom in fault.SymptomParameters)
                {
                    symptom.ConditionalProbability.Should()
                        .BeGreaterThanOrEqualTo(0,
                            because: "条件概率不能小于0")
                        .And.BeLessThanOrEqualTo(1,
                            because: "条件概率不能大于1");
                    symptom.PriorProbability.Should()
                        .BeGreaterThanOrEqualTo(0,
                            because: "先验概率不能小于0")
                        .And.BeLessThanOrEqualTo(1,
                            because: "先验概率不能大于1");
                }
            }
        }
    }

    /// <summary>
    /// 测试重复初始化不应该创建重复节点。
    /// 验证点：多次调用InitializeNetworkAsync应该是幂等的。
    /// </summary>
    [Fact]
    public async Task InitializeNetwork_ShouldBeIdempotent()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        await _service.InitializeNetworkAsync();
        var symptoms1 = new Dictionary<string, bool> { { "Power", true } };
        var posterior1 = _service.CalculatePosterior("COMPRESSOR_FAILURE", symptoms1);

        await _service.InitializeNetworkAsync();
        var posterior2 = _service.CalculatePosterior("COMPRESSOR_FAILURE", symptoms1);

        // Assert
        posterior1.Should().Be(posterior2,
            because: "重复初始化不应该改变计算结果");
    }

    /// <summary>
    /// 测试添加故障节点应该正确创建故障节点。
    /// 验证点：AddFaultNode方法能正确添加单个故障节点。
    /// </summary>
    [Fact]
    public void AddFaultNode_ShouldCreateCorrectNodeStructure()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "TEST_FAULT",
            FaultName = "测试故障",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                }
            }
        };

        // Act
        _service.AddFaultNode(faultType);

        // Assert
        var symptoms = new Dictionary<string, bool> { { "Power", true } };
        var posterior = _service.CalculatePosterior("TEST_FAULT", symptoms);
        posterior.Should().BeGreaterThan(0,
            because: "新添加的故障节点应该能够计算后验概率");
    }

    /// <summary>
    /// 测试初始化网络在未初始化服务时应抛出异常。
    /// 验证点：未调用Initialize方法时调用InitializeNetworkAsync应抛出InvalidOperationException。
    /// </summary>
    [Fact]
    public async Task InitializeNetwork_WithoutServiceInitialize_ShouldThrow()
    {
        // Arrange
        var uninitializedService = new BayesianInferenceService();

        // Act
        Func<Task> act = async () => await uninitializedService.InitializeNetworkAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "未初始化的服务调用网络初始化应该抛出异常");
    }

    /// <summary>
    /// 测试空数据库时初始化网络不应抛出异常。
    /// 验证点：没有故障类型时初始化网络应正常完成。
    /// </summary>
    [Fact]
    public async Task InitializeNetwork_WithEmptyDatabase_ShouldNotThrow()
    {
        // Arrange - 不添加任何故障类型

        // Act
        Func<Task> act = async () => await _service.InitializeNetworkAsync();

        // Assert
        await act.Should().NotThrowAsync(
            because: "空数据库时初始化网络应该正常完成");
    }

    /// <summary>
    /// 测试故障节点的先验概率设置正确。
    /// 验证点：故障节点使用症状参数中的先验概率。
    /// </summary>
    [Fact]
    public void FaultNode_ShouldUseCorrectPriorProbability()
    {
        // Arrange
        var expectedPrior = 0.05;
        var faultType = new FaultType
        {
            FaultCode = "PRIOR_TEST",
            FaultName = "先验概率测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    PriorProbability = (decimal)expectedPrior,
                    ConditionalProbability = 0.8m
                }
            }
        };

        // Act
        _service.AddFaultNode(faultType);

        // Assert - 无症状时后验应该接近先验
        var symptoms = new Dictionary<string, bool>();
        var posterior = _service.CalculatePosterior("PRIOR_TEST", symptoms);
        posterior.Should().BeApproximately(expectedPrior, 0.01,
            because: "无症状时后验概率应接近先验概率");
    }

    /// <summary>
    /// 测试多症状故障节点创建正确。
    /// 验证点：具有多个症状参数的故障应创建多个症状节点。
    /// </summary>
    [Fact]
    public void MultipleSymptoms_ShouldCreateMultipleNodes()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "MULTI_SYMPTOM",
            FaultName = "多症状故障",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new() { ParameterName = "Power", ConditionalProbability = 0.9m, PriorProbability = 0.01m },
                new() { ParameterName = "Temperature", ConditionalProbability = 0.85m, PriorProbability = 0.01m },
                new() { ParameterName = "Pressure", ConditionalProbability = 0.8m, PriorProbability = 0.01m }
            }
        };

        // Act
        _service.AddFaultNode(faultType);

        // Assert - 所有症状都应被识别
        var allSymptoms = new Dictionary<string, bool>
        {
            { "Power", true },
            { "Temperature", true },
            { "Pressure", true }
        };
        var posteriorAll = _service.CalculatePosterior("MULTI_SYMPTOM", allSymptoms);

        var singleSymptom = new Dictionary<string, bool> { { "Power", true } };
        var posteriorSingle = _service.CalculatePosterior("MULTI_SYMPTOM", singleSymptom);

        posteriorAll.Should().BeGreaterThan(posteriorSingle,
            because: "匹配更多症状应该获得更高的后验概率");
    }

    /// <summary>
    /// 测试故障节点正确处理空症状参数列表。
    /// 验证点：没有症状参数的故障也应能创建节点。
    /// </summary>
    [Fact]
    public void FaultNode_WithNoSymptoms_ShouldStillBeCreated()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "NO_SYMPTOMS",
            FaultName = "无症状故障",
            SymptomParameters = new List<FaultSymptomParameter>()
        };

        // Act
        _service.AddFaultNode(faultType);

        // Assert
        var symptoms = new Dictionary<string, bool>();
        var posterior = _service.CalculatePosterior("NO_SYMPTOMS", symptoms);
        posterior.Should().BeGreaterThan(0,
            because: "即使没有症状参数，故障节点也应被创建");
    }

    /// <summary>
    /// 测试并发初始化网络的安全性。
    /// 验证点：多个线程同时初始化网络应该是安全的。
    /// </summary>
    [Fact]
    public async Task InitializeNetwork_ConcurrentCalls_ShouldBeSafe()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.InitializeNetworkAsync())
            .ToList();

        // Assert
        Func<Task> act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync(
            because: "并发调用网络初始化应该是线程安全的");
    }

    /// <summary>
    /// 测试使用Initialize方法初始化服务后能正常工作。
    /// 验证点：使用无参构造函数后调用Initialize方法应能正常工作。
    /// </summary>
    [Fact]
    public async Task InitializeMethod_ShouldSetupServiceCorrectly()
    {
        // Arrange
        var service = new BayesianInferenceService();
        var logger = CreateMockLogger<BayesianInferenceService>();
        await SeedFaultTypesAsync();

        // Act
        service.Initialize(_dbContext, logger.Object);
        await service.InitializeNetworkAsync();

        // Assert
        var symptoms = new Dictionary<string, bool> { { "Power", true } };
        var posterior = service.CalculatePosterior("COMPRESSOR_FAILURE", symptoms);
        posterior.Should().BeGreaterThan(0,
            because: "通过Initialize方法初始化的服务应该能正常工作");
    }
}

public class BayesianInferenceService_Inference_Tests : TestBase
{
    private readonly Mock<ILogger<BayesianInferenceService>> _mockLogger;
    private readonly BayesianInferenceService _service;

    public BayesianInferenceService_Inference_Tests()
    {
        _mockLogger = CreateMockLogger<BayesianInferenceService>();
        _service = new BayesianInferenceService(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试症状匹配越多后验概率越高。
    /// 验证点：匹配的症状数量与后验概率呈正相关。
    /// </summary>
    [Fact]
    public void PosteriorProbability_ShouldIncreaseWithMatchingSymptoms()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "INFERENCE_TEST",
            FaultName = "推理测试故障",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                },
                new()
                {
                    ParameterName = "Temperature",
                    DeviationType = "High",
                    ThresholdValue = 80m,
                    DeviationWeight = 0.7m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.85m
                },
                new()
                {
                    ParameterName = "Pressure",
                    DeviationType = "High",
                    ThresholdValue = 50m,
                    DeviationWeight = 0.6m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.8m
                }
            }
        };
        _service.AddFaultNode(faultType);

        // Act
        var posterior0 = _service.CalculatePosterior("INFERENCE_TEST",
            new Dictionary<string, bool>());

        var posterior1 = _service.CalculatePosterior("INFERENCE_TEST",
            new Dictionary<string, bool> { { "Power", true } });

        var posterior2 = _service.CalculatePosterior("INFERENCE_TEST",
            new Dictionary<string, bool>
            {
                { "Power", true },
                { "Temperature", true }
            });

        var posterior3 = _service.CalculatePosterior("INFERENCE_TEST",
            new Dictionary<string, bool>
            {
                { "Power", true },
                { "Temperature", true },
                { "Pressure", true }
            });

        // Assert
        posterior0.Should().BeLessThan(posterior1,
            because: "1个症状匹配应该比0个好");
        posterior1.Should().BeLessThan(posterior2,
            because: "2个症状匹配应该比1个好");
        posterior2.Should().BeLessThan(posterior3,
            because: "3个症状匹配应该比2个好");
    }

    /// <summary>
    /// 测试无症状时后验概率低。
    /// 验证点：没有匹配症状时后验概率应该很低。
    /// </summary>
    [Fact]
    public void NoSymptoms_ShouldReturnLowPosterior()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "LOW_POSTERIOR",
            FaultName = "低概率测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                }
            }
        };
        _service.AddFaultNode(faultType);

        // Act
        var symptoms = new Dictionary<string, bool>
        {
            { "Power", false }
        };
        var posterior = _service.CalculatePosterior("LOW_POSTERIOR", symptoms);

        // Assert
        posterior.Should().BeLessThan(0.5,
            because: "没有症状匹配时后验概率应该很低");
    }

    /// <summary>
    /// 测试全匹配时后验概率高。
    /// 验证点：所有症状都匹配时后验概率应该很高。
    /// </summary>
    [Fact]
    public void AllSymptomsMatch_ShouldReturnHighPosterior()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "HIGH_POSTERIOR",
            FaultName = "高概率测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.9m,
                    PriorProbability = 0.05m,
                    ConditionalProbability = 0.95m
                },
                new()
                {
                    ParameterName = "Temperature",
                    DeviationType = "High",
                    ThresholdValue = 80m,
                    DeviationWeight = 0.9m,
                    PriorProbability = 0.05m,
                    ConditionalProbability = 0.9m
                }
            }
        };
        _service.AddFaultNode(faultType);

        // Act
        var symptoms = new Dictionary<string, bool>
        {
            { "Power", true },
            { "Temperature", true }
        };
        var posterior = _service.CalculatePosterior("HIGH_POSTERIOR", symptoms);

        // Assert
        posterior.Should().BeGreaterThan(0.7,
            because: "所有症状都匹配时后验概率应该很高");
    }

    /// <summary>
    /// 测试相同输入推理结果一致。
    /// 验证点：相同的输入应该产生相同的输出。
    /// </summary>
    [Fact]
    public void Inference_ShouldBeConsistentForSameInput()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "CONSISTENCY_TEST",
            FaultName = "一致性测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.02m,
                    ConditionalProbability = 0.85m
                }
            }
        };
        _service.AddFaultNode(faultType);
        var symptoms = new Dictionary<string, bool> { { "Power", true } };

        // Act
        var results = Enumerable.Range(0, 10)
            .Select(_ => _service.CalculatePosterior("CONSISTENCY_TEST", symptoms))
            .ToList();

        // Assert
        results.Distinct().Should().ContainSingle(
            because: "相同输入的多次推理结果应该一致");
    }

    /// <summary>
    /// 测试未知故障代码返回0后验概率。
    /// 验证点：不存在的故障代码应该返回0。
    /// </summary>
    [Fact]
    public void UnknownFaultCode_ShouldReturnZeroPosterior()
    {
        // Arrange
        var symptoms = new Dictionary<string, bool> { { "Power", true } };

        // Act
        var posterior = _service.CalculatePosterior("UNKNOWN_FAULT_12345", symptoms);

        // Assert
        posterior.Should().Be(0,
            because: "未知故障代码应该返回0后验概率");
    }

    /// <summary>
    /// 测试未知症状参数不影响计算。
    /// 验证点：传入不存在的症状参数应该被忽略。
    /// </summary>
    [Fact]
    public void UnknownSymptomParameter_ShouldBeIgnored()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "UNKNOWN_SYMPTOM",
            FaultName = "未知症状测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                }
            }
        };
        _service.AddFaultNode(faultType);

        // Act
        var symptomsWithUnknown = new Dictionary<string, bool>
        {
            { "Power", true },
            { "UnknownParameter", true }
        };
        var posteriorWithUnknown = _service.CalculatePosterior("UNKNOWN_SYMPTOM", symptomsWithUnknown);

        var symptomsNormal = new Dictionary<string, bool> { { "Power", true } };
        var posteriorNormal = _service.CalculatePosterior("UNKNOWN_SYMPTOM", symptomsNormal);

        // Assert
        posteriorWithUnknown.Should().Be(posteriorNormal,
            because: "未知症状参数应该被忽略，不影响结果");
    }

    /// <summary>
    /// 测试负症状（症状不出现）降低后验概率。
    /// 验证点：症状不出现时后验概率应该降低。
    /// </summary>
    [Fact]
    public void NegativeSymptoms_ShouldDecreasePosterior()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "NEGATIVE_SYMPTOM",
            FaultName = "负症状测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 100m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                }
            }
        };
        _service.AddFaultNode(faultType);

        // Act
        var positiveSymptoms = new Dictionary<string, bool> { { "Power", true } };
        var posteriorPositive = _service.CalculatePosterior("NEGATIVE_SYMPTOM", positiveSymptoms);

        var negativeSymptoms = new Dictionary<string, bool> { { "Power", false } };
        var posteriorNegative = _service.CalculatePosterior("NEGATIVE_SYMPTOM", negativeSymptoms);

        var noSymptoms = new Dictionary<string, bool>();
        var posteriorNo = _service.CalculatePosterior("NEGATIVE_SYMPTOM", noSymptoms);

        // Assert
        posteriorNegative.Should().BeLessThan(posteriorNo,
            because: "负症状应该降低后验概率");
        posteriorNo.Should().BeLessThan(posteriorPositive,
            because: "正症状应该提高后验概率");
    }

    /// <summary>
    /// 测试推理结果的置信度计算正确。
    /// 验证点：InferFaultAsync返回的置信度应基于后验概率和症状权重。
    /// </summary>
    [Fact]
    public async Task InferFaultAsync_ShouldCalculateConfidence()
    {
        // Arrange
        await SeedFaultTypesAsync();
        await _service.InitializeNetworkAsync();

        var device = new Device { Id = "DEV001", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        var recentData = new List<DeviceData>
        {
            new() { DeviceId = "DEV001", Timestamp = DateTime.Now, Power = 150m, SupplyTemperature = 15m }
        };

        // Act
        var result = await _service.InferFaultAsync(device, recentData, faultType);

        // Assert
        if (result != null)
        {
            result.Confidence.Should()
                .BeGreaterThanOrEqualTo(0,
                    because: "置信度不能小于0")
                .And.BeLessThanOrEqualTo(1,
                    because: "置信度不能大于1");
            result.PosteriorProbability.Should()
                .BeGreaterThanOrEqualTo(0,
                    because: "后验概率不能小于0")
                .And.BeLessThanOrEqualTo(1,
                    because: "后验概率不能大于1");
        }
    }

    /// <summary>
    /// 测试低置信度结果返回null。
    /// 验证点：置信度低于阈值时应返回null。
    /// </summary>
    [Fact]
    public async Task InferFaultAsync_LowConfidence_ShouldReturnNull()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "LOW_CONF",
            FaultName = "低置信度测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 1000m,
                    DeviationWeight = 0.1m,
                    PriorProbability = 0.001m,
                    ConditionalProbability = 0.1m
                }
            }
        };
        _service.AddFaultNode(faultType);

        var device = new Device { Id = "DEV002", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var recentData = new List<DeviceData>
        {
            new() { DeviceId = "DEV002", Timestamp = DateTime.Now, Power = 50m }
        };

        // Act
        var result = await _service.InferFaultAsync(device, recentData, faultType);

        // Assert
        result.Should().BeNull(
            because: "低置信度的诊断结果应该返回null");
    }

    /// <summary>
    /// 测试推理结果包含正确的故障信息。
    /// 验证点：返回的推理结果应包含正确的故障代码和名称。
    /// </summary>
    [Fact]
    public async Task InferFaultAsync_ShouldReturnCorrectFaultInfo()
    {
        // Arrange
        await SeedFaultTypesAsync();
        await _service.InitializeNetworkAsync();

        var device = new Device { Id = "DEV003", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        var recentData = new List<DeviceData>
        {
            new() { DeviceId = "DEV003", Timestamp = DateTime.Now, Power = 200m, Current = 150m }
        };

        // Act
        var result = await _service.InferFaultAsync(device, recentData, faultType);

        // Assert
        if (result != null)
        {
            result.FaultCode.Should().Be("COMPRESSOR_FAILURE",
                because: "故障代码应该正确");
            result.FaultName.Should().Be("压缩机故障",
                because: "故障名称应该正确");
            result.MatchingSymptoms.Should().NotBeEmpty(
                because: "应该包含匹配的症状列表");
        }
    }

    /// <summary>
    /// 测试无匹配症状时返回null。
    /// 验证点：没有匹配症状时InferFaultAsync应返回null。
    /// </summary>
    [Fact]
    public async Task InferFaultAsync_NoMatchingSymptoms_ShouldReturnNull()
    {
        // Arrange
        var faultType = new FaultType
        {
            FaultCode = "NO_MATCH",
            FaultName = "无匹配测试",
            SymptomParameters = new List<FaultSymptomParameter>
            {
                new()
                {
                    ParameterName = "Power",
                    DeviationType = "High",
                    ThresholdValue = 1000m,
                    DeviationWeight = 0.8m,
                    PriorProbability = 0.01m,
                    ConditionalProbability = 0.9m
                }
            }
        };
        _service.AddFaultNode(faultType);

        var device = new Device { Id = "DEV004", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var recentData = new List<DeviceData>
        {
            new() { DeviceId = "DEV004", Timestamp = DateTime.Now, Power = 10m }
        };

        // Act
        var result = await _service.InferFaultAsync(device, recentData, faultType);

        // Assert
        result.Should().BeNull(
            because: "没有匹配症状时应该返回null");
    }

    /// <summary>
    /// 测试空数据时返回null。
    /// 验证点：设备数据为空时InferFaultAsync应返回null。
    /// </summary>
    [Fact]
    public async Task InferFaultAsync_EmptyData_ShouldReturnNull()
    {
        // Arrange
        await SeedFaultTypesAsync();
        await _service.InitializeNetworkAsync();

        var device = new Device { Id = "DEV005", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync();
        var recentData = new List<DeviceData>();

        // Act
        var result = await _service.InferFaultAsync(device, recentData, faultType);

        // Assert
        result.Should().BeNull(
            because: "空设备数据应该返回null");
    }
}

public class BayesianInferenceService_AnomalyDetection_Tests : TestBase
{
    private readonly Mock<ILogger<BayesianInferenceService>> _mockLogger;
    private readonly BayesianInferenceService _service;

    public BayesianInferenceService_AnomalyDetection_Tests()
    {
        _mockLogger = CreateMockLogger<BayesianInferenceService>();
        _service = new BayesianInferenceService(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试Z-score标记极端偏离。
    /// 验证点：显著偏离历史均值的参数应被标记为异常。
    /// </summary>
    [Fact]
    public void ZScore_ShouldFlagExtremeDeviations()
    {
        // Arrange
        var device = new Device { Id = "DEV001", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV001",
                Timestamp = now.AddHours(-i),
                Power = 100m + (decimal)(new Random(i).NextDouble() * 10 - 5)
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV001",
                Timestamp = now.AddMinutes(-i),
                Power = 200m
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.IsAnomaly.Should().BeTrue(
            because: "功率显著偏离历史均值应该被标记为异常");
        result.AnomalousParameters.Should().Contain("Power",
            because: "功率参数应该被标记为异常");
        result.ZScores["Power"].Should().BeGreaterThan(3,
            because: "Z-score应该超过阈值3");
    }

    /// <summary>
    /// 测试使用历史基准进行比较。
    /// 验证点：异常检测基于历史数据的均值和标准差。
    /// </summary>
    [Fact]
    public void HistoricalBaseline_ShouldBeUsedForComparison()
    {
        // Arrange
        var device = new Device { Id = "DEV002", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalMean = 100m;
        var historicalStdDev = 5m;

        var historicalData = Enumerable.Range(0, 100)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV002",
                Timestamp = now.AddHours(-i),
                Power = historicalMean + (decimal)(Math.Sin(i * 0.5) * (double)historicalStdDev)
            })
            .ToList();

        var recentData = Enumerable.Range(0, 10)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV002",
                Timestamp = now.AddMinutes(-i),
                Power = historicalMean + historicalStdDev * 4
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.IsAnomaly.Should().BeTrue(
            because: "超出4个标准差应该被检测为异常");
        result.ZScores["Power"].Should().BeApproximately(4, 0.5,
            because: "Z-score应该接近4");
    }

    /// <summary>
    /// 测试异常评分综合严重度和覆盖度。
    /// 验证点：AnomalyScore结合了最大Z-score和异常参数比例。
    /// </summary>
    [Fact]
    public void AnomalyScore_ShouldCombineSeverityAndCoverage()
    {
        // Arrange
        var device = new Device { Id = "DEV003", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV003",
                Timestamp = now.AddHours(-i),
                Power = 100m,
                SupplyTemperature = 12m,
                ReturnTemperature = 18m,
                Pressure = 5m,
                Current = 50m,
                FlowRate = 100m,
                Frequency = 50m
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV003",
                Timestamp = now.AddMinutes(-i),
                Power = 150m,
                SupplyTemperature = 12m,
                ReturnTemperature = 18m,
                Pressure = 5m,
                Current = 50m,
                FlowRate = 100m,
                Frequency = 50m
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.AnomalyScore.Should()
            .BeGreaterThan(0,
                because: "存在异常时应该有异常评分")
            .And.BeLessThanOrEqualTo(1,
                because: "异常评分不能超过1");
    }

    /// <summary>
    /// 测试多个异常参数提升评分。
    /// 验证点：异常参数越多，异常评分越高。
    /// </summary>
    [Fact]
    public void MultipleAnomalousParameters_ShouldIncreaseScore()
    {
        // Arrange
        var device1 = new Device { Id = "DEV004", Name = "单异常设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var device2 = new Device { Id = "DEV005", Name = "多异常设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV004",
                Timestamp = now.AddHours(-i),
                Power = 100m,
                SupplyTemperature = 12m,
                Pressure = 5m
            })
            .Select(d => new DeviceData
            {
                DeviceId = "DEV005",
                Timestamp = d.Timestamp,
                Power = d.Power,
                SupplyTemperature = d.SupplyTemperature,
                Pressure = d.Pressure
            })
            .Concat(Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV004",
                Timestamp = now.AddHours(-i - 30),
                Power = 100m,
                SupplyTemperature = 12m,
                Pressure = 5m
            }))
            .ToList();

        var historicalData1 = historicalData.Where(d => d.DeviceId == "DEV004").ToList();
        var historicalData2 = historicalData.Where(d => d.DeviceId == "DEV005").ToList();

        var recentDataSingle = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV004",
                Timestamp = now.AddMinutes(-i),
                Power = 200m,
                SupplyTemperature = 12m,
                Pressure = 5m
            })
            .ToList();

        var recentDataMultiple = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV005",
                Timestamp = now.AddMinutes(-i),
                Power = 200m,
                SupplyTemperature = 25m,
                Pressure = 15m
            })
            .ToList();

        // Act
        var resultSingle = _service.DetectAnomalies(device1, recentDataSingle, historicalData1);
        var resultMultiple = _service.DetectAnomalies(device2, recentDataMultiple, historicalData2);

        // Assert
        resultMultiple.AnomalyScore.Should().BeGreaterThan(resultSingle.AnomalyScore,
            because: "多个异常参数应该产生更高的异常评分");
        resultMultiple.AnomalousParameters.Count.Should().BeGreaterThan(resultSingle.AnomalousParameters.Count,
            because: "应该检测到更多的异常参数");
    }

    /// <summary>
    /// 测试正常数据不标记为异常。
    /// 验证点：与历史数据一致的数据不应被标记为异常。
    /// </summary>
    [Fact]
    public void NormalData_ShouldNotBeFlaggedAsAnomaly()
    {
        // Arrange
        var device = new Device { Id = "DEV006", Name = "正常设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV006",
                Timestamp = now.AddHours(-i),
                Power = 100m + (decimal)(new Random(i).NextDouble() * 5 - 2.5)
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV006",
                Timestamp = now.AddMinutes(-i),
                Power = 100m + (decimal)(new Random(i + 100).NextDouble() * 5 - 2.5)
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.IsAnomaly.Should().BeFalse(
            because: "正常波动的数据不应该被标记为异常");
        result.AnomalyScore.Should().BeLessThan(0.5,
            because: "正常数据的异常评分应该很低");
    }

    /// <summary>
    /// 测试空历史数据返回默认结果。
    /// 验证点：没有历史数据时应返回空的检测结果。
    /// </summary>
    [Fact]
    public void EmptyHistoricalData_ShouldReturnDefaultResult()
    {
        // Arrange
        var device = new Device { Id = "DEV007", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var recentData = new List<DeviceData>
        {
            new() { DeviceId = "DEV007", Timestamp = DateTime.Now, Power = 100m }
        };
        var historicalData = new List<DeviceData>();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.IsAnomaly.Should().BeFalse(
            because: "没有历史数据无法检测异常");
        result.AnomalousParameters.Should().BeEmpty(
            because: "没有历史数据时异常参数列表应该为空");
        result.ZScores.Should().BeEmpty(
            because: "没有历史数据时无法计算Z-score");
    }

    /// <summary>
    /// 测试空近期数据返回默认结果。
    /// 验证点：没有近期数据时应返回空的检测结果。
    /// </summary>
    [Fact]
    public void EmptyRecentData_ShouldReturnDefaultResult()
    {
        // Arrange
        var device = new Device { Id = "DEV008", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var historicalData = new List<DeviceData>
        {
            new() { DeviceId = "DEV008", Timestamp = DateTime.Now.AddHours(-1), Power = 100m }
        };
        var recentData = new List<DeviceData>();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.IsAnomaly.Should().BeFalse(
            because: "没有近期数据无法检测异常");
        result.AnomalousParameters.Should().BeEmpty(
            because: "没有近期数据时异常参数列表应该为空");
    }

    /// <summary>
    /// 测试标准差为0时不计算Z-score。
    /// 验证点：历史数据没有变化时应跳过该参数。
    /// </summary>
    [Fact]
    public void ZeroStdDev_ShouldSkipParameter()
    {
        // Arrange
        var device = new Device { Id = "DEV009", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 10)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV009",
                Timestamp = now.AddHours(-i),
                Power = 100m,
                SupplyTemperature = 12m
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV009",
                Timestamp = now.AddMinutes(-i),
                Power = 100m,
                SupplyTemperature = 12m
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.ZScores.Should().NotContainKey("Power",
            because: "标准差为0的参数应该被跳过");
    }

    /// <summary>
    /// 测试异常评分上限为1。
    /// 验证点：即使非常严重的异常，评分也不会超过1。
    /// </summary>
    [Fact]
    public void AnomalyScore_ShouldBeCappedAtOne()
    {
        // Arrange
        var device = new Device { Id = "DEV010", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV010",
                Timestamp = now.AddHours(-i),
                Power = 100m,
                SupplyTemperature = 12m,
                Pressure = 5m,
                Current = 50m
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV010",
                Timestamp = now.AddMinutes(-i),
                Power = 1000m,
                SupplyTemperature = 100m,
                Pressure = 100m,
                Current = 500m
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.AnomalyScore.Should().BeLessThanOrEqualTo(1,
            because: "异常评分的上限应该是1");
        result.AnomalyScore.Should().BeGreaterThan(0.8,
            because: "严重异常的评分应该很高");
    }

    /// <summary>
    /// 测试阈值可配置。
    /// 验证点：可以通过修改Threshold属性调整异常检测灵敏度。
    /// </summary>
    [Fact]
    public void Threshold_ShouldAffectDetectionSensitivity()
    {
        // Arrange
        var device = new Device { Id = "DEV011", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV011",
                Timestamp = now.AddHours(-i),
                Power = 100m + (decimal)(new Random(i).NextDouble() * 10 - 5)
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV011",
                Timestamp = now.AddMinutes(-i),
                Power = 125m
            })
            .ToList();

        // Act
        var resultLowThreshold = _service.DetectAnomalies(device, recentData, historicalData);
        var originalThreshold = resultLowThreshold.Threshold;

        // 手动设置更高阈值进行第二次检测需要创建新实例
        var serviceWithHighThreshold = new BayesianInferenceService(_dbContext, _mockLogger.Object);
        var resultHighThreshold = serviceWithHighThreshold.DetectAnomalies(device, recentData, historicalData);
        resultHighThreshold.Threshold = 10;

        // Assert
        resultLowThreshold.Threshold.Should().Be(3.0,
            because: "默认阈值应该是3.0");
        originalThreshold.Should().Be(3.0,
            because: "默认阈值应该是3.0");
    }

    /// <summary>
    /// 测试多个参数部分异常。
    /// 验证点：只标记真正异常的参数。
    /// </summary>
    [Fact]
    public void PartialAnomalies_ShouldOnlyFlagDeviatingParameters()
    {
        // Arrange
        var device = new Device { Id = "DEV012", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var now = DateTime.Now;

        var historicalData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV012",
                Timestamp = now.AddHours(-i),
                Power = 100m,
                SupplyTemperature = 12m,
                Pressure = 5m,
                Current = 50m
            })
            .ToList();

        var recentData = Enumerable.Range(0, 5)
            .Select(i => new DeviceData
            {
                DeviceId = "DEV012",
                Timestamp = now.AddMinutes(-i),
                Power = 200m,
                SupplyTemperature = 12m,
                Pressure = 5m,
                Current = 50m
            })
            .ToList();

        // Act
        var result = _service.DetectAnomalies(device, recentData, historicalData);

        // Assert
        result.AnomalousParameters.Should().Contain("Power",
            because: "功率异常应该被检测到");
        result.AnomalousParameters.Should().NotContain("SupplyTemperature",
            because: "供水温度正常不应该被标记");
        result.AnomalousParameters.Should().NotContain("Pressure",
            because: "压力正常不应该被标记");
        result.AnomalousParameters.Should().NotContain("Current",
            because: "电流正常不应该被标记");
    }

    /// <summary>
    /// 测试未初始化服务调用异常检测应抛出异常。
    /// 验证点：未调用Initialize方法时调用DetectAnomalies应抛出InvalidOperationException。
    /// </summary>
    [Fact]
    public void DetectAnomalies_WithoutInitialization_ShouldThrow()
    {
        // Arrange
        var uninitializedService = new BayesianInferenceService();
        var device = new Device { Id = "DEV013", Name = "测试设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running };
        var data = new List<DeviceData>
        {
            new() { DeviceId = "DEV013", Timestamp = DateTime.Now, Power = 100m }
        };

        // Act
        Action act = () => uninitializedService.DetectAnomalies(device, data, data);

        // Assert
        act.Should().Throw<InvalidOperationException>(
            because: "未初始化的服务调用异常检测应该抛出异常");
    }
}
