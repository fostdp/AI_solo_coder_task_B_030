using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChillerPlantOptimization.Models;

[Table("Buildings")]
public class Building
{
    [Key]
    [MaxLength(50)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(100)]
    public string BuildingName { get; set; } = string.Empty;

    public BuildingType BuildingType { get; set; }

    [MaxLength(200)]
    public string? Address { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal GrossFloorArea { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CoolingArea { get; set; }

    public int? NumberOfFloors { get; set; }

    public int? YearBuilt { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal DesignCoolingLoad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? PeakCoolingLoad { get; set; }

    [MaxLength(50)]
    public string? ContactPerson { get; set; }

    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    public int Status { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<BuildingDevice>? BuildingDevices { get; set; }
    public virtual ICollection<BuildingEfficiencyMetric>? EfficiencyMetrics { get; set; }
}

[Table("BuildingDevices")]
public class BuildingDevice
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string BuildingId { get; set; } = string.Empty;

    [ForeignKey("BuildingId")]
    public virtual Building? Building { get; set; }

    [MaxLength(50)]
    public string DeviceId { get; set; } = string.Empty;

    [ForeignKey("DeviceId")]
    public virtual Device? Device { get; set; }

    public DateTime? InstallationDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("BuildingEfficiencyMetrics")]
public class BuildingEfficiencyMetric
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string BuildingId { get; set; } = string.Empty;

    [ForeignKey("BuildingId")]
    public virtual Building? Building { get; set; }

    public DateTime StatisticsDate { get; set; }

    [MaxLength(20)]
    public string StatisticsPeriod { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal? EER { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? COP { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? EnergyPerUnitArea { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? CoolingPerUnitArea { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? PUE { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? LoadFactor { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? TotalElectricityConsumption { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? TotalCoolingCapacity { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? PeakDemand { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? OperatingHours { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? TotalCost { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? CostPerUnitArea { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? CostPerCooling { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? OutdoorAvgTemp { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? HDD { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? CDD { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("BenchmarkReports")]
public class BenchmarkReport
{
    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string ReportName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string ReportPeriod { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [MaxLength(500)]
    public string BuildingIds { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? COPRanking { get; set; }

    [MaxLength(1000)]
    public string? EnergyPerAreaRanking { get; set; }

    [MaxLength(1000)]
    public string? CostPerAreaRanking { get; set; }

    [MaxLength(1000)]
    public string? LoadFactorRanking { get; set; }

    [MaxLength(1000)]
    public string? EERRanking { get; set; }

    [MaxLength(2000)]
    public string? BestPractices { get; set; }

    [MaxLength(2000)]
    public string? ImprovementSuggestions { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? OverallScore { get; set; }

    [MaxLength(50)]
    public string? GeneratedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
