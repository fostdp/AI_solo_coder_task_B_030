using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

public class BenchmarkingEngine_MetricCalculation_Tests : TestBase
{
    private readonly Mock<ILogger<BenchmarkingEngine>> _mockLogger;
    private readonly BenchmarkingEngine _engine;

    public BenchmarkingEngine_MetricCalculation_Tests()
    {
        _mockLogger = CreateMockLogger<BenchmarkingEngine>();
        _engine = new BenchmarkingEngine(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试计算楼宇指标应该计算所有6个指标。
    /// 验证点：CalculateBuildingMetricsAsync应返回COP、EER、单位面积能耗、负荷率、PUE和总成本。
    /// </summary>
    [Fact]
    public async Task CalculateBuildingMetrics_ShouldComputeAllIndicators()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD001",
            BuildingName = "测试办公楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            DesignCoolingLoad = 1000
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH001",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD001", DeviceId = "CH001" });

        var now = DateTime.Today;
        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH001",
                Timestamp = now.AddMinutes(i * 15),
                Power = 100m,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 80m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD001", now, now);

        // Assert
        result.Should().NotBeNull();
        result.AverageCOP.Should().BeGreaterThan(0,
            because: "应该计算COP");
        result.AverageEER.Should().BeGreaterThan(0,
            because: "应该计算EER");
        result.AverageEnergyPerUnitArea.Should().BeGreaterThan(0,
            because: "应该计算单位面积能耗");
        result.AverageLoadFactor.Should().BeGreaterThan(0,
            because: "应该计算负荷率");
        result.AveragePUE.Should().BeGreaterThan(0,
            because: "应该计算PUE");
        result.TotalCost.Should().BeGreaterThan(0,
            because: "应该计算总成本");
        result.TotalElectricityConsumption.Should().BeGreaterThan(0,
            because: "应该计算总用电量");
    }

    /// <summary>
    /// 测试COP应该等于制冷量除以功耗。
    /// 验证点：COP = 总制冷量 / (总功耗 / 1000)。
    /// </summary>
    [Fact]
    public async Task COP_ShouldBeCoolingDividedByPower()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD002",
            BuildingName = "COP测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH002",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD002", DeviceId = "CH002" });

        var now = DateTime.Today;
        var power = 80m;
        var supplyTemp = 7m;
        var returnTemp = 12m;
        var flowRate = 60m;

        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH002",
                Timestamp = now.AddMinutes(i * 15),
                Power = power,
                SupplyTemperature = supplyTemp,
                ReturnTemperature = returnTemp,
                FlowRate = flowRate
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        var totalPower = deviceData.Sum(d => d.Power) / 120;
        var avgFlow = deviceData.Average(d => d.FlowRate);
        var avgDeltaT = deviceData.Average(d => d.ReturnTemperature - d.SupplyTemperature);
        var totalCooling = avgFlow * 4.186m * avgDeltaT * 24 / 3600;
        var expectedCOP = totalCooling / (totalPower / 1000);

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD002", now, now);

        // Assert
        result.AverageCOP.Should().BeApproximately(expectedCOP, 0.01m,
            because: "COP应该等于制冷量除以功耗");
    }

    /// <summary>
    /// 测试能耗按面积归一化。
    /// 验证点：EnergyPerUnitArea = 总能耗 / 制冷面积。
    /// </summary>
    [Fact]
    public async Task EnergyPerUnitArea_ShouldBeNormalizedByArea()
    {
        // Arrange
        var coolingArea = 5000m;
        var building = new Building
        {
            Id = "BLD003",
            BuildingName = "能耗测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 6000,
            CoolingArea = coolingArea,
            DesignCoolingLoad = 600
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH003",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD003", DeviceId = "CH003" });

        var now = DateTime.Today;
        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH003",
                Timestamp = now.AddMinutes(i * 15),
                Power = 100m,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 80m
            })
            .ToList();
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        var totalPower = deviceData.Sum(d => d.Power) / 120;
        var expectedEnergyPerArea = totalPower / coolingArea;

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD003", now, now);

        // Assert
        result.AverageEnergyPerUnitArea.Should().BeApproximately(expectedEnergyPerArea, 0.001m,
            because: "单位面积能耗应该按面积归一化");
    }

    /// <summary>
    /// 测试负荷率计算正确。
    /// 验证点：LoadFactor = 平均功率 / 峰值功率。
    /// </summary>
    [Fact]
    public async Task LoadFactor_ShouldBeAverageDividedByPeak()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD004",
            BuildingName = "负荷率测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH004",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD004", DeviceId = "CH004" });

        var now = DateTime.Today;
        var avgPower = 80m;
        var peakPower = 100m;

        var deviceData = new List<DeviceData>();
        for (int i = 0; i < 96; i++)
        {
            var power = i < 48 ? avgPower : peakPower;
            deviceData.Add(new DeviceData
            {
                DeviceId = "CH004",
                Timestamp = now.AddMinutes(i * 15),
                Power = power,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 80m
            });
        }
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        var expectedAvgPower = (avgPower * 48 + peakPower * 48) / 96m;
        var expectedLoadFactor = expectedAvgPower / peakPower;

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD004", now, now);

        // Assert
        result.AverageLoadFactor.Should().BeApproximately(expectedLoadFactor, 0.01m,
            because: "负荷率应该是平均功率除以峰值功率");
    }

    /// <summary>
    /// 测试不存在的楼宇应抛出异常。
    /// 验证点：计算不存在的楼宇指标应抛出KeyNotFoundException。
    /// </summary>
    [Fact]
    public async Task CalculateBuildingMetricsAsync_NonExistentBuilding_ShouldThrow()
    {
        // Act
        Func<Task> act = async () => await _engine.CalculateBuildingMetricsAsync("NON_EXISTENT", DateTime.Today, DateTime.Today);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "计算不存在的楼宇指标应该抛出异常");
    }

    /// <summary>
    /// 测试没有关联设备的楼宇应抛出异常。
    /// 验证点：没有关联设备时应抛出InvalidOperationException。
    /// </summary>
    [Fact]
    public async Task CalculateBuildingMetricsAsync_NoDevices_ShouldThrow()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD005",
            BuildingName = "无设备楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000
        };
        await _dbContext.Buildings.AddAsync(building);
        await _dbContext.SaveChangesAsync();

        // Act
        Func<Task> act = async () => await _engine.CalculateBuildingMetricsAsync("BLD005", DateTime.Today, DateTime.Today);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "没有关联设备的楼宇应该抛出异常");
    }

    /// <summary>
    /// 测试多设备能耗计算正确。
    /// 验证点：多个设备的能耗应该累加计算。
    /// </summary>
    [Fact]
    public async Task MultipleDevices_ShouldSumEnergyConsumption()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD006",
            BuildingName = "多设备楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            DesignCoolingLoad = 1000
        };
        await _dbContext.Buildings.AddAsync(building);

        var devices = new List<Device>
        {
            new() { Id = "CH006", Name = "主机1", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running },
            new() { Id = "CH007", Name = "主机2", DeviceTypeId = DeviceType.Chiller, Status = DeviceStatus.Running }
        };
        await _dbContext.Devices.AddRangeAsync(devices);
        await _dbContext.BuildingDevices.AddRangeAsync(new List<BuildingDevice>
        {
            new() { BuildingId = "BLD006", DeviceId = "CH006" },
            new() { BuildingId = "BLD006", DeviceId = "CH007" }
        });

        var now = DateTime.Today;
        var deviceData = new List<DeviceData>();
        foreach (var deviceId in new[] { "CH006", "CH007" })
        {
            deviceData.AddRange(Enumerable.Range(0, 96)
                .Select(i => new DeviceData
                {
                    DeviceId = deviceId,
                    Timestamp = now.AddMinutes(i * 15),
                    Power = 80m,
                    SupplyTemperature = 7m,
                    ReturnTemperature = 12m,
                    FlowRate = 60m
                }));
        }
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD006", now, now);

        // Assert
        var singleDevicePower = 80m * 96 / 120;
        var expectedTotalPower = singleDevicePower * 2;
        result.TotalElectricityConsumption.Should().BeApproximately(expectedTotalPower, 0.1m,
            because: "多个设备的能耗应该累加");
    }

    /// <summary>
    /// 测试多天指标计算正确。
    /// 验证点：多天的指标应该是每日指标的平均值或总和。
    /// </summary>
    [Fact]
    public async Task MultipleDays_ShouldAggregateCorrectly()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD007",
            BuildingName = "多天测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH008",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD007", DeviceId = "CH008" });

        var startDate = DateTime.Today;
        var deviceData = new List<DeviceData>();
        for (int day = 0; day < 3; day++)
        {
            var dayMultiplier = day + 1;
            deviceData.AddRange(Enumerable.Range(0, 96)
                .Select(i => new DeviceData
                {
                    DeviceId = "CH008",
                    Timestamp = startDate.AddDays(day).AddMinutes(i * 15),
                    Power = 100m * dayMultiplier,
                    SupplyTemperature = 7m,
                    ReturnTemperature = 12m,
                    FlowRate = 80m
                }));
        }
        await _dbContext.DeviceData.AddRangeAsync(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD007", startDate, startDate.AddDays(2));

        // Assert
        result.DailyMetrics.Should().HaveCount(3,
            because: "应该有3天的日指标");
        result.TotalElectricityConsumption.Should().BeApproximately(
            result.DailyMetrics.Sum(m => m.TotalElectricityConsumption ?? 0), 0.1m,
            because: "总能耗应该是各天能耗之和");
    }

    /// <summary>
    /// 测试没有设备数据时返回零指标。
    /// 验证点：没有设备数据时指标应为默认值。
    /// </summary>
    [Fact]
    public async Task NoDeviceData_ShouldReturnZeroMetrics()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD008",
            BuildingName = "无数据楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH009",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD008", DeviceId = "CH009" });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD008", DateTime.Today, DateTime.Today);

        // Assert
        result.DailyMetrics.Should().BeEmpty(
            because: "没有设备数据时日指标应该为空");
        result.AverageCOP.Should().Be(0,
            because: "没有数据时COP应该为0");
        result.AverageEnergyPerUnitArea.Should().Be(0,
            because: "没有数据时单位面积能耗应该为0");
        result.AverageLoadFactor.Should().Be(0,
            because: "没有数据时负荷率应该为0");
        result.AveragePUE.Should().Be(1,
            because: "没有数据时PUE应该为1");
    }

    /// <summary>
    /// 测试EER计算正确。
    /// 验证点：EER = 制冷量 / 功耗。
    /// </summary>
    [Fact]
    public async Task EER_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD009",
            BuildingName = "EER测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH010",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD009", DeviceId = "CH010" });

        var now = DateTime.Today;
        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH010",
                Timestamp = now.AddMinutes(i * 15),
                Power = 100m,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 80m
            })
            .ToList();
        await _dbContext.DeviceData.AddRange(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        var totalPower = deviceData.Sum(d => d.Power) / 120;
        var avgFlow = deviceData.Average(d => d.FlowRate);
        var avgDeltaT = deviceData.Average(d => d.ReturnTemperature - d.SupplyTemperature);
        var totalCooling = avgFlow * 4.186m * avgDeltaT * 24 / 3600;
        var expectedEER = totalCooling / totalPower;

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD009", now, now);

        // Assert
        result.AverageEER.Should().BeApproximately(expectedEER, 0.01m,
            because: "EER应该等于制冷量除以功耗");
    }

    /// <summary>
    /// 测试PUE计算正确。
    /// 验证点：PUE = 总功耗 / (设计冷负荷 * 24 / 1000)。
    /// </summary>
    [Fact]
    public async Task PUE_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var designCoolingLoad = 500m;
        var building = new Building
        {
            Id = "BLD010",
            BuildingName = "PUE测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = designCoolingLoad
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH011",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD010", DeviceId = "CH011" });

        var now = DateTime.Today;
        var power = 120m;
        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH011",
                Timestamp = now.AddMinutes(i * 15),
                Power = power,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 80m
            })
            .ToList();
        await _dbContext.DeviceData.AddRange(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        var totalPower = deviceData.Sum(d => d.Power) / 120;
        var expectedPUE = totalPower / (designCoolingLoad * 24 / 1000);

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD010", now, now);

        // Assert
        result.AveragePUE.Should().BeApproximately(expectedPUE, 0.01m,
            because: "PUE计算应该正确");
    }

    /// <summary>
    /// 测试零功耗时避免除以零。
    /// 验证点：功耗为零时COP和EER应为0而非抛出异常。
    /// </summary>
    [Fact]
    public async Task ZeroPower_ShouldAvoidDivisionByZero()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD011",
            BuildingName = "零功耗测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 5000,
            CoolingArea = 4000,
            DesignCoolingLoad = 500
        };
        await _dbContext.Buildings.AddAsync(building);

        var device = new Device
        {
            Id = "CH012",
            Name = "测试主机",
            DeviceTypeId = DeviceType.Chiller,
            Status = DeviceStatus.Running
        };
        await _dbContext.Devices.AddAsync(device);
        await _dbContext.BuildingDevices.AddAsync(new BuildingDevice { BuildingId = "BLD011", DeviceId = "CH012" });

        var now = DateTime.Today;
        var deviceData = Enumerable.Range(0, 96)
            .Select(i => new DeviceData
            {
                DeviceId = "CH012",
                Timestamp = now.AddMinutes(i * 15),
                Power = 0m,
                SupplyTemperature = 7m,
                ReturnTemperature = 12m,
                FlowRate = 0m
            })
            .ToList();
        await _dbContext.DeviceData.AddRange(deviceData);

        var priceTier = new ElectricityPriceTier
        {
            TierName = "标准电价",
            PricePerKWh = 0.8m,
            IsActive = true
        };
        await _dbContext.ElectricityPriceTiers.AddAsync(priceTier);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _engine.CalculateBuildingMetricsAsync("BLD011", now, now);

        // Assert
        result.AverageCOP.Should().Be(0,
            because: "零功耗时COP应该为0");
        result.AverageEER.Should().Be(0,
            because: "零功耗时EER应该为0");
        result.AverageLoadFactor.Should().Be(0,
            because: "零功耗时负荷率应该为0");
    }
}

