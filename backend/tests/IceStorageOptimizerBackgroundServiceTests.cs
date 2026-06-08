using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using ChillerPlantOptimization.BackgroundServices;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Hubs;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

public class IceStorageOptimizerBackgroundService_Tests : TestBase
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IHubContext<RealtimeHub, IRealtimeClient>> _mockHubContext;
    private readonly Mock<IRealtimeClient> _mockClient;
    private readonly Mock<ILogger<IceStorageOptimizerBackgroundService>> _mockLogger;
    private readonly Mock<IIceStorageOptimizer> _mockOptimizer;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly IceStorageOptimizerBackgroundService _backgroundService;

    public IceStorageOptimizerBackgroundService_Tests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockHubContext = new Mock<IHubContext<RealtimeHub, IRealtimeClient>>();
        _mockClient = new Mock<IRealtimeClient>();
        _mockLogger = CreateMockLogger<IceStorageOptimizerBackgroundService>();
        _mockOptimizer = new Mock<IIceStorageOptimizer>();
        _mockScope = new Mock<IServiceScope>();

        _mockHubContext.Setup(h => h.Clients.All).Returns(_mockClient.Object);
        _mockServiceProvider.Setup(s => s.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider.GetService(typeof(IIceStorageOptimizer))).Returns(_mockOptimizer.Object);
        _mockScope.Setup(s => s.ServiceProvider.GetService(typeof(AppDbContext))).Returns(_dbContext);

        _backgroundService = new IceStorageOptimizerBackgroundService(
            _mockServiceProvider.Object,
            _mockHubContext.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// 测试任务队列按顺序处理。
    /// 验证点：多个任务入队后应该按入队顺序依次处理。
    /// </summary>
    [Fact]
    public async Task TaskQueue_ShouldProcessInOrder()
    {
        // Arrange
        var processingOrder = new ConcurrentBag<string>();
        var cts = new CancellationTokenSource();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m })
            .Callback<DateTime>(d =>
            {
                processingOrder.Add($"DP-{d:yyyyMMdd}");
                Thread.Sleep(50);
            });

        // Act
        var taskId1 = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        var taskId2 = await _backgroundService.QueueCalculationAsync(DateTime.Today.AddDays(1));

        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        processingOrder.Should().HaveCount(2,
            because: "应该处理了2个任务");
        processingOrder.First().Should().Contain(DateTime.Today.ToString("yyyyMMdd"),
            because: "先入队的任务应该先处理");
    }

    /// <summary>
    /// 测试取消操作停止计算。
    /// 验证点：取消正在进行的计算任务应该能够停止计算并记录原因。
    /// </summary>
    [Fact]
    public async Task Cancellation_ShouldStopInProgressCalculation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var calculationStarted = new TaskCompletionSource<bool>();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .Returns(async () =>
            {
                calculationStarted.SetResult(true);
                await Task.Delay(5000);
                return new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m };
            });

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);

        await Task.WhenAny(calculationStarted.Task, Task.Delay(1000));

        var cancelResult = _backgroundService.CancelCalculation(taskId, "用户主动取消");

        await Task.Delay(200);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        cancelResult.Should().BeTrue(
            because: "取消操作应该成功");

        var state = _backgroundService.GetCalculationState(taskId);
        state.Should().NotBeNull();
        state!.Status.Should().Be(DPCalculationStatus.Cancelled,
            because: "任务状态应该变为已取消");
        state.ErrorMessage.Should().Be("用户主动取消",
            because: "应该记录取消原因");
    }

    /// <summary>
    /// 测试失败任务被记录。
    /// 验证点：计算失败的任务应该被记录错误信息并标记为失败状态。
    /// </summary>
    [Fact]
    public async Task FailedTask_ShouldBeLogged()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var expectedException = new InvalidOperationException("模拟计算失败");

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(expectedException);

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        var state = _backgroundService.GetCalculationState(taskId);
        state.Should().NotBeNull();
        state!.Status.Should().Be(DPCalculationStatus.Failed,
            because: "任务状态应该变为失败");
        state.ErrorMessage.Should().Contain("模拟计算失败",
            because: "应该记录错误信息");
    }

    /// <summary>
    /// 测试并发请求入队。
    /// 验证点：同时发起的多个请求应该全部进入队列等待处理。
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_ShouldBeQueued()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var taskCount = 5;
        var taskIds = new List<string>();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m });

        // Act
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = taskCount
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, taskCount), parallelOptions, async (i, token) =>
        {
            var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today.AddDays(i));
            lock (taskIds)
            {
                taskIds.Add(taskId);
            }
        });

        var activeCalculations = _backgroundService.GetActiveCalculations();

        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(1000);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        taskIds.Should().HaveCount(taskCount,
            because: "应该有5个任务入队");
        activeCalculations.Should().HaveCount(taskCount,
            because: "入队后应该有5个活动任务");
    }

    /// <summary>
    /// 测试状态追踪反映进度。
    /// 验证点：计算过程中状态应该正确反映计算进度。
    /// </summary>
    [Fact]
    public async Task StateTracking_ShouldReflectProgress()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var progressReports = new ConcurrentBag<DPProgress>();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .Returns<DateTime>(async date =>
            {
                for (int i = 0; i < 5; i++)
                {
                    progressReports.Add(new DPProgress { CurrentHour = i, StatesProcessed = i * 100 });
                    await Task.Delay(50);
                }
                return new DPResult { ScheduleDate = date, OptimalCost = 1000m, ComputationTimeMs = 500 };
            });

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);

        var stateDuring = _backgroundService.GetCalculationState(taskId);

        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        var stateAfter = _backgroundService.GetCalculationState(taskId);

        // Assert
        stateDuring.Should().NotBeNull();
        stateDuring!.Status.Should().Be(DPCalculationStatus.Calculating,
            because: "计算过程中状态应该为计算中");
        stateDuring.TotalHours.Should().Be(24,
            because: "总小时数应该是24");
        stateDuring.StartedAt.Should().NotBeNull(
            because: "应该记录开始时间");

        stateAfter.Should().NotBeNull();
        stateAfter!.Status.Should().Be(DPCalculationStatus.Completed,
            because: "计算完成后状态应该为已完成");
        stateAfter.CompletedAt.Should().NotBeNull(
            because: "应该记录完成时间");
    }

    /// <summary>
    /// 测试同一日期重复入队返回现有任务。
    /// 验证点：同一日期的计算任务不应该重复入队。
    /// </summary>
    [Fact]
    public async Task SameDate_ShouldReturnExistingTask()
    {
        // Arrange
        var scheduleDate = DateTime.Today;

        // Act
        var taskId1 = await _backgroundService.QueueCalculationAsync(scheduleDate);
        var taskId2 = await _backgroundService.QueueCalculationAsync(scheduleDate);

        // Assert
        taskId1.Should().Be(taskId2,
            because: "同一日期的重复请求应该返回同一个任务ID");
    }

    /// <summary>
    /// 测试取消不存在的任务返回false。
    /// 验证点：取消不存在的任务ID应该返回false。
    /// </summary>
    [Fact]
    public void CancelCalculation_NonExistentTask_ShouldReturnFalse()
    {
        // Act
        var result = _backgroundService.CancelCalculation("NON_EXISTENT_TASK", "测试原因");

        // Assert
        result.Should().BeFalse(
            because: "取消不存在的任务应该返回false");
    }

    /// <summary>
    /// 测试取消已完成的任务返回false。
    /// 验证点：取消已处于终态的任务应该返回false。
    /// </summary>
    [Fact]
    public async Task CancelCalculation_CompletedTask_ShouldReturnFalse()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m });

        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(300);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Act
        var result = _backgroundService.CancelCalculation(taskId, "测试原因");

        // Assert
        result.Should().BeFalse(
            because: "取消已完成的任务应该返回false");
    }

    /// <summary>
    /// 测试获取不存在的任务状态返回null。
    /// 验证点：查询不存在的任务ID应该返回null。
    /// </summary>
    [Fact]
    public void GetCalculationState_NonExistentTask_ShouldReturnNull()
    {
        // Act
        var state = _backgroundService.GetCalculationState("NON_EXISTENT_TASK");

        // Assert
        state.Should().BeNull(
            because: "不存在的任务状态应该为null");
    }

    /// <summary>
    /// 测试活动计算列表正确过滤。
    /// 验证点：GetActiveCalculations应该只返回待处理和计算中的任务。
    /// </summary>
    [Fact]
    public async Task GetActiveCalculations_ShouldFilterCorrectly()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m });

        // Act
        await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.QueueCalculationAsync(DateTime.Today.AddDays(1));
        await _backgroundService.QueueCalculationAsync(DateTime.Today.AddDays(2));

        var activeBefore = _backgroundService.GetActiveCalculations();

        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(800);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        var activeAfter = _backgroundService.GetActiveCalculations();

        // Assert
        activeBefore.Should().HaveCount(3,
            because: "入队后应该有3个活动任务");
        activeAfter.Should().BeEmpty(
            because: "全部完成后应该没有活动任务");
    }

    /// <summary>
    /// 测试任务状态通知被发送。
    /// 验证点：任务状态变化时应该通过SignalR发送通知。
    /// </summary>
    [Fact]
    public async Task StatusUpdates_ShouldBeSentViaSignalR()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var notificationCount = 0;

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m });

        _mockClient.Setup(c => c.ReceiveDPCalculationStatus(It.IsAny<DPCalculationState>()))
            .Callback(() => Interlocked.Increment(ref notificationCount));

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        notificationCount.Should().BeGreaterThan(0,
            because: "应该发送至少一个状态通知");
    }

    /// <summary>
    /// 测试计算完成通知被发送。
    /// 验证点：计算完成时应该通过SignalR发送完成通知。
    /// </summary>
    [Fact]
    public async Task CalculationComplete_ShouldSendCompletionNotification()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        DPResult? receivedResult = null;

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DPResult { ScheduleDate = DateTime.Today, OptimalCost = 1000m, TotalSaving = 200m });

        _mockClient.Setup(c => c.ReceiveDPCalculationComplete(It.IsAny<string>(), It.IsAny<DPResult>()))
            .Callback<string, DPResult>((id, result) =>
            {
                receivedResult = result;
            });

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        receivedResult.Should().NotBeNull(
            because: "应该收到计算完成通知");
        receivedResult!.OptimalCost.Should().Be(1000m,
            because: "计算结果应该正确");
        receivedResult.TotalSaving.Should().Be(200m,
            because: "节省金额应该正确");
    }

    /// <summary>
    /// 测试计算失败通知被发送。
    /// 验证点：计算失败时应该通过SignalR发送失败通知。
    /// </summary>
    [Fact]
    public async Task CalculationFailed_ShouldSendFailureNotification()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        string? receivedError = null;
        string? receivedTaskId = null;

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("模拟失败"));

        _mockClient.Setup(c => c.ReceiveDPCalculationFailed(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((id, error) =>
            {
                receivedTaskId = id;
                receivedError = error;
            });

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);
        await Task.Delay(500);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        receivedTaskId.Should().Be(taskId,
            because: "失败通知应该包含正确的任务ID");
        receivedError.Should().Contain("模拟失败",
            because: "失败通知应该包含错误信息");
    }

    /// <summary>
    /// 测试任务ID生成唯一性。
    /// 验证点：不同日期的任务应该生成唯一的任务ID。
    /// </summary>
    [Fact]
    public async Task TaskIds_ShouldBeUnique()
    {
        // Arrange
        var taskIds = new List<string>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today.AddDays(i));
            taskIds.Add(taskId);
        }

        // Assert
        taskIds.Distinct().Should().HaveCount(10,
            because: "所有任务ID应该唯一");
        taskIds.Should().AllSatisfy(id => id.Should().StartWith("DP-",
            because: "任务ID应该以DP-开头"));
    }

    /// <summary>
    /// 测试空原因取消任务。
    /// 验证点：空的取消原因也应该被记录。
    /// </summary>
    [Fact]
    public async Task CancelCalculation_EmptyReason_ShouldBeRecorded()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var calculationStarted = new TaskCompletionSource<bool>();

        _mockOptimizer.Setup(o => o.CalculateOptimalStrategyAsync(It.IsAny<DateTime>()))
            .Returns(async () =>
            {
                calculationStarted.SetResult(true);
                await Task.Delay(5000);
                return new DPResult();
            });

        // Act
        var taskId = await _backgroundService.QueueCalculationAsync(DateTime.Today);
        await _backgroundService.StartAsync(cts.Token);

        await Task.WhenAny(calculationStarted.Task, Task.Delay(1000));

        var cancelResult = _backgroundService.CancelCalculation(taskId, string.Empty);

        await Task.Delay(200);
        await _backgroundService.StopAsync(cts.Token);
        cts.Cancel();

        // Assert
        cancelResult.Should().BeTrue(
            because: "取消操作应该成功");

        var state = _backgroundService.GetCalculationState(taskId);
        state.Should().NotBeNull();
        state!.Status.Should().Be(DPCalculationStatus.Cancelled,
            because: "任务状态应该变为已取消");
    }
}
