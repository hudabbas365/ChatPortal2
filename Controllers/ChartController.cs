using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ChartController : ControllerBase
{
    private readonly IChartService _chartService;
    private const string SessionKey = "canvas_state";
    private static readonly Regex _pageNameRegex = new Regex(@"^Page\s+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ChartController(IChartService chartService)
    {
        _chartService = chartService;
    }

    private CanvasState GetCanvas()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
        {
            var canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
            HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
            return canvas;
        }
        return JsonConvert.DeserializeObject<CanvasState>(json) ?? new CanvasState();
    }

    private void SaveCanvas(CanvasState canvas)
    {
        canvas.LastModified = DateTime.UtcNow;
        HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var canvas = GetCanvas();
        return Ok(canvas);
    }

    [HttpPost("add")]
    public IActionResult Add([FromBody] ChartDefinition chart)
    {
        var canvas = GetCanvas();
        chart.Id = Guid.NewGuid().ToString("N")[..8];
        canvas.Charts.Add(chart);
        SaveCanvas(canvas);
        return Ok(chart);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] ChartDefinition chart)
    {
        var canvas = GetCanvas();
        // Search all pages so the chart is found regardless of active page index
        foreach (var page in canvas.Pages)
        {
            var idx = page.Charts.FindIndex(c => c.Id == id);
            if (idx >= 0)
            {
                chart.Id = id;
                page.Charts[idx] = chart;
                SaveCanvas(canvas);
                return Ok(chart);
            }
        }
        return NotFound();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var canvas = GetCanvas();
        // Search all pages so the chart is found regardless of active page index
        foreach (var page in canvas.Pages)
        {
            var removed = page.Charts.RemoveAll(c => c.Id == id);
            if (removed > 0)
            {
                SaveCanvas(canvas);
                return Ok(new { success = true });
            }
        }
        return NotFound();
    }

    [HttpPost("reorder")]
    public IActionResult Reorder([FromBody] List<string> ids)
    {
        var canvas = GetCanvas();
        var page = canvas.Pages.ElementAtOrDefault(canvas.ActivePageIndex);
        if (page == null) return BadRequest("No active page");

        var reordered = ids
            .Select(id => page.Charts.FirstOrDefault(c => c.Id == id))
            .Where(c => c != null)
            .Cast<ChartDefinition>()
            .ToList();
        page.Charts = reordered;
        SaveCanvas(canvas);
        return Ok(new { success = true });
    }

    [HttpPost("rename")]
    public IActionResult RenameCanvas([FromBody] RenameRequest req)
    {
        var canvas = GetCanvas();
        canvas.CanvasName = req.Name;
        SaveCanvas(canvas);
        return Ok(new { success = true });
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        var canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
        SaveCanvas(canvas);
        return Ok(canvas);
    }

    [HttpGet("types")]
    public IActionResult GetTypes()
    {
        return Ok(_chartService.GetGroupedCharts()
            .Select(g => new { group = g.Key, charts = g.ToList() }));
    }

    // ── Page management ────────────────────────────────────────────

    [HttpPost("/api/page/add")]
    public IActionResult AddPage()
    {
        var canvas = GetCanvas();
        var usedNumbers = canvas.Pages
            .Select(p => _pageNameRegex.Match(p.Name))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
        int nextNum = 1;
        while (usedNumbers.Contains(nextNum)) nextNum++;
        var newPage = new ReportPage { Name = $"Page {nextNum}" };
        canvas.Pages.Add(newPage);
        canvas.ActivePageIndex = canvas.Pages.Count - 1;
        SaveCanvas(canvas);
        return Ok(new { pageIndex = canvas.ActivePageIndex, name = newPage.Name });
    }

    [HttpPost("/api/page/switch/{index}")]
    public IActionResult SwitchPage(int index)
    {
        var canvas = GetCanvas();
        if (index < 0 || index >= canvas.Pages.Count) return BadRequest("Invalid page index");
        canvas.ActivePageIndex = index;
        SaveCanvas(canvas);
        return Ok(new { success = true });
    }

    [HttpPost("/api/page/rename")]
    public IActionResult RenamePage([FromBody] PageRenameRequest req)
    {
        var canvas = GetCanvas();
        if (req.Index < 0 || req.Index >= canvas.Pages.Count) return BadRequest("Invalid page index");
        canvas.Pages[req.Index].Name = req.Name;
        SaveCanvas(canvas);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/page/{index}")]
    public IActionResult DeletePage(int index)
    {
        var canvas = GetCanvas();
        if (canvas.Pages.Count <= 1) return BadRequest("Cannot delete the only page");
        if (index < 0 || index >= canvas.Pages.Count) return BadRequest("Invalid page index");
        canvas.Pages.RemoveAt(index);
        if (canvas.ActivePageIndex >= canvas.Pages.Count)
            canvas.ActivePageIndex = canvas.Pages.Count - 1;
        SaveCanvas(canvas);
        return Ok(new { success = true });
    }

    // ── XML export / import ────────────────────────────────────────

    [HttpGet("/api/report/export/xml")]
    public IActionResult ExportXml()
    {
        var canvas = GetCanvas();
        var serializer = new XmlSerializer(typeof(CanvasState));
        using var ms = new MemoryStream();
        serializer.Serialize(ms, canvas);
        var safeName = canvas.CanvasName.Replace(" ", "_");
        return File(ms.ToArray(), "application/xml", $"{safeName}.xml");
    }

    [HttpPost("/api/report/import/xml")]
    public IActionResult ImportXml(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("No file provided");
        try
        {
            var serializer = new XmlSerializer(typeof(CanvasState));
            using var stream = file.OpenReadStream();
            var canvas = (CanvasState?)serializer.Deserialize(stream);
            if (canvas == null) return BadRequest("Could not parse XML");
            if (canvas.Pages == null || canvas.Pages.Count == 0)
                canvas.Pages = new() { new ReportPage { Name = "Page 1" } };
            canvas.ActivePageIndex = Math.Clamp(canvas.ActivePageIndex, 0, canvas.Pages.Count - 1);
            SaveCanvas(canvas);
            return Ok(new { pages = canvas.Pages, activePageIndex = canvas.ActivePageIndex, canvasName = canvas.CanvasName });
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid XML: {ex.Message}");
        }
    }
}

public class RenameRequest
{
    public string Name { get; set; } = "";
}

public class PageRenameRequest
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
}
