using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AIInsights.Controllers;

public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtService _jwtService;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    // In-memory store for captcha answers: captchaId -> (answer, expiry)
    private static readonly ConcurrentDictionary<string, (string Answer, DateTime Expiry)> _captchaStore = new();

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        JwtService jwtService,
        AppDbContext db,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpGet("/auth/login")]
    public IActionResult Login() => View();

    [HttpGet("/api/auth/captcha")]
    public IActionResult GenerateCaptcha()
    {
        // Purge expired entries
        var now = DateTime.UtcNow;
        foreach (var key in _captchaStore.Keys)
        {
            if (_captchaStore.TryGetValue(key, out var entry) && entry.Expiry < now)
                _captchaStore.TryRemove(key, out _);
        }

        // Generate a random math problem
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[2];
        rng.GetBytes(bytes);
        var a = (bytes[0] % 20) + 1; // 1-20
        var b = (bytes[1] % 10) + 1; // 1-10

        // Randomly pick + or ×
        rng.GetBytes(bytes);
        var useMultiply = bytes[0] % 3 == 0;
        var op = useMultiply ? "×" : "+";
        var answer = useMultiply ? (a * b) : (a + b);
        var question = $"{a} {op} {b} = ?";

        // Store answer with 3-minute expiry
        var captchaId = Guid.NewGuid().ToString("N");
        _captchaStore[captchaId] = (answer.ToString(), now.AddMinutes(3));

        // Render to SVG image (no System.Drawing dependency needed)
        var svg = GenerateCaptchaSvg(question);
        var svgBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));

        return Ok(new { captchaId, image = $"data:image/svg+xml;base64,{svgBase64}" });
    }

    private static string GenerateCaptchaSvg(string text)
    {
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[10];
        rng.GetBytes(bytes);

        var width = 180;
        var height = 60;
        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'>");
        sb.Append($"<rect width='{width}' height='{height}' fill='#f0f4f8' rx='8'/>");

        // Noise lines
        for (int i = 0; i < 5; i++)
        {
            rng.GetBytes(bytes);
            var x1 = bytes[0] % width;
            var y1 = bytes[1] % height;
            var x2 = bytes[2] % width;
            var y2 = bytes[3] % height;
            var colors = new[] { "#c4d3e0", "#a8c0d4", "#d0dce6", "#b5c9db" };
            var color = colors[bytes[4] % colors.Length];
            sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{color}' stroke-width='1'/>");
        }

        // Noise dots
        for (int i = 0; i < 20; i++)
        {
            rng.GetBytes(bytes);
            var cx = bytes[0] % width;
            var cy = bytes[1] % height;
            sb.Append($"<circle cx='{cx}' cy='{cy}' r='1.5' fill='#bcc8d4' opacity='0.5'/>");
        }

        // Render each character with slight random rotation/offset
        var startX = 20;
        foreach (var ch in text)
        {
            rng.GetBytes(bytes);
            var yOff = 38 + (bytes[0] % 7) - 3;
            var rot = (bytes[1] % 15) - 7;
            var fontColors = new[] { "#1e3a5f", "#2c5282", "#2b4c7e", "#34495e", "#1a365d" };
            var fc = fontColors[bytes[2] % fontColors.Length];
            sb.Append($"<text x='{startX}' y='{yOff}' font-family='monospace,sans-serif' font-size='24' font-weight='bold' fill='{fc}' transform='rotate({rot},{startX},{yOff})'>{System.Net.WebUtility.HtmlEncode(ch.ToString())}</text>");
            startX += 20;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    [HttpGet("/auth/register")]
    public IActionResult Register() => View();

    [HttpPost("/api/auth/register")]
    public async Task<IActionResult> RegisterApi([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        // Create Organization first (with FreeTrial plan so token budget is available)
        var orgName = !string.IsNullOrWhiteSpace(req.OrganizationName) ? req.OrganizationName : (req.FullName ?? req.Email) + "'s Organization";
        var org = new Organization { Name = orgName, Plan = PlanType.FreeTrial };
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

        // Create 30-day free trial subscription
        _db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            UserId = user.Id,
            Plan = PlanType.FreeTrial,
            TrialStartDate = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(30),
            HasUsedTrial = true
        });
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

        // Verify local CAPTCHA
        if (!string.IsNullOrEmpty(req.CaptchaId) && !string.IsNullOrEmpty(req.CaptchaAnswer))
        {
            if (!VerifyLocalCaptcha(req.CaptchaId, req.CaptchaAnswer))
                return BadRequest(new { error = "Incorrect CAPTCHA answer. Please try again." });
        }
        else
        {
            return BadRequest(new { error = "Please complete the CAPTCHA." });
        }

        // Look up by email first, then fall back to username
        var user = await _userManager.FindByEmailAsync(req.Email)
                   ?? await _userManager.FindByNameAsync(req.Email);
        if (user == null)
        {
            _logger.LogWarning("Login failed: no user found for '{Identifier}'.", req.Email);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        // Check if the account is locked out before attempting password check
        if (await _userManager.IsLockedOutAsync(user))
        {
            var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
            var remaining = lockoutEnd.HasValue ? (int)Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes) : 0;
            _logger.LogWarning("Login failed: account '{Email}' is locked out for {Minutes} more minutes.", user.Email, remaining);
            return Unauthorized(new { error = $"Account is locked. Please try again in {remaining} minute(s)." });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, true);
        if (result.IsLockedOut)
        {
            _logger.LogWarning("Login failed: account '{Email}' just became locked out.", user.Email);
            return Unauthorized(new { error = "Too many failed attempts. Account is locked for 15 minutes." });
        }
        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed: invalid password for '{Email}'.", user.Email);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        // Check if organization is blocked
        if (user.OrganizationId.HasValue)
        {
            var org = await _db.Organizations.FindAsync(user.OrganizationId.Value);
            if (org != null && org.IsBlocked)
            {
                _logger.LogWarning("Login blocked: organization '{OrgName}' (ID:{OrgId}) is blocked. User: '{Email}'.", org.Name, org.Id, user.Email);
                return Unauthorized(new { error = "org_blocked", message = $"Your organization has been blocked. Reason: {org.BlockedReason ?? "Contact support for details."}. Please contact support to resolve this issue." });
            }
        }

        var token = _jwtService.GenerateToken(user);
        SetJwtCookie(token);

        _db.ActivityLogs.Add(new ActivityLog { Action = "login", Description = $"{user.Email} signed in.", UserId = user.Id, OrganizationId = user.OrganizationId });
        await _db.SaveChangesAsync();

        var comingSoon = _config.GetValue<bool>("App:ComingSoon");
        var redirectUrl = comingSoon ? "/" : "/chat";
        return Ok(new { token, redirectUrl, user = new { user.Id, user.Email, user.FullName, user.Role, user.OrganizationId } });
    }

    [HttpPost("/api/auth/logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies["jwt"];
        if (!string.IsNullOrEmpty(token))
        {
            var principal = _jwtService.ValidateToken(token);
            var userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? principal?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "logout",
                    Description = "User logged out.",
                    UserId = userId
                });
                await _db.SaveChangesAsync();
            }
        }
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

    private static bool VerifyLocalCaptcha(string captchaId, string userAnswer)
    {
        if (!_captchaStore.TryRemove(captchaId, out var entry))
            return false; // unknown or already used
        if (entry.Expiry < DateTime.UtcNow)
            return false; // expired
        return string.Equals(entry.Answer.Trim(), userAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
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
    public string? CaptchaId { get; set; }
    public string? CaptchaAnswer { get; set; }
}
