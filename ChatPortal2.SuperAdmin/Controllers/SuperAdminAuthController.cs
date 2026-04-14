using AIInsights.Models;
using AIInsights.SuperAdmin.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AIInsights.SuperAdmin.Controllers;

public class SuperAdminAuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly SuperAdminJwtService _jwtService;

    public SuperAdminAuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        SuperAdminJwtService jwtService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
    }

    [HttpGet("/superadmin/login")]
    public IActionResult Login() => View("~/Views/Login.cshtml");

    [HttpGet("/superadmin/register")]
    public IActionResult Register() => View("~/Views/Register.cshtml");

    [HttpPost("/api/superadmin/login")]
    public async Task<IActionResult> LoginApi([FromBody] SuperAdminLoginRequest req)
    {
        if (string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null || user.Role != "SuperAdmin")
            return Unauthorized(new { error = "Access denied. SuperAdmin credentials required." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, false);
        if (!result.Succeeded)
            return Unauthorized(new { error = "Invalid credentials." });

        var token = _jwtService.GenerateToken(user);
        Response.Cookies.Append("sa_jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(8)
        });

        return Ok(new { token, user = new { user.Id, user.Email, user.FullName, user.Role } });
    }

    [HttpPost("/api/superadmin/register")]
    public async Task<IActionResult> RegisterApi([FromBody] SuperAdminRegisterRequest req)
    {
        if (string.IsNullOrEmpty(req.FullName) || string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "All fields are required." });

        if (req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters." });

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
            return BadRequest(new { error = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
            Role = "SuperAdmin",
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { error = errors });
        }

        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("sa_jwt");
        return Ok(new { success = true });
    }
}

public class SuperAdminLoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class SuperAdminRegisterRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
