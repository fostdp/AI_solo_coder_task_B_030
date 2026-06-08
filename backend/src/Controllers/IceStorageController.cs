using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.IceStorage;

namespace ChillerPlantOptimization.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IceStorageController : ControllerBase
{
    private readonly IIceStorageModule _iceStorageModule;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<IceStorageController> _logger;

    public IceStorageController(
        IIceStorageModule iceStorageModule,
        AppDbContext dbContext,
        ILogger<IceStorageController> logger)
    {
        _iceStorageModule = iceStorageModule;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("schedule")]
    public async Task<ActionResult<IEnumerable<IceStorageSchedule>>> GetSchedule([FromQuery] DateTime date)
    {
        var schedules = await _iceStorageModule.GetScheduleForDateAsync(date);
        return Ok(schedules);
    }

    [HttpPost("calculate")]
    public async Task<ActionResult<DPStrategyRecord>> CalculateOptimalStrategy([FromQuery] DateTime date)
    {
        try
        {
            var result = await _iceStorageModule.CalculateOptimalStrategyAsync(date);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算冰蓄冷最优策略失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("tanks")]
    public async Task<ActionResult<IEnumerable<IceStorageTank>>> GetAllTanks()
    {
        var tanks = await _dbContext.IceStorageTanks
            .Include(t => t.Device)
            .ToListAsync();
        return Ok(tanks);
    }

    [HttpGet("tanks/{id}")]
    public async Task<ActionResult<IceStorageTank>> GetTankById(string id)
    {
        var tank = await _dbContext.IceStorageTanks
            .Include(t => t.Device)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tank == null)
        {
            return NotFound();
        }
        return Ok(tank);
    }

    [HttpGet("electricity-prices")]
    public async Task<ActionResult<IEnumerable<ElectricityPriceTier>>> GetElectricityPrices()
    {
        var prices = await _dbContext.ElectricityPriceTiers
            .Where(p => p.IsActive)
            .OrderBy(p => p.StartHour)
            .ToListAsync();
        return Ok(prices);
    }

    [HttpGet("forecasts")]
    public async Task<ActionResult<IEnumerable<LoadForecast>>> GetForecasts([FromQuery] DateTime date)
    {
        var forecasts = await _dbContext.LoadForecasts
            .Where(f => f.ForecastDate.Date == date.Date)
            .OrderBy(f => f.HourOfDay)
            .ToListAsync();
        return Ok(forecasts);
    }

    [HttpPost("forecasts/generate")]
    public async Task<ActionResult> GenerateForecasts()
    {
        try
        {
            await _iceStorageModule.GenerateNextDayForecastAsync();
            return Ok(new { success = true, message = "次日负荷预测已生成" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成负荷预测失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("operations")]
    public async Task<ActionResult<IEnumerable<IceStorageOperation>>> GetOperations([FromQuery] string tankId)
    {
        var operations = await _dbContext.IceStorageOperations
            .Where(o => o.IceTankId == tankId)
            .OrderByDescending(o => o.Timestamp)
            .Take(100)
            .ToListAsync();
        return Ok(operations);
    }
}
