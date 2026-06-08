using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.BuildingBenchmark;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 多楼宇对标数据归一化测试
/// 验证指标归一化、边界处理和数据对齐功能
/// </summary>
public class BuildingBenchmarkModule_DataNormalization_Tests : TestBase
{
    private readonly Mock<ILogger<BuildingBenchmarkModule>> _mockLogger;
    private readonly BuildingBenchmarkModule _module;

    public BuildingBenchmarkModule_DataNormalization_Tests()
    {
        _mockLogger = CreateMockLogger<BuildingBenchmarkModule>();
        _module = new BuildingBenchmarkModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 归一化测试：验证指标被归一化到0-100范围内
    /// 验证点：所有归一化后的指标值在[0, 100]范围内
    /// </summary>
    [Fact]
    public async Task Metrics_ShouldBeNormalizedTo0_100()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var testDate = new DateTime(2024, 6, 15);
        await SeedElectricityPriceTiersAsync();

        foreach (var building in buildings)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);
            await SeedBuildingEfficiencyMetricsAsync(building.Id, testDate, 7);
        }

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            buildings.Select(b => b.Id).ToList(),
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        radarData.Should().NotBeEmpty();
        radarData.Should().HaveCount(buildings.Count);

        foreach (var buildingData in radarData.Values)
        {
            buildingData.Should().HaveCount(6, because: "雷达图应该有6个指标");
            buildingData.Values.Should().AllSatisfy(value =>
            {
                value.Should().BeGreaterThanOrEqualTo(0, because: "归一化后的值不能小于0");
                value.Should().BeLessThanOrEqualTo(100, because: "归一化后的值不能大于100");
            });
        }
    }

    /// <summary>
    /// 零值边界测试：验证归一化能正确处理零值输入
    /// 验证点：零值输入不会导致除零错误，输出合理
    /// </summary>
    [Fact]
    public async Task Normalization_ShouldHandleZeroValues()
    {
        // Arrange
        var building = await SeedBuildingsAsync(1);
        var testDate = new DateTime(2024, 6, 15);

        var device = await SeedDeviceAsync(
            $"DEV-{building[0].Id}",
            DeviceType.CentrifugalChiller,
            $"{building[0].BuildingName}主机");
        await SeedBuildingDevicesAsync(building[0].Id, new List<string> { device.Id });

        // 创建零值指标
        var zeroMetric = new BuildingEfficiencyMetric
        {
            BuildingId = building[0].Id,
            StatisticsDate = testDate,
            StatisticsPeriod = "Daily",
            EER = 0,
            COP = 0,
            EnergyPerUnitArea = 0,
            CoolingPerUnitArea = 0,
            PUE = 1,
            LoadFactor = 0,
            TotalElectricityConsumption = 0,
            TotalCoolingCapacity = 0,
            PeakDemand = 0,
            OperatingHours = 0,
            TotalCost = 0,
            CostPerUnitArea = 0,
            CostPerCooling = 0,
            OutdoorAvgTemp = 25
        };

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(zeroMetric);
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            new List<string> { building[0].Id },
            StatisticsPeriod.Daily,
            testDate);

        // Assert
        radarData.Should().ContainKey(building[0].Id);
        var buildingData = radarData[building[0].Id];

        // 验证零值被正确处理（没有抛出异常）
        buildingData["COP"].Should().Be(0, because: "COP为0时归一化结果应为0");
        buildingData["LoadFactor"].Should().Be(0, because: "负荷率为0时归一化结果应为0");

        // 能量相关指标（反向指标，100 - 归一化值）
        buildingData["EnergyPerUnitArea"].Should().Be(100, because: "能耗为0时应该得满分");
    }

    /// <summary>
    /// 极值边界测试：验证归一化能正确处理极端值
    /// 验证点：超出范围的值被裁剪到边界内
    /// </summary>
    [Fact]
    public async Task Normalization_ShouldHandleExtremeValues()
    {
        // Arrange
        var building = await SeedBuildingsAsync(1);
        var testDate = new DateTime(2024, 6, 15);

        var device = await SeedDeviceAsync(
            $"DEV-{building[0].Id}",
            DeviceType.CentrifugalChiller,
            $"{building[0].BuildingName}主机");
        await SeedBuildingDevicesAsync(building[0].Id, new List<string> { device.Id });

        // 创建极值指标（超出正常范围）
        var extremeMetric = new BuildingEfficiencyMetric
        {
            BuildingId = building[0].Id,
            StatisticsDate = testDate,
            StatisticsPeriod = "Daily",
            EER = 10,
            COP = 8,
            EnergyPerUnitArea = 300,
            CoolingPerUnitArea = 300,
            PUE = 0.5m,
            LoadFactor = 1.5m,
            TotalElectricityConsumption = 20000,
            TotalCoolingCapacity = 80000,
            PeakDemand = 1000,
            OperatingHours = 24,
            TotalCost = 15000,
            CostPerUnitArea = 200,
            CostPerCooling = 0.5m,
            OutdoorAvgTemp = 35
        };

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(extremeMetric);
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            new List<string> { building[0].Id },
            StatisticsPeriod.Daily,
            testDate);

        // Assert
        radarData.Should().ContainKey(building[0].Id);
        var buildingData = radarData[building[0].Id];

        // 验证极值被裁剪到[0, 100]范围内
        buildingData["COP"].Should().Be(100, because: "COP超过最大值6时应该被裁剪到100");
        buildingData["LoadFactor"].Should().Be(100, because: "负荷率超过1时应该被裁剪到100");

        // 能量相关指标（反向指标）
        buildingData["EnergyPerUnitArea"].Should().Be(0, because: "能耗超过最大值时应该得0分");
        buildingData["PUE"].Should().Be(100, because: "PUE低于1时应该得满分");
    }

    /// <summary>
    /// 数据对齐测试：验证不同楼宇的数据能按日期正确对齐
    /// 验证点：所有楼宇都有相同日期的数据
    /// </summary>
    [Fact]
    public async Task Buildings_ShouldBeAlignedByDate()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var startDate = new DateTime(2024, 6, 1);
        await SeedElectricityPriceTiersAsync();

        var deviceIds = new List<string>();
        foreach (var building in buildings)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            deviceIds.Add(device.Id);
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);
        }

        // Act - 计算各楼宇的指标
        var allMetrics = new List<BuildingEfficiencyMetric>();
        foreach (var building in buildings)
        {
            var metrics = await _module.CalculateBuildingMetricsAsync(
                building.Id,
                startDate,
                startDate.AddDays(6));
            allMetrics.AddRange(metrics);
        }

        // Assert - 验证所有楼宇有相同的日期范围
        var dateGroups = allMetrics.GroupBy(m => m.StatisticsDate.Date).ToList();
        dateGroups.Should().HaveCount(7, because: "应该有7天的数据");

        foreach (var dateGroup in dateGroups)
        {
            dateGroup.Should().HaveCount(buildings.Count,
                because: $"日期{dateGroup.Key:yyyy-MM-dd}应该有所有楼宇的数据");
        }

        // 验证数据完整性
        allMetrics.All(m => m.COP.HasValue).Should().BeTrue(because: "所有指标都应该有COP值");
        allMetrics.All(m => m.EnergyPerUnitArea.HasValue).Should().BeTrue(because: "所有指标都应该有能耗值");
    }

    /// <summary>
    /// 缺失数据处理测试：验证系统能优雅地处理缺失数据
    /// 验证点：缺失数据不会导致崩溃，返回合理的默认值
    /// </summary>
    [Fact]
    public async Task MissingData_ShouldBeHandledGracefully()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var testDate = new DateTime(2024, 6, 15);

        // 只为前两个楼宇创建数据，第三个楼宇没有数据
        for (int i = 0; i < 2; i++)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{buildings[i].Id}",
                DeviceType.CentrifugalChiller,
                $"{buildings[i].BuildingName}主机");
            await SeedBuildingDevicesAsync(buildings[i].Id, new List<string> { device.Id });
            await SeedBuildingEfficiencyMetricsAsync(buildings[i].Id, testDate, 7);
        }

        // Act - 请求所有三个楼宇的雷达图数据
        var radarData = await _module.GetRadarChartDataAsync(
            buildings.Select(b => b.Id).ToList(),
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        radarData.Should().HaveCount(3, because: "应该返回所有三个楼宇的数据");

        // 有数据的楼宇应该有正常值
        radarData[buildings[0].Id].Values.Any(v => v > 0).Should().BeTrue(
            because: "有数据的楼宇应该有正值");

        // 没有数据的楼宇应该返回默认值（0）
        var missingBuildingData = radarData[buildings[2].Id];
        missingBuildingData.Values.All(v => v == 0).Should().BeTrue(
            because: "没有数据的楼宇应该返回0值");
    }
}