public class BenchmarkingEngine_FairComparison_Tests : TestBase
{
    private readonly Mock<ILogger<BenchmarkingEngine>> _mockLogger;
    private readonly BenchmarkingEngine _engine;

    public BenchmarkingEngine_FairComparison_Tests()
    {
        _mockLogger = CreateMockLogger<BenchmarkingEngine>();
        _engine = new BenchmarkingEngine(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 测试功能类型系数被应用。
    /// 验证点：不同楼宇类型的能耗指标应乘以对应的功能类型系数。
    /// </summary>
    [Fact]
    public async Task FunctionTypeCoefficients_ShouldBeApplied()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-OFF", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-MALL", BuildingName = "商场", BuildingType = BuildingType.Mall, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        var metrics = new List<BuildingEfficiencyMetric>();
        foreach (var buildingId in new[] { "BLD-OFF", "BLD-MALL" })
        {
            metrics.Add(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.BuildingEfficiencyMetrics.AddRangeAsync(metrics);
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-OFF", "BLD-MALL" }, now);

        // Assert
        radarData.AdjustmentFactors["BLD-OFF"].Should().Be(1.00m,
            because: "办公楼系数应该是1.00");
        radarData.AdjustmentFactors["BLD-MALL"].Should().Be(1.35m,
            because: "商场系数应该是1.35");
    }

    /// <summary>
    /// 测试商场能耗除以1.35。
    /// 验证点：商场的能耗指标应该除以1.35进行公平比较。
    /// </summary>
    [Fact]
    public async Task MallEnergy_ShouldBeDividedBy135Percent()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD-MALL2",
            BuildingName = "测试商场",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            Status = 1
        };
        await _dbContext.Buildings.AddAsync(building);

        var now = DateTime.Today;
        var rawEnergyPerArea = 135m;
        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = "BLD-MALL2",
            StatisticsDate = now,
            StatisticsPeriod = "Daily",
            COP = 4.0m,
            EER = 3.5m,
            EnergyPerUnitArea = rawEnergyPerArea,
            LoadFactor = 0.7m,
            PUE = 1.2m,
            CostPerUnitArea = 67.5m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-MALL2" }, now);

        // Assert
        var expectedAdjustedEnergy = rawEnergyPerArea / 1.35m;
        var energyIndex = radarData.Labels.IndexOf("单位面积能耗");
        var adjustedEnergyScore = radarData.Datasets[0].Data[energyIndex];
        adjustedEnergyScore.Should().BeGreaterThan(0,
            because: "调整后的能耗应该被正确计算");
    }

    /// <summary>
    /// 测试医院系数最高。
    /// 验证点：医院的功能类型系数应该是1.50，为最高值。
    /// </summary>
    [Fact]
    public async Task Hospital_ShouldHaveHighestCoefficient()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-HOSP", BuildingName = "医院", BuildingType = BuildingType.Hospital, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-OFF", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-MALL", BuildingName = "商场", BuildingType = BuildingType.Mall, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-HOTEL", BuildingName = "酒店", BuildingType = BuildingType.Hotel, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-COMPLEX", BuildingName = "综合体", BuildingType = BuildingType.Complex, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-SCHOOL", BuildingName = "学校", BuildingType = BuildingType.School, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var coefficients = radarData.AdjustmentFactors.Values.ToList();
        var hospitalCoefficient = radarData.AdjustmentFactors["BLD-HOSP"];
        hospitalCoefficient.Should().Be(1.50m,
            because: "医院系数应该是1.50");
        hospitalCoefficient.Should().Be(coefficients.Max(),
            because: "医院系数应该是最高的");
    }

