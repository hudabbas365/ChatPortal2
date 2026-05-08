using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using AIInsights.Services.Transforms;
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

// HttpClientFactory (needed by CohereService). The default 100s timeout is too
// short for the auto-report generator which streams a large multi-page JSON
// plan (max_tokens up to 8000). Widen the timeout for this named client only.
builder.Services.AddHttpClient("cohere", c =>
{
    c.Timeout = TimeSpan.FromMinutes(5);
});

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
builder.Services.AddSingleton<IAiInsightTransformService, AiInsightTransformService>();
builder.Services.AddScoped<ITransformDefinitionParser, TransformDefinitionParser>();
builder.Services.AddScoped<ITransformSuggestionService, TransformSuggestionService>();
builder.Services.AddScoped<ITransformValidationService, TransformValidationService>();
builder.Services.AddScoped<ITransformDraftService, TransformDraftService>();
builder.Services.AddScoped<ITransformPreviewService, TransformPreviewService>();
builder.Services.AddScoped<ITransformPublishService, TransformPublishService>();
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

// Auto-report builders — one per datasource type. Order does not matter; the
// controller picks the first builder whose CanHandle() returns true and falls
// back to SQL if none match. Add a new builder + DI line here to support a
// new datasource type without touching the controller.
builder.Services.AddScoped<AIInsights.Services.AutoReport.IAutoReportBuilder, AIInsights.Services.AutoReport.SqlAutoReportBuilder>();
builder.Services.AddScoped<AIInsights.Services.AutoReport.IAutoReportBuilder, AIInsights.Services.AutoReport.PowerBiAutoReportBuilder>();
builder.Services.AddScoped<AIInsights.Services.AutoReport.IAutoReportBuilder, AIInsights.Services.AutoReport.RestApiAutoReportBuilder>();
builder.Services.AddScoped<AIInsights.Services.AutoReport.IAutoReportBuilder, AIInsights.Services.AutoReport.FileUrlAutoReportBuilder>();

// Per-datasource-type services — same fan-out pattern as the auto-report
// builders above. Each implementation owns the test/introspect logic for one
// datasource family so the controller stays a thin HTTP shim.
builder.Services.AddScoped<AIInsights.Services.Datasources.IDatasourceTypeService, AIInsights.Services.Datasources.SqlDatasourceService>();
builder.Services.AddScoped<AIInsights.Services.Datasources.IDatasourceTypeService, AIInsights.Services.Datasources.PowerBiDatasourceService>();
builder.Services.AddScoped<AIInsights.Services.Datasources.IDatasourceTypeService, AIInsights.Services.Datasources.RestApiDatasourceService>();
builder.Services.AddScoped<AIInsights.Services.Datasources.IDatasourceTypeService, AIInsights.Services.Datasources.FileUrlDatasourceService>();

// Per-rule-type handlers for the AI Insights 365 ETL workbench. Registered as
// singletons because each handler is a stateless pure-function class.
// Adding a new rule type requires only a new ITransformRuleHandler implementation
// and a registration here — no changes to the core switch or any allowlist.
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.RemoveDuplicatesHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.HandleNullsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.ReplaceValuesHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.SplitColumnHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.StandardizeFormatHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.FilterRowsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.SortRowsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.SelectColumnsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.RenameColumnsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.CastTypesHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.PivotTableHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.UnpivotTableHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.TransposeTableHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.AppendRowsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.MergeTablesHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.AggregateHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.ValidateSchemaHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.ReferentialIntegrityHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.DerivedFieldHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.ConvertUnitsHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.ApplyNamingConventionHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.MapCodesHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.KpiClassificationHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.SentimentScoreHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.FlattenJsonHandler>();
builder.Services.AddSingleton<AIInsights.Services.Transforms.ITransformRuleHandler, AIInsights.Services.Transforms.LogTransformationsHandler>();
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

// File URL datasource (CSV / XLSX public share links — anonymous fetch, no storage)
builder.Services.AddHttpClient("fileds");
builder.Services.AddSingleton<IFileDatasourceService, FileDatasourceService>();
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

// Configure antiforgery to accept tokens via request header (for AJAX/SPA calls)
builder.Services.AddAntiforgery(opts =>
{
    opts.HeaderName = "RequestVerificationToken";
});

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

// Issue XSRF-TOKEN cookie so JS can read and send it in fetch calls for CSRF-protected API endpoints
app.Use(async (context, next) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    // HttpOnly=false is intentional: JS needs to read this cookie to send the header
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
    await next();
});

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

// Browsers auto-request /favicon.ico regardless of <link rel="icon"> tags.
// We only ship favicon.svg, so serve it (with the correct content-type) at /favicon.ico
// to prevent 404s in logs and crawler reports.
app.MapGet("/favicon.ico", async context =>
{
    var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    var svgPath = Path.Combine(env.WebRootPath, "favicon.svg");
    if (!File.Exists(svgPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    context.Response.ContentType = "image/svg+xml";
    context.Response.Headers.CacheControl = "public, max-age=86400";
    await context.Response.SendFileAsync(svgPath);
});

app.Run();
