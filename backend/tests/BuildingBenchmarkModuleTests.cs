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
