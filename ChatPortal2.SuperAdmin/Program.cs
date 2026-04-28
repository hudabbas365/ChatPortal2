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
using Microsoft.AspNetCore.DataProtection;

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

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false; // Must be JS-readable for SPA-style AJAX
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Persist Data Protection keys to a stable filesystem path so the antiforgery
// middleware (which runs on every request) can issue/validate the XSRF-TOKEN
// cookie under IIS. The default key-ring location
// (%LOCALAPPDATA%\ASP.NET\DataProtection-Keys) is typically not writable by
// ApplicationPoolIdentity, which causes a 500 on the very first GET / and
// surfaces in Chrome as "Unsafe attempt to load URL ... from frame with URL
// chrome-error://chromewebdata/". Grant the App Pool identity Modify on the
// directory below (override via DataProtection:KeyPath in appsettings if needed).
var dpKeyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? @"C:\inetpub\dpkeys\superadmin";
try { Directory.CreateDirectory(dpKeyPath); } catch { /* fall back to default if path is invalid */ }
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeyPath))
    .SetApplicationName("ChatPortal2.SuperAdmin");
builder.Services.AddHttpClient("cohere");
builder.Services.AddScoped<AIInsights.Services.CohereService>();
builder.Services.AddScoped<SuperAdminJwtService>();
builder.Services.AddScoped<AIInsights.SuperAdmin.Services.IUrgentNotificationEmailer,
    AIInsights.SuperAdmin.Services.SmtpUrgentNotificationEmailer>();
builder.Services.AddHostedService<AIInsights.SuperAdmin.Services.NotificationDispatcher>();
builder.Services.AddScoped<InvoicePdfService>();
builder.Services.AddScoped<IInvoiceEmailSender, SmtpInvoiceEmailSender>();
builder.Services.AddHostedService<IntegrationHealthService>();
builder.Services.AddHostedService<WeeklyDigestService>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<DigestSenderService>();

builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson()
    .ConfigureApplicationPartManager(manager =>
    {
        // Remove the main ChatPortal2 (AIInsights) assembly so its controllers
        // are not discovered inside the SuperAdmin host. The main project's
        // assembly name is "ChatPortal2" (csproj filename) — its root namespace
        // is "AIInsights" but the ApplicationPart is named after the assembly,
        // so filtering on "AIInsights" silently matched nothing and let every
        // OrgAdmin / Auth / Billing controller leak in here, which broke action
        // selection (POST /api/superadmin/login -> 405).
        var partsToRemove = manager.ApplicationParts
            .Where(p => p.Name == "ChatPortal2" || p.Name == "AIInsights")
            .ToList();
        foreach (var part in partsToRemove)
            manager.ApplicationParts.Remove(part);
    });

var app = builder.Build();

// Show detailed errors in Development; in Production use a lambda-based handler
// so we don't depend on a "/superadmin/error" endpoint actually existing — if
// that endpoint is missing, UseExceptionHandler("/path") re-executes, fails,
// and turns every error into a bare 500 with no output anywhere.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errApp =>
    {
        errApp.Run(async context =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            var logger  = context.RequestServices.GetRequiredService<ILoggerFactory>()
                                                  .CreateLogger("UnhandledException");
            if (feature?.Error != null)
                logger.LogError(feature.Error, "Unhandled exception on {Path}", context.Request.Path);

            // TEMP DIAGNOSTIC: dump the real exception so we can see what is failing
            // under IIS publish (debug works, publish doesn't). Also persist it to a
            // log file so it survives if the response is replaced by IIS.
            try
            {
                var logDir = @"C:\inetpub\dpkeys\superadmin\logs";
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "superadmin-errors.log");
                var line = $"[{DateTime.UtcNow:O}] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}\n{feature?.Error}\n\n";
                await File.AppendAllTextAsync(logPath, line);
            }
            catch { /* best-effort */ }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            if (feature?.Error != null)
            {
                await context.Response.WriteAsync(
                    "Unhandled exception (diagnostic mode):\n\n" + feature.Error.ToString());
            }
            else
            {
                await context.Response.WriteAsync("An internal error occurred. Please contact support.");
            }
        });
    });
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Issue XSRF-TOKEN cookie on every request so JS can read it for fetch calls.
// `Secure` is gated on the actual request scheme so the cookie isn't dropped
// silently when the site is reached over plain HTTP (e.g. http://localhost:88
// during local IIS testing); a Secure cookie on an HTTP origin is rejected by
// the browser, which can cascade into antiforgery validation failures.
app.Use(async (context, next) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
    await next();
});

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

// Explicitly register attribute-routed controllers (e.g. POST /api/superadmin/login).
// MapDefaultControllerRoute alone has been observed to leave attribute-routed
// POST endpoints out of the resolved endpoint table in some build/restart
// scenarios, which produces a 405 on POST while GET on the same path works.
app.MapControllers();
app.MapDefaultControllerRoute();

app.Run();
