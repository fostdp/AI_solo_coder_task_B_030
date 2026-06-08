using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChillerPlantOptimization.Models;

[Table("FaultTypes")]
public class FaultType
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string FaultCode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FaultName { get; set; } = string.Empty;

    public DeviceType DeviceTypeId { get; set; }

    public FaultSeverity Severity { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? TypicalCauses { get; set; }

    [MaxLength(1000)]
    public string? TypicalSolution { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedRepairHours { get; set; }

    public virtual ICollection<FaultSymptomParameter>? SymptomParameters { get; set; }
    public virtual ICollection<FaultDiagnosisResult>? DiagnosisResults { get; set; }
}

[Table("FaultSymptomParameters")]
public class FaultSymptomParameter
{
    [Key]
    public int Id { get; set; }

    public int FaultTypeId { get; set; }

    [ForeignKey("FaultTypeId")]
    public virtual FaultType? FaultType { get; set; }

    [MaxLength(100)]
    public string ParameterName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DeviationType { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal ThresholdValue { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal DeviationWeight { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal PriorProbability { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ConditionalProbability { get; set; }
}

[Table("BayesianNetworkNodes")]
public class BayesianNetworkNode
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string NodeName { get; set; } = string.Empty;

    public BayesianNodeType NodeType { get; set; }

    public DeviceType? DeviceTypeId { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    [MaxLength(200)]
    public string? ParentNodes { get; set; }
}

[Table("FaultDiagnosisResults")]
public class FaultDiagnosisResult
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string DeviceId { get; set; } = string.Empty;

    [ForeignKey("DeviceId")]
    public virtual Device? Device { get; set; }

    public int FaultTypeId { get; set; }

    [ForeignKey("FaultTypeId")]
    public virtual FaultType? FaultType { get; set; }

    public DateTime Timestamp { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Confidence { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BayesProbability { get; set; }

    [MaxLength(500)]
    public string? MatchingSymptoms { get; set; }

    [MaxLength(1000)]
    public string? DeviationDetails { get; set; }

    [MaxLength(1000)]
    public string? MaintenanceRecommendation { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedDowntimeHours { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedRepairCost { get; set; }

    public bool IsConfirmed { get; set; } = false;

    [MaxLength(50)]
    public string? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }
}

[Table("DiagnosisStatistics")]
public class DiagnosisStatistic
{
    [Key]
    public long Id { get; set; }

    public DateTime StatisticsDate { get; set; }

    public int TotalDiagnosisCount { get; set; }

    public int ConfirmedFaultCount { get; set; }

    public int FalsePositiveCount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal AverageConfidence { get; set; }

    [MaxLength(500)]
    public string? TopFaultTypes { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? AccuracyRate { get; set; }
}
