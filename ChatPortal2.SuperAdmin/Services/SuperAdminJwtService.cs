using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AIInsights.Models;
using Microsoft.IdentityModel.Tokens;

namespace AIInsights.SuperAdmin.Services;

public class SuperAdminJwtService
{
    private readonly IConfiguration _config;

    public SuperAdminJwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(ApplicationUser user)
    {
        var rawKey = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key 'Jwt:Key' is not configured.");
        var keyBytes = Encoding.UTF8.GetBytes(rawKey);
        // HMAC-SHA256 requires at least 256 bits (32 bytes). Fail fast with a
        // clear message instead of the cryptic IDX10653 from the signer.
        if (keyBytes.Length < 32)
            throw new InvalidOperationException(
                $"JWT key 'Jwt:Key' must be at least 32 bytes (256 bits) for HMAC-SHA256; got {keyBytes.Length}.");
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claim constructors throw ArgumentNullException on null values, so
        // coalesce optional profile columns (Role / FullName can legitimately
        // be NULL on legacy rows) to empty strings.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim("role", user.Role ?? string.Empty),
            new Claim("fullName", user.FullName ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
