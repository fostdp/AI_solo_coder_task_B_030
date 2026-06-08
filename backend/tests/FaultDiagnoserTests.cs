using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

public class FaultDiagnoser_Integration_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnoser>> _mockLogger;
    private readonly Mock<IBayesianInferenceService> _mockBayesian;
    private readonly FaultDiagnoser _diagnoser;

    public FaultDiagnoser_Integration_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnoser>();
        _mockBayesian = new Mock<IBayesianInferenceService>();
        _diagnoser = new FaultDiagnoser(_dbContext, _mockLogger.Object, _mockBayesian.Object);
    }

    /// <summary>
    /// 测试已知故障应该以高置信度被诊断。
    /// 验证点：当设备数据匹配已知故障模式时，应返回高置信度的诊断结果。
    /// </summary>
    [Fact]
    public async Task KnownFault_ShouldBeDiagnosedWithHighConfidence()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER001",
            Name = "测试冷水机组",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER001",
                Timestamp = now.AddMinutes(-i),
                Power = 200m,
                Current = 180m,
                SupplyTemperature = 15m,
                ReturnTemperature = 22m,
                Pressure = 8m,
                FlowRate = 90m,
                Frequency = 50m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.85,
                AnomalousParameters = new List<string> { "Power", "Current" },
                ZScores = new Dictionary<string, double> { { "Power", 4.5 }, { "Current", 3.8 } }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.92,
                Confidence = 0.88,
                MatchingSymptoms = new List<string> { "Power", "Current" },
                DeviationDetails = "功率偏高,电流偏大",
                Recommendation = "检查压缩机绕组,更换轴承"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER001");

        // Assert
        results.Should().NotBeEmpty(
            because: "应该诊断出已知故障");
        results.First().FaultCode.Should().Be("COMPRESSOR_FAILURE",
            because: "应该正确识别压缩机故障");
        results.First().Confidence.Should().BeGreaterThan(0.7m,
            because: "已知故障应该有高置信度");
    }

    /// <summary>
    /// 测试未知故障模式应该创建未知故障记录。
    /// 验证点：当检测到异常但不匹配任何已知故障时，应创建未知故障记录。
    /// </summary>
    [Fact]
    public async Task UnknownFaultPattern_ShouldCreateUnknownFaultRecord()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER002",
            Name = "测试冷水机组2",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER002",
                Timestamp = now.AddMinutes(-i),
                Power = 500m,
                SupplyTemperature = 25m,
                Pressure = 20m,
                Vibration = 15m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.75,
                AnomalousParameters = new List<string> { "Power", "SupplyTemperature", "Pressure" },
                ZScores = new Dictionary<string, double> { { "Power", 5.2 }, { "SupplyTemperature", 4.8 }, { "Pressure", 4.1 } }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<FaultType>()))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER002");

        // Assert
        results.Should().NotBeEmpty(
            because: "检测到异常时应该返回诊断结果");
        results.First().IsUnknownFault.Should().BeTrue(
            because: "不匹配已知故障时应该标记为未知故障");
        results.First().FaultCode.Should().Be("UNKNOWN-001",
            because: "未知故障代码应该正确");
    }

    /// <summary>
    /// 测试诊断所有运行设备。
    /// 验证点：DiagnoseAllAsync应该处理所有状态为Running的设备。
    /// </summary>
    [Fact]
    public async Task DiagnoseAll_ShouldProcessAllRunningDevices()
    {
        // Arrange
        await SeedFaultTypesAsync();

        var devices = new List<Device>
        {
            new() { Id = "CHILLER001", Name = "运行设备1", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running },
            new() { Id = "CHILLER002", Name = "运行设备2", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running },
            new() { Id = "CHILLER003", Name = "停机设备", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Offline },
            new() { Id = "PUMP001", Name = "运行水泵", DeviceTypeId = DeviceType.Pump, Status = DeviceStatus.Running }
        };
        await _dbContext.Devices.AddRangeAsync(devices);

        var now = DateTime.UtcNow;
        var allData = new List<DeviceData>();
        foreach (var device in devices.Where(d => d.Status == DeviceStatus.Running))
        {
            allData.AddRange(Enumerable.Range(0, 10)
                .Select(i => new DeviceData
                {
                    DeviceId = device.Id,
                    Timestamp = now.AddMinutes(-i),
                    Power = 100m
                }));
        }
        await _dbContext.DeviceData.AddRangeAsync(allData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = false,
                AnomalyScore = 0.1,
                AnomalousParameters = new List<string>()
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<FaultType>()))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseAllAsync();

        // Assert
        _mockBayesian.Verify(b => b.DetectAnomalies(
            It.Is<Device>(d => d.Status == DeviceStatus.Running),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()),
            Times.Exactly(3),
            because: "应该只诊断运行中的设备");
    }

    /// <summary>
    /// 测试诊断结果持久化。
    /// 验证点：诊断结果应该保存到数据库中。
    /// </summary>
    [Fact]
    public async Task DiagnosisResults_ShouldBePersisted()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER004",
            Name = "测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER004",
                Timestamp = now.AddMinutes(-i),
                Power = 180m,
                Current = 160m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.8,
                AnomalousParameters = new List<string> { "Power" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.9,
                Confidence = 0.85,
                MatchingSymptoms = new List<string> { "Power", "Current" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER004");

        // Assert
        using var newContext = CreateNewContext();
        var savedResults = await newContext.FaultDiagnosisResults
            .Where(r => r.DeviceId == "CHILLER004")
            .ToListAsync();

        savedResults.Should().NotBeEmpty(
            because: "诊断结果应该被持久化到数据库");
        savedResults.First().FaultCode.Should().Be("COMPRESSOR_FAILURE",
            because: "保存的故障代码应该正确");
        savedResults.First().Confidence.Should().Be(0.85m,
            because: "保存的置信度应该正确");
    }

    /// <summary>
    /// 测试不存在的设备诊断应抛出异常。
    /// 验证点：诊断不存在的设备时应抛出KeyNotFoundException。
    /// </summary>
    [Fact]
    public async Task DiagnoseDeviceAsync_NonExistentDevice_ShouldThrow()
    {
        // Arrange
        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        // Act
        Func<Task> act = async () => await _diagnoser.DiagnoseDeviceAsync("NON_EXISTENT");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "诊断不存在的设备应该抛出异常");
    }

    /// <summary>
    /// 测试没有设备数据时返回空列表。
    /// 验证点：设备没有运行数据时应返回空的诊断结果列表。
    /// </summary>
    [Fact]
    public async Task DiagnoseDeviceAsync_NoDeviceData_ShouldReturnEmpty()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER005",
            Name = "无数据设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER005");

        // Assert
        results.Should().BeEmpty(
            because: "没有设备数据时应该返回空列表");
    }

    /// <summary>
    /// 测试诊断确认功能。
    /// 验证点：ConfirmDiagnosisAsync应该正确更新诊断状态。
    /// </summary>
    [Fact]
    public async Task ConfirmDiagnosisAsync_ShouldUpdateStatus()
    {
        // Arrange
        var diagnosis = new FaultDiagnosisResult
        {
            DeviceId = "CHILLER001",
            FaultTypeId = 1,
            Timestamp = DateTime.UtcNow,
            Confidence = 0.9m,
            BayesProbability = 0.85m,
            IsConfirmed = false
        };
        await _dbContext.FaultDiagnosisResults.AddAsync(diagnosis);
        await _dbContext.SaveChangesAsync();

        // Act
        await _diagnoser.ConfirmDiagnosisAsync(diagnosis.Id, "test_user");

        // Assert
        using var newContext = CreateNewContext();
        var updated = await newContext.FaultDiagnosisResults.FindAsync(diagnosis.Id);
        updated!.IsConfirmed.Should().BeTrue(
            because: "诊断应该被标记为已确认");
        updated.ConfirmedBy.Should().Be("test_user",
            because: "确认人应该正确记录");
        updated.ConfirmedAt.Should().NotBeNull(
            because: "确认时间应该被记录");
    }

    /// <summary>
    /// 测试确认不存在的诊断应抛出异常。
    /// 验证点：确认不存在的诊断ID时应抛出KeyNotFoundException。
    /// </summary>
    [Fact]
    public async Task ConfirmDiagnosisAsync_NonExistent_ShouldThrow()
    {
        // Act
        Func<Task> act = async () => await _diagnoser.ConfirmDiagnosisAsync(999999, "test_user");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "确认不存在的诊断应该抛出异常");
    }

    /// <summary>
    /// 测试获取设备类型对应的故障类型。
    /// 验证点：GetFaultTypesForDeviceTypeAsync应该返回正确的故障类型。
    /// </summary>
    [Fact]
    public async Task GetFaultTypesForDeviceTypeAsync_ShouldReturnCorrectTypes()
    {
        // Arrange
        await SeedFaultTypesAsync();

        // Act
        var chillerFaults = await _diagnoser.GetFaultTypesForDeviceTypeAsync(DeviceType.Chiller);
        var pumpFaults = await _diagnoser.GetFaultTypesForDeviceTypeAsync(DeviceType.Pump);

        // Assert
        chillerFaults.Should().NotBeEmpty(
            because: "冷水机组应该有对应的故障类型");
        chillerFaults.All(f => f.DeviceTypeId == DeviceType.Chiller).Should().BeTrue(
            because: "所有返回的故障类型都应该属于冷水机组");

        pumpFaults.Should().NotBeEmpty(
            because: "水泵应该有对应的故障类型");
        pumpFaults.All(f => f.DeviceTypeId == DeviceType.Pump).Should().BeTrue(
            because: "所有返回的故障类型都应该属于水泵");
    }

    /// <summary>
    /// 测试每日统计功能。
    /// 验证点：GetDailyStatisticsAsync应该正确计算统计数据。
    /// </summary>
    [Fact]
    public async Task GetDailyStatisticsAsync_ShouldCalculateCorrectly()
    {
        // Arrange
        var today = DateTime.UtcNow.Date;
        var diagnoses = new List<FaultDiagnosisResult>
        {
            new() { DeviceId = "D1", FaultTypeId = 1, Timestamp = today.AddHours(8), Confidence = 0.9m, IsConfirmed = true },
            new() { DeviceId = "D2", FaultTypeId = 2, Timestamp = today.AddHours(10), Confidence = 0.85m, IsConfirmed = true },
            new() { DeviceId = "D3", FaultTypeId = 1, Timestamp = today.AddHours(14), Confidence = 0.7m, IsConfirmed = false },
            new() { DeviceId = "D4", FaultTypeId = 3, Timestamp = today.AddHours(16), Confidence = 0.5m, IsConfirmed = false }
        };
        await _dbContext.FaultDiagnosisResults.AddRangeAsync(diagnoses);
        await _dbContext.SaveChangesAsync();

        // Act
        var stats = await _diagnoser.GetDailyStatisticsAsync(today);

        // Assert
        stats.TotalDiagnosisCount.Should().Be(4,
            because: "应该统计当天所有诊断");
        stats.ConfirmedFaultCount.Should().Be(2,
            because: "应该统计已确认的故障数");
        stats.FalsePositiveCount.Should().Be(1,
            because: "应该统计高置信度但未确认的误报数");
        stats.AverageConfidence.Should().BeApproximately(0.7375m, 0.0001m,
            because: "平均置信度计算应该正确");
        stats.AccuracyRate.Should().Be(0.5m,
            because: "准确率应该是确认数除以总数");
    }

    /// <summary>
    /// 测试异常检测异步方法。
    /// 验证点：DetectAnomaliesAsync应该返回正确的异常报告。
    /// </summary>
    [Fact]
    public async Task DetectAnomaliesAsync_ShouldReturnCorrectReport()
    {
        // Arrange
        var device = new Device
        {
            Id = "CHILLER006",
            Name = "异常测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER006",
                Timestamp = now.AddMinutes(-i),
                Power = 150m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        var expectedResult = new AnomalyDetectionResult
        {
            IsAnomaly = true,
            AnomalyScore = 0.75,
            AnomalousParameters = new List<string> { "Power" },
            ZScores = new Dictionary<string, double> { { "Power", 3.5 } }
        };

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(expectedResult);

        // Act
        var report = await _diagnoser.DetectAnomaliesAsync("CHILLER006");

        // Assert
        report.Should().NotBeNull(
            because: "应该返回异常报告");
        report.IsAnomaly.Should().BeTrue(
            because: "异常状态应该正确");
        report.AnomalyScore.Should().Be(0.75,
            because: "异常评分应该正确");
        report.AnomalousParameters.Should().Contain("Power",
            because: "异常参数应该正确");
        report.ZScores["Power"].Should().Be(3.5,
            because: "Z-score应该正确");
    }

    /// <summary>
    /// 测试诊断单个设备时错误处理。
    /// 验证点：DiagnoseAllAsync中单个设备诊断失败不应影响其他设备。
    /// </summary>
    [Fact]
    public async Task DiagnoseAllAsync_SingleDeviceFailure_ShouldContinue()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var devices = new List<Device>
        {
            new() { Id = "DEV001", Name = "设备1", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running },
            new() { Id = "DEV002", Name = "设备2", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running }
        };
        await _dbContext.Devices.AddRangeAsync(devices);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.Is<Device>(d => d.Id == "DEV001"),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Throws(new InvalidOperationException("模拟错误"));

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.Is<Device>(d => d.Id == "DEV002"),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult { IsAnomaly = false });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<FaultType>()))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        Func<Task> act = async () => await _diagnoser.DiagnoseAllAsync();

        // Assert
        await act.Should().NotThrowAsync(
            because: "单个设备失败不应该导致整体失败");
    }
}

