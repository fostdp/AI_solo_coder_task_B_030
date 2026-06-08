using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;

namespace ChillerPlantOptimization.Modules.DemandResponse;

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
    private readonly object _currentLimitsLock = new();

    private decimal _currentChillerOutputLimit = 1.0m;
    private decimal _currentIceMeltingRate = 0m;
    private string? _activeRequestId;

    public DemandResponseModule(
        AppDbContext dbContext,
        ILogger<DemandResponseModule> logger,
        IIceStorageModule? iceStorageModule = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _iceStorageModule = iceStorageModule;
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

        lock (_currentLimitsLock)
        {
            _currentChillerOutputLimit = request.MaxChillerOutputLimit ?? 1.0m;
            _currentIceMeltingRate = request.MinIceMeltingRate ?? 0m;
            _activeRequestId = requestId;
        }

        request.Status = DRRequestStatus.Executing;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "开始执行需求响应: {Id}, 主机出力上限: {Limit:P0}, 融冰速率: {Rate}kW",
            requestId, _currentChillerOutputLimit, _currentIceMeltingRate);

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

        decimal chillerOutputLimit;
        decimal iceMeltingRateApplied;
        lock (_currentLimitsLock)
        {
            chillerOutputLimit = _currentChillerOutputLimit;
            iceMeltingRateApplied = _currentIceMeltingRate;
        }

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

        lock (_currentLimitsLock)
        {
            _currentChillerOutputLimit = 1.0m;
            _currentIceMeltingRate = 0m;
            if (_activeRequestId == requestId)
                _activeRequestId = null;
        }

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
        lock (_currentLimitsLock)
        {
            return Task.FromResult(_currentChillerOutputLimit);
        }
    }

    public Task<decimal> GetCurrentIceMeltingRateAsync()
    {
        lock (_currentLimitsLock)
        {
            return Task.FromResult(_currentIceMeltingRate);
        }
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
