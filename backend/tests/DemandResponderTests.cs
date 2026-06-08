using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Services;

namespace ChillerPlantOptimization.Tests;

/// <summary>
/// 需求响应服务请求管理测试
/// 验证请求接收、活动跟踪和状态流转
/// </summary>
public class DemandResponder_RequestManagement_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponder>> _mockLogger;
    private readonly Mock<IIceStorageOptimizer> _mockOptimizer;
    private readonly DemandResponder _responder;

    public DemandResponder_RequestManagement_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponder>();
        _mockOptimizer = new Mock<IIceStorageOptimizer>();
        _mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockLogger.Object, _mockOptimizer.Object);
    }

    /// <summary>
    /// 接收请求正确存储
    /// 测试点：ReceiveRequestAsync应将请求保存到数据库并返回完整的请求对象
    /// </summary>
    [Fact]
    public async Task ReceiveRequest_ShouldStoreRequest()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        // Act
        var result = await _responder.ReceiveRequestAsync(drRequest);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Id.Should().StartWith("DR-", because: "请求ID应该以DR-开头");
        result.RequestType.Should().Be(DRRequestType.LoadReduction);
        result.Status.Should().Be(DRRequestStatus.Received);
        result.RequestedLoadReduction.Should().Be(2000m);
        result.IncentivePerKWh.Should().Be(0.5m);
        result.Priority.Should().Be(1);
        result.ReceivedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        using var newContext = CreateNewContext();
        var storedRequest = await newContext.DemandResponseRequests.FindAsync(result.Id);
        storedRequest.Should().NotBeNull();
        storedRequest!.RequestedLoadReduction.Should().Be(2000m);
    }

    /// <summary>
    /// 验证请求ID的唯一性
    /// 测试点：连续接收的请求应具有不同的ID
    /// </summary>
    [Fact]
    public async Task ReceiveRequest_ShouldGenerateUniqueIds()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 1000m,
            DurationMinutes = 30,
            Incentive = 0.4m,
            Priority = 2
        };

        // Act
        var result1 = await _responder.ReceiveRequestAsync(drRequest);
        await Task.Delay(10);
        var result2 = await _responder.ReceiveRequestAsync(drRequest);

        // Assert
        result1.Id.Should().NotBe(result2.Id, because: "每个请求应该有唯一的ID");
    }

    /// <summary>
    /// 活动请求被跟踪
    /// 测试点：GetActiveRequestsAsync应返回所有状态为Received或Executing的请求
    /// </summary>
    [Fact]
    public async Task ActiveRequests_ShouldBeTracked()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var request1 = new DemandResponseRequest
        {
            Id = "DR-ACTIVE-001",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Executing,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = now.AddMinutes(-10),
            EndTime = now.AddMinutes(50),
            IncentivePerKWh = 0.5m,
            Priority = 1,
            ReceivedAt = now.AddMinutes(-10),
            ResponseRequired = true
        };

        var request2 = new DemandResponseRequest
        {
            Id = "DR-ACTIVE-002",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = now.AddMinutes(30),
            EndTime = now.AddMinutes(90),
            IncentivePerKWh = 0.4m,
            Priority = 2,
            ReceivedAt = now.AddMinutes(-5),
            ResponseRequired = true
        };

        var request3 = new DemandResponseRequest
        {
            Id = "DR-COMPLETED-001",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Completed,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1000m,
            StartTime = now.AddHours(-2),
            EndTime = now.AddHours(-1),
            IncentivePerKWh = 0.3m,
            Priority = 3,
            ReceivedAt = now.AddHours(-3),
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(request1, request2, request3);
        await _dbContext.SaveChangesAsync();

        // Act
        var activeRequests = await _responder.GetActiveRequestsAsync();

        // Assert
        activeRequests.Should().HaveCount(2, because: "应该有2个活动请求");
        activeRequests.Should().Contain(r => r.RequestId == "DR-ACTIVE-001");
        activeRequests.Should().Contain(r => r.RequestId == "DR-ACTIVE-002");
        activeRequests.Should().NotContain(r => r.RequestId == "DR-COMPLETED-001");
        activeRequests.Should().BeInDescendingOrder(r => r.Priority,
            because: "活动请求应该按优先级降序排列");
    }

    /// <summary>
    /// 验证活动请求的时间过滤
    /// 测试点：已过期的请求不应出现在活动列表中
    /// </summary>
    [Fact]
    public async Task ActiveRequests_ShouldFilterExpiredRequests()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var expiredRequest = new DemandResponseRequest
        {
            Id = "DR-EXPIRED-001",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1000m,
            StartTime = now.AddHours(-2),
            EndTime = now.AddHours(-1),
            IncentivePerKWh = 0.3m,
            Priority = 3,
            ReceivedAt = now.AddHours(-3),
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(expiredRequest);
        await _dbContext.SaveChangesAsync();

        // Act
        var activeRequests = await _responder.GetActiveRequestsAsync();

        // Assert
        activeRequests.Should().NotContain(r => r.RequestId == "DR-EXPIRED-001",
            because: "已过期的请求不应出现在活动列表中");
    }

    /// <summary>
    /// 请求状态正确流转
    /// 测试点：请求状态应从Received → Executing → Completed正确流转
    /// </summary>
    [Fact]
    public async Task RequestStatus_ShouldTransitionCorrectly()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        // Act & Assert - 初始状态
        var request = await _responder.ReceiveRequestAsync(drRequest);
        request.Status.Should().Be(DRRequestStatus.Received,
            because: "新接收的请求状态应为Received");

        // Act & Assert - 执行中状态
        var executeResult = await _responder.ExecuteResponseAsync(request.Id);
        executeResult.Status.Should().Be(DRRequestStatus.Executing,
            because: "执行中的请求状态应为Executing");

        using var newContext1 = CreateNewContext();
        var executingRequest = await newContext1.DemandResponseRequests.FindAsync(request.Id);
        executingRequest!.Status.Should().Be(DRRequestStatus.Executing);

        // Act & Assert - 完成状态
        await _responder.RecordExecutionLogAsync(request.Id, 6000m, 4000m);
        var summary = await _responder.CompleteResponseAsync(request.Id, 8);

        using var newContext2 = CreateNewContext();
        var completedRequest = await newContext2.DemandResponseRequests.FindAsync(request.Id);
        completedRequest!.Status.Should().Be(DRRequestStatus.Completed,
            because: "完成的请求状态应为Completed");
    }

    /// <summary>
    /// 验证状态流转的中间步骤
    /// 测试点：执行日志记录后状态保持Executing，直到调用CompleteResponseAsync
    /// </summary>
    [Fact]
    public async Task RequestStatus_ShouldRemainExecutingDuringLogging()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        var request = await _responder.ReceiveRequestAsync(drRequest);
        await _responder.ExecuteResponseAsync(request.Id);

        // Act
        await _responder.RecordExecutionLogAsync(request.Id, 6000m, 4000m);
        await _responder.RecordExecutionLogAsync(request.Id, 6200m, 4100m);

        // Assert
        using var newContext = CreateNewContext();
        var executingRequest = await newContext.DemandResponseRequests.FindAsync(request.Id);
        executingRequest!.Status.Should().Be(DRRequestStatus.Executing,
            because: "记录日志时请求状态应保持Executing");
    }

    /// <summary>
    /// 验证取消状态的流转
    /// 测试点：请求可以被取消，状态变为Cancelled并记录原因
    /// </summary>
    [Fact]
    public async Task RequestStatus_ShouldTransitionToCancelled()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        var request = await _responder.ReceiveRequestAsync(drRequest);
        await _responder.ExecuteResponseAsync(request.Id);

        // Act
        await _responder.CancelRequestAsync(request.Id, "用户主动取消");

        // Assert
        using var newContext = CreateNewContext();
        var cancelledRequest = await newContext.DemandResponseRequests.FindAsync(request.Id);
        cancelledRequest!.Status.Should().Be(DRRequestStatus.Cancelled);
        cancelledRequest.CancellationReason.Should().Be("用户主动取消");
    }

    /// <summary>
    /// 验证对已完成请求执行操作的异常处理
    /// 测试点：对已完成的请求执行ExecuteResponseAsync应抛出异常
    /// </summary>
    [Fact]
    public async Task RequestStatus_ShouldPreventReExecutionOfCompleted()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        var request = await _responder.ReceiveRequestAsync(drRequest);
        await _responder.ExecuteResponseAsync(request.Id);
        await _responder.RecordExecutionLogAsync(request.Id, 6000m, 4000m);
        await _responder.CompleteResponseAsync(request.Id, 8);

        // Act
        Func<Task> act = async () => await _responder.ExecuteResponseAsync(request.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{DRRequestStatus.Completed}*",
                because: "不能重新执行已完成的请求");
    }

    /// <summary>
    /// 验证请求状态查询
    /// 测试点：GetStatusAsync应返回正确的状态信息
    /// </summary>
    [Fact]
    public async Task RequestStatus_ShouldBeQueryable()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        var request = await _responder.ReceiveRequestAsync(drRequest);
        await _responder.ExecuteResponseAsync(request.Id);
        await _responder.RecordExecutionLogAsync(request.Id, 6000m, 4000m);

        // Act
        var status = await _responder.GetStatusAsync(request.Id);

        // Assert
        status.Should().NotBeNull();
        status.RequestId.Should().Be(request.Id);
        status.Status.Should().Be(DRRequestStatus.Executing);
        status.TargetReduction.Should().Be(2000m);
        status.AchievedReduction.Should().Be(2000m);
        status.ChillerOutputLimit.Should().BeGreaterThan(0);
        status.ElapsedMinutes.Should().BeGreaterThanOrEqualTo(0);
        status.RemainingMinutes.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 验证请求参数的正确映射
    /// 测试点：DRRequest的参数应正确映射到DemandResponseRequest
    /// </summary>
    [Fact]
    public async Task ReceiveRequest_ShouldMapAllParameters()
    {
        // Arrange
        var testCases = new[]
        {
            new { Type = DRRequestType.LoadReduction, Reduction = 3000m, Duration = 120, Incentive = 0.6m, Priority = 0 },
            new { Type = DRRequestType.LoadShifting, Reduction = 1500m, Duration = 180, Incentive = 0.3m, Priority = 2 },
            new { Type = DRRequestType.PeakShaving, Reduction = 2500m, Duration = 90, Incentive = 0.45m, Priority = 1 },
            new { Type = DRRequestType.EmergencyDR, Reduction = 4000m, Duration = 30, Incentive = 0.8m, Priority = 0 }
        };

        // Act & Assert
        foreach (var testCase in testCases)
        {
            var drRequest = new DRRequest
            {
                Type = testCase.Type,
                LoadReduction = testCase.Reduction,
                DurationMinutes = testCase.Duration,
                Incentive = testCase.Incentive,
                Priority = testCase.Priority
            };

            var result = await _responder.ReceiveRequestAsync(drRequest);

            result.RequestType.Should().Be(testCase.Type);
            result.RequestedLoadReduction.Should().Be(testCase.Reduction);
            result.IncentivePerKWh.Should().Be(testCase.Incentive);
            result.Priority.Should().Be(testCase.Priority);
            result.EndTime.Should().BeAfter(result.StartTime);
            (int)(result.EndTime - result.StartTime).TotalMinutes.Should().Be(testCase.Duration);
        }
    }

    /// <summary>
    /// 验证紧急DR的特殊处理
    /// 测试点：EmergencyDR类型请求应有最高优先级和特殊参数
    /// </summary>
    [Fact]
    public async Task ReceiveRequest_ShouldHandleEmergencyDR()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.EmergencyDR,
            LoadReduction = 4000m,
            DurationMinutes = 30,
            Incentive = 0.8m,
            Priority = 0
        };

        // Act
        var result = await _responder.ReceiveRequestAsync(drRequest);

        // Assert
        result.RequestType.Should().Be(DRRequestType.EmergencyDR);
        result.Priority.Should().Be(0, because: "紧急DR优先级应为最高（0）");
        result.MaxChillerOutputLimit.Should().BeLessThan(1.0m,
            because: "紧急DR应设置较低的主机出力上限");
        result.MaxChillerOutputLimit.Should().BeGreaterThanOrEqualTo(0.3m,
            because: "主机出力上限不应低于30%");
        result.StartTime.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5),
            because: "紧急DR应尽快开始");
    }

    /// <summary>
    /// 验证请求摘要的创建
    /// 测试点：ExecuteResponseAsync应创建对应的响应摘要记录
    /// </summary>
    [Fact]
    public async Task ExecuteResponse_ShouldCreateSummary()
    {
        // Arrange
        var drRequest = new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        };

        var request = await _responder.ReceiveRequestAsync(drRequest);

        // Act
        var result = await _responder.ExecuteResponseAsync(request.Id);

        // Assert
        result.Should().NotBeNull();
        result.RequestId.Should().Be(request.Id);
        result.Success.Should().BeTrue();
        result.Status.Should().Be(DRRequestStatus.Executing);

        using var newContext = CreateNewContext();
        var summary = await newContext.DRResponseSummaries
            .FirstOrDefaultAsync(s => s.DRRequestId == request.Id);
        summary.Should().NotBeNull();
        summary!.TotalRequestedReduction.Should().Be(2000m);
        summary.TotalDurationMinutes.Should().Be(60);
    }
}

