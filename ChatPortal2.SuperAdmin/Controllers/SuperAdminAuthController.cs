using ChatPortal2.Models;
using ChatPortal2.SuperAdmin.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatPortal2.SuperAdmin.Controllers;

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
