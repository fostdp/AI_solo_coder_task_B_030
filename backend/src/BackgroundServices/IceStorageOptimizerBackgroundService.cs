using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Hubs;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.BackgroundServices;

public class DPCalculationTask
{
    public string TaskId { get; set; } = string.Empty;
    public DateTime ScheduleDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DPCalculationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}

public enum DPCalculationStatus
{
    Pending,
    Calculating,
    Completed,
    Failed,
    Cancelled
}

public class DPCalculationState
{
    public string TaskId { get; set; } = string.Empty;
    public DateTime ScheduleDate { get; set; }
    public DPCalculationStatus Status { get; set; }
    public int CurrentHour { get; set; }
    public int TotalHours { get; set; }
    public int StatesProcessed { get; set; }
    public decimal? CurrentCost { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class IceStorageOptimizerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<RealtimeHub, IRealtimeClient> _hubContext;
    private readonly ILogger<IceStorageOptimizerBackgroundService> _logger;
    
    private readonly Channel<DPCalculationTask> _taskChannel;
    private readonly ConcurrentDictionary<string, DPCalculationTask> _activeTasks;
    private readonly ConcurrentDictionary<string, DPCalculationState> _calculationStates;
    
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly int _maxConcurrentCalculations = 1;

    public IceStorageOptimizerBackgroundService(
        IServiceProvider serviceProvider,
        IHubContext<RealtimeHub, IRealtimeClient> hubContext,
        ILogger<IceStorageOptimizerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
        
        _taskChannel = Channel.CreateUnbounded<DPCalculationTask>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        
        _activeTasks = new ConcurrentDictionary<string, DPCalculationTask>();
        _calculationStates = new ConcurrentDictionary<string, DPCalculationState>();
    }

    public async Task<string> QueueCalculationAsync(DateTime scheduleDate)
    {
        var taskId = $"DP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        
        var existingTask = _activeTasks.Values
            .FirstOrDefault(t => t.ScheduleDate.Date == scheduleDate.Date 
                && (t.Status == DPCalculationStatus.Pending || t.Status == DPCalculationStatus.Calculating));
        
        if (existingTask != null)
        {
            _logger.LogInformation("该日期的DP计算已在队列中: {Date}, 任务ID: {TaskId}", 
                scheduleDate.Date, existingTask.TaskId);
            return existingTask.TaskId;
        }

        var task = new DPCalculationTask
        {
            TaskId = taskId,
            ScheduleDate = scheduleDate.Date,
            CreatedAt = DateTime.UtcNow,
            Status = DPCalculationStatus.Pending,
            CancellationTokenSource = new CancellationTokenSource()
        };

        _activeTasks[taskId] = task;
        _calculationStates[taskId] = new DPCalculationState
        {
            TaskId = taskId,
            ScheduleDate = scheduleDate.Date,
            Status = DPCalculationStatus.Pending,
            TotalHours = 24
        };

        await _taskChannel.Writer.WriteAsync(task);
        
        _logger.LogInformation("DP计算任务已入队: {TaskId}, 日期: {Date}", taskId, scheduleDate.Date);
        
        await NotifyStatusUpdateAsync(taskId);
        
        return taskId;
    }

    public bool CancelCalculation(string taskId, string reason)
    {
        if (!_activeTasks.TryGetValue(taskId, out var task))
        {
            _logger.LogWarning("取消计算失败: 任务不存在 {TaskId}", taskId);
            return false;
        }

        if (task.Status == DPCalculationStatus.Completed || 
            task.Status == DPCalculationStatus.Failed ||
            task.Status == DPCalculationStatus.Cancelled)
        {
            _logger.LogWarning("取消计算失败: 任务已处于终态 {TaskId}, 状态: {Status}", 
                taskId, task.Status);
            return false;
        }

        task.CancellationTokenSource?.Cancel();
        task.Status = DPCalculationStatus.Cancelled;
        task.ErrorMessage = reason;

        if (_calculationStates.TryGetValue(taskId, out var state))
        {
            state.Status = DPCalculationStatus.Cancelled;
            state.ErrorMessage = reason;
        }

        _logger.LogInformation("DP计算任务已取消: {TaskId}, 原因: {Reason}", taskId, reason);
        
        _ = NotifyStatusUpdateAsync(taskId);
        
        return true;
    }

    public DPCalculationState? GetCalculationState(string taskId)
    {
        _calculationStates.TryGetValue(taskId, out var state);
        return state;
    }

    public List<DPCalculationState> GetActiveCalculations()
    {
        return _calculationStates.Values
            .Where(s => s.Status == DPCalculationStatus.Pending || 
                       s.Status == DPCalculationStatus.Calculating)
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("冰蓄冷DP计算后台服务已启动");

        await ProcessTaskQueueAsync(stoppingToken);
        
        _logger.LogInformation("冰蓄冷DP计算后台服务已停止");
    }

    private async Task ProcessTaskQueueAsync(CancellationToken stoppingToken)
    {
        var semaphore = new SemaphoreSlim(_maxConcurrentCalculations);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var task = await _taskChannel.Reader.ReadAsync(stoppingToken);
                
                if (task.Status == DPCalculationStatus.Cancelled)
                {
                    _logger.LogInformation("跳过已取消的任务: {TaskId}", task.TaskId);
                    _activeTasks.TryRemove(task.TaskId, out _);
                    continue;
                }

                await semaphore.WaitAsync(stoppingToken);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessSingleTaskAsync(task, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理DP计算任务时发生未处理异常: {TaskId}", task.TaskId);
                        UpdateTaskState(task.TaskId, DPCalculationStatus.Failed, ex.Message);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DP计算队列处理已取消");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从队列读取任务时出错");
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessSingleTaskAsync(DPCalculationTask task, CancellationToken stoppingToken)
    {
        var taskId = task.TaskId;
        var scheduleDate = task.ScheduleDate;
        
        _logger.LogInformation("开始处理DP计算任务: {TaskId}, 日期: {Date}", taskId, scheduleDate.Date);

        UpdateTaskState(taskId, DPCalculationStatus.Calculating, null);
        
        var progress = new Progress<DPProgress>(progress =>
        {
            UpdateCalculationProgress(taskId, progress);
        });

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var optimizer = scope.ServiceProvider.GetRequiredService<IIceStorageOptimizer>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, 
                task.CancellationTokenSource?.Token ?? default);

            var result = await optimizer.CalculateOptimalStrategyAsync(scheduleDate);
            
            if (task.CancellationTokenSource?.IsCancellationRequested == true)
            {
                _logger.LogInformation("DP计算任务已被取消: {TaskId}", taskId);
                UpdateTaskState(taskId, DPCalculationStatus.Cancelled, "用户取消");
                return;
            }

            await UpdateDatabaseAsync(dbContext, result, taskId);
            
            UpdateTaskState(taskId, DPCalculationStatus.Completed, null);
            
            await NotifyCalculationCompleteAsync(taskId, result);
            
            _logger.LogInformation(
                "DP计算任务完成: {TaskId}, 日期: {Date}, 最优成本: {Cost:C}, 节省: {Saving:C}, 耗时: {Ms}ms",
                taskId, scheduleDate.Date, result.OptimalCost, result.TotalSaving, result.ComputationTimeMs);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DP计算任务已被取消: {TaskId}", taskId);
            UpdateTaskState(taskId, DPCalculationStatus.Cancelled, "操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DP计算任务失败: {TaskId}", taskId);
            UpdateTaskState(taskId, DPCalculationStatus.Failed, ex.Message);
            await NotifyCalculationFailedAsync(taskId, ex.Message);
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            task.CancellationTokenSource?.Dispose();
        }
    }

