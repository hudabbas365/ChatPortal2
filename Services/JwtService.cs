using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AIInsights.Models;
using Microsoft.IdentityModel.Tokens;

namespace AIInsights.Services;

public class JwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(double.Parse(_config["Jwt:ExpiryHours"] ?? "24"));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim("fullName", user.FullName),
            new Claim("role", user.Role),
            new Claim("organizationId", user.OrganizationId?.ToString() ?? ""),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var result = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return result;
        }
        catch
        {
            return null;
        }
    }

    // ── Embed tokens ────────────────────────────────────────────────────────
    // Signed, scoped, time-limited tokens that grant anonymous viewers access
    // to a single Published report's data endpoint. Reuses the same HMAC key
    // as auth tokens but is distinguished by a "tt=embed" claim so an embed
    // token can never be mistaken for a user-auth token.

    public string GenerateEmbedToken(
        string reportGuid,
        int datasourceId,
        int tokenVersion,
        IEnumerable<string> tables,
        int expiresInDays)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var days = Math.Clamp(expiresInDays, 1, 365);
        var expiry = DateTime.UtcNow.AddDays(days);

        // Comma-separated allow-list — JWT claims are flat strings; this is
        // simpler than emitting one claim per table.
        var tableList = string.Join(",",
            (tables ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

        var claims = new[]
        {
            new Claim("tt", "embed"),
            new Claim("rid", reportGuid),
            new Claim("dsid", datasourceId.ToString()),
            new Claim("tv", tokenVersion.ToString()),
            new Claim("tables", tableList),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public EmbedTokenClaims? ValidateEmbedToken(string token)
    {
        var principal = ValidateToken(token);
        if (principal == null) return null;
        if (principal.FindFirstValue("tt") != "embed") return null;

        var rid = principal.FindFirstValue("rid");
        var dsidStr = principal.FindFirstValue("dsid");
        var tvStr = principal.FindFirstValue("tv");
        if (string.IsNullOrEmpty(rid)
            || !int.TryParse(dsidStr, out var dsid)
            || !int.TryParse(tvStr, out var tv))
            return null;

        var tables = (principal.FindFirstValue("tables") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new EmbedTokenClaims(rid, dsid, tv, tables);
    }
}

public record EmbedTokenClaims(string ReportGuid, int DatasourceId, int TokenVersion, string[] Tables);
