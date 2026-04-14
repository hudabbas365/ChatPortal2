using System.Text;
using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.SuperAdmin.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key 'Jwt:Key' is not configured.");

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
builder.Services.AddScoped<ChatPortal2.Services.CohereService>();
builder.Services.AddScoped<SuperAdminJwtService>();
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson()
    .ConfigureApplicationPartManager(manager =>
    {
        // Remove the main ChatPortal2 assembly so its controllers are not discovered
        var mainPart = manager.ApplicationParts
            .FirstOrDefault(p => p.Name == "ChatPortal2");
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