public class FaultDiagnoser_ResultPresentation_Tests : TestBase
{
    private readonly Mock<ILogger<FaultDiagnoser>> _mockLogger;
    private readonly Mock<IBayesianInferenceService> _mockBayesian;
    private readonly FaultDiagnoser _diagnoser;

    public FaultDiagnoser_ResultPresentation_Tests()
    {
        _mockLogger = CreateMockLogger<FaultDiagnoser>();
        _mockBayesian = new Mock<IBayesianInferenceService>();
        _diagnoser = new FaultDiagnoser(_dbContext, _mockLogger.Object, _mockBayesian.Object);
    }

    /// <summary>
    /// 测试结果按置信度降序排列。
    /// 验证点：多个诊断结果应该按置信度从高到低排序。
    /// </summary>
    [Fact]
    public async Task Results_ShouldBeSortedByConfidenceDescending()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER010",
            Name = "排序测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER010",
                Timestamp = now.AddMinutes(-i),
                Power = 180m,
                Current = 150m,
                SupplyTemperature = 16m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.8,
                AnomalousParameters = new List<string> { "Power" }
            });

        var faultTypes = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .Where(f => f.DeviceTypeId == DeviceType.Chiller)
            .Take(3)
            .ToListAsync();

        var confidences = new[] { 0.7, 0.9, 0.8 };
        for (int i = 0; i < faultTypes.Count; i++)
        {
            var fault = faultTypes[i];
            var confidence = confidences[i];
            _mockBayesian.Setup(b => b.InferFaultAsync(
                It.IsAny<Device>(),
                It.IsAny<List<DeviceData>>(),
                It.Is<FaultType>(f => f.FaultCode == fault.FaultCode)))
                .ReturnsAsync(new InferenceResult
                {
                    FaultCode = fault.FaultCode,
                    FaultName = fault.FaultName,
                    PosteriorProbability = confidence - 0.05,
                    Confidence = confidence,
                    MatchingSymptoms = new List<string> { "Power" },
                    DeviationDetails = "功率异常",
                    Recommendation = $"建议检修{fault.FaultName}"
                });
        }

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => !faultTypes.Any(ft => ft.FaultCode == f.FaultCode))))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER010");

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(2,
            because: "应该返回多个诊断结果");
        
        var confidencesResult = results.Select(r => r.Confidence).ToList();
        confidencesResult.Should().BeInDescendingOrder(
            because: "结果应该按置信度降序排列");
    }

    /// <summary>
    /// 测试未知故障的贝叶斯概率为0。
    /// 验证点：未知故障模式的BayesProbability属性应为0。
    /// </summary>
    [Fact]
    public async Task UnknownFault_ShouldHaveZeroBayesProbability()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER011",
            Name = "未知故障测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 25)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER011",
                Timestamp = now.AddMinutes(-i),
                Power = 300m,
                SupplyTemperature = 30m,
                Pressure = 25m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.85,
                AnomalousParameters = new List<string> { "Power", "SupplyTemperature", "Pressure" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<FaultType>()))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER011");

        // Assert
        var unknownFault = results.First(r => r.IsUnknownFault);
        unknownFault.BayesProbability.Should().Be(0m,
            because: "未知故障的贝叶斯概率应该为0");
    }

    /// <summary>
    /// 测试所有结果包含异常评分。
    /// 验证点：每个诊断结果都应包含AnomalyScore属性。
    /// </summary>
    [Fact]
    public async Task AllResults_ShouldIncludeAnomalyScore()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER012",
            Name = "异常评分测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER012",
                Timestamp = now.AddMinutes(-i),
                Power = 180m,
                Current = 160m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        var expectedAnomalyScore = 0.75m;

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = (double)expectedAnomalyScore,
                AnomalousParameters = new List<string> { "Power" }
            });

        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = new List<string> { "Power" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER012");

        // Assert
        results.Should().NotBeEmpty();
        foreach (var result in results)
        {
            result.AnomalyScore.Should().NotBeNull(
                because: "所有诊断结果都应该包含异常评分");
            result.AnomalyScore.Should().Be(expectedAnomalyScore,
                because: "异常评分应该正确");
        }
    }

    /// <summary>
    /// 测试检修建议与故障对应。
    /// 验证点：不同的故障类型应有对应的检修建议。
    /// </summary>
    [Fact]
    public async Task MaintenanceRecommendation_ShouldBeSpecificToFault()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER013",
            Name = "建议测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER013",
                Timestamp = now.AddMinutes(-i),
                Power = 190m,
                Current = 170m
            })
            .ToList();
        await _dbContext.DeviceData.AddRange(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.8,
                AnomalousParameters = new List<string> { "Power" }
            });

        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        var expectedRecommendation = "检查压缩机绕组,更换轴承,清理散热片";

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.88,
                Confidence = 0.85,
                MatchingSymptoms = new List<string> { "Power", "Current" },
                DeviationDetails = "功率偏高,电流偏大",
                Recommendation = expectedRecommendation
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER013");

        // Assert
        var result = results.First();
        result.MaintenanceRecommendation.Should().Be(expectedRecommendation,
            because: "检修建议应该与故障类型对应");
        result.MaintenanceRecommendation.Should().NotBeNullOrWhiteSpace(
            because: "检修建议不应该为空");
    }

    /// <summary>
    /// 测试诊断结果包含正确的故障信息。
    /// 验证点：DiagnosisResult应包含正确的故障代码和名称。
    /// </summary>
    [Fact]
    public async Task DiagnosisResult_ShouldContainCorrectFaultInfo()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER014",
            Name = "信息测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER014",
                Timestamp = now.AddMinutes(-i),
                Power = 170m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.7,
                AnomalousParameters = new List<string> { "Power" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = new List<string> { "Power" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER014");

        // Assert
        var result = results.First();
        result.FaultCode.Should().Be("COMPRESSOR_FAILURE",
            because: "故障代码应该正确");
        result.FaultName.Should().Be("压缩机故障",
            because: "故障名称应该正确");
        result.DeviceId.Should().Be("CHILLER014",
            because: "设备ID应该正确");
    }

    /// <summary>
    /// 测试匹配症状正确序列化。
    /// 验证点：MatchingSymptoms属性应为逗号分隔的字符串。
    /// </summary>
    [Fact]
    public async Task MatchingSymptoms_ShouldBeCommaSeparated()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER015",
            Name = "症状测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER015",
                Timestamp = now.AddMinutes(-i),
                Power = 180m,
                Current = 160m,
                SupplyTemperature = 16m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.75,
                AnomalousParameters = new List<string> { "Power", "Current", "SupplyTemperature" }
            });

        var expectedSymptoms = new List<string> { "Power", "Current", "SupplyTemperature" };

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = expectedSymptoms,
                DeviationDetails = "多个参数异常",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER015");

        // Assert
        var result = results.First();
        result.MatchingSymptoms.Should().Be("Power,Current,SupplyTemperature",
            because: "匹配症状应该用逗号分隔");
    }

    /// <summary>
    /// 测试估计维修成本计算正确。
    /// 验证点：EstimatedRepairCost应基于估计维修工时计算。
    /// </summary>
    [Fact]
    public async Task EstimatedRepairCost_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER016",
            Name = "成本测试设备",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER016",
                Timestamp = now.AddMinutes(-i),
                Power = 180m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.7,
                AnomalousParameters = new List<string> { "Power" }
            });

        var faultType = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .FirstAsync(f => f.FaultCode == "COMPRESSOR_FAILURE");

        var expectedHours = faultType.EstimatedRepairHours ?? 8;
        var expectedCost = expectedHours * 500;

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = new List<string> { "Power" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER016");

        // Assert
        var result = results.First();
        result.EstimatedRepairCost.Should().Be(expectedCost,
            because: "维修成本应该是维修工时乘以500");
        result.EstimatedDowntimeHours.Should().Be(expectedHours,
            because: "停机时间应该等于估计维修工时");
    }

    /// <summary>
    /// 测试未知故障的检修建议包含异常详情。
    /// 验证点：未知故障的MaintenanceRecommendation应包含异常参数和评分。
    /// </summary>
    [Fact]
    public async Task UnknownFault_Recommendation_ShouldIncludeAnomalyDetails()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER017",
            Name = "未知建议测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER017",
                Timestamp = now.AddMinutes(-i),
                Power = 400m,
                SupplyTemperature = 35m,
                Pressure = 30m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        var anomalyScore = 0.85;
        var anomalousParams = new List<string> { "Power", "SupplyTemperature", "Pressure" };

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = anomalyScore,
                AnomalousParameters = anomalousParams,
                ZScores = new Dictionary<string, double> { { "Power", 5.5 }, { "SupplyTemperature", 4.8 }, { "Pressure", 4.2 } }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<FaultType>()))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER017");

        // Assert
        var unknownFault = results.First();
        unknownFault.MaintenanceRecommendation.Should().Contain("未知异常模式",
            because: "应该明确标记为未知异常");
        unknownFault.MaintenanceRecommendation.Should().Contain($"{anomalyScore:P0}",
            because: "应该包含异常评分");
        foreach (var param in anomalousParams)
        {
            unknownFault.MaintenanceRecommendation.Should().Contain(param,
                because: $"应该包含异常参数 {param}");
        }
    }

    /// <summary>
    /// 测试诊断结果包含时间戳。
    /// 验证点：每个诊断结果应有正确的诊断时间。
    /// </summary>
    [Fact]
    public async Task DiagnosisResult_ShouldHaveTimestamp()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER018",
            Name = "时间戳测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER018",
                Timestamp = now.AddMinutes(-i),
                Power = 170m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.7,
                AnomalousParameters = new List<string> { "Power" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = new List<string> { "Power" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var before = DateTime.UtcNow;
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER018");
        var after = DateTime.UtcNow;

        // Assert
        var result = results.First();
        result.Timestamp.Should().BeOnOrAfter(before,
            because: "诊断时间应该在调用之前或同时");
        result.Timestamp.Should().BeOnOrBefore(after,
            because: "诊断时间应该在调用之后或同时");
    }

    /// <summary>
    /// 测试最多返回3个诊断结果。
    /// 验证点：即使有多个高置信度故障，也只返回前3个。
    /// </summary>
    [Fact]
    public async Task DiagnosisResults_ShouldBeLimitedToThree()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER019",
            Name = "数量限制测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 30)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER019",
                Timestamp = now.AddMinutes(-i),
                Power = 180m,
                Current = 160m,
                SupplyTemperature = 16m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.8,
                AnomalousParameters = new List<string> { "Power" }
            });

        var faultTypes = await _dbContext.FaultTypes
            .Include(f => f.SymptomParameters)
            .Where(f => f.DeviceTypeId == DeviceType.Chiller)
            .Take(5)
            .ToListAsync();

        var confidences = new[] { 0.95, 0.9, 0.85, 0.8, 0.75 };
        for (int i = 0; i < faultTypes.Count; i++)
        {
            var fault = faultTypes[i];
            var confidence = confidences[i];
            _mockBayesian.Setup(b => b.InferFaultAsync(
                It.IsAny<Device>(),
                It.IsAny<List<DeviceData>>(),
                It.Is<FaultType>(f => f.FaultCode == fault.FaultCode)))
                .ReturnsAsync(new InferenceResult
                {
                    FaultCode = fault.FaultCode,
                    FaultName = fault.FaultName,
                    PosteriorProbability = confidence - 0.05,
                    Confidence = confidence,
                    MatchingSymptoms = new List<string> { "Power" },
                    DeviationDetails = "功率异常",
                    Recommendation = $"检修{fault.FaultName}"
                });
        }

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => !faultTypes.Any(ft => ft.FaultCode == f.FaultCode))))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER019");

        // Assert
        results.Should().HaveCount(3,
            because: "最多只返回前3个诊断结果");
    }

    /// <summary>
    /// 测试诊断结果的置信度范围正确。
    /// 验证点：所有诊断结果的置信度应在[0, 1]范围内。
    /// </summary>
    [Fact]
    public async Task Confidence_ShouldBeInValidRange()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER020",
            Name = "置信度测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER020",
                Timestamp = now.AddMinutes(-i),
                Power = 180m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.75,
                AnomalousParameters = new List<string> { "Power" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.85,
                Confidence = 0.8,
                MatchingSymptoms = new List<string> { "Power" },
                DeviationDetails = "功率偏高",
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER020");

        // Assert
        foreach (var result in results)
        {
            result.Confidence.Should()
                .BeGreaterThanOrEqualTo(0m,
                    because: "置信度不能小于0")
                .And.BeLessThanOrEqualTo(1m,
                    because: "置信度不能大于1");
            result.BayesProbability.Should()
                .BeGreaterThanOrEqualTo(0m,
                    because: "贝叶斯概率不能小于0")
                .And.BeLessThanOrEqualTo(1m,
                    because: "贝叶斯概率不能大于1");
        }
    }

    /// <summary>
    /// 测试偏离详情正确格式化。
    /// 验证点：DeviationDetails属性应包含有意义的偏离信息。
    /// </summary>
    [Fact]
    public async Task DeviationDetails_ShouldBeFormattedCorrectly()
    {
        // Arrange
        await SeedFaultTypesAsync();
        var device = new Device
        {
            Id = "CHILLER021",
            Name = "偏离详情测试",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);

        var now = DateTime.UtcNow;
        var deviceData = Enumerable.Range(0, 20)
            .Select(i => new DeviceData
            {
                DeviceId = "CHILLER021",
                Timestamp = now.AddMinutes(-i),
                Power = 200m,
                Current = 180m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);
        await _dbContext.SaveChangesAsync();

        var expectedDetails = "Power: 200.00, 阈值: 100.00, 偏离: 100.0%, 权重: 0.80; Current: 180.00, 阈值: 80.00, 偏离: 125.0%, 权重: 0.75";

        _mockBayesian.Setup(b => b.InitializeNetworkAsync())
            .Returns(Task.CompletedTask);

        _mockBayesian.Setup(b => b.DetectAnomalies(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.IsAny<List<DeviceData>>()))
            .Returns(new AnomalyDetectionResult
            {
                IsAnomaly = true,
                AnomalyScore = 0.8,
                AnomalousParameters = new List<string> { "Power", "Current" }
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode == "COMPRESSOR_FAILURE")))
            .ReturnsAsync(new InferenceResult
            {
                FaultCode = "COMPRESSOR_FAILURE",
                FaultName = "压缩机故障",
                PosteriorProbability = 0.9,
                Confidence = 0.85,
                MatchingSymptoms = new List<string> { "Power", "Current" },
                DeviationDetails = expectedDetails,
                Recommendation = "检查压缩机"
            });

        _mockBayesian.Setup(b => b.InferFaultAsync(
            It.IsAny<Device>(),
            It.IsAny<List<DeviceData>>(),
            It.Is<FaultType>(f => f.FaultCode != "COMPRESSOR_FAILURE")))
            .ReturnsAsync((InferenceResult)null!);

        // Act
        var results = await _diagnoser.DiagnoseDeviceAsync("CHILLER021");

        // Assert
        var result = results.First();
        result.DeviationDetails.Should().Be(expectedDetails,
            because: "偏离详情应该正确格式化");
        result.DeviationDetails.Should().NotBeNullOrWhiteSpace(
            because: "偏离详情不应该为空");
    }
}
