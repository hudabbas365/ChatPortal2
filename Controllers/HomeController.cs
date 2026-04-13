using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChatPortal2.Controllers;

public class HomeController : Controller
{
    private readonly ISeoService _seoService;

    private static readonly JsonSerializerSettings CamelCaseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    public HomeController(ISeoService seoService)
    {
        _seoService = seoService;
    }

    private async Task SetSeoAsync(string path)
    {
        ViewBag.SeoEntry = await _seoService.GetByUrlAsync(path);
    }

    public async Task<IActionResult> Index()
    {
        await SetSeoAsync("/");
        return View();
    }

    public async Task<IActionResult> About()
    {
        await SetSeoAsync("/about");
        return View();
    }

    public async Task<IActionResult> Pricing()
    {
        await SetSeoAsync("/pricing");
        return View();
    }

    public async Task<IActionResult> Docs()
    {
        await SetSeoAsync("/docs");
        return View();
    }

    public async Task<IActionResult> Blog()
    {
        await SetSeoAsync("/blog");
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();

    [Route("/access-denied")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult AccessDenied([FromQuery] int? statusCode)
    {
        ViewData["StatusCode"] = statusCode ?? 401;
        return View();
    }
}
