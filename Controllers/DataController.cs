using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatPortal2.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DataController : ControllerBase
{
    private readonly IDataService _dataService;

    public DataController(IDataService dataService)
    {
        _dataService = dataService;
    }

    [HttpGet("datasets")]
    public IActionResult GetDatasets() => Ok(_dataService.GetDatasets());

    [HttpGet("{name}")]
    public IActionResult GetData(string name) => Ok(_dataService.GetData(name));

    [HttpGet("{name}/fields")]
    public IActionResult GetFields(string name) => Ok(_dataService.GetFields(name));

    [HttpGet("{name}/aggregated")]
    public IActionResult GetAggregated(
        string name,
        [FromQuery] string labelField,
        [FromQuery] string valueField,
        [FromQuery] string aggregation = "SUM")
    {
        return Ok(_dataService.GetAggregated(name, labelField, valueField, aggregation));
    }

    [HttpPost("custom")]
    public IActionResult PostCustomData([FromBody] CustomDataRequest req)
    {
        try
        {
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(req.Json ?? "[]")
                         ?? new List<Dictionary<string, object>>();
            return Ok(result);
        }
        catch
        {
            return BadRequest(new { error = "Invalid JSON" });
        }
    }
}

public class CustomDataRequest
{
    public string? Json { get; set; }
}