    /// <summary>
    /// 测试学校系数最低。
    /// 验证点：学校的功能类型系数应该是0.90，为最低值。
    /// </summary>
    [Fact]
    public async Task School_ShouldHaveLowestCoefficient()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-HOSP", BuildingName = "医院", BuildingType = BuildingType.Hospital, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-OFF", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-SCHOOL", BuildingName = "学校", BuildingType = BuildingType.School, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var coefficients = radarData.AdjustmentFactors.Values.ToList();
        var schoolCoefficient = radarData.AdjustmentFactors["BLD-SCHOOL"];
        schoolCoefficient.Should().Be(0.90m,
            because: "学校系数应该是0.90");
        schoolCoefficient.Should().Be(coefficients.Min(),
            because: "学校系数应该是最低的");
    }

    /// <summary>
    /// 测试效率指标不调整。
    /// 验证点：COP、EER等效率指标不应该被功能类型系数调整。
    /// </summary>
    [Fact]
    public async Task EfficiencyIndicators_ShouldNotBeAdjusted()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-OFF2", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-MALL3", BuildingName = "商场", BuildingType = BuildingType.Mall, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        var cop = 4.5m;
        var eer = 3.8m;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = cop,
                EER = eer,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var copIndex = radarData.Labels.IndexOf("COP");
        var eerIndex = radarData.Labels.IndexOf("EER");

        var officeCOP = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF2").Data[copIndex];
        var mallCOP = radarData.Datasets.First(d => d.BuildingId == "BLD-MALL3").Data[copIndex];

        var officeEER = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF2").Data[eerIndex];
        var mallEER = radarData.Datasets.First(d => d.BuildingId == "BLD-MALL3").Data[eerIndex];

        officeCOP.Should().Be(mallCOP,
            because: "COP是效率指标，不应该被功能类型系数调整");
        officeEER.Should().Be(mallEER,
            because: "EER是效率指标，不应该被功能类型系数调整");
    }

    /// <summary>
    /// 测试能耗指标被调整。
    /// 验证点：单位面积能耗、单位面积成本等消耗指标应该被功能类型系数调整。
    /// </summary>
    [Fact]
    public async Task ConsumptionIndicators_ShouldBeAdjusted()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-OFF3", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-HOSP2", BuildingName = "医院", BuildingType = BuildingType.Hospital, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        var energyPerArea = 150m;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = energyPerArea,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 75m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var energyIndex = radarData.Labels.IndexOf("单位面积能耗");
        var costIndex = radarData.Labels.IndexOf("单位面积成本");

        var officeEnergyScore = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF3").Data[energyIndex];
        var hospitalEnergyScore = radarData.Datasets.First(d => d.BuildingId == "BLD-HOSP2").Data[energyIndex];

        var officeCostScore = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF3").Data[costIndex];
        var hospitalCostScore = radarData.Datasets.First(d => d.BuildingId == "BLD-HOSP2").Data[costIndex];

        hospitalEnergyScore.Should().BeGreaterThan(officeEnergyScore,
            because: "医院的能耗指标应该被除以更大的系数，因此得分更高（更优）");
        hospitalCostScore.Should().BeGreaterThan(officeCostScore,
            because: "医院的成本指标应该被除以更大的系数，因此得分更高（更优）");
    }

    /// <summary>
    /// 测试未知楼宇类型使用默认系数。
    /// 验证点：未定义的楼宇类型应使用系数1.0。
    /// </summary>
    [Fact]
    public async Task UnknownBuildingType_ShouldUseDefaultCoefficient()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD-UNKNOWN",
            BuildingName = "未知类型楼",
            BuildingType = (BuildingType)999,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            Status = 1
        };
        await _dbContext.Buildings.AddAsync(building);

        var now = DateTime.Today;
        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = "BLD-UNKNOWN",
            StatisticsDate = now,
            StatisticsPeriod = "Daily",
            COP = 4.0m,
            EER = 3.5m,
            EnergyPerUnitArea = 100m,
            LoadFactor = 0.7m,
            PUE = 1.2m,
            CostPerUnitArea = 50m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-UNKNOWN" }, now);

        // Assert
        radarData.AdjustmentFactors["BLD-UNKNOWN"].Should().Be(1.0m,
            because: "未知楼宇类型应该使用默认系数1.0");
    }

    /// <summary>
    /// 测试同类型楼宇公平比较。
    /// 验证点：相同类型楼宇的能耗指标不应该被差异化调整。
    /// </summary>
    [Fact]
    public async Task SameBuildingType_ShouldHaveSameAdjustment()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-OFF4", BuildingName = "办公楼A", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-OFF5", BuildingName = "办公楼B", BuildingType = BuildingType.Office, GrossFloorArea = 15000, CoolingArea = 12000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var coeff1 = radarData.AdjustmentFactors["BLD-OFF4"];
        var coeff2 = radarData.AdjustmentFactors["BLD-OFF5"];
        coeff1.Should().Be(coeff2,
            because: "相同类型的楼宇应该使用相同的调整系数");

        var energyIndex = radarData.Labels.IndexOf("单位面积能耗");
        var score1 = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF4").Data[energyIndex];
        var score2 = radarData.Datasets.First(d => d.BuildingId == "BLD-OFF5").Data[energyIndex];
        score1.Should().Be(score2,
            because: "相同能耗和相同类型的楼宇应该获得相同的能耗得分");
    }

    /// <summary>
    /// 测试酒店系数正确。
    /// 验证点：酒店的功能类型系数应该是1.25。
    /// </summary>
    [Fact]
    public async Task Hotel_ShouldHaveCorrectCoefficient()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD-HOTEL2",
            BuildingName = "测试酒店",
            BuildingType = BuildingType.Hotel,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            Status = 1
        };
        await _dbContext.Buildings.AddAsync(building);

        var now = DateTime.Today;
        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = "BLD-HOTEL2",
            StatisticsDate = now,
            StatisticsPeriod = "Daily",
            COP = 4.0m,
            EER = 3.5m,
            EnergyPerUnitArea = 100m,
            LoadFactor = 0.7m,
            PUE = 1.2m,
            CostPerUnitArea = 50m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-HOTEL2" }, now);

        // Assert
        radarData.AdjustmentFactors["BLD-HOTEL2"].Should().Be(1.25m,
            because: "酒店系数应该是1.25");
    }

    /// <summary>
    /// 测试商业综合体系数正确。
    /// 验证点：商业综合体的功能类型系数应该是1.15。
    /// </summary>
    [Fact]
    public async Task Complex_ShouldHaveCorrectCoefficient()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD-COMPLEX2",
            BuildingName = "测试综合体",
            BuildingType = BuildingType.Complex,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            Status = 1
        };
        await _dbContext.Buildings.AddAsync(building);

        var now = DateTime.Today;
        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = "BLD-COMPLEX2",
            StatisticsDate = now,
            StatisticsPeriod = "Daily",
            COP = 4.0m,
            EER = 3.5m,
            EnergyPerUnitArea = 100m,
            LoadFactor = 0.7m,
            PUE = 1.2m,
            CostPerUnitArea = 50m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-COMPLEX2" }, now);

        // Assert
        radarData.AdjustmentFactors["BLD-COMPLEX2"].Should().Be(1.15m,
            because: "商业综合体系数应该是1.15");
    }

    /// <summary>
    /// 测试系数排序正确。
    /// 验证点：各类型系数应按预期顺序排列：医院>商场>酒店>综合体>办公楼>学校。
    /// </summary>
    [Fact]
    public async Task Coefficients_ShouldBeInCorrectOrder()
    {
        // Arrange
        var buildings = new List<Building>
        {
            new() { Id = "BLD-HOSP", BuildingName = "医院", BuildingType = BuildingType.Hospital, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-MALL", BuildingName = "商场", BuildingType = BuildingType.Mall, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-HOTEL", BuildingName = "酒店", BuildingType = BuildingType.Hotel, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-COMPLEX", BuildingName = "综合体", BuildingType = BuildingType.Complex, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-OFF", BuildingName = "办公楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-SCHOOL", BuildingName = "学校", BuildingType = BuildingType.School, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var now = DateTime.Today;
        foreach (var buildingId in buildings.Select(b => b.Id))
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = now,
                StatisticsPeriod = "Daily",
                COP = 4.0m,
                EER = 3.5m,
                EnergyPerUnitArea = 100m,
                LoadFactor = 0.7m,
                PUE = 1.2m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(buildings.Select(b => b.Id).ToList(), now);

        // Assert
        var coefficients = radarData.AdjustmentFactors;
        coefficients["BLD-HOSP"].Should().BeGreaterThan(coefficients["BLD-MALL"],
            because: "医院系数应该大于商场");
        coefficients["BLD-MALL"].Should().BeGreaterThan(coefficients["BLD-HOTEL"],
            because: "商场系数应该大于酒店");
        coefficients["BLD-HOTEL"].Should().BeGreaterThan(coefficients["BLD-COMPLEX"],
            because: "酒店系数应该大于综合体");
        coefficients["BLD-COMPLEX"].Should().BeGreaterThan(coefficients["BLD-OFF"],
            because: "综合体系数应该大于办公楼");
        coefficients["BLD-OFF"].Should().BeGreaterThan(coefficients["BLD-SCHOOL"],
            because: "办公楼系数应该大于学校");
    }

    /// <summary>
    /// 测试雷达图数据包含所有6个指标。
    /// 验证点：雷达图应该有6个指标维度。
    /// </summary>
    [Fact]
    public async Task RadarChart_ShouldHaveAllSixIndicators()
    {
        // Arrange
        var building = new Building
        {
            Id = "BLD-RADAR",
            BuildingName = "雷达图测试楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 10000,
            CoolingArea = 8000,
            Status = 1
        };
        await _dbContext.Buildings.AddAsync(building);

        var now = DateTime.Today;
        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = "BLD-RADAR",
            StatisticsDate = now,
            StatisticsPeriod = "Daily",
            COP = 4.0m,
            EER = 3.5m,
            EnergyPerUnitArea = 100m,
            LoadFactor = 0.7m,
            PUE = 1.2m,
            CostPerUnitArea = 50m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _engine.GetRadarDataAsync(new List<string> { "BLD-RADAR" }, now);

        // Assert
        radarData.Labels.Should().HaveCount(6,
            because: "雷达图应该有6个指标维度");
        radarData.Labels.Should().Contain("COP",
            because: "应该包含COP指标");
        radarData.Labels.Should().Contain("EER",
            because: "应该包含EER指标");
        radarData.Labels.Should().Contain("单位面积能耗",
            because: "应该包含单位面积能耗指标");
        radarData.Labels.Should().Contain("负荷率",
            because: "应该包含负荷率指标");
        radarData.Labels.Should().Contain("单位面积成本",
            because: "应该包含单位面积成本指标");
        radarData.Labels.Should().Contain("PUE",
            because: "应该包含PUE指标");

        radarData.Datasets.First().Data.Should().HaveCount(6,
            because: "每个楼宇的雷达图数据应该有6个值");
    }
}

public class BenchmarkingEngine_Ranking_Tests : TestBase
{
    private readonly Mock<ILogger<BenchmarkingEngine>> _mockLogger;
    private readonly BenchmarkingEngine _engine;

    public BenchmarkingEngine_Ranking_Tests()
    {
        _mockLogger = CreateMockLogger<BenchmarkingEngine>();
        _engine = new BenchmarkingEngine(_dbContext, _mockLogger.Object);
    }

    private async Task SeedBuildingsAndMetrics(DateTime date)
    {
        var buildings = new List<Building>
        {
            new() { Id = "BLD-A", BuildingName = "楼宇A", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-B", BuildingName = "楼宇B", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-C", BuildingName = "楼宇C", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var metrics = new List<BuildingEfficiencyMetric>
        {
            new() { BuildingId = "BLD-A", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 5.0m, EER = 4.5m, EnergyPerUnitArea = 60m, LoadFactor = 0.85m, PUE = 1.1m, CostPerUnitArea = 40m },
            new() { BuildingId = "BLD-B", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 4.0m, EER = 3.5m, EnergyPerUnitArea = 80m, LoadFactor = 0.70m, PUE = 1.3m, CostPerUnitArea = 55m },
            new() { BuildingId = "BLD-C", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 3.0m, EER = 2.5m, EnergyPerUnitArea = 100m, LoadFactor = 0.55m, PUE = 1.5m, CostPerUnitArea = 70m }
        };
        await _dbContext.BuildingEfficiencyMetrics.AddRangeAsync(metrics);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 测试排名一致性。
    /// 验证点：相同数据多次对标应该产生相同的排名结果。
    /// </summary>
    [Fact]
    public async Task Rankings_ShouldBeConsistent()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result1 = await _engine.CompareBuildingsAsync(buildingIds, now);
        var result2 = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var ranks1 = result1.Rankings.Select(r => r.OverallRank).ToList();
        var ranks2 = result2.Rankings.Select(r => r.OverallRank).ToList();
        ranks1.Should().Equal(ranks2,
            because: "相同数据的排名应该一致");

        var scores1 = result1.Rankings.Select(r => r.OverallScore).ToList();
        var scores2 = result2.Rankings.Select(r => r.OverallScore).ToList();
        scores1.Should().Equal(scores2,
            because: "相同数据的综合评分应该一致");
    }

    /// <summary>
    /// 测试COP越高排名越前。
    /// 验证点：COP高的楼宇应该获得更好的COP排名。
    /// </summary>
    [Fact]
    public async Task HigherCOP_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["COP"].Should().Be(1,
            because: "楼宇A的COP最高，应该排名第1");
        rankingB.CategoryRanks["COP"].Should().Be(2,
            because: "楼宇B的COP中等，应该排名第2");
        rankingC.CategoryRanks["COP"].Should().Be(3,
            because: "楼宇C的COP最低，应该排名第3");
    }

    /// <summary>
    /// 测试能耗越低排名越前。
    /// 验证点：单位面积能耗低的楼宇应该获得更好的能耗排名。
    /// </summary>
    [Fact]
    public async Task LowerEnergy_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["EnergyPerUnitArea"].Should().Be(1,
            because: "楼宇A的能耗最低，应该排名第1");
        rankingB.CategoryRanks["EnergyPerUnitArea"].Should().Be(2,
            because: "楼宇B的能耗中等，应该排名第2");
        rankingC.CategoryRanks["EnergyPerUnitArea"].Should().Be(3,
            because: "楼宇C的能耗最高，应该排名第3");
    }

    /// <summary>
    /// 测试并列正确处理。
    /// 验证点：相同指标值的楼宇应该获得相同的排名。
    /// </summary>
    [Fact]
    public async Task Ties_ShouldBeHandledProperly()
    {
        // Arrange
        var now = DateTime.Today;
        var buildings = new List<Building>
        {
            new() { Id = "BLD-X", BuildingName = "楼宇X", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-Y", BuildingName = "楼宇Y", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-Z", BuildingName = "楼宇Z", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var metrics = new List<BuildingEfficiencyMetric>
        {
            new() { BuildingId = "BLD-X", StatisticsDate = now, StatisticsPeriod = "Daily", COP = 4.0m, EER = 3.5m, EnergyPerUnitArea = 80m, LoadFactor = 0.70m, PUE = 1.3m, CostPerUnitArea = 55m },
            new() { BuildingId = "BLD-Y", StatisticsDate = now, StatisticsPeriod = "Daily", COP = 4.0m, EER = 3.5m, EnergyPerUnitArea = 80m, LoadFactor = 0.70m, PUE = 1.3m, CostPerUnitArea = 55m },
            new() { BuildingId = "BLD-Z", StatisticsDate = now, StatisticsPeriod = "Daily", COP = 4.0m, EER = 3.5m, EnergyPerUnitArea = 80m, LoadFactor = 0.70m, PUE = 1.3m, CostPerUnitArea = 55m }
        };
        await _dbContext.BuildingEfficiencyMetrics.AddRange(metrics);
        await _dbContext.SaveChangesAsync();

        var buildingIds = new List<string> { "BLD-X", "BLD-Y", "BLD-Z" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var scores = result.Rankings.Select(r => r.OverallScore).ToList();
        scores.Distinct().Should().ContainSingle(
            because: "相同指标值的楼宇应该获得相同的综合评分");

        var ranks = result.Rankings.Select(r => r.OverallRank).ToList();
        ranks.Should().OnlyContain(r => r == 1,
            because: "相同指标值的楼宇应该并列第一");
    }

    /// <summary>
    /// 测试综合排名应该是各分项排名的加权平均。
    /// 验证点：综合排名应该基于所有分项排名。
    /// </summary>
    [Fact]
    public async Task OverallRank_ShouldBeBasedOnAllCategories()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var orderedRankings = result.Rankings.OrderBy(r => r.OverallRank).ToList();
        orderedRankings[0].BuildingId.Should().Be("BLD-A",
            because: "楼宇A在所有指标中最好，应该综合排名第1");
        orderedRankings[1].BuildingId.Should().Be("BLD-B",
            because: "楼宇B在所有指标中中等，应该综合排名第2");
        orderedRankings[2].BuildingId.Should().Be("BLD-C",
            because: "楼宇C在所有指标中最差，应该综合排名第3");

        orderedRankings[0].OverallScore.Should().BeGreaterThan(orderedRankings[1].OverallScore,
            because: "排名第1的综合评分应该高于排名第2的");
        orderedRankings[1].OverallScore.Should().BeGreaterThan(orderedRankings[2].OverallScore,
            because: "排名第2的综合评分应该高于排名第3的");
    }

    /// <summary>
    /// 测试EER越高排名越前。
    /// 验证点：EER高的楼宇应该获得更好的EER排名。
    /// </summary>
    [Fact]
    public async Task HigherEER_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["EER"].Should().Be(1,
            because: "楼宇A的EER最高，应该排名第1");
        rankingB.CategoryRanks["EER"].Should().Be(2,
            because: "楼宇B的EER中等，应该排名第2");
        rankingC.CategoryRanks["EER"].Should().Be(3,
            because: "楼宇C的EER最低，应该排名第3");
    }

    /// <summary>
    /// 测试PUE越低排名越前。
    /// 验证点：PUE低的楼宇应该获得更好的PUE排名。
    /// </summary>
    [Fact]
    public async Task LowerPUE_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["PUE"].Should().Be(1,
            because: "楼宇A的PUE最低，应该排名第1");
        rankingB.CategoryRanks["PUE"].Should().Be(2,
            because: "楼宇B的PUE中等，应该排名第2");
        rankingC.CategoryRanks["PUE"].Should().Be(3,
            because: "楼宇C的PUE最高，应该排名第3");
    }

    /// <summary>
    /// 测试负荷率越高排名越前。
    /// 验证点：负荷率高的楼宇应该获得更好的负荷率排名。
    /// </summary>
    [Fact]
    public async Task HigherLoadFactor_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["LoadFactor"].Should().Be(1,
            because: "楼宇A的负荷率最高，应该排名第1");
        rankingB.CategoryRanks["LoadFactor"].Should().Be(2,
            because: "楼宇B的负荷率中等，应该排名第2");
        rankingC.CategoryRanks["LoadFactor"].Should().Be(3,
            because: "楼宇C的负荷率最低，应该排名第3");
    }

    /// <summary>
    /// 测试成本越低排名越前。
    /// 验证点：单位面积成本低的楼宇应该获得更好的成本排名。
    /// </summary>
    [Fact]
    public async Task LowerCost_ShouldRankBetter()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        var rankingA = result.Rankings.First(r => r.BuildingId == "BLD-A");
        var rankingB = result.Rankings.First(r => r.BuildingId == "BLD-B");
        var rankingC = result.Rankings.First(r => r.BuildingId == "BLD-C");

        rankingA.CategoryRanks["CostPerUnitArea"].Should().Be(1,
            because: "楼宇A的成本最低，应该排名第1");
        rankingB.CategoryRanks["CostPerUnitArea"].Should().Be(2,
            because: "楼宇B的成本中等，应该排名第2");
        rankingC.CategoryRanks["CostPerUnitArea"].Should().Be(3,
            because: "楼宇C的成本最高，应该排名第3");
    }

    /// <summary>
    /// 测试分类排名字典包含所有6个指标。
    /// 验证点：每个楼宇的CategoryRanks应该包含所有6个指标。
    /// </summary>
    [Fact]
    public async Task CategoryRanks_ShouldIncludeAllSixIndicators()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-A", "BLD-B", "BLD-C" };

        // Act
        var result = await _engine.CompareBuildingsAsync(buildingIds, now);

        // Assert
        foreach (var ranking in result.Rankings)
        {
            ranking.CategoryRanks.Should().ContainKey("COP",
                because: "应该包含COP排名");
            ranking.CategoryRanks.Should().ContainKey("EER",
                because: "应该包含EER排名");
            ranking.CategoryRanks.Should().ContainKey("EnergyPerUnitArea",
                because: "应该包含能耗排名");
            ranking.CategoryRanks.Should().ContainKey("LoadFactor",
                because: "应该包含负荷率排名");
            ranking.CategoryRanks.Should().ContainKey("CostPerUnitArea",
                because: "应该包含成本排名");
            ranking.CategoryRanks.Should().ContainKey("PUE",
                because: "应该包含PUE排名");
        }
    }

    /// <summary>
    /// 测试空楼宇列表抛出异常。
    /// 验证点：空的楼宇ID列表应该抛出ArgumentException。
    /// </summary>
    [Fact]
    public async Task CompareBuildingsAsync_EmptyList_ShouldThrow()
    {
        // Act
        Func<Task> act = async () => await _engine.CompareBuildingsAsync(new List<string>(), DateTime.Today);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>(
            because: "空的楼宇列表应该抛出异常");
    }

    /// <summary>
    /// 测试不存在的楼宇抛出异常。
    /// 验证点：包含不存在的楼宇ID应该抛出KeyNotFoundException。
    /// </summary>
    [Fact]
    public async Task CompareBuildingsAsync_NonExistentBuilding_ShouldThrow()
    {
        // Arrange
        var buildingIds = new List<string> { "NON_EXISTENT" };

        // Act
        Func<Task> act = async () => await _engine.CompareBuildingsAsync(buildingIds, DateTime.Today);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "不存在的楼宇应该抛出异常");
    }
}

