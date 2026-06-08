using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChillerPlantOptimization.Models;

[Table("DemandResponseRequests")]
public class DemandResponseRequest
{
    [Key]
    [MaxLength(50)]
    public string Id { get; set; } = string.Empty;

    public DRRequestType RequestType { get; set; }

    public DRRequestStatus Status { get; set; } = DRRequestStatus.Received;

    [MaxLength(100)]
    public string SourcePlatform { get; set; } = "Simulation";

    [Column(TypeName = "decimal(18,4)")]
    public decimal RequestedLoadReduction { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IncentivePerKWh { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? MaxChillerOutputLimit { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? MinIceMeltingRate { get; set; }

    public int Priority { get; set; } = 1;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public bool ResponseRequired { get; set; } = true;

    [MaxLength(200)]
    public string? CancellationReason { get; set; }

    public virtual ICollection<DRExecutionLog>? ExecutionLogs { get; set; }
    public virtual DRResponseSummary? ResponseSummary { get; set; }
}

[Table("DRExecutionLogs")]
public class DRExecutionLog
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string DRRequestId { get; set; } = string.Empty;

    [ForeignKey("DRRequestId")]
    public virtual DemandResponseRequest? DRRequest { get; set; }

    public DateTime Timestamp { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BaselineLoad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ActualLoad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal AchievedReduction { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TargetReduction { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ChillerOutputLimit { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IceMeltingRateApplied { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ElectricitySaved { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IncentiveEarned { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal CostSaving { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalBenefit { get; set; }
}

[Table("DRResponseSummaries")]
public class DRResponseSummary
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string DRRequestId { get; set; } = string.Empty;

    [ForeignKey("DRRequestId")]
    public virtual DemandResponseRequest? DRRequest { get; set; }

    public int TotalDurationMinutes { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalRequestedReduction { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalActualReduction { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal AverageComplianceRate { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalElectricitySaved { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalIncentiveEarned { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalCostSaving { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalBenefit { get; set; }

    public int? UserSatisfactionScore { get; set; }

    public DateTime? CompletedAt { get; set; }
}
