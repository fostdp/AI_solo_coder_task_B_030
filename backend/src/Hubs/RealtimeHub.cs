using Microsoft.AspNetCore.SignalR;

namespace ChillerPlantOptimization.Hubs;

public interface IRealtimeClient
{
    Task ReceiveDeviceDataUpdate(object data);
    Task ReceiveAlarmUpdate(object alarm);
    Task ReceiveEfficiencyUpdate(object efficiency);
    Task ReceiveMetricsUpdate(object metrics);
    Task ReceiveDPCalculationStatus(object status);
    Task ReceiveDPCalculationProgress(string taskId, object progress);
    Task ReceiveDPCalculationComplete(string taskId, object result);
    Task ReceiveDPCalculationFailed(string taskId, string errorMessage);
}

public class RealtimeHub : Hub<IRealtimeClient>
{
    private readonly ILogger<RealtimeHub> _logger;

    public RealtimeHub(ILogger<RealtimeHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("客户端已连接: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端已断开: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToDevices()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Devices");
        _logger.LogInformation("客户端 {ConnectionId} 已订阅设备数据", Context.ConnectionId);
    }

    public async Task SubscribeToAlarms()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Alarms");
        _logger.LogInformation("客户端 {ConnectionId} 已订阅告警数据", Context.ConnectionId);
    }

    public async Task SubscribeToMetrics()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Metrics");
        _logger.LogInformation("客户端 {ConnectionId} 已订阅系统指标", Context.ConnectionId);
    }

    public async Task UnsubscribeAll()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Devices");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Alarms");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Metrics");
        _logger.LogInformation("客户端 {ConnectionId} 已取消所有订阅", Context.ConnectionId);
    }
}
