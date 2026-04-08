using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatPortal2.Controllers;

public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtService _jwtService;
    private readonly AppDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        JwtService jwtService,
        AppDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _db = db;
    }

    [HttpGet("/auth/login")]
    public IActionResult Login() => View();

    [HttpGet("/auth/register")]
    public IActionResult Register() => View();

    [HttpPost("/api/auth/register")]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        // Create Organization first
        var orgName = !string.IsNullOrWhiteSpace(req.OrganizationName) ? req.OrganizationName : (req.FullName ?? req.Email) + "'s Organization";
        var org = new Organization { Name = orgName };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName ?? req.Email,
            Role = "OrgAdmin",
            OrganizationId = org.Id
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join(", ", result.Errors.Select(e => e.Description)) });

        // Create free subscription
        _db.SubscriptionPlans.Add(new SubscriptionPlan { UserId = user.Id, Plan = PlanType.Free });
        await _db.SaveChangesAsync();

        var token = _jwtService.GenerateToken(user);
        SetJwtCookie(token);

        return Ok(new { token, user = new { user.Id, user.Email, user.FullName, user.Role, user.OrganizationId, orgName = org.Name } });
    }

    [HttpPost("/api/auth/login")]
    public async Task<IActionResult> LoginApi([FromBody] LoginRequest req)
    {
        if (string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null)
            return Unauthorized(new { error = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, false);
        if (!result.Succeeded)
            return Unauthorized(new { error = "Invalid credentials." });

        var token = _jwtService.GenerateToken(user);
        SetJwtCookie(token);

        _db.ActivityLogs.Add(new ActivityLog { Action = "login", Description = $"{user.Email} signed in.", UserId = user.Id, OrganizationId = user.OrganizationId });
        await _db.SaveChangesAsync();

        return Ok(new { token, user = new { user.Id, user.Email, user.FullName, user.Role, user.OrganizationId } });
    }

    [HttpPost("/api/auth/logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("jwt");
        return Ok(new { success = true });
    }

    [HttpGet("/api/auth/me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var token = Request.Cookies["jwt"];
        if (string.IsNullOrEmpty(token)) return Unauthorized();

        var principal = _jwtService.ValidateToken(token);
        if (principal == null) return Unauthorized();

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return Unauthorized();

        return Ok(new { user.Id, user.Email, user.FullName, user.Role, user.OrganizationId });
    }

    private void SetJwtCookie(string token)
    {
        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(24)
        });
    }
}

public class RegisterRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
    public string? OrganizationName { get; set; }
}

public class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}
