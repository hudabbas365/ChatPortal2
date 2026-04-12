using System.Text;
using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Entity Framework + SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddSingleton<IQueryExecutionService, QueryExecutionService>();
builder.Services.AddScoped<SubscriptionService>();

// Session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

// MVC with views + Newtonsoft.Json
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson();

var app = builder.Build();

// Database initialization and SEO seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Apply any schema columns added after initial EnsureCreated (SQLite has no migration support here)
    await ApplySchemaPatchesAsync(db);

    var seoService = scope.ServiceProvider.GetRequiredService<ISeoService>();
    await seoService.SeedDefaultEntriesAsync();
}

async Task ApplySchemaPatchesAsync(AppDbContext db)
{
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    try
    {
        // Check and add missing columns to AspNetUsers
        var existing = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('AspNetUsers')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existing.Add(reader.GetString(1)); // column name is index 1
        }

        var patches = new[]
        {
            ("Status", "TEXT NOT NULL DEFAULT 'Active'"),
        };

        foreach (var (col, def) in patches)
        {
            if (!existing.Contains(col))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE AspNetUsers ADD COLUMN \"{col}\" {def}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Create WorkspaceUsers table if it doesn't exist
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS WorkspaceUsers (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    WorkspaceId INTEGER NOT NULL,
                    UserId    TEXT NOT NULL,
                    Role      TEXT NOT NULL DEFAULT 'Viewer',
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UNIQUE(WorkspaceId, UserId),
                    FOREIGN KEY(WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE,
                    FOREIGN KEY(UserId)      REFERENCES AspNetUsers(Id) ON DELETE CASCADE
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        // Create WorkspaceMemories table if it doesn't exist
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS WorkspaceMemories (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    WorkspaceId INTEGER NOT NULL,
                    Content     TEXT NOT NULL DEFAULT '',
                    Source      TEXT NOT NULL DEFAULT 'auto',
                    Category    TEXT NOT NULL DEFAULT 'general',
                    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY(WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        // Create ActivityLogs table if it doesn't exist
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ActivityLogs (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    Action         TEXT NOT NULL DEFAULT '',
                    Description    TEXT NOT NULL DEFAULT '',
                    UserId         TEXT NOT NULL DEFAULT '',
                    OrganizationId INTEGER,
                    CreatedAt      TEXT NOT NULL DEFAULT (datetime('now'))
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add missing columns to Workspaces (added in model but never migrated)
        var wsCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Workspaces')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                wsCols.Add(reader.GetString(1));
        }

        var wsPatches = new[]
        {
            ("Description", "TEXT"),
            ("LogoUrl", "TEXT"),
            ("OwnerId", "TEXT"),
            ("Guid", "TEXT"),
        };

        foreach (var (col, def) in wsPatches)
        {
            if (!wsCols.Contains(col))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE Workspaces ADD COLUMN \"{col}\" {def}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Backfill Guid on existing workspaces if null
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Workspaces SET Guid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))) WHERE Guid IS NULL OR Guid = ''";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add missing columns to Agents
        var agCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Agents')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                agCols.Add(reader.GetString(1));
        }

        var agPatches = new[]
        {
            ("Guid", "TEXT"),
            ("WorkspaceId", "INTEGER"),
        };

        foreach (var (col, def) in agPatches)
        {
            if (!agCols.Contains(col))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE Agents ADD COLUMN \"{col}\" {def}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Backfill Guid on existing agents
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Agents SET Guid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))) WHERE Guid IS NULL OR Guid = ''";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add missing columns to Datasources
        var dsCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Datasources')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                dsCols.Add(reader.GetString(1));
        }

        if (!dsCols.Contains("Guid"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Datasources ADD COLUMN \"Guid\" TEXT";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Datasources SET Guid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))) WHERE Guid IS NULL OR Guid = ''";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add credential and table selection columns to Datasources
        var dsPatches2 = new[]
        {
            ("DbUser", "TEXT"),
            ("DbPassword", "TEXT"),
            ("SelectedTables", "TEXT"),
            ("WorkspaceId", "INTEGER"),
        };
        foreach (var (col, def) in dsPatches2)
        {
            if (!dsCols.Contains(col))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE Datasources ADD COLUMN \"{col}\" {def}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Create Dashboards table if it doesn't exist
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Dashboards (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Guid        TEXT NOT NULL DEFAULT '',
                    Name        TEXT NOT NULL DEFAULT 'Dashboard',
                    WorkspaceId INTEGER NOT NULL,
                    AgentId     INTEGER,
                    DatasourceId INTEGER,
                    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY(WorkspaceId) REFERENCES Workspaces(Id) ON DELETE CASCADE,
                    FOREIGN KEY(AgentId) REFERENCES Agents(Id) ON DELETE SET NULL,
                    FOREIGN KEY(DatasourceId) REFERENCES Datasources(Id) ON DELETE SET NULL
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add DatasourceId column to Dashboards if missing
        var dashCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Dashboards')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                dashCols.Add(reader.GetString(1));
        }
        if (!dashCols.Contains("DatasourceId"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Dashboards ADD COLUMN \"DatasourceId\" INTEGER";
            await cmd.ExecuteNonQueryAsync();
        }

        // Create Reports table if it doesn't exist
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Reports (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Guid         TEXT NOT NULL DEFAULT '',
                    Name         TEXT NOT NULL DEFAULT 'Untitled Report',
                    WorkspaceId  INTEGER NOT NULL,
                    DashboardId  INTEGER,
                    DatasourceId INTEGER,
                    AgentId      INTEGER,
                    ChartIds     TEXT,
                    CanvasJson   TEXT,
                    Status       TEXT NOT NULL DEFAULT 'Draft',
                    CreatedBy    TEXT,
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY(WorkspaceId)  REFERENCES Workspaces(Id)  ON DELETE CASCADE,
                    FOREIGN KEY(DashboardId)  REFERENCES Dashboards(Id)  ON DELETE SET NULL,
                    FOREIGN KEY(DatasourceId) REFERENCES Datasources(Id) ON DELETE SET NULL,
                    FOREIGN KEY(AgentId)      REFERENCES Agents(Id)      ON DELETE SET NULL
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        // Add AgentId column to ChatMessages if missing
        var cmCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('ChatMessages')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cmCols.Add(reader.GetString(1));
        }
        if (!cmCols.Contains("AgentId"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE ChatMessages ADD COLUMN \"AgentId\" TEXT";
            await cmd.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await conn.CloseAsync();
    }
}

// Middleware pipeline
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();

app.Run();
