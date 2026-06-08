using Microsoft.AspNetCore.Mvc;
using ChillerPlantOptimization.Data;
using ChillerPlantOptimization.Models;
using ChillerPlantOptimization.Modules.BuildingBenchmark;

namespace ChillerPlantOptimization.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BuildingBenchmarkController : ControllerBase
{
    private readonly IBuildingBenchmarkModule _buildingBenchmarkModule;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BuildingBenchmarkController> _logger;

    public BuildingBenchmarkController(
        IBuildingBenchmarkModule buildingBenchmarkModule,
        AppDbContext dbContext,
        ILogger<BuildingBenchmarkController> logger)
    {
        _buildingBenchmarkModule = buildingBenchmarkModule;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("buildings")]
    public async Task<ActionResult<IEnumerable<Building>>> GetBuildings()
    {
        var buildings = await _buildingBenchmarkModule.GetBuildingsAsync();
        return Ok(buildings);
    }

    [HttpPost("buildings")]
    public async Task<ActionResult<Building>> AddBuilding([FromBody] Building building)
    {
        try
        {
            var result = await _buildingBenchmarkModule.AddBuildingAsync(building);
            return CreatedAtAction(nameof(GetBuildings), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新增楼宇失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPut("buildings/{id}")]
    public async Task<ActionResult<Building>> UpdateBuilding(string id, [FromBody] Building building)
    {
        try
        {
            var result = await _buildingBenchmarkModule.UpdateBuildingAsync(id, building);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新楼宇失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("buildings/{id}")]
    public async Task<ActionResult> DeleteBuilding(string id)
    {
        try
        {
            var result = await _buildingBenchmarkModule.DeleteBuildingAsync(id);
            if (!result)
            {
                return NotFound(new { success = false, message = "楼宇不存在" });
            }
            return Ok(new { success = true, message = "楼宇已删除" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除楼宇失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("buildings/{id}/metrics")]
    public async Task<ActionResult<IEnumerable<BuildingEfficiencyMetric>>> GetBuildingMetrics(
        string id,
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Daily,
        [FromQuery] DateTime startDate = default)
    {
        var actualStartDate = startDate == default ? DateTime.Today.AddDays(-30) : startDate;
        var metrics = await _buildingBenchmarkModule.GetBuildingMetricsAsync(id, period, actualStartDate);
        return Ok(metrics);
    }

    [HttpPost("calculate-metrics")]
    public async Task<ActionResult<IEnumerable<BuildingEfficiencyMetric>>> CalculateMetrics(
        [FromQuery] string buildingId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        try
        {
            var metrics = await _buildingBenchmarkModule.CalculateBuildingMetricsAsync(buildingId, startDate, endDate);
            return Ok(metrics);
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
            _logger.LogError(ex, "计算楼宇指标失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("reports")]
    public async Task<ActionResult<BenchmarkReport>> GenerateReport([FromBody] GenerateReportRequest request)
    {
        try
        {
            var report = await _buildingBenchmarkModule.GenerateBenchmarkReportAsync(
                request.BuildingIds,
                request.ReportName,
                request.StartDate,
                request.EndDate);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成对标报告失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("reports")]
    public async Task<ActionResult<IEnumerable<BenchmarkReport>>> GetReports(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var reports = await _buildingBenchmarkModule.GetBenchmarkReportsAsync(page, pageSize);
        return Ok(reports);
    }

    [HttpGet("radar-data")]
    public async Task<ActionResult> GetRadarData(
        [FromQuery] List<string> buildingIds,
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Daily,
        [FromQuery] DateTime date = default)
    {
        try
        {
            var actualDate = date == default ? DateTime.Today : date;
            var data = await _buildingBenchmarkModule.GetRadarChartDataAsync(buildingIds, period, actualDate);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取雷达图数据失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

public class GenerateReportRequest
{
    public List<string> BuildingIds { get; set; } = new();
    public string ReportName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
