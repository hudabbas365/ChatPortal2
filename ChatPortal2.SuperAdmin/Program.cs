using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.SuperAdmin.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using System.Security.Claims;

// Set QuestPDF community license
QuestPDF.Settings.License = LicenseType.Community;

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
builder.Services.AddScoped<InvoicePdfService>();
builder.Services.AddScoped<IInvoiceEmailSender, SmtpInvoiceEmailSender>();
builder.Services.AddMemoryCache();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
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

// Show detailed errors in Development only; use a generic error page in Production.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/superadmin/error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Middleware: update LastSeenAt for authenticated users (at most once every 5 minutes)
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
        {
            var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"lastseen:{userId}";
            if (!cache.TryGetValue(cacheKey, out _))
            {
                var db = context.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    user.LastSeenAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
                cache.Set(cacheKey, true, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5)
                });
            }
        }
    }
    await next();
});

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
