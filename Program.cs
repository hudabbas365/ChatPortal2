using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Entity Framework + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ASP.NET Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key 'Jwt:Key' is not configured in appsettings.json.");
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
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
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

// Service DI registrations
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CohereService>();
builder.Services.AddScoped<ISeoService, SeoService>();
builder.Services.AddSingleton<IChartService, ChartService>();
builder.Services.AddSingleton<IDataService, DataService>();
builder.Services.AddSingleton<IPowerBiService, PowerBiService>();
builder.Services.AddSingleton<IQueryExecutionService, QueryExecutionService>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<IWorkspacePermissionService, WorkspacePermissionService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<ITokenBudgetService, TokenBudgetService>();
builder.Services.AddScoped<ITrialEnforcementService, TrialEnforcementService>();

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
    db.Database.EnsureCreated();

    var seoService = scope.ServiceProvider.GetRequiredService<ISeoService>();
    await seoService.SeedDefaultEntriesAsync();
}

// Middleware pipeline
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
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
