using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceData> DeviceData => Set<DeviceData>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<AlarmThreshold> AlarmThresholds => Set<AlarmThreshold>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<EfficiencyRecord> EfficiencyRecords => Set<EfficiencyRecord>();
    public DbSet<OptimizationRecommendation> OptimizationRecommendations => Set<OptimizationRecommendation>();
    public DbSet<SystemMetric> SystemMetrics => Set<SystemMetric>();
    public DbSet<DiagnosisReport> DiagnosisReports => Set<DiagnosisReport>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<User> Users => Set<User>();

    public DbSet<ElectricityPriceTier> ElectricityPriceTiers => Set<ElectricityPriceTier>();
    public DbSet<LoadForecast> LoadForecasts => Set<LoadForecast>();
    public DbSet<IceStorageTank> IceStorageTanks => Set<IceStorageTank>();
    public DbSet<IceStorageSchedule> IceStorageSchedules => Set<IceStorageSchedule>();
    public DbSet<IceStorageOperation> IceStorageOperations => Set<IceStorageOperation>();
    public DbSet<DPStrategyRecord> DPStrategyRecords => Set<DPStrategyRecord>();

    public DbSet<DemandResponseRequest> DemandResponseRequests => Set<DemandResponseRequest>();
    public DbSet<DRExecutionLog> DRExecutionLogs => Set<DRExecutionLog>();
    public DbSet<DRResponseSummary> DRResponseSummaries => Set<DRResponseSummary>();

    public DbSet<FaultType> FaultTypes => Set<FaultType>();
    public DbSet<FaultSymptomParameter> FaultSymptomParameters => Set<FaultSymptomParameter>();
    public DbSet<BayesianNetworkNode> BayesianNetworkNodes => Set<BayesianNetworkNode>();
    public DbSet<FaultDiagnosisResult> FaultDiagnosisResults => Set<FaultDiagnosisResult>();
    public DbSet<DiagnosisStatistic> DiagnosisStatistics => Set<DiagnosisStatistic>();

    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<BuildingDevice> BuildingDevices => Set<BuildingDevice>();
    public DbSet<BuildingEfficiencyMetric> BuildingEfficiencyMetrics => Set<BuildingEfficiencyMetric>();
    public DbSet<BenchmarkReport> BenchmarkReports => Set<BenchmarkReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceData>()
            .HasIndex(d => new { d.DeviceId, d.Timestamp })
            .IsDescending(false, true);

        modelBuilder.Entity<DeviceData>()
            .HasIndex(d => d.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<EfficiencyRecord>()
            .HasIndex(e => e.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<SystemMetric>()
            .HasIndex(s => s.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<Alarm>()
            .HasIndex(a => a.StartTime)
            .IsDescending(true);

        modelBuilder.Entity<OptimizationRecommendation>()
            .HasIndex(o => o.GeneratedAt)
            .IsDescending(true);

        modelBuilder.Entity<DiagnosisReport>()
            .HasIndex(d => d.ReportDate)
            .IsDescending(true);

        modelBuilder.Entity<WorkOrder>()
            .HasIndex(w => w.WorkOrderNo)
            .IsUnique();

        modelBuilder.Entity<LoadForecast>()
            .HasIndex(l => new { l.ForecastDate, l.HourOfDay })
            .IsUnique();

        modelBuilder.Entity<IceStorageSchedule>()
            .HasIndex(s => new { s.ScheduleDate, s.HourOfDay })
            .IsUnique();

        modelBuilder.Entity<IceStorageSchedule>()
            .HasIndex(s => s.ScheduleDate)
            .IsDescending(true);

        modelBuilder.Entity<IceStorageOperation>()
            .HasIndex(o => o.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<IceStorageOperation>()
            .HasIndex(o => new { o.IceTankId, o.Timestamp })
            .IsDescending(false, true);

        modelBuilder.Entity<DemandResponseRequest>()
            .HasIndex(r => r.StartTime)
            .IsDescending(true);

        modelBuilder.Entity<DemandResponseRequest>()
            .HasIndex(r => r.Status);

        modelBuilder.Entity<DRExecutionLog>()
            .HasIndex(l => l.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<DRExecutionLog>()
            .HasIndex(l => l.DRRequestId);

        modelBuilder.Entity<FaultDiagnosisResult>()
            .HasIndex(r => r.Timestamp)
            .IsDescending(true);

        modelBuilder.Entity<FaultDiagnosisResult>()
            .HasIndex(r => new { r.DeviceId, r.Confidence, r.Timestamp })
            .IsDescending(false, true, true);

        modelBuilder.Entity<FaultDiagnosisResult>()
            .HasIndex(r => r.Confidence)
            .IsDescending(true);

        modelBuilder.Entity<DiagnosisStatistic>()
            .HasIndex(s => s.StatisticsDate)
            .IsUnique()
            .IsDescending(true);

        modelBuilder.Entity<BuildingEfficiencyMetric>()
            .HasIndex(m => new { m.BuildingId, m.StatisticsDate, m.StatisticsPeriod })
            .IsUnique();

        modelBuilder.Entity<BuildingEfficiencyMetric>()
            .HasIndex(m => new { m.StatisticsPeriod, m.StatisticsDate })
            .IsDescending(false, true);

        modelBuilder.Entity<BenchmarkReport>()
            .HasIndex(r => r.StartDate)
            .IsDescending(true);

        modelBuilder.Entity<BuildingDevice>()
            .HasIndex(d => new { d.BuildingId, d.DeviceId })
            .IsUnique();

        modelBuilder.Entity<DPStrategyRecord>()
            .HasIndex(r => r.ScheduleDate)
            .IsUnique()
            .IsDescending(true);
    }
}
