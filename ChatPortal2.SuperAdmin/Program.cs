using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.SuperAdmin.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 10;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key 'Jwt:Key' is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtLegacyIssuer = builder.Configuration["Jwt:LegacyIssuer"] ?? "ChatPortal2";
var jwtLegacyAudience = builder.Configuration["Jwt:LegacyAudience"] ?? "ChatPortal2Users";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuers = new[] { jwtIssuer, jwtLegacyIssuer }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToArray()!,
        ValidateAudience = true,
        ValidAudiences = new[] { jwtAudience, jwtLegacyAudience }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToArray()!,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            context.Token = context.Request.Cookies["sa_jwt"];
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            if (!context.Response.HasStarted)
            {
                context.HandleResponse();
                context.Response.Redirect("/superadmin/login");
            }
            return Task.CompletedTask;
        },
        OnForbidden = context =>
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Redirect("/superadmin/login");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddHttpClient("cohere");
builder.Services.AddScoped<AIInsights.Services.CohereService>();
builder.Services.AddScoped<SuperAdminJwtService>();
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson()
    .ConfigureApplicationPartManager(manager =>
    {
        // Remove the main AIInsights assembly so its controllers are not discovered
        var mainPart = manager.ApplicationParts
            .FirstOrDefault(p => p.Name == "AIInsights");
        if (mainPart != null)
            manager.ApplicationParts.Remove(mainPart);
    });

var app = builder.Build();

// Show detailed errors so 500 responses include the full exception.
// Remove or guard with app.Environment.IsDevelopment() once the issue is resolved.
app.UseDeveloperExceptionPage();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (response.StatusCode is 401 or 403 && !response.HasStarted)
    {
        response.Redirect("/superadmin/login");
    }
});

app.MapGet("/", context =>
{
    context.Response.Redirect("/superadmin");
    return Task.CompletedTask;
});

app.MapDefaultControllerRoute();

app.Run();