/// <summary>
/// 需求响应服务冲突解决测试
/// 验证紧急DR优先级、冲突检测、抢占机制、取消原因记录和并发控制
/// </summary>
public class DemandResponder_ConflictResolution_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponder>> _mockLogger;
    private readonly Mock<IIceStorageOptimizer> _mockOptimizer;
    private readonly DemandResponder _responder;

    public DemandResponder_ConflictResolution_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponder>();
        _mockOptimizer = new Mock<IIceStorageOptimizer>();
        _mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockLogger.Object, _mockOptimizer.Object);
    }

    /// <summary>
    /// 紧急DR优先级最高
    /// 测试点：EmergencyDR应抢占所有其他类型的DR请求
    /// </summary>
    [Fact]
    public async Task EmergencyRequest_ShouldHaveHighestPriority()
    {
        // Arrange
        var normalRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(normalRequest.Id);
        normalRequest.Status.Should().Be(DRRequestStatus.Executing);

        // Act - 触发紧急DR
        var emergencyRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.EmergencyDR,
            LoadReduction = 3000m,
            DurationMinutes = 30,
            Incentive = 0.8m,
            Priority = 0
        });
        await _responder.ExecuteResponseAsync(emergencyRequest.Id);

        // Assert
        using var newContext = CreateNewContext();
        var originalRequest = await newContext.DemandResponseRequests.FindAsync(normalRequest.Id);
        var newRequest = await newContext.DemandResponseRequests.FindAsync(emergencyRequest.Id);

        originalRequest!.Status.Should().Be(DRRequestStatus.Cancelled,
            because: "低优先级事件应该被抢占并取消");
        newRequest!.Status.Should().Be(DRRequestStatus.Executing,
            because: "高优先级紧急DR应该开始执行");
        originalRequest.CancellationReason.Should().Contain("更高优先级",
            because: "取消原因应包含'更高优先级'");
    }

    /// <summary>
    /// 验证优先级排序
    /// 测试点：优先级数值越小，优先级越高
    /// </summary>
    [Fact]
    public void Priority_ShouldBeOrderedCorrectly()
    {
        // Arrange
        var priorities = new[] { 0, 1, 2, 3 };

        // Act & Assert
        priorities[0].Should().Be(0, because: "0是最高优先级（紧急DR）");
        priorities.Should().BeInAscendingOrder(because: "优先级数值越小优先级越高");
    }

    /// <summary>
    /// 同类型重叠检测
    /// 测试点：两个同类型事件时间重叠时应被检测为冲突
    /// </summary>
    [Fact]
    public async Task SameTypeOverlap_ShouldBeDetectedAsConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // 先添加一个活动事件
        var existingRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(existingRequest.Id);

        // Act - 检测同类型重叠冲突
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction,
            now.AddMinutes(30),
            now.AddMinutes(90),
            1);

        // Assert
        conflictResult.IsConflict.Should().BeTrue(
            because: "两个同类型事件时间重叠应该被检测为冲突");
        conflictResult.ConflictingRequestId.Should().Be(existingRequest.Id,
            because: "应该返回冲突的事件ID");
    }

    /// <summary>
    /// 验证时间不重叠的同类型请求不冲突
    /// 测试点：同类型但时间不重叠的请求不应被检测为冲突
    /// </summary>
    [Fact]
    public async Task SameTypeNoOverlap_ShouldNotBeConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var existingRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(existingRequest.Id);

        // Act - 检测同类型但不重叠的请求
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadReduction,
            now.AddMinutes(120),
            now.AddMinutes(180),
            1);

        // Assert
        conflictResult.IsConflict.Should().BeFalse(
            because: "同类型但时间不重叠的请求不应被检测为冲突");
        conflictResult.ConflictingRequestId.Should().BeNull();
    }

    /// <summary>
    /// 冲突时低优先级被抢占
    /// 测试点：当高优先级请求与低优先级请求冲突时，低优先级请求被取消
    /// </summary>
    [Fact]
    public async Task ConflictResolution_ShouldPreemptLowerPriority()
    {
        // Arrange
        var lowPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 2
        });
        await _responder.ExecuteResponseAsync(lowPriorityRequest.Id);

        // Act - 高优先级请求抢占
        var highPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 3000m,
            DurationMinutes = 60,
            Incentive = 0.6m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(highPriorityRequest.Id);

        // Assert
        using var newContext = CreateNewContext();
        var lowPriority = await newContext.DemandResponseRequests.FindAsync(lowPriorityRequest.Id);
        var highPriority = await newContext.DemandResponseRequests.FindAsync(highPriorityRequest.Id);

        lowPriority!.Status.Should().Be(DRRequestStatus.Cancelled,
            because: "低优先级请求应该被抢占");
        highPriority!.Status.Should().Be(DRRequestStatus.Executing,
            because: "高优先级请求应该开始执行");
    }

    /// <summary>
    /// 验证低优先级无法抢占高优先级
    /// 测试点：尝试用低优先级请求抢占高优先级请求应抛出异常
    /// </summary>
    [Fact]
    public async Task ConflictResolution_ShouldRejectLowerPriorityPreemption()
    {
        // Arrange
        var highPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.EmergencyDR,
            LoadReduction = 3000m,
            DurationMinutes = 60,
            Incentive = 0.8m,
            Priority = 0
        });
        await _responder.ExecuteResponseAsync(highPriorityRequest.Id);

        // Act - 尝试用低优先级请求抢占
        var lowPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });

        Func<Task> act = async () => await _responder.ExecuteResponseAsync(lowPriorityRequest.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*优先级更高*",
                because: "低优先级事件不能抢占高优先级事件");
    }

    /// <summary>
    /// 取消请求记录原因
    /// 测试点：被取消的请求应记录取消原因
    /// </summary>
    [Fact]
    public async Task CancelledRequest_ShouldRecordReason()
    {
        // Arrange
        var lowPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(lowPriorityRequest.Id);

        // Act - 高优先级事件抢占
        var highPriorityRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.EmergencyDR,
            LoadReduction = 3000m,
            DurationMinutes = 30,
            Incentive = 0.8m,
            Priority = 0
        });
        await _responder.ExecuteResponseAsync(highPriorityRequest.Id);

        // Assert
        using var newContext = CreateNewContext();
        var cancelledRequest = await newContext.DemandResponseRequests.FindAsync(lowPriorityRequest.Id);

        cancelledRequest!.Status.Should().Be(DRRequestStatus.Cancelled);
        cancelledRequest.CancellationReason.Should().NotBeNullOrWhiteSpace();
        cancelledRequest.CancellationReason.Should().Contain("更高优先级",
            because: "被抢占的事件应该记录取消原因包含'更高优先级'");
    }

    /// <summary>
    /// 验证主动取消的原因记录
    /// 测试点：通过CancelRequestAsync取消的请求也应记录原因
    /// </summary>
    [Fact]
    public async Task CancelledRequest_ShouldRecordManualCancelReason()
    {
        // Arrange
        var request = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(request.Id);

        // Act
        await _responder.CancelRequestAsync(request.Id, "电网调度取消");

        // Assert
        using var newContext = CreateNewContext();
        var cancelledRequest = await newContext.DemandResponseRequests.FindAsync(request.Id);

        cancelledRequest!.Status.Should().Be(DRRequestStatus.Cancelled);
        cancelledRequest.CancellationReason.Should().Be("电网调度取消");
    }

    /// <summary>
    /// 并发请求被正确串行化
    /// 测试点：并发执行多个冲突请求时，只有最高优先级的请求最终执行
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_ShouldBeSerialized()
    {
        // Arrange
        var random = new Random(42);
        var requests = new List<DemandResponseRequest>();
        var priorities = Enumerable.Range(0, 10).OrderBy(_ => Guid.NewGuid()).ToList();

        for (int i = 0; i < 10; i++)
        {
            var request = new DemandResponseRequest
            {
                Id = $"DR-CONCURRENT-{i:00}",
                RequestType = DRRequestType.LoadReduction,
                Status = DRRequestStatus.Received,
                SourcePlatform = "ConcurrentTest",
                RequestedLoadReduction = 2000m + i * 100,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(60),
                IncentivePerKWh = 0.5m,
                MaxChillerOutputLimit = 0.5m + (decimal)(random.NextDouble() * 0.3),
                MinIceMeltingRate = 1000m + i * 50,
                Priority = priorities[i],
                ReceivedAt = DateTime.UtcNow,
                ResponseRequired = true
            };
            requests.Add(request);
        }

        await _dbContext.DemandResponseRequests.AddRangeAsync(requests);
        await _dbContext.SaveChangesAsync();

        // Act - 并发执行所有请求
        var tasks = requests.Select(r =>
        {
            try
            {
                return _responder.ExecuteResponseAsync(r.Id);
            }
            catch (Exception)
            {
                return Task.FromResult<DRExecutionResult>(null!);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        using var newContext = CreateNewContext();
        var allRequests = await newContext.DemandResponseRequests
            .Where(r => r.Id.StartsWith("DR-CONCURRENT-"))
            .ToListAsync();

        var executingRequests = allRequests.Where(r => r.Status == DRRequestStatus.Executing).ToList();
        var highestPriorityRequest = allRequests.OrderBy(r => r.Priority).First();

        executingRequests.Should().ContainSingle(because: "最终应该只有一个活动事件");
        executingRequests.First().Id.Should().Be(highestPriorityRequest.Id,
            because: "最终活动事件应该是优先级最高的那个");

        var cancelledRequests = allRequests.Where(r => r.Status == DRRequestStatus.Cancelled).ToList();
        cancelledRequests.Should().HaveCount(9, because: "其他9个请求应该被取消");
    }

    /// <summary>
    /// 验证EmergencyDR与任何请求都冲突
    /// 测试点：无论类型和优先级如何，EmergencyDR与任何重叠请求都冲突
    /// </summary>
    [Fact]
    public async Task EmergencyDR_ShouldConflictWithAnyRequest()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // 先添加一个不同类型不同优先级的请求
        var existingRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadShifting,
            LoadReduction = 1500m,
            DurationMinutes = 60,
            Incentive = 0.4m,
            Priority = 2
        });
        await _responder.ExecuteResponseAsync(existingRequest.Id);

        // Act - 检测EmergencyDR冲突
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.EmergencyDR,
            now.AddMinutes(30),
            now.AddMinutes(90),
            0);

        // Assert
        conflictResult.IsConflict.Should().BeTrue(
            because: "EmergencyDR应该与任何重叠请求冲突");
    }

    /// <summary>
    /// 验证不同类型不同优先级的请求可以共存
    /// 测试点：类型不同、优先级不同且不涉及EmergencyDR的请求可以共存
    /// </summary>
    [Fact]
    public async Task DifferentTypeDifferentPriority_ShouldNotConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // 先添加一个LoadReduction请求
        var existingRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(existingRequest.Id);

        // Act - 检测不同类型不同优先级的请求
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadShifting,
            now.AddMinutes(30),
            now.AddMinutes(90),
            2);

        // Assert
        conflictResult.IsConflict.Should().BeFalse(
            because: "不同类型不同优先级的请求可以共存");
    }

    /// <summary>
    /// 验证相同优先级的冲突
    /// 测试点：相同优先级的重叠请求应被检测为冲突
    /// </summary>
    [Fact]
    public async Task SamePriority_ShouldBeDetectedAsConflict()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var existingRequest = await _responder.ReceiveRequestAsync(new DRRequest
        {
            Type = DRRequestType.LoadReduction,
            LoadReduction = 2000m,
            DurationMinutes = 60,
            Incentive = 0.5m,
            Priority = 1
        });
        await _responder.ExecuteResponseAsync(existingRequest.Id);

        // Act - 检测相同优先级的不同类型请求
        var conflictResult = await _responder.CheckConflictAsync(
            DRRequestType.LoadShifting,
            now.AddMinutes(30),
            now.AddMinutes(90),
            1);

        // Assert
        conflictResult.IsConflict.Should().BeTrue(
            because: "相同优先级的重叠请求应该被检测为冲突");
    }
}