    private void UpdateTaskState(string taskId, DPCalculationStatus status, string? errorMessage)
    {
        if (_calculationStates.TryGetValue(taskId, out var state))
        {
            state.Status = status;
            
            if (status == DPCalculationStatus.Calculating)
            {
                state.StartedAt = DateTime.UtcNow;
            }
            else if (status == DPCalculationStatus.Completed || 
                     status == DPCalculationStatus.Failed ||
                     status == DPCalculationStatus.Cancelled)
            {
                state.CompletedAt = DateTime.UtcNow;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                state.ErrorMessage = errorMessage;
            }
        }

        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.Status = status;
            task.ErrorMessage = errorMessage;
        }

        _ = NotifyStatusUpdateAsync(taskId);
    }

    private void UpdateCalculationProgress(string taskId, DPProgress progress)
    {
        if (_calculationStates.TryGetValue(taskId, out var state))
        {
            state.CurrentHour = progress.CurrentHour;
            state.StatesProcessed = progress.StatesProcessed;
        }

        _ = NotifyProgressUpdateAsync(taskId, progress);
    }

    private async Task UpdateDatabaseAsync(AppDbContext dbContext, DPResult result, string taskId)
    {
        var record = await dbContext.DPStrategyRecords
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(r => r.ScheduleDate == result.ScheduleDate.Date);

        if (record != null)
        {
            record.BackgroundTaskId = taskId;
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task NotifyStatusUpdateAsync(string taskId)
    {
        if (_calculationStates.TryGetValue(taskId, out var state))
        {
            await _hubContext.Clients.All.ReceiveDPCalculationStatus(state);
        }
    }

    private async Task NotifyProgressUpdateAsync(string taskId, DPProgress progress)
    {
        await _hubContext.Clients.All.ReceiveDPCalculationProgress(taskId, progress);
    }

    private async Task NotifyCalculationCompleteAsync(string taskId, DPResult result)
    {
        await _hubContext.Clients.All.ReceiveDPCalculationComplete(taskId, result);
    }

    private async Task NotifyCalculationFailedAsync(string taskId, string errorMessage)
    {
        await _hubContext.Clients.All.ReceiveDPCalculationFailed(taskId, errorMessage);
    }

    public override void Dispose()
    {
        foreach (var task in _activeTasks.Values)
        {
            task.CancellationTokenSource?.Cancel();
            task.CancellationTokenSource?.Dispose();
        }
        
        _taskChannel.Writer.Complete();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
