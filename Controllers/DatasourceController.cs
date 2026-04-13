using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/datasources")]
[ApiController]
public class DatasourceController : ControllerBase
{
    private static readonly List<string> DatasourceTypes = new()
    {
        "SQL Server",
        "Power BI",
        // TODO: Additional datasource types are disabled pending SQL Server-first rollout
        // "PostgreSQL", "MySQL", "MariaDB", "Oracle", "MongoDB",
        // "Redis", "Cassandra", "CouchDB", "DynamoDB", "Firebase Realtime DB", "Firestore",
        // "Elasticsearch", "OpenSearch", "Solr", "InfluxDB", "TimescaleDB", "QuestDB",
        // "REST API", "GraphQL API", "SOAP / WSDL", "OData", "WebSocket",
        // "CSV", "Excel (XLSX)", "Google Sheets", "JSON File", "XML File",
        // "Parquet", "Avro", "ORC", "Feather", "HDF5",
        // "Snowflake", "BigQuery", "Amazon Redshift", "Azure Synapse", "Databricks",
        // "Teradata", "IBM Db2", "SAP HANA", "Vertica", "Greenplum",
        // "Amazon S3", "Azure Blob Storage", "Google Cloud Storage", "HDFS",
        // "Apache Kafka", "Apache Spark", "Apache Flink", "RabbitMQ", "Azure Event Hubs",
        // "Salesforce", "HubSpot", "Zendesk", "Shopify", "Stripe",
        // "Google Analytics", "Mixpanel", "Amplitude", "Segment", "Heap",
        // "Looker", "Tableau", "Metabase", "Mode Analytics",
        // "Airtable", "Notion", "Coda", "Smartsheet",
        // "GitHub", "GitLab", "Jira", "Confluence", "Linear",
        // "Slack", "Microsoft Teams", "Discord",
        // "MySQL Cluster", "CockroachDB", "PlanetScale", "Neon", "Supabase",
        // "FTP / SFTP", "Email (IMAP/SMTP)", "SMS API", "Push Notification Service",
        // "Custom JDBC", "Custom ODBC", "In-Memory Cache"
    };

    private readonly AppDbContext _db;
    private readonly IQueryExecutionService _queryService;
    private readonly IWorkspacePermissionService _permissions;
    private readonly IDataProtectionService _dataProtection;

    public DatasourceController(AppDbContext db, IQueryExecutionService queryService, IWorkspacePermissionService permissions, IDataProtectionService dataProtection)
    {
        _db = db;
        _queryService = queryService;
        _permissions = permissions;
        _dataProtection = dataProtection;
    }