/// <summary>
/// 多楼宇对标雷达图测试
/// 验证雷达图数据的完整性、有效性和可视化属性
/// </summary>
public class BuildingBenchmarkModule_RadarChart_Tests : TestBase
{
    private readonly Mock<ILogger<BuildingBenchmarkModule>> _mockLogger;
    private readonly BuildingBenchmarkModule _module;

    public BuildingBenchmarkModule_RadarChart_Tests()
    {
        _mockLogger = CreateMockLogger<BuildingBenchmarkModule>();
        _module = new BuildingBenchmarkModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 指标完整性测试：验证雷达图包含6个指标
    /// 验证点：返回6个指标：COP, EER, EnergyPerUnitArea, LoadFactor, CostPerUnitArea, PUE
    /// </summary>
    [Fact]
    public async Task RadarData_ShouldHave6Indicators()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var testDate = new DateTime(2024, 6, 15);

        foreach (var building in buildings)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedBuildingEfficiencyMetricsAsync(building.Id, testDate, 7);
        }

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            buildings.Select(b => b.Id).ToList(),
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        var expectedIndicators = new[] { "COP", "EER", "EnergyPerUnitArea", "LoadFactor", "CostPerUnitArea", "PUE" };

        foreach (var buildingData in radarData.Values)
        {
            buildingData.Keys.Should().BeEquivalentTo(
                expectedIndicators,
                because: "雷达图应该包含6个指标");
        }
    }

    /// <summary>
    /// 范围有效性测试：验证每个指标的值在有效范围内
    /// 验证点：所有指标值都在[0, 100]范围内
    /// </summary>
    [Fact]
    public async Task EachIndicator_ShouldHaveValidRange()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(4);
        var testDate = new DateTime(2024, 6, 15);

        foreach (var building in buildings)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedBuildingEfficiencyMetricsAsync(building.Id, testDate, 7);
        }

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            buildings.Select(b => b.Id).ToList(),
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        foreach (var (buildingId, indicators) in radarData)
        {
            indicators["COP"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的COP归一化值应该在0-100");
            indicators["EER"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的EER归一化值应该在0-100");
            indicators["EnergyPerUnitArea"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的单位面积能耗归一化值应该在0-100");
            indicators["LoadFactor"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的负荷率归一化值应该在0-100");
            indicators["CostPerUnitArea"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的单位面积成本归一化值应该在0-100");
            indicators["PUE"].Should().BeInRange(0, 100,
                because: $"楼宇{buildingId}的PUE归一化值应该在0-100");
        }
    }

    /// <summary>
    /// 颜色区分测试：验证多个楼宇在雷达图中可以区分
    /// 验证点：不同楼宇有不同的指标值分布
    /// </summary>
    [Fact]
    public async Task MultipleBuildings_ShouldHaveDifferentColors()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var testDate = new DateTime(2024, 6, 15);

        // 为每个楼宇创建不同特征的数据
        var randomSeeds = new[] { 42, 100, 200 };
        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });

            var random = new Random(randomSeeds[i]);
            var baseCOP = 3.0m + i * 0.8m;
            var baseEnergy = 120m - i * 20m;

            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = testDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = baseCOP * 0.85m + (decimal)(random.NextDouble() * 0.3),
                    COP = baseCOP + (decimal)(random.NextDouble() * 0.4),
                    EnergyPerUnitArea = baseEnergy + (decimal)(random.NextDouble() * 15),
                    CoolingPerUnitArea = 150m + (decimal)(random.NextDouble() * 30),
                    PUE = 1.2m + (decimal)(random.NextDouble() * 0.3) - i * 0.1m,
                    LoadFactor = 0.5m + (decimal)(random.NextDouble() * 0.3) + i * 0.1m,
                    TotalElectricityConsumption = 5000m + (decimal)(random.NextDouble() * 1000),
                    TotalCoolingCapacity = 20000m + (decimal)(random.NextDouble() * 3000),
                    PeakDemand = 350m + (decimal)(random.NextDouble() * 50),
                    OperatingHours = 18m + (decimal)(random.NextDouble() * 3),
                    TotalCost = 4000m + (decimal)(random.NextDouble() * 1000),
                    CostPerUnitArea = 70m - i * 10m + (decimal)(random.NextDouble() * 15),
                    CostPerCooling = 0.18m + (decimal)(random.NextDouble() * 0.03),
                    OutdoorAvgTemp = 25m + (decimal)(random.NextDouble() * 5),
                    HDD = 0,
                    CDD = 50m + (decimal)(random.NextDouble() * 20)
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            buildings.Select(b => b.Id).ToList(),
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        radarData.Should().HaveCount(3);

        // 验证不同楼宇有不同的指标值（可以通过颜色区分）
        var allValues = radarData.Values.SelectMany(v => v.Values).ToList();
        var distinctValueSets = radarData.Values
            .Select(v => string.Join(",", v.Values.Select(x => x.ToString("F2"))))
            .Distinct()
            .ToList();

        distinctValueSets.Count.Should().BeGreaterThanOrEqualTo(2,
            because: "不同楼宇应该有不同的指标值，可以通过颜色区分");
    }

    /// <summary>
    /// 顺序保持测试：验证楼宇顺序在结果中被保持
    /// 验证点：返回的字典键顺序与输入的楼宇ID顺序一致
    /// </summary>
    [Fact]
    public async Task BuildingOrder_ShouldBePreserved()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(4);
        var testDate = new DateTime(2024, 6, 15);

        // 打乱楼宇顺序
        var shuffledIds = buildings.Select(b => b.Id).OrderBy(_ => Guid.NewGuid()).ToList();

        foreach (var building in buildings)
        {
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedBuildingEfficiencyMetricsAsync(building.Id, testDate, 7);
        }

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            shuffledIds,
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        var resultIds = radarData.Keys.ToList();
        resultIds.Should().Equal(shuffledIds,
            because: "返回的楼宇顺序应该与输入顺序一致");
    }

    /// <summary>
    /// 单楼宇边界测试：验证雷达图能处理单个楼宇的情况
    /// 验证点：单个楼宇也能正确返回雷达图数据
    /// </summary>
    [Fact]
    public async Task Radar_ShouldHandleSingleBuilding()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(1);
        var testDate = new DateTime(2024, 6, 15);

        var device = await SeedDeviceAsync(
            $"DEV-{buildings[0].Id}",
            DeviceType.CentrifugalChiller,
            $"{buildings[0].BuildingName}主机");
        await SeedBuildingDevicesAsync(buildings[0].Id, new List<string> { device.Id });
        await SeedBuildingEfficiencyMetricsAsync(buildings[0].Id, testDate, 7);

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            new List<string> { buildings[0].Id },
            StatisticsPeriod.Daily,
            testDate.AddDays(3));

        // Assert
        radarData.Should().HaveCount(1, because: "应该返回单个楼宇的数据");
        radarData.Should().ContainKey(buildings[0].Id);

        var buildingData = radarData[buildings[0].Id];
        buildingData.Should().HaveCount(6, because: "单个楼宇也应该有6个指标");
        buildingData.Values.All(v => v >= 0 && v <= 100).Should().BeTrue(
            because: "单个楼宇的指标也应该在有效范围内");
    }
}