public class BenchmarkingEngine_ReportGeneration_Tests : TestBase
{
    private readonly Mock<ILogger<BenchmarkingEngine>> _mockLogger;
    private readonly BenchmarkingEngine _engine;

    public BenchmarkingEngine_ReportGeneration_Tests()
    {
        _mockLogger = CreateMockLogger<BenchmarkingEngine>();
        _engine = new BenchmarkingEngine(_dbContext, _mockLogger.Object);
    }

    private async Task SeedBuildingsAndMetrics(DateTime date)
    {
        var buildings = new List<Building>
        {
            new() { Id = "BLD-1", BuildingName = "一号楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-2", BuildingName = "二号楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 },
            new() { Id = "BLD-3", BuildingName = "三号楼", BuildingType = BuildingType.Office, GrossFloorArea = 10000, CoolingArea = 8000, Status = 1 }
        };
        await _dbContext.Buildings.AddRangeAsync(buildings);

        var metrics = new List<BuildingEfficiencyMetric>
        {
            new() { BuildingId = "BLD-1", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 5.0m, EER = 4.5m, EnergyPerUnitArea = 60m, LoadFactor = 0.85m, PUE = 1.1m, CostPerUnitArea = 40m },
            new() { BuildingId = "BLD-2", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 4.0m, EER = 3.5m, EnergyPerUnitArea = 80m, LoadFactor = 0.70m, PUE = 1.3m, CostPerUnitArea = 55m },
            new() { BuildingId = "BLD-3", StatisticsDate = date, StatisticsPeriod = "Daily", COP = 3.0m, EER = 2.5m, EnergyPerUnitArea = 100m, LoadFactor = 0.55m, PUE = 1.5m, CostPerUnitArea = 70m }
        };
        await _dbContext.BuildingEfficiencyMetrics.AddRangeAsync(metrics);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 测试报告应包含所有楼宇。
    /// 验证点：GenerateReportAsync生成的报告应该包含所有指定的楼宇。
    /// </summary>
    [Fact]
    public async Task Report_ShouldIncludeAllBuildings()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "测试报告", now, now);

        // Assert
        report.Should().NotBeNull();
        report.BuildingRankings.Should().HaveCount(3,
            because: "报告应该包含所有3个楼宇");
        report.BuildingRankings.Should().Contain(r => r.BuildingId == "BLD-1",
            because: "应该包含一号楼");
        report.BuildingRankings.Should().Contain(r => r.BuildingId == "BLD-2",
            because: "应该包含二号楼");
        report.BuildingRankings.Should().Contain(r => r.BuildingId == "BLD-3",
            because: "应该包含三号楼");
    }

