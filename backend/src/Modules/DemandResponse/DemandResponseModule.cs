using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

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
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DemandResponseModule> _logger;
    private readonly IIceStorageModule? _iceStorageModule;
    private readonly ConcurrentDictionary<string, ActiveDRRequest> _activeRequests = new();
    private readonly SemaphoreSlim _conflictResolutionSemaphore = new(1, 1);

    public DemandResponseModule(
        AppDbContext dbContext,
        ILogger<DemandResponseModule> logger,
        IIceStorageModule? iceStorageModule = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _iceStorageModule = iceStorageModule;
    }

    private async Task<(bool IsConflict, string? ConflictingRequestId)> CheckConflictAsync(
        DRRequestType newType,
        DateTime newStart,
        DateTime newEnd,
        int newPriority)
    {
        var activeList = _activeRequests.Values.ToList();
        foreach (var active in activeList)
        {
            var timeOverlap = newStart < active.EndTime && newEnd > active.StartTime;
            if (!timeOverlap) continue;

            if (newType == DRRequestType.EmergencyDR || active.Type == DRRequestType.EmergencyDR)
            {
                return (true, active.RequestId);
            }

            if (newType == active.Type)
            {
                return (true, active.RequestId);
            }

            if (newPriority == active.Priority)
            {
                return (true, active.RequestId);
            }
        }
        return (false, null);
    }

    private async Task ResolveConflictAsync(string existingRequestId, int newPriority)
    {
        await _conflictResolutionSemaphore.WaitAsync();
        try
        {
            if (!_activeRequests.TryGetValue(existingRequestId, out var existing))
                return;

            if (newPriority < existing.Priority)
            {
                _logger.LogWarning(
                    "DR事件抢占: 高优先级事件抢占 {ExistingId} (优先级 {OldPriority} → {NewPriority})",
                    existingRequestId, existing.Priority, newPriority);

                await CancelExistingRequestAsync(existingRequestId);
            }
            else
            {
                throw new InvalidOperationException(
                    $"无法执行：现有事件 {existingRequestId} 优先级更高");
            }
        }
        finally
        {
            _conflictResolutionSemaphore.Release();
        }
    }

    private async Task CancelExistingRequestAsync(string requestId)
    {
        var request = await _dbContext.DemandResponseRequests.FindAsync(requestId);
        if (request != null)
        {
            request.Status = DRRequestStatus.Cancelled;
            request.CancellationReason = "被更高优先级DR事件抢占";
        }
        _activeRequests.TryRemove(requestId, out _);
        await RecalculateAggregatedLimitsAsync();
    }

    private Task RecalculateAggregatedLimitsAsync()
    {
        if (!_activeRequests.Any())
        {
            return Task.CompletedTask;
        }

        var minChillerLimit = _activeRequests.Values.Min(r => r.ChillerOutputLimit);
        var maxMeltingRate = _activeRequests.Values.Max(r => r.IceMeltingRate);
        var totalRequestedReduction = _activeRequests.Values.Sum(r => r.RequestedLoadReduction);

        _logger.LogInformation(
            "DR限制重新计算: 活动事件数={Count}, 主机上限={Limit:P0}, 融冰速率={Rate}kW, 总减载={Reduction}kW",
            _activeRequests.Count, minChillerLimit, maxMeltingRate, totalRequestedReduction);

        return Task.CompletedTask;
    }

    public async Task<DemandResponseRequest> SimulateDRRequestAsync(
        DRRequestType type,
        decimal loadReduction,
        int durationMinutes,
        decimal incentive)
    {
        var request = new DemandResponseRequest
        {
            Id = $"DR-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = type,
            Status = DRRequestStatus.Received,
            SourcePlatform = "PowerGridSimulation",
            RequestedLoadReduction = loadReduction,
            StartTime = DateTime.UtcNow.AddMinutes(5),
            EndTime = DateTime.UtcNow.AddMinutes(5 + durationMinutes),
            IncentivePerKWh = incentive,
            MaxChillerOutputLimit = type == DRRequestType.LoadReduction || type == DRRequestType.EmergencyDR
                ? Math.Max(0.3m, 1.0m - (loadReduction / 8000m))
                : null,
            MinIceMeltingRate = loadReduction > 1000 ? Math.Min(3000, loadReduction * 0.5m) : null,
            Priority = type == DRRequestType.EmergencyDR ? 0 : 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(request);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "收到需求响应指令: {Id}, 类型: {Type}, 减载量: {Reduction}kW, 时长: {Duration}分钟, 补贴: {Incentive:C}/kWh",
            request.Id, type, loadReduction, durationMinutes, incentive);

        return request;
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
        var request = await _dbContext.DemandResponseRequests.FindAsync(requestId);
        if (request == null)
            throw new KeyNotFoundException($"Demand response request {requestId} not found");

        if (request.Status == DRRequestStatus.Completed || request.Status == DRRequestStatus.Cancelled)
            throw new InvalidOperationException($"Request {requestId} is already {request.Status}");

        var (hasConflict, conflictingId) = await CheckConflictAsync(
            request.RequestType, request.StartTime, request.EndTime, request.Priority);

        if (hasConflict && conflictingId != null)
        {
            await ResolveConflictAsync(conflictingId, request.Priority);
        }

        var activeRequest = new ActiveDRRequest
        {
            RequestId = requestId,
            Type = request.RequestType,
            Priority = request.Priority,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ChillerOutputLimit = request.MaxChillerOutputLimit ?? 1.0m,
            IceMeltingRate = request.MinIceMeltingRate ?? 0m,
            RequestedLoadReduction = request.RequestedLoadReduction,
            IsMutuallyExclusive = request.RequestType == DRRequestType.EmergencyDR
        };

        _activeRequests[requestId] = activeRequest;
        await RecalculateAggregatedLimitsAsync();

        request.Status = DRRequestStatus.Executing;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "开始执行需求响应: {Id}, 主机出力上限: {Limit:P0}, 融冰速率: {Rate}kW",
            requestId, activeRequest.ChillerOutputLimit, activeRequest.IceMeltingRate);

        var summary = new DRResponseSummary
        {
            DRRequestId = requestId,
            TotalDurationMinutes = (int)(request.EndTime - request.StartTime).TotalMinutes,
            TotalRequestedReduction = request.RequestedLoadReduction,
            TotalActualReduction = 0,
            AverageComplianceRate = 0,
            TotalElectricitySaved = 0,
            TotalIncentiveEarned = 0,
            TotalCostSaving = 0,
            TotalBenefit = 0
        };

        await _dbContext.DRResponseSummaries.AddAsync(summary);
        await _dbContext.SaveChangesAsync();

        return summary;
    }

    public async Task<DRExecutionLog> RecordExecutionLogAsync(string requestId, decimal baselineLoad, decimal actualLoad)
    {
        var request = await _dbContext.DemandResponseRequests.FindAsync(requestId);
        if (request == null)
            throw new KeyNotFoundException($"Request {requestId} not found");

        var achievedReduction = baselineLoad - actualLoad;
        var targetReduction = request.RequestedLoadReduction;
        var complianceRate = targetReduction > 0
            ? Math.Min(1.5m, achievedReduction / targetReduction)
            : achievedReduction > 0 ? 1.0m : 0m;

        var chillerOutputLimit = _activeRequests.Any()
            ? _activeRequests.Values.Min(r => r.ChillerOutputLimit)
            : 1.0m;
        var iceMeltingRateApplied = _activeRequests.Any()
            ? _activeRequests.Values.Max(r => r.IceMeltingRate)
            : 0m;

        var electricitySaved = achievedReduction / 4.0m;
        var incentiveEarned = electricitySaved * request.IncentivePerKWh;
        var price = _iceStorageModule != null
            ? await _iceStorageModule.GetElectricityPriceForHourAsync(DateTime.Now.Hour)
            : 0.84m;
        var costSaving = electricitySaved * price;
        var totalBenefit = costSaving + incentiveEarned;

        var log = new DRExecutionLog
        {
            DRRequestId = requestId,
            Timestamp = DateTime.UtcNow,
            BaselineLoad = baselineLoad,
            ActualLoad = actualLoad,
            AchievedReduction = achievedReduction,
            TargetReduction = targetReduction,
            ChillerOutputLimit = chillerOutputLimit,
            IceMeltingRateApplied = iceMeltingRateApplied,
            ElectricitySaved = electricitySaved,
            IncentiveEarned = incentiveEarned,
            CostSaving = costSaving,
            TotalBenefit = totalBenefit
        };

        await _dbContext.DRExecutionLogs.AddAsync(log);
        await UpdateSummaryAsync(requestId);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "需求响应执行记录: {RequestId}, 基准: {Baseline}kW, 实际: {Actual}kW, 减载: {Reduction}kW, 达标率: {Compliance:P0}, 收益: {Benefit:C}",
            requestId, baselineLoad, actualLoad, achievedReduction, complianceRate, totalBenefit);

        return log;
    }

    public async Task<DRResponseSummary> CompleteResponseAsync(string requestId, int satisfactionScore)
    {
        var request = await _dbContext.DemandResponseRequests.FindAsync(requestId);
        if (request == null)
            throw new KeyNotFoundException($"Request {requestId} not found");

        var summary = await _dbContext.DRResponseSummaries
            .FirstOrDefaultAsync(s => s.DRRequestId == requestId);

        if (summary == null)
            throw new KeyNotFoundException($"Summary for request {requestId} not found");

        _activeRequests.TryRemove(requestId, out _);
        await RecalculateAggregatedLimitsAsync();

        request.Status = DRRequestStatus.Completed;
        summary.UserSatisfactionScore = satisfactionScore;
        summary.CompletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "需求响应完成: {Id}, 总减载: {Reduction}kWh, 总收益: {Benefit:C}, 满意度: {Score}/10",
            requestId, summary.TotalActualReduction, summary.TotalBenefit, satisfactionScore);

        return summary;
    }

    public Task<decimal> GetCurrentChillerOutputLimitAsync()
    {
        if (!_activeRequests.Any())
            return Task.FromResult(1.0m);
        return Task.FromResult(_activeRequests.Values.Min(r => r.ChillerOutputLimit));
    }

    public Task<decimal> GetCurrentIceMeltingRateAsync()
    {
        if (!_activeRequests.Any())
            return Task.FromResult(0m);
        return Task.FromResult(_activeRequests.Values.Max(r => r.IceMeltingRate));
    }

    private async Task UpdateSummaryAsync(string requestId)
    {
        var logs = await _dbContext.DRExecutionLogs
            .Where(l => l.DRRequestId == requestId)
            .ToListAsync();

        var summary = await _dbContext.DRResponseSummaries
            .FirstOrDefaultAsync(s => s.DRRequestId == requestId);

        if (summary == null || !logs.Any()) return;

        summary.TotalActualReduction = logs.Sum(l => l.AchievedReduction);
        summary.AverageComplianceRate = logs.Average(l =>
            l.TargetReduction > 0
                ? Math.Min(1.5m, l.AchievedReduction / l.TargetReduction)
                : l.AchievedReduction > 0 ? 1.0m : 0m);
        summary.TotalElectricitySaved = logs.Sum(l => l.ElectricitySaved);
        summary.TotalIncentiveEarned = logs.Sum(l => l.IncentiveEarned);
        summary.TotalCostSaving = logs.Sum(l => l.CostSaving);
        summary.TotalBenefit = logs.Sum(l => l.TotalBenefit);
    }
}