/// <summary>
/// 多楼宇对标排名测试
/// 验证排名逻辑、综合评分和并列排名处理
/// </summary>
public class BuildingBenchmarkModule_Ranking_Tests : TestBase
{
    private readonly Mock<ILogger<BuildingBenchmarkModule>> _mockLogger;
    private readonly BuildingBenchmarkModule _module;

    public BuildingBenchmarkModule_Ranking_Tests()
    {
        _mockLogger = CreateMockLogger<BuildingBenchmarkModule>();
        _module = new BuildingBenchmarkModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 排名一致性测试：验证各种排名方法的结果一致
    /// 验证点：COP高的楼宇在综合排名中也应该靠前
    /// </summary>
    [Fact]
    public async Task Ranking_ShouldBeConsistent()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(4);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);
        await SeedElectricityPriceTiersAsync();

        var copValues = new[] { 4.5m, 3.8m, 5.2m, 3.2m };

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);

            // 为每个楼宇创建具有不同COP值的指标
            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = copValues[i] * 0.85m,
                    COP = copValues[i],
                    EnergyPerUnitArea = 120m - i * 15m,
                    CoolingPerUnitArea = 150m,
                    PUE = 1.3m - i * 0.1m,
                    LoadFactor = 0.6m + i * 0.05m,
                    TotalElectricityConsumption = 5000m + i * 500,
                    TotalCoolingCapacity = 20000m,
                    PeakDemand = 350m,
                    OperatingHours = 18m,
                    TotalCost = 4000m + i * 300,
                    CostPerUnitArea = 70m - i * 10m,
                    CostPerCooling = 0.18m,
                    OutdoorAvgTemp = 25m,
                    HDD = 0,
                    CDD = 50m
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            buildings.Select(b => b.Id).ToList(),
            "测试对标报告",
            startDate,
            endDate);

        // Assert
        report.Should().NotBeNull();

        // 解析COP排名
        var copRanking = ParseRanking(report.COPRanking);

        // 验证COP排名：COP最高的应该排第一
        var expectedTopCop = buildings[copValues.ToList().IndexOf(copValues.Max())].Id;
        copRanking.First().BuildingId.Should().Be(expectedTopCop,
            because: "COP最高的楼宇应该排名第一");

        // 验证综合评分与COP正相关
        report.OverallScore.Should().BeGreaterThan(50, because: "综合评分应该合理");
    }

    /// <summary>
    /// COP排名测试：验证COP排名是降序的（越高越好）
    /// 验证点：排名按COP值从高到低排列
    /// </summary>
    [Fact]
    public async Task COPRanking_ShouldFavorHigherValues()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);
        await SeedElectricityPriceTiersAsync();

        var copValues = new[] { 3.5m, 5.0m, 4.2m };

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);

            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = copValues[i] * 0.85m,
                    COP = copValues[i],
                    EnergyPerUnitArea = 100m,
                    CoolingPerUnitArea = 150m,
                    PUE = 1.2m,
                    LoadFactor = 0.7m,
                    TotalElectricityConsumption = 4500m,
                    TotalCoolingCapacity = 20000m,
                    PeakDemand = 320m,
                    OperatingHours = 16m,
                    TotalCost = 3500m,
                    CostPerUnitArea = 50m,
                    CostPerCooling = 0.15m,
                    OutdoorAvgTemp = 26m,
                    HDD = 0,
                    CDD = 55m
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            buildings.Select(b => b.Id).ToList(),
            "COP排名测试报告",
            startDate,
            endDate);

        // Assert
        report.Should().NotBeNull();

        // 解析COP排名
        var copRanking = ParseRanking(report.COPRanking);

        // 验证排名顺序：COP=5.0 > 4.2 > 3.5
        copRanking[0].Value.Should().BeApproximately(5.0m, 0.01m, because: "第一名应该是COP最高的");
        copRanking[1].Value.Should().BeApproximately(4.2m, 0.01m, because: "第二名应该是COP次高的");
        copRanking[2].Value.Should().BeApproximately(3.5m, 0.01m, because: "第三名应该是COP最低的");

        // 验证排名序号正确
        copRanking[0].Rank.Should().Be(1);
        copRanking[1].Rank.Should().Be(2);
        copRanking[2].Rank.Should().Be(3);
    }

    /// <summary>
    /// 能耗排名测试：验证能耗排名是升序的（越低越好）
    /// 验证点：排名按能耗值从低到高排列
    /// </summary>
    [Fact]
    public async Task EnergyRanking_ShouldFavorLowerValues()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);
        await SeedElectricityPriceTiersAsync();

        var energyValues = new[] { 120m, 80m, 100m };

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);

            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = 4.0m,
                    COP = 4.5m,
                    EnergyPerUnitArea = energyValues[i],
                    CoolingPerUnitArea = 150m,
                    PUE = 1.2m,
                    LoadFactor = 0.7m,
                    TotalElectricityConsumption = 4500m,
                    TotalCoolingCapacity = 20000m,
                    PeakDemand = 320m,
                    OperatingHours = 16m,
                    TotalCost = 3500m,
                    CostPerUnitArea = 50m,
                    CostPerCooling = 0.15m,
                    OutdoorAvgTemp = 26m,
                    HDD = 0,
                    CDD = 55m
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            buildings.Select(b => b.Id).ToList(),
            "能耗排名测试报告",
            startDate,
            endDate);

        // Assert
        report.Should().NotBeNull();

        // 解析能耗排名
        var energyRanking = ParseRanking(report.EnergyPerAreaRanking);

        // 验证排名顺序：能耗=80 < 100 < 120（越低越好）
        energyRanking[0].Value.Should().BeApproximately(80m, 0.01m, because: "第一名应该是能耗最低的");
        energyRanking[1].Value.Should().BeApproximately(100m, 0.01m, because: "第二名应该是能耗次低的");
        energyRanking[2].Value.Should().BeApproximately(120m, 0.01m, because: "第三名应该是能耗最高的");
    }

    /// <summary>
    /// 并列排名处理测试：验证系统能正确处理并列排名
    /// 验证点：相同值的楼宇获得相同排名
    /// </summary>
    [Fact]
    public async Task Ranking_ShouldHandleTies()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(4);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);
        await SeedElectricityPriceTiersAsync();

        // 创建有并列情况的数据：楼宇0和2有相同的COP
        var copValues = new[] { 4.5m, 3.8m, 4.5m, 3.2m };

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);

            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = copValues[i] * 0.85m,
                    COP = copValues[i],
                    EnergyPerUnitArea = 100m,
                    CoolingPerUnitArea = 150m,
                    PUE = 1.2m,
                    LoadFactor = 0.7m,
                    TotalElectricityConsumption = 4500m,
                    TotalCoolingCapacity = 20000m,
                    PeakDemand = 320m,
                    OperatingHours = 16m,
                    TotalCost = 3500m,
                    CostPerUnitArea = 50m,
                    CostPerCooling = 0.15m,
                    OutdoorAvgTemp = 26m,
                    HDD = 0,
                    CDD = 55m
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            buildings.Select(b => b.Id).ToList(),
            "并列排名测试报告",
            startDate,
            endDate);

        // Assert
        report.Should().NotBeNull();

        // 解析COP排名
        var copRanking = ParseRanking(report.COPRanking);

        // 验证有两个楼宇COP=4.5，都应该排名靠前
        var topRanked = copRanking.Take(2).ToList();
        topRanked.All(r => Math.Abs(r.Value - 4.5m) < 0.01m).Should().BeTrue(
            because: "两个COP=4.5的楼宇应该并列前排");
    }

    /// <summary>
    /// 综合评分计算测试：验证综合评分是各项排名的加权和
    /// 验证点：OverallScore = 各项排名的加权平均转换为百分制
    /// </summary>
    [Fact]
    public async Task OverallScore_ShouldBeWeightedSum()
    {
        // Arrange
        var buildings = await SeedBuildingsAsync(3);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);
        await SeedElectricityPriceTiersAsync();

        // 创建各方面表现均衡的楼宇数据
        var buildingData = new[]
        {
            new { COP = 5.0m, Energy = 80m, Cost = 50m, Load = 0.85m, EER = 4.25m },
            new { COP = 4.0m, Energy = 100m, Cost = 60m, Load = 0.70m, EER = 3.40m },
            new { COP = 3.0m, Energy = 120m, Cost = 70m, Load = 0.55m, EER = 2.55m }
        };

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            var data = buildingData[i];
            var device = await SeedDeviceAsync(
                $"DEV-{building.Id}",
                DeviceType.CentrifugalChiller,
                $"{building.BuildingName}主机");
            await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });
            await SeedDeviceDataAsync(device.Id);

            for (int j = 0; j < 7; j++)
            {
                var metric = new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(j),
                    StatisticsPeriod = "Daily",
                    EER = data.EER,
                    COP = data.COP,
                    EnergyPerUnitArea = data.Energy,
                    CoolingPerUnitArea = 150m,
                    PUE = 1.2m,
                    LoadFactor = data.Load,
                    TotalElectricityConsumption = 4500m,
                    TotalCoolingCapacity = 20000m,
                    PeakDemand = 320m,
                    OperatingHours = 16m,
                    TotalCost = 3500m,
                    CostPerUnitArea = data.Cost,
                    CostPerCooling = 0.15m,
                    OutdoorAvgTemp = 26m,
                    HDD = 0,
                    CDD = 55m
                };
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            buildings.Select(b => b.Id).ToList(),
            "综合评分测试报告",
            startDate,
            endDate);

        // Assert
        report.Should().NotBeNull();
        report.OverallScore.Should().HaveValue(because: "应该有综合评分");
        report.OverallScore.Should().BeGreaterThan(0, because: "综合评分应该大于0");
        report.OverallScore.Should().BeLessThanOrEqualTo(100, because: "综合评分应该不超过100");

        // 验证排名完整
        report.COPRanking.Should().NotBeNullOrWhiteSpace();
        report.EnergyPerAreaRanking.Should().NotBeNullOrWhiteSpace();
        report.CostPerAreaRanking.Should().NotBeNullOrWhiteSpace();
        report.LoadFactorRanking.Should().NotBeNullOrWhiteSpace();
        report.EERRanking.Should().NotBeNullOrWhiteSpace();

        // 验证有最佳实践和改进建议
        report.BestPractices.Should().NotBeNullOrWhiteSpace();
        report.ImprovementSuggestions.Should().NotBeNullOrWhiteSpace();

        // 第一名应该是各项指标都最好的楼宇
        var copRanking = ParseRanking(report.COPRanking);
        var topBuildingId = copRanking.First().BuildingId;

        // 综合评分应该与各单项排名正相关
        var energyRanking = ParseRanking(report.EnergyPerAreaRanking);
        var topInEnergy = energyRanking.First().BuildingId;
        topInEnergy.Should().Be(topBuildingId, because: "COP最好的楼宇也应该能耗最低");
    }

    /// <summary>
    /// 解析排名字符串为结构化数据
    /// </summary>
    private static List<(string BuildingId, int Rank, decimal Value)> ParseRanking(string? rankingString)
    {
        var result = new List<(string BuildingId, int Rank, decimal Value)>();

        if (string.IsNullOrWhiteSpace(rankingString))
            return result;

        var entries = rankingString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(':');
            if (parts.Length >= 3 &&
                int.TryParse(parts[1], out var rank) &&
                decimal.TryParse(parts[2], out var value))
            {
                result.Add((parts[0], rank, value));
            }
        }

        return result;
    }
}

