using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Entity Framework + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ASP.NET Identity
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

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key 'Jwt:Key' is not configured in appsettings.json.");
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
            context.Token = context.Request.Cookies["jwt"];
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            if (!context.Response.HasStarted && context.Request.Headers["Accept"].ToString().Contains("text/html"))
            {
                context.HandleResponse();
                context.Response.Redirect("/auth/login");
            }
            return Task.CompletedTask;
        },
        OnForbidden = context =>
        {
            if (!context.Response.HasStarted && context.Request.Headers["Accept"].ToString().Contains("text/html"))
            {
                context.Response.Redirect("/access-denied?statusCode=403");
            }
            return Task.CompletedTask;
        }
    };
});

// HttpClientFactory (needed by CohereService)
builder.Services.AddHttpClient("cohere");

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration["App:BaseUrl"] ?? "https://localhost:5001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Service DI registrations
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CohereService>();
builder.Services.AddScoped<ISeoService, SeoService>();
builder.Services.AddSingleton<IChartService, ChartService>();
builder.Services.AddSingleton<IDataService, DataService>();
builder.Services.AddSingleton<IPowerBiService, PowerBiService>();
// Query execution with caching decorator — reduces redundant hits to live datasources.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<QueryExecutionService>();
builder.Services.AddSingleton<CachingQueryExecutionService>(sp =>
    new CachingQueryExecutionService(
        sp.GetRequiredService<QueryExecutionService>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<ILogger<CachingQueryExecutionService>>()));
builder.Services.AddSingleton<IQueryExecutionService>(sp => sp.GetRequiredService<CachingQueryExecutionService>());
builder.Services.AddSingleton<IQueryCacheInvalidator>(sp => sp.GetRequiredService<CachingQueryExecutionService>());
builder.Services.AddScoped<IRelationshipService, RelationshipService>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<IWorkspacePermissionService, WorkspacePermissionService>();
builder.Services.AddScoped<IEmailService, SendGridEmailService>();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<ITokenBudgetService, TokenBudgetService>();
builder.Services.AddScoped<IContentSeeder, ContentSeeder>();
builder.Services.AddScoped<ITrialEnforcementService, TrialEnforcementService>();
builder.Services.AddHostedService<NotificationSeedingService>();
builder.Services.AddHttpClient("PayPal");
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddHostedService<SubscriptionExpiryJob>();

// GeoIP service (D25)
builder.Services.AddHttpClient("geoip");
builder.Services.AddScoped<GeoIpService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ActivityLogger>();

// Session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

// MVC with views + Newtonsoft.Json
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson();

var app = builder.Build();

// Database initialization and SEO seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();
        var applied = db.Database.GetAppliedMigrations().ToList();
        var pending = db.Database.GetPendingMigrations().ToList();
        startupLogger.LogInformation("EF migrations applied: {AppliedCount}, pending: {PendingCount}", applied.Count, pending.Count);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "MigrateAsync failed (no migrations may exist yet). Falling back to EnsureCreated.");
        db.Database.EnsureCreated();
    }

    var seoService = scope.ServiceProvider.GetRequiredService<ISeoService>();
    await seoService.SeedDefaultEntriesAsync();

    // Seed release-note docs + AIInsights365.net Phase 1/2 blog posts
    // (also registers their URLs in sitemap.xml via SeoEntries).
    try
    {
        var contentSeeder = scope.ServiceProvider.GetRequiredService<IContentSeeder>();
        await contentSeeder.SeedAsync();
        startupLogger.LogInformation("ContentSeeder completed (docs + blog posts).");
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "ContentSeeder failed to seed docs/blog posts.");
    }
}

// Middleware pipeline
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    // Allow public/embed report views to be iframed by external sites.
    // Everything else still defaults to DENY to prevent clickjacking.
    var path = context.Request.Path.Value ?? "";
    var isEmbeddableReport = path.StartsWith("/report/view/", StringComparison.OrdinalIgnoreCase);
    if (!isEmbeddableReport)
    {
        context.Response.Headers.Append("X-Frame-Options", "DENY");
    }
    else
    {
        context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors *");
    }
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseCors();
app.UseSession();
app.UseAuthentication();

// Impersonation middleware (D23): if imp_jwt cookie is present and valid,
// override the current principal with the impersonated user's identity.
// Must run AFTER UseAuthentication() and BEFORE UseAuthorization() so
// authorization decisions are made against the impersonated principal.
app.Use(async (context, next) =>
{
    var impCookie = context.Request.Cookies["imp_jwt"];
    if (!string.IsNullOrEmpty(impCookie))
    {
        var jwtKey = context.RequestServices.GetRequiredService<IConfiguration>()["Jwt:Key"];
        if (!string.IsNullOrEmpty(jwtKey))
        {
            try
            {
                var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(jwtKey));
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                handler.MapInboundClaims = false;
                var principal = handler.ValidateToken(impCookie, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                // Only override if this is actually an impersonation token (has act:sub claim)
                if (principal.FindFirst("act:sub") != null)
                {
                    context.User = principal;
                    // Store impersonation info in Items for banner rendering
                    var targetEmail = principal.FindFirst("email")?.Value
                                     ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                    var actorEmail = principal.FindFirst("act:email")?.Value;
                    var impExpClaim = principal.FindFirst("imp_exp")?.Value;
                    if (long.TryParse(impExpClaim, out var impExpUnix))
                    {
                        var impEnd = DateTimeOffset.FromUnixTimeSeconds(impExpUnix).UtcDateTime;
                        context.Items["ImpersonationBanner"] = $"⚠️ Impersonating {targetEmail}. Started by {actorEmail}. Ends at {impEnd:HH:mm} UTC.";
                        context.Items["ImpersonationActive"] = true;
                    }
                }
            }
            catch { /* invalid or expired imp_jwt — ignore */ }
        }
    }
    await next();
});

app.UseAuthorization();

// Redirect 401/403 to the Access Denied page for browser requests
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    var request = context.HttpContext.Request;
    if (response.StatusCode is 401 or 403)
    {
        var accept = request.Headers.Accept.ToString();
        var isApi = request.Path.StartsWithSegments("/api")
                    || accept.Contains("application/json")
                    || request.Headers.ContainsKey("X-Requested-With");
        if (!isApi)
        {
            response.Redirect($"/access-denied?statusCode={response.StatusCode}");
        }
    }
});

app.MapDefaultControllerRoute();

app.Run();