    private async Task<int> ResolveOrganizationIdAsync(int supplied, string? userId)
    {
        if (supplied > 0) return supplied;
        if (!string.IsNullOrEmpty(userId))
        {
            var userOrgId = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.OrganizationId)
                .FirstOrDefaultAsync();
            if (userOrgId.HasValue && userOrgId.Value > 0) return userOrgId.Value;
        }
        var org = new Organization { Name = "Default Organization" };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(userId))
        {
            var userEntity = await _db.Users.FindAsync(userId);
            if (userEntity != null)
            {
                userEntity.OrganizationId = org.Id;
                await _db.SaveChangesAsync();
            }
        }

        return org.Id;
    }

    [HttpGet("types")]
    public IActionResult GetTypes() => Ok(DatasourceTypes);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int organizationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins are always scoped to their own org.
        if (appUser?.Role != "SuperAdmin")
        {
            if (callerOrgId <= 0)
                return StatusCode(403, new { error = "User is not assigned to an organization." });
            organizationId = callerOrgId;
        }

        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";

        var query = _db.Datasources.Where(d => d.OrganizationId == organizationId);

        if (!isOrgLevel)
        {
            query = query.Where(d =>
                !d.WorkspaceId.HasValue ||
                _db.Workspaces.Any(w => w.Id == d.WorkspaceId && w.OwnerId == userId) ||
                _db.WorkspaceUsers.Any(wu => wu.WorkspaceId == d.WorkspaceId && wu.UserId == userId));
        }

        var datasources = await query.ToListAsync();
        return Ok(datasources);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DatasourceRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";

        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            if (!await _permissions.CanEditAsync(req.WorkspaceId.Value, userId))
                return StatusCode(403, new { error = "You need Editor or Admin role to create datasources." });
        }

        var orgId = await ResolveOrganizationIdAsync(req.OrganizationId, userId);

        // Power BI requires an XMLA endpoint
        if (string.Equals(req.Type, "Power BI", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(req.XmlaEndpoint))
            return BadRequest(new { error = "XMLA Endpoint is required for Power BI datasources." });

        var ds = new Datasource
        {
            Name = req.Name ?? "New Datasource",
            Type = req.Type ?? "SQL Server",
            ConnectionString = !string.IsNullOrEmpty(req.ConnectionString) ? _dataProtection.Protect(req.ConnectionString) : "",
            DbUser = !string.IsNullOrEmpty(req.DbUser) ? _dataProtection.Protect(req.DbUser) : null,
            DbPassword = !string.IsNullOrEmpty(req.DbPassword) ? _dataProtection.Protect(req.DbPassword) : null,
            XmlaEndpoint = req.XmlaEndpoint,
            MicrosoftAccountTenantId = req.MicrosoftAccountTenantId,
            OrganizationId = orgId,
            WorkspaceId = req.WorkspaceId
        };
        _db.Datasources.Add(ds);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "datasource_created",
            Description = $"Datasource '{ds.Name}' ({ds.Type}) connected.",
            UserId = userId,
            OrganizationId = orgId
        });
        await _db.SaveChangesAsync();

        return Ok(new { ds.Id, ds.Guid, ds.Name, ds.Type, ds.OrganizationId });
    }

    [HttpGet("{id}/fields")]
    public async Task<IActionResult> GetFields(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this datasource." });
            }
        }

        if (!string.IsNullOrWhiteSpace(ds.ConnectionString))
        {
            try
            {
                var sql = GetFieldsQuery(ds.Type, ds.SelectedTables);
                if (sql != null)
                {
                    var result = await _queryService.ExecuteReadOnlyAsync(ds, sql);
                    if (result.Success && result.Data.Count > 0)
                    {
                        var fields = result.Data
                            .Select(r => r.Values.First()?.ToString() ?? "")
                            .Where(f => !string.IsNullOrEmpty(f))
                            .Distinct()
                            .ToList();
                        if (fields.Count > 0) return Ok(fields);
                    }
                }
            }
            catch { /* fall through to placeholder */ }
        }

        var placeholderFields = new List<string> { "id", "name", "region", "revenue", "date", "category", "quantity", "price", "status" };
        return Ok(placeholderFields);
    }

    private static string? GetFieldsQuery(string type, string? selectedTables)
    {
        var t = type?.Trim() ?? "";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) || t.Equals("Power BI", StringComparison.OrdinalIgnoreCase))
            return "SELECT DISTINCT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS ORDER BY COLUMN_NAME";
        if (t.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            return "SELECT DISTINCT column_name FROM information_schema.columns WHERE table_schema = 'public' ORDER BY column_name";
        if (t.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || t.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
            return "SELECT DISTINCT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY COLUMN_NAME";
        return null;
    }

    [HttpGet("{id}/tables")]
    public async Task<IActionResult> GetTables(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this datasource." });
            }
        }

        if (!string.IsNullOrWhiteSpace(ds.ConnectionString))
        {
            try
            {
                var sql = GetTablesQuery(ds.Type);
                if (sql != null)
                {
                    var result = await _queryService.ExecuteReadOnlyAsync(ds, sql);
                    if (result.Success && result.Data.Count > 0)
                    {
                        var tables = result.Data.Select(r =>
                        {
                            var name = r.ContainsKey("table_name") ? r["table_name"]?.ToString()
                                     : r.ContainsKey("TABLE_NAME") ? r["TABLE_NAME"]?.ToString()
                                     : r.ContainsKey("name") ? r["name"]?.ToString()
                                     : r.Values.FirstOrDefault()?.ToString() ?? "";
                            var rawType = r.ContainsKey("table_type") ? r["table_type"]?.ToString()
                                        : r.ContainsKey("TABLE_TYPE") ? r["TABLE_TYPE"]?.ToString()
                                        : r.ContainsKey("type") ? r["type"]?.ToString()
                                        : "Table";
                            var ttype = rawType?.Contains("VIEW", StringComparison.OrdinalIgnoreCase) == true ? "View" : "Table";
                            return new { name, type = ttype, rowCount = 0 };
                        }).Where(t => !string.IsNullOrEmpty(t.name)).ToList();
                        if (tables.Count > 0) return Ok(tables);
                    }
                }
            }
            catch { /* fall through to placeholder */ }
        }

        var placeholderTables = new List<object>
        {
            new { name = "Customers", type = "Table", rowCount = 15420 },
            new { name = "Orders", type = "Table", rowCount = 89230 },
            new { name = "Products", type = "Table", rowCount = 3500 },
            new { name = "Sales", type = "Table", rowCount = 245600 },
            new { name = "Employees", type = "Table", rowCount = 580 },
            new { name = "vw_MonthlyRevenue", type = "View", rowCount = 0 },
            new { name = "vw_CustomerSummary", type = "View", rowCount = 0 },
            new { name = "vw_TopProducts", type = "View", rowCount = 0 }
        };
        return Ok(placeholderTables);
    }

    private static string? GetTablesQuery(string type)
    {
        var t = type?.Trim() ?? "";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) || t.Equals("Power BI", StringComparison.OrdinalIgnoreCase))
            return "SELECT TABLE_SCHEMA + '.' + TABLE_NAME as table_name, TABLE_TYPE as table_type FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_TYPE, TABLE_SCHEMA, TABLE_NAME";
        if (t.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            return "SELECT table_name, table_type FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_type, table_name";
        if (t.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || t.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
            return "SELECT TABLE_NAME as table_name, TABLE_TYPE as table_type FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_TYPE, TABLE_NAME";
        return null;
    }

    [HttpGet("{id}/schema")]
    public async Task<IActionResult> GetSchema(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this datasource." });
            }
        }

        List<object>? schema = null;

        // Try real DB introspection
        if (!string.IsNullOrWhiteSpace(ds.ConnectionString))
        {
            try
            {
                schema = await BuildRealSchemaAsync(ds);
            }
            catch { /* fall through to placeholder */ }
        }

        schema ??= BuildPlaceholderSchema(ds);

        return Ok(new
        {
            datasourceId = ds.Id,
            datasourceGuid = ds.Guid,
            datasourceName = ds.Name,
            datasourceType = ds.Type,
            tables = schema
        });
    }

    private async Task<List<object>?> BuildRealSchemaAsync(Datasource ds)
    {
        var sql = GetSchemaQuery(ds.Type);
        if (sql == null) return null;

        var result = await _queryService.ExecuteReadOnlyAsync(ds, sql);
        if (!result.Success || result.Data.Count == 0) return null;

        var grouped = result.Data
            .GroupBy(r => r.ContainsKey("table_name") ? r["table_name"]?.ToString() ?? "" : "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => (object)new
            {
                name = g.Key,
                type = g.First().ContainsKey("table_type")
                    ? (g.First()["table_type"]?.ToString()?.Contains("VIEW", StringComparison.OrdinalIgnoreCase) == true ? "View" : "Table")
                    : "Table",
                columns = g.Select(r => new
                {
                    name = r.ContainsKey("column_name") ? r["column_name"]?.ToString() ?? "" : "",
                    dataType = r.ContainsKey("data_type") ? r["data_type"]?.ToString() ?? "" : "",
                    isPrimaryKey = false
                }).Where(c => !string.IsNullOrEmpty(c.name)).ToList()
            }).ToList();

        return grouped.Count > 0 ? grouped : null;
    }

    private static string? GetSchemaQuery(string type)
    {
        var t = type?.Trim() ?? "";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) || t.Equals("Power BI", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME as table_name, t.TABLE_TYPE as table_type, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";
        if (t.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.table_name, t.table_type, c.column_name, c.data_type FROM information_schema.tables t JOIN information_schema.columns c ON c.table_name = t.table_name AND c.table_schema = t.table_schema WHERE t.table_schema = 'public' ORDER BY t.table_name, c.ordinal_position";
        if (t.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || t.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.TABLE_NAME as table_name, t.TABLE_TYPE as table_type, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA WHERE t.TABLE_SCHEMA = DATABASE() ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";
        return null;
    }

    private static List<object> BuildPlaceholderSchema(Datasource ds)
    {
        var tableNames = string.IsNullOrEmpty(ds.SelectedTables)
            ? new[] { "Customers", "Orders", "Products", "Sales", "Employees" }
            : ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var knownSchemas = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customers"] = new object[] {
                new { name = "Id", dataType = "int", isPrimaryKey = true },
                new { name = "Name", dataType = "nvarchar(200)", isPrimaryKey = false },
                new { name = "Email", dataType = "nvarchar(200)", isPrimaryKey = false },
                new { name = "Region", dataType = "nvarchar(50)", isPrimaryKey = false },
                new { name = "CreatedAt", dataType = "datetime", isPrimaryKey = false }
            },
            ["Orders"] = new object[] {
                new { name = "Id", dataType = "int", isPrimaryKey = true },
                new { name = "CustomerId", dataType = "int", isPrimaryKey = false },
                new { name = "OrderDate", dataType = "datetime", isPrimaryKey = false },
                new { name = "TotalAmount", dataType = "decimal(18,2)", isPrimaryKey = false },
                new { name = "Status", dataType = "nvarchar(20)", isPrimaryKey = false },
                new { name = "Region", dataType = "nvarchar(50)", isPrimaryKey = false }
            },
            ["Products"] = new object[] {
                new { name = "Id", dataType = "int", isPrimaryKey = true },
                new { name = "Name", dataType = "nvarchar(200)", isPrimaryKey = false },
                new { name = "Category", dataType = "nvarchar(50)", isPrimaryKey = false },
                new { name = "Price", dataType = "decimal(18,2)", isPrimaryKey = false },
                new { name = "StockQuantity", dataType = "int", isPrimaryKey = false }
            },
            ["Sales"] = new object[] {
                new { name = "Id", dataType = "int", isPrimaryKey = true },
                new { name = "ProductId", dataType = "int", isPrimaryKey = false },
                new { name = "CustomerId", dataType = "int", isPrimaryKey = false },
                new { name = "Quantity", dataType = "int", isPrimaryKey = false },
                new { name = "TotalRevenue", dataType = "decimal(18,2)", isPrimaryKey = false },
                new { name = "SaleDate", dataType = "datetime", isPrimaryKey = false },
                new { name = "Region", dataType = "nvarchar(50)", isPrimaryKey = false }
            },
            ["Employees"] = new object[] {
                new { name = "Id", dataType = "int", isPrimaryKey = true },
                new { name = "Name", dataType = "nvarchar(100)", isPrimaryKey = false },
                new { name = "Department", dataType = "nvarchar(50)", isPrimaryKey = false },
                new { name = "HireDate", dataType = "datetime", isPrimaryKey = false },
                new { name = "Salary", dataType = "decimal(18,2)", isPrimaryKey = false }
            },
            ["vw_MonthlyRevenue"] = new object[] {
                new { name = "Month", dataType = "nvarchar(20)", isPrimaryKey = false },
                new { name = "TotalRevenue", dataType = "decimal(18,2)", isPrimaryKey = false },
                new { name = "OrderCount", dataType = "int", isPrimaryKey = false }
            },
            ["vw_CustomerSummary"] = new object[] {
                new { name = "CustomerId", dataType = "int", isPrimaryKey = false },
                new { name = "CustomerName", dataType = "nvarchar(200)", isPrimaryKey = false },
                new { name = "TotalOrders", dataType = "int", isPrimaryKey = false },
                new { name = "TotalSpent", dataType = "decimal(18,2)", isPrimaryKey = false }
            },
            ["vw_TopProducts"] = new object[] {
                new { name = "ProductId", dataType = "int", isPrimaryKey = false },
                new { name = "ProductName", dataType = "nvarchar(200)", isPrimaryKey = false },
                new { name = "TotalSold", dataType = "int", isPrimaryKey = false },
                new { name = "Revenue", dataType = "decimal(18,2)", isPrimaryKey = false }
            }
        };

        var genericColumns = new object[] {
            new { name = "Id", dataType = "int", isPrimaryKey = true },
            new { name = "Name", dataType = "nvarchar(200)", isPrimaryKey = false },
            new { name = "Value", dataType = "decimal(18,2)", isPrimaryKey = false },
            new { name = "CreatedAt", dataType = "datetime", isPrimaryKey = false }
        };

        return tableNames.Select(t =>
        {
            var trimmed = t.Trim();
            var isView = trimmed.StartsWith("vw_", StringComparison.OrdinalIgnoreCase);
            var cols = knownSchemas.TryGetValue(trimmed, out var c) ? c : genericColumns;
            return (object)new { name = trimmed, type = isView ? "View" : "Table", columns = cols };
        }).ToList();
    }

    [HttpPut("{guid}")]
    public async Task<IActionResult> Update(string guid, [FromBody] DatasourceRequest req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot modify datasources from another organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        var wsId = ds.WorkspaceId ?? req.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanEditAsync(wsId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update datasources." });

        if (req.Name != null) ds.Name = req.Name;
        if (req.DbUser != null) ds.DbUser = !string.IsNullOrEmpty(req.DbUser) ? _dataProtection.Protect(req.DbUser) : null;
        if (req.DbPassword != null) ds.DbPassword = !string.IsNullOrEmpty(req.DbPassword) ? _dataProtection.Protect(req.DbPassword) : null;
        if (req.SelectedTables != null) ds.SelectedTables = req.SelectedTables;

        await _db.SaveChangesAsync();
        return Ok(new { ds.Id, ds.Guid, ds.Name, ds.SelectedTables });
    }

    [HttpDelete("{guid}")]
    public async Task<IActionResult> Delete(string guid)
    {
        Datasource? ds = null;
        if (int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null)
            ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot delete datasources from another organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        var wsId = ds.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanDeleteAsync(wsId, userId))
            return StatusCode(403, new { error = "Only Admins can delete datasources." });

        // Null out Agent references to avoid FK constraint failures
        var agents = await _db.Agents.Where(a => a.DatasourceId == ds.Id).ToListAsync();
        foreach (var a in agents) a.DatasourceId = null;

        _db.Datasources.Remove(ds);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class DatasourceRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? ConnectionString { get; set; }
    public string? DbUser { get; set; }
    public string? DbPassword { get; set; }
    public string? SelectedTables { get; set; }
    public string? XmlaEndpoint { get; set; }
    public string? MicrosoftAccountTenantId { get; set; }
    public int OrganizationId { get; set; }
    public int? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}