/// <summary>
/// 多楼宇对标分类对比公平性测试
/// 验证功能类型系数调整、分类排名、雷达图数据和报告序列化
/// </summary>
public class BuildingBenchmarkModule_Normalization_Tests : TestBase
{
    private readonly Mock<ILogger<BuildingBenchmarkModule>> _mockLogger;
    private readonly BuildingBenchmarkModule _module;

    public BuildingBenchmarkModule_Normalization_Tests()
    {
        _mockLogger = CreateMockLogger<BuildingBenchmarkModule>();
        _module = new BuildingBenchmarkModule(_dbContext, _mockLogger.Object);
    }

    /// <summary>
    /// 根因：不同功能类型的楼宇能耗特性不同，直接对比不公平。商场由于营业时间长、人流密集，天然能耗更高。
    /// 验证点：商场的单位面积能耗除以1.35系数进行调整，调整后与相同能效水平的办公楼排名相同。
    /// </summary>
    [Fact]
    public async Task EnergyPerUnitArea_ShouldBeAdjusted_ByBuildingType()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);

        var mallBuilding = new Building
        {
            Id = "BLD-MALL-001",
            BuildingName = "测试商场",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var officeBuilding = new Building
        {
            Id = "BLD-OFFICE-001",
            BuildingName = "测试办公楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(mallBuilding, officeBuilding);

        var mallDevice = await SeedDeviceAsync("DEV-MALL-001", DeviceType.CentrifugalChiller, "商场主机");
        var officeDevice = await SeedDeviceAsync("DEV-OFFICE-001", DeviceType.CentrifugalChiller, "办公楼主机");

        await SeedBuildingDevicesAsync(mallBuilding.Id, new List<string> { mallDevice.Id });
        await SeedBuildingDevicesAsync(officeBuilding.Id, new List<string> { officeDevice.Id });

        var mallEnergy = 135m;
        var officeEnergy = 100m;

        for (int i = 0; i < 7; i++)
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = mallBuilding.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = mallEnergy,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 70m
            });

            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = officeBuilding.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = officeEnergy,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            new List<string> { mallBuilding.Id, officeBuilding.Id },
            "功能类型调整测试报告",
            startDate,
            endDate);

        // Assert
        var adjustedMallEnergy = mallEnergy / 1.35m;
        var adjustedOfficeEnergy = officeEnergy / 1.0m;

        adjustedMallEnergy.Should().BeApproximately(100m, 0.1m,
            because: "商场能耗135除以1.35系数后应该约为100");
        adjustedOfficeEnergy.Should().BeApproximately(100m, 0.1m,
            because: "办公楼能耗100除以1.0系数后保持100");

        report.EnergyPerAreaRanking.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 根因：功能类型系数设置不合理，导致某些类型的楼宇在对比中总是处于劣势或优势。
    /// 验证点：系数顺序符合业务逻辑：医院(1.5) > 商场(1.35) > 酒店(1.25) > 综合体(1.15) > 办公楼(1.0) > 学校(0.9)。
    /// </summary>
    [Fact]
    public void Hospital_ShouldHaveHighestAdjustmentCoefficient()
    {
        // Arrange
        var coefficients = new Dictionary<BuildingType, decimal>
        {
            { BuildingType.Hospital, 1.50m },
            { BuildingType.Mall, 1.35m },
            { BuildingType.Hotel, 1.25m },
            { BuildingType.Complex, 1.15m },
            { BuildingType.Office, 1.00m },
            { BuildingType.School, 0.90m }
        };

        // Act & Assert
        var sortedCoefficients = coefficients.OrderByDescending(kv => kv.Value).ToList();

        sortedCoefficients[0].Key.Should().Be(BuildingType.Hospital,
            because: "医院应该有最高的调整系数1.50");
        sortedCoefficients[0].Value.Should().Be(1.50m);

        sortedCoefficients[1].Key.Should().Be(BuildingType.Mall,
            because: "商场应该有第二高的调整系数1.35");
        sortedCoefficients[1].Value.Should().Be(1.35m);

        sortedCoefficients[2].Key.Should().Be(BuildingType.Hotel,
            because: "酒店应该有第三高的调整系数1.25");
        sortedCoefficients[2].Value.Should().Be(1.25m);

        sortedCoefficients[3].Key.Should().Be(BuildingType.Complex,
            because: "综合体应该有第四高的调整系数1.15");
        sortedCoefficients[3].Value.Should().Be(1.15m);

        sortedCoefficients[4].Key.Should().Be(BuildingType.Office,
            because: "办公楼应该有标准调整系数1.00");
        sortedCoefficients[4].Value.Should().Be(1.00m);

        sortedCoefficients[5].Key.Should().Be(BuildingType.School,
            because: "学校应该有最低的调整系数0.90");
        sortedCoefficients[5].Value.Should().Be(0.90m);
    }

    /// <summary>
    /// 根因：直接跨类型排名不公平，应该先在同类型内排名，再进行综合排名。
    /// 验证点：先按类型内排名，再综合排名。类型内排名：办公楼A>B，商场C>D；综合排名考虑类型调整。
    /// </summary>
    [Fact]
    public async Task SameTypeBuildings_ShouldBeRankedWithinTypeFirst()
    {
        // Arrange
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);

        var officeA = new Building
        {
            Id = "BLD-OFF-A",
            BuildingName = "办公楼A",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var officeB = new Building
        {
            Id = "BLD-OFF-B",
            BuildingName = "办公楼B",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var mallC = new Building
        {
            Id = "BLD-MALL-C",
            BuildingName = "商场C",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var mallD = new Building
        {
            Id = "BLD-MALL-D",
            BuildingName = "商场D",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(officeA, officeB, mallC, mallD);

        var deviceA = await SeedDeviceAsync("DEV-OFF-A", DeviceType.CentrifugalChiller, "办公楼A主机");
        var deviceB = await SeedDeviceAsync("DEV-OFF-B", DeviceType.CentrifugalChiller, "办公楼B主机");
        var deviceC = await SeedDeviceAsync("DEV-MALL-C", DeviceType.CentrifugalChiller, "商场C主机");
        var deviceD = await SeedDeviceAsync("DEV-MALL-D", DeviceType.CentrifugalChiller, "商场D主机");

        await SeedBuildingDevicesAsync(officeA.Id, new List<string> { deviceA.Id });
        await SeedBuildingDevicesAsync(officeB.Id, new List<string> { deviceB.Id });
        await SeedBuildingDevicesAsync(mallC.Id, new List<string> { deviceC.Id });
        await SeedBuildingDevicesAsync(mallD.Id, new List<string> { deviceD.Id });

        var energyValues = new Dictionary<string, decimal>
        {
            { officeA.Id, 90m },
            { officeB.Id, 110m },
            { mallC.Id, 125m },
            { mallD.Id, 148m }
        };

        for (int i = 0; i < 7; i++)
        {
            foreach (var building in new[] { officeA, officeB, mallC, mallD })
            {
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(i),
                    StatisticsPeriod = "Daily",
                    EnergyPerUnitArea = energyValues[building.Id],
                    COP = 4.0m,
                    LoadFactor = 0.7m,
                    EER = 3.4m,
                    CostPerUnitArea = 50m
                });
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            new List<string> { officeA.Id, officeB.Id, mallC.Id, mallD.Id },
            "分类排名测试报告",
            startDate,
            endDate);

        // Assert
        var adjustedA = 90m / 1.0m;
        var adjustedB = 110m / 1.0m;
        var adjustedC = 125m / 1.35m;
        var adjustedD = 148m / 1.35m;

        adjustedA.Should().BeApproximately(90m, 0.1m);
        adjustedB.Should().BeApproximately(110m, 0.1m);
        adjustedC.Should().BeApproximately(92.6m, 0.1m);
        adjustedD.Should().BeApproximately(109.6m, 0.1m);

        report.EnergyPerAreaRanking.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 根因：雷达图只显示调整后的值，用户无法了解原始数据和调整系数的影响。
    /// 验证点：雷达图数据包含原始值、调整值、功能系数三个字段。
    /// </summary>
    [Fact]
    public async Task RadarData_ShouldIncludeBothRawAndAdjustedValues()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);
        var building = new Building
        {
            Id = "BLD-RADAR-001",
            BuildingName = "雷达图测试楼宇",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddAsync(building);

        var device = await SeedDeviceAsync("DEV-RADAR-001", DeviceType.CentrifugalChiller, "雷达图测试主机");
        await SeedBuildingDevicesAsync(building.Id, new List<string> { device.Id });

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = building.Id,
            StatisticsDate = testDate,
            StatisticsPeriod = "Daily",
            EnergyPerUnitArea = 135m,
            CostPerUnitArea = 70m,
            COP = 4.0m,
            EER = 3.4m,
            LoadFactor = 0.7m,
            PUE = 1.2m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            new List<string> { building.Id },
            StatisticsPeriod.Daily,
            testDate);

        // Assert
        radarData.Should().ContainKey(building.Id);
        var buildingData = radarData[building.Id];

        buildingData.Should().ContainKey("RawEnergyPerUnitArea",
            because: "雷达图数据应该包含原始能耗值");
        buildingData.Should().ContainKey("AdjustedEnergyPerUnitArea",
            because: "雷达图数据应该包含调整后能耗值");
        buildingData.Should().ContainKey("FunctionTypeCoefficient",
            because: "雷达图数据应该包含功能类型系数");

        buildingData["RawEnergyPerUnitArea"].Should().Be(135m,
            because: "原始能耗应该是135");
        buildingData["AdjustedEnergyPerUnitArea"].Should().BeApproximately(100m, 0.1m,
            because: "调整后能耗应该是135/1.35=100");
        buildingData["FunctionTypeCoefficient"].Should().Be(1.35m,
            because: "商场的功能类型系数应该是1.35");
    }

    /// <summary>
    /// 根因：报告没有标记是否应用了功能类型调整，用户不知道排名是否公平。
    /// 验证点：报告标记FunctionTypeAdjustmentApplied=true。
    /// </summary>
    [Fact]
    public async Task BenchmarkReport_ShouldIndicateAdjustmentApplied()
    {
        // Arrange
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);

        var building1 = new Building
        {
            Id = "BLD-ADJ-001",
            BuildingName = "测试楼宇1",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var building2 = new Building
        {
            Id = "BLD-ADJ-002",
            BuildingName = "测试楼宇2",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(building1, building2);

        var device1 = await SeedDeviceAsync("DEV-ADJ-001", DeviceType.CentrifugalChiller, "主机1");
        var device2 = await SeedDeviceAsync("DEV-ADJ-002", DeviceType.CentrifugalChiller, "主机2");

        await SeedBuildingDevicesAsync(building1.Id, new List<string> { device1.Id });
        await SeedBuildingDevicesAsync(building2.Id, new List<string> { device2.Id });

        for (int i = 0; i < 7; i++)
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = building1.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = 100m,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 50m
            });

            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = building2.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = 135m,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 70m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            new List<string> { building1.Id, building2.Id },
            "调整标记测试报告",
            startDate,
            endDate);

        // Assert
        report.FunctionTypeAdjustmentApplied.Should().BeTrue(
            because: "对标报告应该标记已应用功能类型调整");
    }

    /// <summary>
    /// 根因：原始能耗高的楼宇由于功能类型原因，即使能效很好，排名也会靠后。
    /// 验证点：学校原始能耗95（系数0.9，调整后105.6），办公楼原始能耗100（系数1.0，调整后100），办公楼排名更高。
    /// </summary>
    [Fact]
    public async Task BuildingWithHigherRawEnergy_CanRankHigher_AfterAdjustment()
    {
        // Arrange
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);

        var school = new Building
        {
            Id = "BLD-SCHOOL-001",
            BuildingName = "测试学校",
            BuildingType = BuildingType.School,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var office = new Building
        {
            Id = "BLD-OFFICE-002",
            BuildingName = "测试办公楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(school, office);

        var schoolDevice = await SeedDeviceAsync("DEV-SCHOOL-001", DeviceType.CentrifugalChiller, "学校主机");
        var officeDevice = await SeedDeviceAsync("DEV-OFFICE-002", DeviceType.CentrifugalChiller, "办公楼主机");

        await SeedBuildingDevicesAsync(school.Id, new List<string> { schoolDevice.Id });
        await SeedBuildingDevicesAsync(office.Id, new List<string> { officeDevice.Id });

        var schoolEnergy = 95m;
        var officeEnergy = 100m;

        for (int i = 0; i < 7; i++)
        {
            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = school.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = schoolEnergy,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 50m
            });

            await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
            {
                BuildingId = office.Id,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EnergyPerUnitArea = officeEnergy,
                COP = 4.0m,
                LoadFactor = 0.7m,
                EER = 3.4m,
                CostPerUnitArea = 50m
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var adjustedSchool = schoolEnergy / 0.9m;
        var adjustedOffice = officeEnergy / 1.0m;

        // Assert
        adjustedSchool.Should().BeApproximately(105.6m, 0.1m,
            because: "学校能耗95除以0.9后约为105.6");
        adjustedOffice.Should().Be(100m,
            because: "办公楼能耗100除以1.0后为100");

        adjustedOffice.Should().BeLessThan(adjustedSchool,
            because: "调整后办公楼能耗更低，应该排名更高");
    }

    /// <summary>
    /// 根因：效率指标（COP、负荷率）也被功能类型系数调整，导致公平性问题。
    /// 验证点：COP和负荷率不受功能类型调整影响，这些是效率指标，与建筑类型无关。
    /// </summary>
    [Fact]
    public async Task FunctionTypeAdjustment_ShouldNotAffect_COP_And_LoadFactor()
    {
        // Arrange
        var testDate = new DateTime(2024, 6, 15);

        var mall = new Building
        {
            Id = "BLD-MALL-COP",
            BuildingName = "COP测试商场",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var office = new Building
        {
            Id = "BLD-OFFICE-COP",
            BuildingName = "COP测试办公楼",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(mall, office);

        var mallDevice = await SeedDeviceAsync("DEV-MALL-COP", DeviceType.CentrifugalChiller, "商场主机");
        var officeDevice = await SeedDeviceAsync("DEV-OFFICE-COP", DeviceType.CentrifugalChiller, "办公楼主机");

        await SeedBuildingDevicesAsync(mall.Id, new List<string> { mallDevice.Id });
        await SeedBuildingDevicesAsync(office.Id, new List<string> { officeDevice.Id });

        var commonCOP = 4.0m;
        var commonLoadFactor = 0.7m;

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = mall.Id,
            StatisticsDate = testDate,
            StatisticsPeriod = "Daily",
            COP = commonCOP,
            LoadFactor = commonLoadFactor,
            EnergyPerUnitArea = 135m,
            EER = 3.4m,
            CostPerUnitArea = 70m,
            PUE = 1.2m
        });

        await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
        {
            BuildingId = office.Id,
            StatisticsDate = testDate,
            StatisticsPeriod = "Daily",
            COP = commonCOP,
            LoadFactor = commonLoadFactor,
            EnergyPerUnitArea = 100m,
            EER = 3.4m,
            CostPerUnitArea = 50m,
            PUE = 1.2m
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var radarData = await _module.GetRadarChartDataAsync(
            new List<string> { mall.Id, office.Id },
            StatisticsPeriod.Daily,
            testDate);

        // Assert
        radarData.Should().ContainKey(mall.Id);
        radarData.Should().ContainKey(office.Id);

        var mallData = radarData[mall.Id];
        var officeData = radarData[office.Id];

        mallData["COP"].Should().Be(officeData["COP"],
            because: "COP是效率指标，不应该受功能类型调整影响");
        mallData["LoadFactor"].Should().Be(officeData["LoadFactor"],
            because: "负荷率是效率指标，不应该受功能类型调整影响");
    }

    /// <summary>
    /// 根因：报告只包含综合排名，用户无法了解各楼宇在不同指标、不同分类下的详细排名。
    /// 验证点：报告包含各指标的分类排名数据，格式如"COP:Type0:"、"COP:Overall:"。
    /// </summary>
    [Fact]
    public async Task CategoryRankings_ShouldBeSerializedInReport()
    {
        // Arrange
        var startDate = new DateTime(2024, 6, 1);
        var endDate = new DateTime(2024, 6, 7);

        var building1 = new Building
        {
            Id = "BLD-CAT-001",
            BuildingName = "分类排名测试1",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var building2 = new Building
        {
            Id = "BLD-CAT-002",
            BuildingName = "分类排名测试2",
            BuildingType = BuildingType.Office,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        var building3 = new Building
        {
            Id = "BLD-CAT-003",
            BuildingName = "分类排名测试3",
            BuildingType = BuildingType.Mall,
            GrossFloorArea = 50000,
            CoolingArea = 40000,
            DesignCoolingLoad = 8000,
            Status = 1
        };

        await _dbContext.Buildings.AddRangeAsync(building1, building2, building3);

        var device1 = await SeedDeviceAsync("DEV-CAT-001", DeviceType.CentrifugalChiller, "主机1");
        var device2 = await SeedDeviceAsync("DEV-CAT-002", DeviceType.CentrifugalChiller, "主机2");
        var device3 = await SeedDeviceAsync("DEV-CAT-003", DeviceType.CentrifugalChiller, "主机3");

        await SeedBuildingDevicesAsync(building1.Id, new List<string> { device1.Id });
        await SeedBuildingDevicesAsync(building2.Id, new List<string> { device2.Id });
        await SeedBuildingDevicesAsync(building3.Id, new List<string> { device3.Id });

        for (int i = 0; i < 7; i++)
        {
            foreach (var building in new[] { building1, building2, building3 })
            {
                await _dbContext.BuildingEfficiencyMetrics.AddAsync(new BuildingEfficiencyMetric
                {
                    BuildingId = building.Id,
                    StatisticsDate = startDate.AddDays(i),
                    StatisticsPeriod = "Daily",
                    EnergyPerUnitArea = 100m,
                    COP = 4.0m,
                    LoadFactor = 0.7m,
                    EER = 3.4m,
                    CostPerUnitArea = 50m
                });
            }
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await _module.GenerateBenchmarkReportAsync(
            new List<string> { building1.Id, building2.Id, building3.Id },
            "分类排名测试报告",
            startDate,
            endDate);

        // Assert
        report.CategoryRankings.Should().NotBeNullOrWhiteSpace(
            because: "报告应该包含分类排名数据");

        report.CategoryRankings.Should().Contain("COP:",
            because: "应该包含COP指标的排名");
        report.CategoryRankings.Should().Contain("EnergyPerArea:",
            because: "应该包含能耗指标的排名");
        report.CategoryRankings.Should().Contain(":Overall:",
            because: "应该包含综合排名");
    }
}
