using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.FaultDiagnosis;

namespace ChillerPlantOptimization.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FaultDiagnosisController : ControllerBase
{
    private readonly IFaultDiagnosisModule _faultDiagnosisModule;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FaultDiagnosisController> _logger;

    public FaultDiagnosisController(
        IFaultDiagnosisModule faultDiagnosisModule,
        AppDbContext dbContext,
        ILogger<FaultDiagnosisController> logger)
    {
        _faultDiagnosisModule = faultDiagnosisModule;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost("diagnose/{deviceId}")]
    public async Task<ActionResult<IEnumerable<FaultDiagnosisResult>>> DiagnoseDevice(string deviceId)
    {
        try
        {
            var results = await _faultDiagnosisModule.DiagnoseDeviceAsync(deviceId);
            return Ok(results);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "诊断设备 {DeviceId} 失败", deviceId);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("diagnose-all")]
    public async Task<ActionResult<IEnumerable<FaultDiagnosisResult>>> DiagnoseAllDevices()
    {
        try
        {
            var results = await _faultDiagnosisModule.DiagnoseAllDevicesAsync();
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量诊断设备失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("results")]
    public async Task<ActionResult<IEnumerable<FaultDiagnosisResult>>> GetResults(
        [FromQuery] string? deviceId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _dbContext.FaultDiagnosisResults
            .Include(r => r.Device)
            .Include(r => r.FaultType)
            .AsQueryable();

        if (!string.IsNullOrEmpty(deviceId))
        {
            query = query.Where(r => r.DeviceId == deviceId);
        }

        var results = await query
            .OrderByDescending(r => r.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(results);
    }

    [HttpGet("results/{id}")]
    public async Task<ActionResult<FaultDiagnosisResult>> GetResultById(long id)
    {
        var result = await _dbContext.FaultDiagnosisResults
            .Include(r => r.Device)
            .Include(r => r.FaultType)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("results/{id}/confirm")]
    public async Task<ActionResult> ConfirmResult(long id, [FromQuery] string confirmedBy)
    {
        try
        {
            await _faultDiagnosisModule.ConfirmDiagnosisAsync(id, confirmedBy);
            return Ok(new { success = true, message = "诊断结果已确认" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "确认诊断结果失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("fault-types")]
    public async Task<ActionResult<IEnumerable<FaultType>>> GetFaultTypes([FromQuery] int deviceTypeId)
    {
        var faultTypes = await _faultDiagnosisModule.GetFaultTypesForDeviceTypeAsync((DeviceType)deviceTypeId);
        return Ok(faultTypes);
    }

    [HttpGet("statistics")]
    public async Task<ActionResult<DiagnosisStatistic>> GetStatistics([FromQuery] DateTime date)
    {
        try
        {
            var statistics = await _faultDiagnosisModule.GetDailyStatisticsAsync(date);
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取诊断统计失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