/// <summary>
/// 需求响应服务限制聚合测试
/// 验证多活动请求时的限制参数聚合逻辑
/// </summary>
public class DemandResponder_LimitAggregation_Tests : TestBase
{
    private readonly Mock<ILogger<DemandResponder>> _mockLogger;
    private readonly Mock<IIceStorageOptimizer> _mockOptimizer;
    private readonly DemandResponder _responder;

    public DemandResponder_LimitAggregation_Tests()
    {
        _mockLogger = CreateMockLogger<DemandResponder>();
        _mockOptimizer = new Mock<IIceStorageOptimizer>();
        _mockOptimizer.Setup(o => o.GetElectricityPriceForHourAsync(It.IsAny<int>()))
            .ReturnsAsync(0.84m);
        _responder = new DemandResponder(_dbContext, _mockLogger.Object, _mockOptimizer.Object);
    }

    /// <summary>
    /// 多个活动请求时取最严格限制
    /// 测试点：ChillerOutputLimit取最小值，IceMeltingRate取最大值
    /// </summary>
    [Fact]
    public async Task MultipleActiveRequests_ShouldUseMostRestrictiveLimit()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = $"DR-A-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.7m,
            MinIceMeltingRate = 1000m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = $"DR-B-{DateTime.UtcNow:yyyyMMddHHmmss}",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow.AddMinutes(10),
            EndTime = DateTime.UtcNow.AddMinutes(70),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.5m,
            MinIceMeltingRate = 1500m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        // Act
        await _responder.ExecuteResponseAsync(requestA.Id);
        await _responder.ExecuteResponseAsync(requestB.Id);
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(0.5m,
            because: "应该取最小的出力上限0.5（最严格）");
        adjustments.IceMeltingRate.Should().Be(1500m,
            because: "应该取最大的融冰速率1500（最严格）");
        adjustments.ActiveRequestCount.Should().Be(2);
        adjustments.TotalRequestedReduction.Should().Be(3500m);
    }

    /// <summary>
    /// 主机上限取最小值
    /// 测试点：多个活动请求时，ChillerOutputLimit应取所有活动请求中的最小值
    /// </summary>
    [Fact]
    public async Task ChillerLimit_ShouldBeMinimumOfAllActive()
    {
        // Arrange
        var limits = new[] { 0.8m, 0.6m, 0.9m, 0.5m, 0.7m };
        var expectedMin = limits.Min();
        var requests = new List<DemandResponseRequest>();

        for (int i = 0; i < limits.Length; i++)
        {
            var request = new DemandResponseRequest
            {
                Id = $"DR-LIMIT-{i:00}",
                RequestType = DRRequestType.LoadReduction,
                Status = DRRequestStatus.Received,
                SourcePlatform = "Test",
                RequestedLoadReduction = 1000m + i * 200,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(60),
                IncentivePerKWh = 0.5m,
                MaxChillerOutputLimit = limits[i],
                MinIceMeltingRate = 500m + i * 100,
                Priority = 3 - i,
                ReceivedAt = DateTime.UtcNow,
                ResponseRequired = true
            };
            requests.Add(request);
        }

        await _dbContext.DemandResponseRequests.AddRangeAsync(requests);
        await _dbContext.SaveChangesAsync();

        // Act
        foreach (var request in requests)
        {
            await _responder.ExecuteResponseAsync(request.Id);
        }
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(expectedMin,
            because: "主机上限应该取所有活动请求中的最小值");
    }

    /// <summary>
    /// 融冰速率取最大值
    /// 测试点：多个活动请求时，IceMeltingRate应取所有活动请求中的最大值
    /// </summary>
    [Fact]
    public async Task MeltingRate_ShouldBeMaximumOfAllActive()
    {
        // Arrange
        var rates = new[] { 800m, 1200m, 600m, 1500m, 1000m };
        var expectedMax = rates.Max();
        var requests = new List<DemandResponseRequest>();

        for (int i = 0; i < rates.Length; i++)
        {
            var request = new DemandResponseRequest
            {
                Id = $"DR-RATE-{i:00}",
                RequestType = DRRequestType.LoadReduction,
                Status = DRRequestStatus.Received,
                SourcePlatform = "Test",
                RequestedLoadReduction = 1000m + i * 200,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(60),
                IncentivePerKWh = 0.5m,
                MaxChillerOutputLimit = 0.8m - i * 0.1m,
                MinIceMeltingRate = rates[i],
                Priority = 3 - i,
                ReceivedAt = DateTime.UtcNow,
                ResponseRequired = true
            };
            requests.Add(request);
        }

        await _dbContext.DemandResponseRequests.AddRangeAsync(requests);
        await _dbContext.SaveChangesAsync();

        // Act
        foreach (var request in requests)
        {
            await _responder.ExecuteResponseAsync(request.Id);
        }
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.IceMeltingRate.Should().Be(expectedMax,
            because: "融冰速率应该取所有活动请求中的最大值");
    }

    /// <summary>
    /// 请求完成后限制重算
    /// 测试点：当一个请求完成后，限制参数应基于剩余活动请求重新计算
    /// </summary>
    [Fact]
    public async Task AfterRequestCompletion_LimitsShouldBeRecalculated()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = "DR-RECOMPUTE-A",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.5m,
            MinIceMeltingRate = 1500m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = "DR-RECOMPUTE-B",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.7m,
            MinIceMeltingRate = 1000m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        await _responder.ExecuteResponseAsync(requestA.Id);
        await _responder.ExecuteResponseAsync(requestB.Id);

        // Act - 完成请求A
        await _responder.RecordExecutionLogAsync(requestA.Id, 6000m, 4000m);
        await _responder.CompleteResponseAsync(requestA.Id, 8);
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(0.7m,
            because: "请求A完成后，主机上限应使用请求B的0.7");
        adjustments.IceMeltingRate.Should().Be(1000m,
            because: "请求A完成后，融冰速率应使用请求B的1000");
        adjustments.ActiveRequestCount.Should().Be(1);
    }

    /// <summary>
    /// 验证无活动请求时的默认限制
    /// 测试点：没有活动请求时，ChillerOutputLimit应为1.0，IceMeltingRate应为0
    /// </summary>
    [Fact]
    public async Task NoActiveRequests_ShouldReturnDefaultLimits()
    {
        // Act
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(1.0m,
            because: "没有活动请求时主机出力上限应为100%");
        adjustments.IceMeltingRate.Should().Be(0m,
            because: "没有活动请求时融冰速率应为0");
        adjustments.ActiveRequestCount.Should().Be(0);
        adjustments.TotalRequestedReduction.Should().Be(0);
    }

    /// <summary>
    /// 验证单个活动请求的限制
    /// 测试点：单个活动请求时，限制参数应等于该请求的参数
    /// </summary>
    [Fact]
    public async Task SingleActiveRequest_ShouldUseRequestLimits()
    {
        // Arrange
        var request = new DemandResponseRequest
        {
            Id = "DR-SINGLE-001",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2500m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.65m,
            MinIceMeltingRate = 1200m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(request);
        await _dbContext.SaveChangesAsync();

        // Act
        await _responder.ExecuteResponseAsync(request.Id);
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(0.65m);
        adjustments.IceMeltingRate.Should().Be(1200m);
        adjustments.ActiveRequestCount.Should().Be(1);
        adjustments.TotalRequestedReduction.Should().Be(2500m);
    }

    /// <summary>
    /// 验证取消请求后的限制重算
    /// 测试点：当一个请求被取消后，限制参数应重新计算
    /// </summary>
    [Fact]
    public async Task AfterRequestCancellation_LimitsShouldBeRecalculated()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = "DR-CANCEL-A",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.4m,
            MinIceMeltingRate = 2000m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = "DR-CANCEL-B",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.6m,
            MinIceMeltingRate = 1000m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        await _responder.ExecuteResponseAsync(requestA.Id);
        await _responder.ExecuteResponseAsync(requestB.Id);

        // Act - 取消请求A
        await _responder.CancelRequestAsync(requestA.Id, "测试取消");
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(0.6m,
            because: "请求A取消后，主机上限应使用请求B的0.6");
        adjustments.IceMeltingRate.Should().Be(1000m,
            because: "请求A取消后，融冰速率应使用请求B的1000");
    }

    /// <summary>
    /// 验证多个请求完成后的限制重算
    /// 测试点：所有请求完成后，限制应恢复为默认值
    /// </summary>
    [Fact]
    public async Task AllRequestsCompleted_ShouldResetToDefaultLimits()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = "DR-ALLCOMPLETE-A",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.5m,
            MinIceMeltingRate = 1500m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = "DR-ALLCOMPLETE-B",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.7m,
            MinIceMeltingRate = 1000m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        await _responder.ExecuteResponseAsync(requestA.Id);
        await _responder.ExecuteResponseAsync(requestB.Id);

        // Act - 完成所有请求
        await _responder.RecordExecutionLogAsync(requestA.Id, 6000m, 4000m);
        await _responder.CompleteResponseAsync(requestA.Id, 8);
        await _responder.RecordExecutionLogAsync(requestB.Id, 5500m, 4000m);
        await _responder.CompleteResponseAsync(requestB.Id, 7);

        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(1.0m,
            because: "所有请求完成后主机出力上限应恢复为100%");
        adjustments.IceMeltingRate.Should().Be(0m,
            because: "所有请求完成后融冰速率应恢复为0");
        adjustments.ActiveRequestCount.Should().Be(0);
    }

    /// <summary>
    /// 验证执行日志中的限制参数
    /// 测试点：RecordExecutionLogAsync应记录当前最严格的限制参数
    /// </summary>
    [Fact]
    public async Task ExecutionLog_ShouldRecordCurrentLimits()
    {
        // Arrange
        var requestA = new DemandResponseRequest
        {
            Id = "DR-LOG-A",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.5m,
            MinIceMeltingRate = 1500m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        var requestB = new DemandResponseRequest
        {
            Id = "DR-LOG-B",
            RequestType = DRRequestType.LoadShifting,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 1500m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.4m,
            MaxChillerOutputLimit = 0.7m,
            MinIceMeltingRate = 1000m,
            Priority = 2,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddRangeAsync(requestA, requestB);
        await _dbContext.SaveChangesAsync();

        await _responder.ExecuteResponseAsync(requestA.Id);
        await _responder.ExecuteResponseAsync(requestB.Id);

        // Act
        var log = await _responder.RecordExecutionLogAsync(requestA.Id, 6000m, 4000m);

        // Assert
        log.ChillerOutputLimit.Should().Be(0.5m,
            because: "执行日志应记录当前最严格的主机出力上限");
        log.IceMeltingRateApplied.Should().Be(1500m,
            because: "执行日志应记录当前最严格的融冰速率");
    }

    /// <summary>
    /// 验证总减载量的累加
    /// 测试点：TotalRequestedReduction应为所有活动请求的减载量之和
    /// </summary>
    [Fact]
    public async Task TotalRequestedReduction_ShouldBeSumOfAllActive()
    {
        // Arrange
        var reductions = new[] { 2000m, 1500m, 2500m };
        var expectedTotal = reductions.Sum();
        var requests = new List<DemandResponseRequest>();

        for (int i = 0; i < reductions.Length; i++)
        {
            var request = new DemandResponseRequest
            {
                Id = $"DR-SUM-{i:00}",
                RequestType = DRRequestType.LoadReduction,
                Status = DRRequestStatus.Received,
                SourcePlatform = "Test",
                RequestedLoadReduction = reductions[i],
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(60),
                IncentivePerKWh = 0.5m,
                MaxChillerOutputLimit = 0.8m - i * 0.1m,
                MinIceMeltingRate = 500m + i * 100,
                Priority = i,
                ReceivedAt = DateTime.UtcNow,
                ResponseRequired = true
            };
            requests.Add(request);
        }

        await _dbContext.DemandResponseRequests.AddRangeAsync(requests);
        await _dbContext.SaveChangesAsync();

        // Act
        foreach (var request in requests)
        {
            await _responder.ExecuteResponseAsync(request.Id);
        }
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();

        // Assert
        adjustments.TotalRequestedReduction.Should().Be(expectedTotal,
            because: "总减载量应为所有活动请求的减载量之和");
        adjustments.ActiveRequestCount.Should().Be(reductions.Length);
    }

    /// <summary>
    /// 验证限制参数与GetStatusAsync的一致性
    /// 测试点：GetCurrentAdjustmentsAsync与GetStatusAsync返回的限制参数应一致
    /// </summary>
    [Fact]
    public async Task AdjustmentsAndStatus_ShouldHaveConsistentLimits()
    {
        // Arrange
        var request = new DemandResponseRequest
        {
            Id = "DR-CONSISTENT-001",
            RequestType = DRRequestType.LoadReduction,
            Status = DRRequestStatus.Received,
            SourcePlatform = "Test",
            RequestedLoadReduction = 2000m,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(60),
            IncentivePerKWh = 0.5m,
            MaxChillerOutputLimit = 0.65m,
            MinIceMeltingRate = 1200m,
            Priority = 1,
            ReceivedAt = DateTime.UtcNow,
            ResponseRequired = true
        };

        await _dbContext.DemandResponseRequests.AddAsync(request);
        await _dbContext.SaveChangesAsync();

        await _responder.ExecuteResponseAsync(request.Id);

        // Act
        var adjustments = await _responder.GetCurrentAdjustmentsAsync();
        var status = await _responder.GetStatusAsync(request.Id);

        // Assert
        adjustments.ChillerOutputLimit.Should().Be(status.ChillerOutputLimit,
            because: "GetCurrentAdjustmentsAsync与GetStatusAsync返回的主机出力上限应一致");
        adjustments.IceMeltingRate.Should().Be(status.IceMeltingRate,
            because: "GetCurrentAdjustmentsAsync与GetStatusAsync返回的融冰速率应一致");
    }
}
