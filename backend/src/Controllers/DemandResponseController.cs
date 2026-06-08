using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.DemandResponse;

namespace ChillerPlantOptimization.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DemandResponseController : ControllerBase
{
    private readonly IDemandResponseModule _demandResponseModule;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DemandResponseController> _logger;

    public DemandResponseController(
        IDemandResponseModule demandResponseModule,
        AppDbContext dbContext,
        ILogger<DemandResponseController> logger)
    {
        _demandResponseModule = demandResponseModule;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<DemandResponseRequest>>> GetActiveRequests()
    {
        var requests = await _demandResponseModule.GetActiveRequestsAsync();
        return Ok(requests);
    }

    [HttpPost("simulate")]
    public async Task<ActionResult<DemandResponseRequest>> SimulateDR(
        [FromQuery] int type,
        [FromQuery] decimal loadReduction,
        [FromQuery] int durationMinutes,
        [FromQuery] decimal incentive)
    {
        try
        {
            var request = await _demandResponseModule.SimulateDRRequestAsync(
                (DRRequestType)type,
                loadReduction,
                durationMinutes,
                incentive);
            return Ok(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟DR指令失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{requestId}/execute")]
    public async Task<ActionResult<DRResponseSummary>> ExecuteDR(string requestId)
    {
        try
        {
            var summary = await _demandResponseModule.ExecuteResponseAsync(requestId);
            return Ok(summary);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行DR失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{requestId}/complete")]
    public async Task<ActionResult<DRResponseSummary>> CompleteDR(
        string requestId,
        [FromQuery] int satisfactionScore)
    {
        try
        {
            var summary = await _demandResponseModule.CompleteResponseAsync(requestId, satisfactionScore);
            return Ok(summary);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成DR失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{requestId}/logs")]
    public async Task<ActionResult<IEnumerable<DRExecutionLog>>> GetExecutionLogs(string requestId)
    {
        var logs = await _dbContext.DRExecutionLogs
            .Where(l => l.DRRequestId == requestId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
        return Ok(logs);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<DemandResponseRequest>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var requests = await _dbContext.DemandResponseRequests
            .Where(r => r.Status == DRRequestStatus.Completed || r.Status == DRRequestStatus.Cancelled)
            .OrderByDescending(r => r.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return Ok(requests);
    }

    [HttpGet("current-limits")]
    public async Task<ActionResult> GetCurrentLimits()
    {
        var chillerLimit = await _demandResponseModule.GetCurrentChillerOutputLimitAsync();
        var iceMeltingRate = await _demandResponseModule.GetCurrentIceMeltingRateAsync();

        return Ok(new
        {
            chillerOutputLimit = chillerLimit,
            iceMeltingRate = iceMeltingRate,
            timestamp = DateTime.UtcNow
        });
    }
}
