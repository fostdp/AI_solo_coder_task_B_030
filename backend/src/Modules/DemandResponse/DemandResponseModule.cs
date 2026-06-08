using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Modules.DemandResponse;

public class ActiveDRRequest
{
    public string RequestId { get; set; } = string.Empty;
    public DRRequestType Type { get; set; }
    public int Priority { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal ChillerOutputLimit { get; set; }
    public decimal IceMeltingRate { get; set; }
    public decimal RequestedLoadReduction { get; set; }
    public bool IsMutuallyExclusive { get; set; }
}

public interface IDemandResponseModule
{
    Task<DemandResponseRequest> SimulateDRRequestAsync(DRRequestType type, decimal loadReduction, int durationMinutes, decimal incentive);
    Task<List<DemandResponseRequest>> GetActiveRequestsAsync();
    Task<DRResponseSummary> ExecuteResponseAsync(string requestId);
    Task<DRExecutionLog> RecordExecutionLogAsync(string requestId, decimal baselineLoad, decimal actualLoad);
    Task<DRResponseSummary> CompleteResponseAsync(string requestId, int satisfactionScore);
    Task<decimal> GetCurrentChillerOutputLimitAsync();
    Task<decimal> GetCurrentIceMeltingRateAsync();
}

public class DemandResponseModule : IDemandResponseModule
{
    private readonly IDemandResponder _responder;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DemandResponseModule> _logger;
    private readonly IIceStorageModule? _iceStorageModule;

    public DemandResponseModule(
        AppDbContext dbContext,
        ILogger<DemandResponseModule> logger,
        IIceStorageModule? iceStorageModule = null,
        IDemandResponder? responder = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _iceStorageModule = iceStorageModule;
        _responder = responder ?? new DemandResponder(
            dbContext,
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DemandResponder>(),
            iceStorageModule != null ? new IceStorageOptimizer(dbContext, 
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<IceStorageOptimizer>()) : null);
    }

    public async Task<DemandResponseRequest> SimulateDRRequestAsync(
        DRRequestType type,
        decimal loadReduction,
        int durationMinutes,
        decimal incentive)
    {
        var request = new DRRequest
        {
            Type = type,
            LoadReduction = loadReduction,
            DurationMinutes = durationMinutes,
            Incentive = incentive,
            Priority = type == DRRequestType.EmergencyDR ? 0 : 1
        };

        return await _responder.ReceiveRequestAsync(request);
    }

    public async Task<List<DemandResponseRequest>> GetActiveRequestsAsync()
    {
        var now = DateTime.UtcNow;
        return await _dbContext.DemandResponseRequests
            .Where(r => r.Status == DRRequestStatus.Received || r.Status == DRRequestStatus.Executing)
            .Where(r => r.EndTime > now)
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.StartTime)
            .ToListAsync();
    }

    public async Task<DRResponseSummary> ExecuteResponseAsync(string requestId)
    {
        await _responder.ExecuteResponseAsync(requestId);
        
        var summary = await _dbContext.DRResponseSummaries
            .FirstOrDefaultAsync(s => s.DRRequestId == requestId);

        return summary ?? new DRResponseSummary
        {
            DRRequestId = requestId,
            TotalDurationMinutes = 0,
            TotalRequestedReduction = 0,
            TotalActualReduction = 0,
            AverageComplianceRate = 0,
            TotalElectricitySaved = 0,
            TotalIncentiveEarned = 0,
            TotalCostSaving = 0,
            TotalBenefit = 0
        };
    }

    public async Task<DRExecutionLog> RecordExecutionLogAsync(string requestId, decimal baselineLoad, decimal actualLoad)
    {
        return await _responder.RecordExecutionLogAsync(requestId, baselineLoad, actualLoad);
    }

    public async Task<DRResponseSummary> CompleteResponseAsync(string requestId, int satisfactionScore)
    {
        return await _responder.CompleteResponseAsync(requestId, satisfactionScore);
    }

    public async Task<decimal> GetCurrentChillerOutputLimitAsync()
    {
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();
        return adjustments.ChillerOutputLimit;
    }

    public async Task<decimal> GetCurrentIceMeltingRateAsync()
    {
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();
        return adjustments.IceMeltingRate;
    }

    private async Task<(bool IsConflict, string? ConflictingRequestId)> CheckConflictAsync(
        DRRequestType newType,
        DateTime newStart,
        DateTime newEnd,
        int newPriority)
    {
        return await _responder.CheckConflictAsync(newType, newStart, newEnd, newPriority);
    }
}