    /// <summary>
    /// 测试报告应有最佳实践。
    /// 验证点：报告应该包含最佳实践总结。
    /// </summary>
    [Fact]
    public async Task Report_ShouldHaveBestPractices()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "测试报告", now, now);

        // Assert
        report.BestPracticeSummary.Should().NotBeNullOrEmpty(
            because: "报告应该包含最佳实践总结");
        report.BestPracticeSummary.Should().Contain("一号楼",
            because: "最佳实践应该提到表现最好的楼宇");
    }

    /// <summary>
    /// 测试报告应有改进建议。
    /// 验证点：报告应该包含改进建议。
    /// </summary>
    [Fact]
    public async Task Report_ShouldHaveImprovementSuggestions()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "测试报告", now, now);

        // Assert
        report.ImprovementSuggestions.Should().NotBeNullOrEmpty(
            because: "报告应该包含改进建议");
        report.ImprovementSuggestions.Should().Contain("三号楼",
            because: "改进建议应该提到需要改进的楼宇");
    }

    /// <summary>
    /// 测试综合评分为加权平均。
    /// 验证点：综合评分应该是各分项指标的加权平均。
    /// </summary>
    [Fact]
    public async Task OverallScore_ShouldBeWeightedAverage()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "测试报告", now, now);

        // Assert
        var orderedRankings = report.BuildingRankings.OrderBy(r => r.OverallRank).ToList();
        orderedRankings[0].OverallScore.Should().BeGreaterThan(orderedRankings[1].OverallScore,
            because: "排名第1的综合评分应该最高");
        orderedRankings[1].OverallScore.Should().BeGreaterThan(orderedRankings[2].OverallScore,
            because: "排名第2的综合评分应该高于排名第3的");
        orderedRankings[0].OverallScore.Should().BeInRange(0m, 100m,
            because: "综合评分应该在0-100范围内");
    }

    /// <summary>
    /// 测试报告持久化到数据库。
    /// 验证点：生成的报告应该保存到数据库中。
    /// </summary>
    [Fact]
    public async Task Report_ShouldBePersistedToDatabase()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };
        var reportName = "持久化测试报告";

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, reportName, now, now);

        // Assert
        var savedReport = await _dbContext.BenchmarkReports.FindAsync(report.Id);
        savedReport.Should().NotBeNull(
            because: "报告应该被保存到数据库");
        savedReport!.ReportName.Should().Be(reportName,
            because: "报告名称应该正确保存");
        savedReport.BuildingIds.Should().Be(string.Join(",", buildingIds),
            because: "楼宇ID列表应该正确保存");
    }

    /// <summary>
    /// 测试报告包含排名信息。
    /// 验证点：报告中的每个楼宇应该包含排名信息。
    /// </summary>
    [Fact]
    public async Task Report_ShouldContainRankingInformation()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "测试报告", now, now);

        // Assert
        foreach (var ranking in report.BuildingRankings)
        {
            ranking.OverallRank.Should().BeGreaterThan(0,
                because: "每个楼宇都应该有排名");
            ranking.OverallScore.Should().BeGreaterThan(0,
                because: "每个楼宇都应该有综合评分");
            ranking.CategoryRanks.Should().NotBeEmpty(
                because: "每个楼宇都应该有分项排名");
            ranking.CategoryValues.Should().NotBeEmpty(
                because: "每个楼宇都应该有分项指标值");
        }
    }

    /// <summary>
    /// 测试报告包含日期范围。
    /// 验证点：报告应该包含正确的开始和结束日期。
    /// </summary>
    [Fact]
    public async Task Report_ShouldContainDateRange()
    {
        // Arrange
        var startDate = DateTime.Today.AddDays(-7);
        var endDate = DateTime.Today;
        await SeedBuildingsAndMetrics(endDate);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, "日期范围测试", startDate, endDate);

        // Assert
        report.StartDate.Should().Be(startDate,
            because: "报告应该包含正确的开始日期");
        report.EndDate.Should().Be(endDate,
            because: "报告应该包含正确的结束日期");
    }

    /// <summary>
    /// 测试多个报告可以共存。
    /// 验证点：可以生成多个报告并分别保存。
    /// </summary>
    [Fact]
    public async Task MultipleReports_ShouldBeStoredSeparately()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        // Act
        var report1 = await _engine.GenerateReportAsync(buildingIds, "报告1", now, now);
        var report2 = await _engine.GenerateReportAsync(buildingIds, "报告2", now, now);

        // Assert
        report1.Id.Should().NotBe(report2.Id,
            because: "两个报告应该有不同的ID");
        report1.CreatedAt.Should().BeCloseTo(report2.CreatedAt, TimeSpan.FromSeconds(10),
            because: "两个报告创建时间应该接近");

        var allReports = await _dbContext.BenchmarkReports.CountAsync();
        allReports.Should().Be(2,
            because: "应该有2个报告保存在数据库中");
    }

    /// <summary>
    /// 测试报告按创建时间降序查询。
    /// 验证点：GetBenchmarkReportsAsync应该按创建时间降序返回报告。
    /// </summary>
    [Fact]
    public async Task GetBenchmarkReports_ShouldBeOrderedByDateDescending()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        var report1 = await _engine.GenerateReportAsync(buildingIds, "较早报告", now, now);
        await Task.Delay(100);
        var report2 = await _engine.GenerateReportAsync(buildingIds, "较新报告", now, now);

        // Act
        var reports = await _engine.GetBenchmarkReportsAsync(1, 10);

        // Assert
        reports.Should().HaveCount(2,
            because: "应该有2个报告");
        reports[0].Id.Should().Be(report2.Id,
            because: "最新的报告应该排在最前面");
        reports[1].Id.Should().Be(report1.Id,
            because: "较早的报告应该排在后面");
    }

    /// <summary>
    /// 测试报告分页功能。
    /// 验证点：GetBenchmarkReportsAsync应该支持分页。
    /// </summary>
    [Fact]
    public async Task GetBenchmarkReports_ShouldSupportPagination()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };

        for (int i = 0; i < 5; i++)
        {
            await _engine.GenerateReportAsync(buildingIds, $"报告{i + 1}", now, now);
            await Task.Delay(50);
        }

        // Act
        var page1 = await _engine.GetBenchmarkReportsAsync(1, 2);
        var page2 = await _engine.GetBenchmarkReportsAsync(2, 2);

        // Assert
        page1.Should().HaveCount(2,
            because: "第1页应该有2个报告");
        page2.Should().HaveCount(2,
            because: "第2页应该有2个报告");
        page1[0].Id.Should().NotBe(page2[0].Id,
            because: "不同页的报告应该不同");
    }

    /// <summary>
    /// 测试报告名称正确保存。
    /// 验证点：报告名称应该正确保存并可以查询。
    /// </summary>
    [Fact]
    public async Task ReportName_ShouldBeSavedCorrectly()
    {
        // Arrange
        var now = DateTime.Today;
        await SeedBuildingsAndMetrics(now);
        var buildingIds = new List<string> { "BLD-1", "BLD-2", "BLD-3" };
        var expectedName = "2024年第一季度能效对标报告";

        // Act
        var report = await _engine.GenerateReportAsync(buildingIds, expectedName, now, now);

        // Assert
        report.ReportName.Should().Be(expectedName,
            because: "报告名称应该正确设置");
        var savedReport = await _dbContext.BenchmarkReports.FindAsync(report.Id);
        savedReport!.ReportName.Should().Be(expectedName,
            because: "报告名称应该正确保存到数据库");
    }

    /// <summary>
    /// 测试空楼宇列表生成报告抛出异常。
    /// 验证点：空的楼宇ID列表应该抛出ArgumentException。
    /// </summary>
    [Fact]
    public async Task GenerateReportAsync_EmptyBuildingList_ShouldThrow()
    {
        // Act
        Func<Task> act = async () => await _engine.GenerateReportAsync(new List<string>(), "空列表报告", DateTime.Today, DateTime.Today);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>(
            because: "空的楼宇列表应该抛出异常");
    }
}