using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 测试基类，提供通用的测试基础设施
/// 包括内存数据库创建、日志模拟、测试数据初始化等功能
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly AppDbContext _dbContext;
    protected readonly DbContextOptions<AppDbContext> _dbOptions;
    protected readonly string _testDatabaseName;

    /// <summary>
    /// 构造函数，初始化测试基础设施
    /// </summary>
    protected TestBase()
    {
        _testDatabaseName = Guid.NewGuid().ToString();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_testDatabaseName)
            .Options;

        _dbContext = new AppDbContext(_dbOptions);
        _dbContext.Database.EnsureCreated();
    }

    /// <summary>
    /// 创建新的数据库上下文实例，用于验证数据持久化
    /// </summary>
    /// <returns>新的AppDbContext实例</returns>
    protected AppDbContext CreateNewContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_testDatabaseName)
            .Options);
    }

    /// <summary>
    /// 创建指定类型的模拟日志记录器
    /// </summary>
    /// <typeparam name="T">日志记录器类型</typeparam>
    /// <returns>模拟的ILogger实例</returns>
    protected Mock<ILogger<T>> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }

    /// <summary>
    /// 验证日志是否被记录
    /// </summary>
    /// <typeparam name="T">日志记录器类型</typeparam>
    /// <param name="logger">模拟的日志记录器</param>
    /// <param name="logLevel">日志级别</param>
    /// <param name="times">调用次数</param>
    protected void VerifyLogger<T>(Mock<ILogger<T>> logger, LogLevel logLevel, Times times)
    {
        logger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    #region 测试数据创建方法

    /// <summary>
    /// 创建电价时段测试数据
    /// </summary>
    protected async Task SeedElectricityPriceTiersAsync()
    {
        var tiers = new List<ElectricityPriceTier>
        {
            new()
            {
                TierName = "谷段",
                PricePerKWh = 0.35m,
                StartHour = 23,
                EndHour = 7,
                Color = "#22c55e",
                IsActive = true
            },
            new()
            {
                TierName = "平段",
                PricePerKWh = 0.68m,
                StartHour = 7,
                EndHour = 10,
                Color = "#eab308",
                IsActive = true
            },
            new()
            {
                TierName = "平段",
                PricePerKWh = 0.68m,
                StartHour = 14,
                EndHour = 19,
                Color = "#eab308",
                IsActive = true
            },
            new()
            {
                TierName = "峰段",
                PricePerKWh = 1.05m,
                StartHour = 10,
                EndHour = 14,
                Color = "#ef4444",
                IsActive = true
            },
            new()
            {
                TierName = "峰段",
                PricePerKWh = 1.05m,
                StartHour = 19,
                EndHour = 23,
                Color = "#ef4444",
                IsActive = true
            }
        };

        await _dbContext.ElectricityPriceTiers.AddRangeAsync(tiers);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 创建冰蓄冷储罐测试数据
    /// </summary>
    protected async Task<IceStorageTank> SeedIceStorageTankAsync(string tankId = "TANK-001")
    {
        var device = new Device
        {
            Id = tankId,
            Name = "冰蓄冷储罐1号",
            DeviceTypeId = DeviceType.IceStorageTank,
            DesignCOP = 4.2m,
            RatedPower = 500m,
            RatedCoolingCapacity = 2000m,
            BACnetAddress = "192.168.1.101",
            BACnetInstance = 1001,
            Status = DeviceStatus.Running,
            PositionX = 100,
            PositionY = 100
        };

        var tank = new IceStorageTank
        {
            Id = tankId,
            MaxIceCapacity = 10000m,
            CurrentIceAmount = 3000m,
            IceMakingRate = 500m,
            IceMeltingRateMax = 800m,
            CurrentMeltingRate = 0,
            IceMakingCOP = 3.5m,
            IceMeltingEfficiency = 0.92m,
            CurrentMode = IceStorageMode.Standby,
            TankTemperature = -5.5m,
            BrineConcentration = 28.5m
        };

        await _dbContext.Devices.AddAsync(device);
        await _dbContext.IceStorageTanks.AddAsync(tank);
        await _dbContext.SaveChangesAsync();

        return tank;
    }

    /// <summary>
    /// 创建负荷预测测试数据
    /// </summary>
    protected async Task SeedLoadForecastsAsync(DateTime forecastDate)
    {
        var loadProfile = new[]
        {
            new { Hour = 0, Load = 0.3 },
            new { Hour = 1, Load = 0.25 },
            new { Hour = 2, Load = 0.22 },
            new { Hour = 3, Load = 0.2 },
            new { Hour = 4, Load = 0.2 },
            new { Hour = 5, Load = 0.22 },
            new { Hour = 6, Load = 0.35 },
            new { Hour = 7, Load = 0.55 },
            new { Hour = 8, Load = 0.75 },
            new { Hour = 9, Load = 0.88 },
            new { Hour = 10, Load = 0.95 },
            new { Hour = 11, Load = 0.98 },
            new { Hour = 12, Load = 1.0 },
            new { Hour = 13, Load = 0.98 },
            new { Hour = 14, Load = 0.97 },
            new { Hour = 15, Load = 0.95 },
            new { Hour = 16, Load = 0.92 },
            new { Hour = 17, Load = 0.88 },
            new { Hour = 18, Load = 0.82 },
            new { Hour = 19, Load = 0.78 },
            new { Hour = 20, Load = 0.7 },
            new { Hour = 21, Load = 0.6 },
            new { Hour = 22, Load = 0.5 },
            new { Hour = 23, Load = 0.4 }
        };

        var peakLoad = 8000m;

        foreach (var profile in loadProfile)
        {
            await _dbContext.LoadForecasts.AddAsync(new LoadForecast
            {
                ForecastDate = forecastDate.Date,
                HourOfDay = profile.Hour,
                PredictedLoad = peakLoad * (decimal)profile.Load,
                PredictionModel = "HistoricalProfile",
                Confidence = 0.85m,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 创建设备测试数据
    /// </summary>
    protected async Task<Device> SeedDeviceAsync(string deviceId, DeviceType deviceType, string name)
    {
        var device = new Device
        {
            Id = deviceId,
            Name = name,
            DeviceTypeId = deviceType,
            DesignCOP = deviceType == DeviceType.CentrifugalChiller ? 5.2m : 4.0m,
            RatedPower = deviceType == DeviceType.CentrifugalChiller ? 800m : 200m,
            RatedCoolingCapacity = deviceType == DeviceType.CentrifugalChiller ? 4200m : 800m,
            BACnetAddress = $"192.168.1.{100 + int.Parse(deviceId.Split('-').Last())}",
            BACnetInstance = 1000 + int.Parse(deviceId.Split('-').Last()),
            Status = DeviceStatus.Running,
            PositionX = 50 + int.Parse(deviceId.Split('-').Last()) * 30,
            PositionY = 50
        };

        await _dbContext.Devices.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        return device;
    }

    /// <summary>
    /// 创建设备数据测试数据
    /// </summary>
    protected async Task SeedDeviceDataAsync(string deviceId, int dataPoints = 30)
    {
        var deviceDataList = new List<DeviceData>();
        var baseTime = DateTime.UtcNow.AddHours(-dataPoints / 6);
        var random = new Random(42);

        for (int i = 0; i < dataPoints; i++)
        {
            deviceDataList.Add(new DeviceData
            {
                DeviceId = deviceId,
                Timestamp = baseTime.AddMinutes(i * 2),
                Power = 700m + (decimal)(random.NextDouble() * 200),
                SupplyTemperature = 6.5m + (decimal)(random.NextDouble() * 1.5),
                ReturnTemperature = 12.5m + (decimal)(random.NextDouble() * 2.0),
                Pressure = 0.45m + (decimal)(random.NextDouble() * 0.1),
                FlowRate = 180m + (decimal)(random.NextDouble() * 30),
                Frequency = 48m + (decimal)(random.NextDouble() * 4),
                Current = 120m + (decimal)(random.NextDouble() * 30),
                Voltage = 380m + (decimal)(random.NextDouble() * 10),
                InletTemperature = 28m + (decimal)(random.NextDouble() * 3),
                OutletTemperature = 32m + (decimal)(random.NextDouble() * 2),
                FanSpeed = 85m + (decimal)(random.NextDouble() * 15)
            });
        }

        await _dbContext.DeviceData.AddRangeAsync(deviceDataList);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 创建故障类型测试数据
    /// </summary>
    protected async Task SeedFaultTypesAsync()
    {
        var faultTypes = new List<FaultType>
        {
            new()
            {
                FaultCode = "COND-001",
                FaultName = "冷凝器结垢",
                DeviceTypeId = DeviceType.CentrifugalChiller,
                Severity = FaultSeverity.Moderate,
                Description = "冷凝器换热管结垢导致换热效率下降",
                TypicalCauses = "冷却水水质不佳、长期未清洗",
                TypicalSolution = "进行化学清洗或机械清洗，改善水质处理",
                EstimatedRepairHours = 8,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new()
                    {
                        ParameterName = "CoolingWaterTempDiff",
                        DeviationType = "Low",
                        ThresholdValue = 3.0m,
                        DeviationWeight = 0.8m,
                        PriorProbability = 0.05m,
                        ConditionalProbability = 0.85m
                    },
                    new()
                    {
                        ParameterName = "Pressure",
                        DeviationType = "High",
                        ThresholdValue = 0.6m,
                        DeviationWeight = 0.7m,
                        PriorProbability = 0.05m,
                        ConditionalProbability = 0.75m
                    },
                    new()
                    {
                        ParameterName = "Power",
                        DeviationType = "High",
                        ThresholdValue = 850m,
                        DeviationWeight = 0.6m,
                        PriorProbability = 0.05m,
                        ConditionalProbability = 0.7m
                    }
                }
            },
            new()
            {
                FaultCode = "EVAP-001",
                FaultName = "蒸发器泄漏",
                DeviceTypeId = DeviceType.CentrifugalChiller,
                Severity = FaultSeverity.Severe,
                Description = "蒸发器换热管泄漏导致制冷剂损失",
                TypicalCauses = "腐蚀、磨损、制造缺陷",
                TypicalSolution = "检漏、补焊或更换换热管，补充制冷剂",
                EstimatedRepairHours = 16,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new()
                    {
                        ParameterName = "SupplyTemperature",
                        DeviationType = "High",
                        ThresholdValue = 8.0m,
                        DeviationWeight = 0.9m,
                        PriorProbability = 0.02m,
                        ConditionalProbability = 0.9m
                    },
                    new()
                    {
                        ParameterName = "Pressure",
                        DeviationType = "Low",
                        ThresholdValue = 0.35m,
                        DeviationWeight = 0.85m,
                        PriorProbability = 0.02m,
                        ConditionalProbability = 0.85m
                    },
                    new()
                    {
                        ParameterName = "TemperatureRise",
                        DeviationType = "Low",
                        ThresholdValue = 4.0m,
                        DeviationWeight = 0.75m,
                        PriorProbability = 0.02m,
                        ConditionalProbability = 0.8m
                    }
                }
            },
            new()
            {
                FaultCode = "FAN-001",
                FaultName = "风机故障",
                DeviceTypeId = DeviceType.CoolingTower,
                Severity = FaultSeverity.Moderate,
                Description = "冷却塔风机轴承损坏或电机故障",
                TypicalCauses = "润滑不足、轴承磨损、电机过载",
                TypicalSolution = "更换轴承或电机，检查传动系统",
                EstimatedRepairHours = 6,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new()
                    {
                        ParameterName = "FanSpeed",
                        DeviationType = "Low",
                        ThresholdValue = 50m,
                        DeviationWeight = 0.95m,
                        PriorProbability = 0.03m,
                        ConditionalProbability = 0.95m
                    },
                    new()
                    {
                        ParameterName = "OutletTemperature",
                        DeviationType = "High",
                        ThresholdValue = 35m,
                        DeviationWeight = 0.7m,
                        PriorProbability = 0.03m,
                        ConditionalProbability = 0.75m
                    },
                    new()
                    {
                        ParameterName = "Current",
                        DeviationType = "Pattern",
                        ThresholdValue = 20m,
                        DeviationWeight = 0.65m,
                        PriorProbability = 0.03m,
                        ConditionalProbability = 0.7m
                    }
                }
            },
            new()
            {
                FaultCode = "PUMP-001",
                FaultName = "水泵气蚀",
                DeviceTypeId = DeviceType.CoolingWaterPump,
                Severity = FaultSeverity.Moderate,
                Description = "水泵叶轮气蚀导致流量和扬程下降",
                TypicalCauses = "吸入压力过低、水温过高",
                TypicalSolution = "检查吸入管路，清理过滤器，降低水温",
                EstimatedRepairHours = 4,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "FlowRate", DeviationType = "Low", ThresholdValue = 150m, DeviationWeight = 0.85m, PriorProbability = 0.04m, ConditionalProbability = 0.85m },
                    new() { ParameterName = "Pressure", DeviationType = "Pattern", ThresholdValue = 15m, DeviationWeight = 0.75m, PriorProbability = 0.04m, ConditionalProbability = 0.8m },
                    new() { ParameterName = "Current", DeviationType = "Pattern", ThresholdValue = 15m, DeviationWeight = 0.7m, PriorProbability = 0.04m, ConditionalProbability = 0.75m }
                }
            },
            new()
            {
                FaultCode = "COMP-001",
                FaultName = "压缩机过载",
                DeviceTypeId = DeviceType.ScrewChiller,
                Severity = FaultSeverity.Severe,
                Description = "螺杆压缩机负载过大导致电流过高",
                TypicalCauses = "冷凝压力过高、滑阀故障",
                TypicalSolution = "检查冷却水系统，检修滑阀机构",
                EstimatedRepairHours = 12,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "Current", DeviationType = "High", ThresholdValue = 180m, DeviationWeight = 0.9m, PriorProbability = 0.025m, ConditionalProbability = 0.9m },
                    new() { ParameterName = "Power", DeviationType = "High", ThresholdValue = 750m, DeviationWeight = 0.8m, PriorProbability = 0.025m, ConditionalProbability = 0.85m },
                    new() { ParameterName = "Pressure", DeviationType = "High", ThresholdValue = 0.7m, DeviationWeight = 0.75m, PriorProbability = 0.025m, ConditionalProbability = 0.8m }
                }
            },
            new()
            {
                FaultCode = "COND-002",
                FaultName = "冷凝器堵塞",
                DeviceTypeId = DeviceType.CentrifugalChiller,
                Severity = FaultSeverity.Moderate,
                Description = "冷凝器管路堵塞导致流量不足",
                TypicalCauses = "杂物堆积、生物污泥",
                TypicalSolution = "物理清洗，安装过滤装置",
                EstimatedRepairHours = 6,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "FlowRate", DeviationType = "Low", ThresholdValue = 160m, DeviationWeight = 0.85m, PriorProbability = 0.035m, ConditionalProbability = 0.85m },
                    new() { ParameterName = "Pressure", DeviationType = "High", ThresholdValue = 0.65m, DeviationWeight = 0.75m, PriorProbability = 0.035m, ConditionalProbability = 0.8m },
                    new() { ParameterName = "CoolingWaterTempDiff", DeviationType = "High", ThresholdValue = 6m, DeviationWeight = 0.7m, PriorProbability = 0.035m, ConditionalProbability = 0.75m }
                }
            },
            new()
            {
                FaultCode = "EXP-001",
                FaultName = "膨胀阀故障",
                DeviceTypeId = DeviceType.CentrifugalChiller,
                Severity = FaultSeverity.Moderate,
                Description = "电子膨胀阀开度异常导致供液不足",
                TypicalCauses = "控制器故障、阀芯磨损",
                TypicalSolution = "检修或更换膨胀阀，校准控制器",
                EstimatedRepairHours = 5,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "SupplyTemperature", DeviationType = "High", ThresholdValue = 7.5m, DeviationWeight = 0.8m, PriorProbability = 0.04m, ConditionalProbability = 0.8m },
                    new() { ParameterName = "Pressure", DeviationType = "Low", ThresholdValue = 0.38m, DeviationWeight = 0.75m, PriorProbability = 0.04m, ConditionalProbability = 0.75m },
                    new() { ParameterName = "TemperatureRise", DeviationType = "Low", ThresholdValue = 4.5m, DeviationWeight = 0.7m, PriorProbability = 0.04m, ConditionalProbability = 0.7m }
                }
            },
            new()
            {
                FaultCode = "FAN-002",
                FaultName = "风机皮带磨损",
                DeviceTypeId = DeviceType.CoolingTower,
                Severity = FaultSeverity.Minor,
                Description = "传动皮带磨损导致转速下降",
                TypicalCauses = "长期使用、张紧度不足",
                TypicalSolution = "更换皮带，调整张紧度",
                EstimatedRepairHours = 2,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "FanSpeed", DeviationType = "Low", ThresholdValue = 70m, DeviationWeight = 0.85m, PriorProbability = 0.06m, ConditionalProbability = 0.85m },
                    new() { ParameterName = "Current", DeviationType = "Low", ThresholdValue = 40m, DeviationWeight = 0.65m, PriorProbability = 0.06m, ConditionalProbability = 0.7m },
                    new() { ParameterName = "OutletTemperature", DeviationType = "High", ThresholdValue = 34m, DeviationWeight = 0.6m, PriorProbability = 0.06m, ConditionalProbability = 0.65m }
                }
            },
            new()
            {
                FaultCode = "PUMP-002",
                FaultName = "水泵轴承磨损",
                DeviceTypeId = DeviceType.ChilledWaterPump,
                Severity = FaultSeverity.Moderate,
                Description = "水泵轴承磨损导致振动和噪音",
                TypicalCauses = "润滑失效、长期运行",
                TypicalSolution = "更换轴承，检查联轴器",
                EstimatedRepairHours = 4,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "Current", DeviationType = "Pattern", ThresholdValue = 18m, DeviationWeight = 0.8m, PriorProbability = 0.045m, ConditionalProbability = 0.8m },
                    new() { ParameterName = "Pressure", DeviationType = "Pattern", ThresholdValue = 12m, DeviationWeight = 0.75m, PriorProbability = 0.045m, ConditionalProbability = 0.75m },
                    new() { ParameterName = "FlowRate", DeviationType = "Pattern", ThresholdValue = 10m, DeviationWeight = 0.7m, PriorProbability = 0.045m, ConditionalProbability = 0.7m }
                }
            },
            new()
            {
                FaultCode = "SENS-001",
                FaultName = "温度传感器漂移",
                DeviceTypeId = DeviceType.CentrifugalChiller,
                Severity = FaultSeverity.Minor,
                Description = "温度传感器校准漂移导致读数不准",
                TypicalCauses = "老化、环境影响",
                TypicalSolution = "重新校准或更换传感器",
                EstimatedRepairHours = 2,
                SymptomParameters = new List<FaultSymptomParameter>
                {
                    new() { ParameterName = "SupplyTemperature", DeviationType = "Pattern", ThresholdValue = 2m, DeviationWeight = 0.75m, PriorProbability = 0.055m, ConditionalProbability = 0.75m },
                    new() { ParameterName = "ReturnTemperature", DeviationType = "Pattern", ThresholdValue = 2m, DeviationWeight = 0.75m, PriorProbability = 0.055m, ConditionalProbability = 0.75m },
                    new() { ParameterName = "TemperatureRise", DeviationType = "Pattern", ThresholdValue = 1.5m, DeviationWeight = 0.7m, PriorProbability = 0.055m, ConditionalProbability = 0.7m }
                }
            }
        };

        await _dbContext.FaultTypes.AddRangeAsync(faultTypes);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 创建楼宇测试数据
    /// </summary>
    protected async Task<List<Building>> SeedBuildingsAsync(int count = 3)
    {
        var buildingTypes = new[] { BuildingType.Office, BuildingType.Mall, BuildingType.Hotel };
        var buildings = new List<Building>();

        for (int i = 0; i < count; i++)
        {
            var building = new Building
            {
                Id = $"BLD-{i + 1:D3}",
                BuildingName = $"示范楼宇{i + 1}号",
                BuildingType = buildingTypes[i % buildingTypes.Length],
                Address = $"示范路{i + 1}号",
                GrossFloorArea = 50000 + i * 10000,
                CoolingArea = 40000 + i * 8000,
                NumberOfFloors = 20 + i * 5,
                YearBuilt = 2010 + i,
                DesignCoolingLoad = 8000 + i * 1000,
                PeakCoolingLoad = 8500 + i * 1000,
                ContactPerson = $"联系人{i + 1}",
                ContactPhone = $"138000000{i + 1:D2}",
                Status = 1
            };

            buildings.Add(building);
        }

        await _dbContext.Buildings.AddRangeAsync(buildings);
        await _dbContext.SaveChangesAsync();

        return buildings;
    }

    /// <summary>
    /// 创建楼宇设备关联测试数据
    /// </summary>
    protected async Task SeedBuildingDevicesAsync(string buildingId, List<string> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            await _dbContext.BuildingDevices.AddAsync(new BuildingDevice
            {
                BuildingId = buildingId,
                DeviceId = deviceId,
                InstallationDate = new DateTime(2023, 1, 1),
                Notes = "测试设备"
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 创建楼宇能效指标测试数据
    /// </summary>
    protected async Task SeedBuildingEfficiencyMetricsAsync(string buildingId, DateTime startDate, int days = 7)
    {
        var random = new Random(buildingId.GetHashCode());
        var baseCOP = 3.5m + (decimal)(random.NextDouble() * 1.5);
        var baseEnergy = 80m + (decimal)(random.NextDouble() * 60);

        for (int i = 0; i < days; i++)
        {
            var metric = new BuildingEfficiencyMetric
            {
                BuildingId = buildingId,
                StatisticsDate = startDate.AddDays(i),
                StatisticsPeriod = "Daily",
                EER = baseCOP * 0.85m + (decimal)(random.NextDouble() * 0.5),
                COP = baseCOP + (decimal)(random.NextDouble() * 0.6),
                EnergyPerUnitArea = baseEnergy + (decimal)(random.NextDouble() * 20),
                CoolingPerUnitArea = 150m + (decimal)(random.NextDouble() * 50),
                PUE = 1.2m + (decimal)(random.NextDouble() * 0.5),
                LoadFactor = 0.6m + (decimal)(random.NextDouble() * 0.3),
                TotalElectricityConsumption = 5000m + (decimal)(random.NextDouble() * 2000),
                TotalCoolingCapacity = 20000m + (decimal)(random.NextDouble() * 5000),
                PeakDemand = 350m + (decimal)(random.NextDouble() * 100),
                OperatingHours = 18m + (decimal)(random.NextDouble() * 4),
                TotalCost = 4000m + (decimal)(random.NextDouble() * 1500),
                CostPerUnitArea = 60m + (decimal)(random.NextDouble() * 30),
                CostPerCooling = 0.18m + (decimal)(random.NextDouble() * 0.05),
                OutdoorAvgTemp = 25m + (decimal)(random.NextDouble() * 8),
                HDD = 0,
                CDD = 50m + (decimal)(random.NextDouble() * 30)
            };

            await _dbContext.BuildingEfficiencyMetrics.AddAsync(metric);
        }

        await _dbContext.SaveChangesAsync();
    }

    #endregion

    /// <summary>
    /// 释放资源，清理数据库
    /// </summary>
    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
