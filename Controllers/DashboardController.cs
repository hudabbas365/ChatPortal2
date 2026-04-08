using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChatPortal2.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IChartService _chartService;
    private readonly IDataService _dataService;
    private const string SessionKey = "canvas_state";

    private static readonly JsonSerializerSettings CamelCaseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    public DashboardController(IChartService chartService, IDataService dataService)
    {
        _chartService = chartService;
        _dataService = dataService;
    }

    [HttpGet("/dashboard")]
    public IActionResult Index()
    {
        var canvasJson = HttpContext.Session.GetString(SessionKey);
        CanvasState canvas;
        if (string.IsNullOrEmpty(canvasJson))
        {
            canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
            HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
        }
        else
        {
            canvas = JsonConvert.DeserializeObject<CanvasState>(canvasJson) ?? new CanvasState();
        }

        ViewBag.InitialCharts = JsonConvert.SerializeObject(canvas.Charts, CamelCaseSettings);
        ViewBag.Pages = JsonConvert.SerializeObject(canvas.Pages, CamelCaseSettings);
        ViewBag.ActivePageIndex = canvas.ActivePageIndex;
        ViewBag.CanvasName = canvas.CanvasName;
        ViewBag.ChartLibrary = JsonConvert.SerializeObject(_chartService.GetGroupedCharts()
            .Select(g => new { group = g.Key, charts = g.ToList() }), CamelCaseSettings);
        ViewBag.Datasets = JsonConvert.SerializeObject(_dataService.GetDatasets(), CamelCaseSettings);

        return View(canvas);
    }
}
