using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChillerPlantOptimization.Models;

[Table("ElectricityPriceTiers")]
public class ElectricityPriceTier
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string TierName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal PricePerKWh { get; set; }

    public int StartHour { get; set; }

    public int EndHour { get; set; }

    [MaxLength(20)]
    public string Color { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

[Table("LoadForecasts")]
public class LoadForecast
{
    [Key]
    public long Id { get; set; }

    public DateTime ForecastDate { get; set; }

    public int HourOfDay { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal PredictedLoad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? ActualLoad { get; set; }

    [MaxLength(50)]
    public string? PredictionModel { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? Confidence { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("IceStorageTanks")]
public class IceStorageTank
{
    [Key]
    [MaxLength(50)]
    public string Id { get; set; } = string.Empty;

    [ForeignKey("Id")]
    public virtual Device? Device { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal MaxIceCapacity { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentIceAmount { get; set; } = 0;

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceMakingRate { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceMeltingRateMax { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentMeltingRate { get; set; } = 0;

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceMakingCOP { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceMeltingEfficiency { get; set; }

    public IceStorageMode CurrentMode { get; set; } = IceStorageMode.Standby;

    [Column(TypeName = "decimal(18,4)")]
    public decimal? TankTemperature { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? BrineConcentration { get; set; }

    public virtual ICollection<IceStorageOperation>? IceStorageOperations { get; set; }
    public virtual ICollection<IceStorageSchedule>? IceStorageSchedules { get; set; }
}

[Table("IceStorageSchedules")]
public class IceStorageSchedule
{
    [Key]
    public long Id { get; set; }

    public DateTime ScheduleDate { get; set; }

    public int HourOfDay { get; set; }

    public IceStorageMode Mode { get; set; }

    [MaxLength(50)]
    public string? IceTankId { get; set; }

    [ForeignKey("IceTankId")]
    public virtual IceStorageTank? IceStorageTank { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TargetIceAmount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TargetMeltingRate { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ChillerLoadRatio { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceLoadRatio { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ExpectedCost { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BaselineCost { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CostSaving { get; set; }

    public bool IsOptimized { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("IceStorageOperations")]
public class IceStorageOperation
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string IceTankId { get; set; } = string.Empty;

    [ForeignKey("IceTankId")]
    public virtual IceStorageTank? IceStorageTank { get; set; }

    public DateTime Timestamp { get; set; }

    public IceStorageMode Mode { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceAmountBefore { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceAmountAfter { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceAmountDelta { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal MeltingRate { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal PowerConsumption { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CoolingProvided { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ElectricityPrice { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Cost { get; set; }
}

[Table("DPStrategyRecords")]
public class DPStrategyRecord
{
    [Key]
    public long Id { get; set; }

    public DateTime ScheduleDate { get; set; }

    public int TotalStatesEvaluated { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal OptimalCost { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BaselineCost { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalSaving { get; set; }

    public int ComputationTimeMs { get; set; }

    [MaxLength(50)]
    public string Algorithm { get; set; } = "DynamicProgramming";

    [Column(TypeName = "decimal(18,4)")]
    public decimal? RobustnessMargin { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? ForecastErrorConsidered { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? BackgroundTaskId { get; set; }
}
