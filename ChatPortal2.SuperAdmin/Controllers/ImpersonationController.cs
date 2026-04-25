using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class ImpersonationController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public ImpersonationController(AppDbContext db, UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _db = db;
        _userManager = userManager;
        _config = config;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    [HttpPost("/api/superadmin/impersonate/{userId}")]
    public async Task<IActionResult> StartImpersonation(string userId, [FromBody] ImpersonateRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var minutes = req.Minutes is > 0 and <= 120 ? req.Minutes : 30;
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length < 10)
            return BadRequest(new { error = "Reason is required and must be at least 10 characters." });

        var targetUser = await _userManager.FindByIdAsync(userId);
        if (targetUser == null) return NotFound(new { error = "User not found." });

        if (targetUser.Role == "SuperAdmin")
            return BadRequest(new { error = "Cannot impersonate another SuperAdmin." });

        var superAdminId = GetCurrentUserId()!;
        var superAdmin = await _userManager.FindByIdAsync(superAdminId);

        var expiry = DateTime.UtcNow.AddMinutes(minutes);
        var impToken = GenerateImpersonationToken(targetUser, superAdmin!, expiry);

        // Set short-lived impersonation cookie with consistent options
        Response.Cookies.Append("imp_jwt", impToken, GetImpersonationCookieOptions(expiry));

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Security.ImpersonateStart",
            Description = $"SuperAdmin {superAdmin?.Email} impersonated {targetUser.Email}. Reason: {req.Reason.Trim()}. Duration: {minutes} min.",
            UserId = superAdminId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { redirectUrl = "/" });
    }

    [HttpPost("/api/superadmin/impersonate/stop")]
    public async Task<IActionResult> StopImpersonation()
    {
        // Write stop audit log if cookie exists
        var impCookie = Request.Cookies["imp_jwt"];
        if (!string.IsNullOrEmpty(impCookie))
        {
            var principal = ValidateToken(impCookie);
            if (principal != null)
            {
                var targetEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
                var actorEmail = principal.FindFirstValue("act:email");
                var actorId = principal.FindFirstValue("act:sub") ?? GetCurrentUserId() ?? "";
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "Security.ImpersonateStop",
                    Description = $"Impersonation of {targetEmail} stopped by {actorEmail}.",
                    UserId = actorId,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }

        Response.Cookies.Delete("imp_jwt", GetImpersonationCookieOptions(expiry: null));
        return Ok(new { success = true, redirectUrl = "/superadmin" });
    }

    private static CookieOptions GetImpersonationCookieOptions(DateTime? expiry) => new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiry
    };

    private string GenerateImpersonationToken(ApplicationUser target, ApplicationUser superAdmin, DateTime expiry)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim("sub", target.Id),
            new Claim(ClaimTypes.NameIdentifier, target.Id),
            new Claim("email", target.Email ?? ""),
            new Claim(ClaimTypes.Email, target.Email ?? ""),
            new Claim("role", target.Role),
            new Claim("act:sub", superAdmin.Id),
            new Claim("act:email", superAdmin.Email ?? ""),
            new Claim("act:role", "SuperAdmin"),
            new Claim("imp_exp", new DateTimeOffset(expiry).ToUnixTimeSeconds().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    public class ImpersonateRequest
    {
        public int Minutes { get; set; } = 30;
        public string Reason { get; set; } = "";
    }
}
