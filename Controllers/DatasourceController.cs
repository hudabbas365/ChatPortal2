using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
[Route("api/datasources")]
[ApiController]
public class DatasourceController : ControllerBase
{
    private static readonly List<string> DatasourceTypes = new()
    {
        "SQL Server",
        "Power BI",
        "REST API",
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
    private readonly IEncryptionService _encryption;
    private readonly IRelationshipService _relationships;
    private readonly IQueryCacheInvalidator _cacheInvalidator;

    public DatasourceController(AppDbContext db, IQueryExecutionService queryService, IWorkspacePermissionService permissions, IEncryptionService encryption, IRelationshipService relationships, IQueryCacheInvalidator cacheInvalidator)
    {
        _db = db;
        _queryService = queryService;
        _permissions = permissions;
        _encryption = encryption;
        _relationships = relationships;
        _cacheInvalidator = cacheInvalidator;
    }

    // Manual cache refresh — flushes every cached query result for this datasource so the
    // next request re-runs against the live database. Surfaced to the UI via a refresh
    // button on the datasource details modal.
    [HttpPost("{guid}/refresh-cache")]
    public async Task<IActionResult> RefreshCache(string guid)
    {
        Datasource? ds = null;
        if (int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        ds ??= await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0
            && !await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId)
            && appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        _cacheInvalidator.InvalidateDatasource(ds.Id);
        return Ok(new { success = true, datasourceId = ds.Id, datasourceGuid = ds.Guid });
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
        var result = datasources.Select(d => new
        {
            d.Id,
            d.Guid,
            d.Name,
            d.Type,
            ConnectionString = MaskConnectionString(_encryption.Decrypt(d.ConnectionString)),
            DbUser = !string.IsNullOrEmpty(d.DbUser) ? "••••••" : null,
            DbPassword = !string.IsNullOrEmpty(d.DbPassword) ? "••••••" : null,
            d.SelectedTables,
            XmlaEndpoint = !string.IsNullOrEmpty(d.XmlaEndpoint) ? "••••••" : null,
            d.OrganizationId,
            d.WorkspaceId,
            d.CreatedAt
        });
        return Ok(result);
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] DatasourceRequest req)
    {
        var type = req.Type ?? "SQL Server";
        var isRestApi = string.Equals(type, "REST API", StringComparison.OrdinalIgnoreCase);

        if (isRestApi)
        {
            var (success, error) = await _queryService.TestRestApiAsync(req.ApiUrl, req.ApiKey, req.ApiMethod);
            if (!success)
                return BadRequest(new { connected = false, error = error ?? "REST API connection failed." });
            return Ok(new { connected = true });
        }

        var (dbSuccess, dbError) = await _queryService.TestConnectionAsync(
            type,
            req.ConnectionString ?? "",
            req.DbUser,
            req.DbPassword,
            req.XmlaEndpoint,
            req.MicrosoftAccountTenantId);

        if (!dbSuccess)
            return BadRequest(new { connected = false, error = dbError ?? "Connection failed." });

        return Ok(new { connected = true });
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
            ConnectionString = _encryption.Encrypt(req.ConnectionString ?? ""),
            DbUser = _encryption.Encrypt(req.DbUser ?? ""),
            DbPassword = _encryption.Encrypt(req.DbPassword ?? ""),
            XmlaEndpoint = _encryption.Encrypt(req.XmlaEndpoint ?? ""),
            MicrosoftAccountTenantId = _encryption.Encrypt(req.MicrosoftAccountTenantId ?? ""),
            ApiUrl = _encryption.Encrypt(req.ApiUrl ?? ""),
            ApiKey = _encryption.Encrypt(req.ApiKey ?? ""),
            ApiMethod = req.ApiMethod ?? "GET",
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

        // REST API: extract field names from a sample API call
        if (string.Equals(ds.Type, "REST API", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                {
                    var fields = apiResult.Data.First().Keys.ToList();
                    if (fields.Count > 0) return Ok(fields);
                }
            }
            catch { /* fall through */ }
            return Ok(new List<string> { "id", "name", "value", "status" });
        }

        var isPbiFields = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
        var hasConnFields = isPbiFields
            ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
            : !string.IsNullOrWhiteSpace(ds.ConnectionString);

        if (hasConnFields)
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
        if (t.Contains("Power BI", StringComparison.OrdinalIgnoreCase) || t.Equals("PowerBI", StringComparison.OrdinalIgnoreCase))
            return "EVALUATE SELECTCOLUMNS(FILTER(INFO.COLUMNS(), NOT [IsHidden]), \"COLUMN_NAME\", [ExplicitName])";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
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

        // REST API: return a single virtual "table" representing the API endpoint
        if (string.Equals(ds.Type, "REST API", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                {
                    var tableName = ds.Name.Replace(" ", "_");
                    return Ok(new[] { new { name = tableName, type = "API Endpoint", rowCount = apiResult.Data.Count } });
                }
                // API returned an error — include the virtual table but attach the error message
                return Ok(new[] { new { name = ds.Name.Replace(" ", "_"), type = "API Endpoint", rowCount = 0, error = apiResult.Error ?? "REST API returned no data." } });
            }
            catch (Exception ex)
            {
                return Ok(new[] { new { name = ds.Name.Replace(" ", "_"), type = "API Endpoint", rowCount = 0, error = $"REST API connection failed: {ex.Message}" } });
            }
        }

        var isPbi = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
        var hasConnection = isPbi
            ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
            : !string.IsNullOrWhiteSpace(ds.ConnectionString);

        if (hasConnection)
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
        if (t.Contains("Power BI", StringComparison.OrdinalIgnoreCase) || t.Equals("PowerBI", StringComparison.OrdinalIgnoreCase))
            return "EVALUATE SELECTCOLUMNS(FILTER(INFO.TABLES(), NOT [IsHidden]), \"table_name\", [Name], \"table_type\", \"Table\")";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
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

        // REST API: build schema from sample response
        if (string.Equals(ds.Type, "REST API", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                {
                    var firstRow = apiResult.Data.First();
                    var columns = firstRow.Select(kv => (object)new
                    {
                        name = kv.Key,
                        dataType = InferJsonType(kv.Value),
                        isPrimaryKey = string.Equals(kv.Key, "id", StringComparison.OrdinalIgnoreCase)
                    }).ToList();
                    schema = new List<object>
                    {
                        new { name = ds.Name.Replace(" ", "_"), type = "API Endpoint", columns }
                    };
                }
            }
            catch { /* fall through */ }
            schema ??= new List<object>();
        }
        else
        {
            // Try real DB introspection
            var isPbiSchema = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
            var hasConnSchema = isPbiSchema
                ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
                : !string.IsNullOrWhiteSpace(ds.ConnectionString);

            if (hasConnSchema)
            {
                try
                {
                    schema = await BuildRealSchemaAsync(ds);
                }
                catch { /* fall through to placeholder */ }
            }

            schema ??= BuildPlaceholderSchema(ds);
        }

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
        if (t.Contains("Power BI", StringComparison.OrdinalIgnoreCase) || t.Equals("PowerBI", StringComparison.OrdinalIgnoreCase))
            return "EVALUATE VAR _tables = SELECTCOLUMNS(FILTER(INFO.TABLES(), NOT [IsHidden]), \"TableID\", [ID], \"table_name\", [Name]) " +
                   "VAR _cols = SELECTCOLUMNS(FILTER(INFO.COLUMNS(), NOT [IsHidden]), \"TableID\", [TableID], \"column_name\", [ExplicitName], " +
                   "\"data_type\", SWITCH([DataType], 2, \"String\", 6, \"Int64\", 8, \"Double\", 9, \"DateTime\", 10, \"Decimal\", 11, \"Boolean\", \"Other\")) " +
                   "RETURN SELECTCOLUMNS(NATURALLEFTOUTERJOIN(_tables, _cols), \"table_name\", [table_name], \"table_type\", \"Table\", \"column_name\", [column_name], \"data_type\", [data_type])";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || t.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
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
        if (req.ConnectionString != null) ds.ConnectionString = _encryption.Encrypt(req.ConnectionString);
        if (req.DbUser != null) ds.DbUser = _encryption.Encrypt(req.DbUser);
        if (req.DbPassword != null) ds.DbPassword = _encryption.Encrypt(req.DbPassword);
        if (req.XmlaEndpoint != null) ds.XmlaEndpoint = _encryption.Encrypt(req.XmlaEndpoint);
        if (req.MicrosoftAccountTenantId != null) ds.MicrosoftAccountTenantId = _encryption.Encrypt(req.MicrosoftAccountTenantId);
        if (req.SelectedTables != null) ds.SelectedTables = req.SelectedTables;
        if (req.ApiUrl != null) ds.ApiUrl = _encryption.Encrypt(req.ApiUrl);
        if (req.ApiKey != null) ds.ApiKey = _encryption.Encrypt(req.ApiKey);
        if (req.ApiMethod != null) ds.ApiMethod = req.ApiMethod;

        await _db.SaveChangesAsync();
        // Flush any cached query results for this datasource — connection details or
        // selected tables may have changed, so stale results must not be served.
        _cacheInvalidator.InvalidateDatasource(ds.Id);
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
        _cacheInvalidator.InvalidateDatasource(ds.Id);
        return Ok(new { success = true });
    }

    private static string MaskConnectionString(string connStr)
    {
        if (string.IsNullOrEmpty(connStr)) return "";
        // Mask password values in the connection string
        var masked = System.Text.RegularExpressions.Regex.Replace(
            connStr,
            @"(Password|Pwd)\s*=\s*[^;]+",
            "$1=••••••",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return masked;
    }

    private static string InferJsonType(object? value)
    {
        if (value == null) return "string";
        if (value is bool) return "boolean";
        if (value is int or long or short or byte) return "integer";
        if (value is float or double or decimal) return "decimal";
        var s = value.ToString() ?? "";
        if (DateTime.TryParse(s, out _)) return "datetime";
        return "string";
    }

    [HttpGet("{id}/relationships")]
    public async Task<IActionResult> GetRelationships(string id)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == id);
        if (ds == null && int.TryParse(id, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        if (appUser?.Role != "SuperAdmin" && appUser?.OrganizationId > 0 && ds.OrganizationId != appUser.OrganizationId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        try
        {
            var rels = await _relationships.GetRelationshipsAsync(ds);
            return Ok(new
            {
                datasourceId = ds.Id,
                datasourceGuid = ds.Guid,
                relationships = rels.Select(r => new { r.FromTable, r.FromColumn, r.ToTable, r.ToColumn, r.Source, r.Confidence })
            });
        }
        catch (Exception ex)
        {
            return Ok(new { datasourceId = ds.Id, datasourceGuid = ds.Guid, relationships = Array.Empty<object>(), error = ex.Message });
        }
    }

    // ─── Phase 36 — Column Profiling (A11 stats + A9 quality) ─────────────────

    private static string QuoteIdent(string type, string name)
    {
        var safe = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ' ').ToArray());
        if (QueryExecutionService.PowerBiTypes.Contains(type)) return "'" + safe.Replace("'", "''") + "'";
        if (string.Equals(type, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Postgres", StringComparison.OrdinalIgnoreCase))
            return "\"" + safe + "\"";
        if (string.Equals(type, "MySQL", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "MariaDB", StringComparison.OrdinalIgnoreCase))
            return "`" + safe + "`";
        return "[" + safe + "]";
    }

    [HttpGet("{id}/profile")]
    public async Task<IActionResult> ProfileColumn(int id,
        [FromQuery] string table, [FromQuery] string column,
        [FromQuery] bool numeric = false, [FromQuery] int topN = 5)
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
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
            return BadRequest(new { error = "table and column are required." });
        if (QueryExecutionService.PowerBiTypes.Contains(ds.Type) || QueryExecutionService.RestApiTypes.Contains(ds.Type))
            return Ok(new { supported = false, reason = "Profiling is not supported for this datasource type." });

        var col = QuoteIdent(ds.Type, column);
        var tbl = QuoteIdent(ds.Type, table);
        var summarySql = numeric
            ? $"SELECT COUNT(*) AS total_count, COUNT({col}) AS non_null_count, COUNT(DISTINCT {col}) AS distinct_count, MIN({col}) AS min_val, MAX({col}) AS max_val, AVG(CAST({col} AS FLOAT)) AS avg_val FROM {tbl}"
            : $"SELECT COUNT(*) AS total_count, COUNT({col}) AS non_null_count, COUNT(DISTINCT {col}) AS distinct_count FROM {tbl}";
        var summary = await _queryService.ExecuteReadOnlyAsync(ds, summarySql, 1);
        if (!summary.Success || summary.Data.Count == 0)
            return Ok(new { supported = true, success = false, error = summary.Error ?? "No data returned." });
        var row = summary.Data[0];
        object? GetVal(string k) => row.TryGetValue(k, out var v) ? v : null;

        List<object>? topValues = null;
        try
        {
            var limit = Math.Max(1, Math.Min(20, topN));
            var isPgMy = string.Equals(ds.Type, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "Postgres", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "MySQL", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "MariaDB", StringComparison.OrdinalIgnoreCase);
            string topSql = isPgMy
                ? $"SELECT {col} AS value, COUNT(*) AS freq FROM {tbl} WHERE {col} IS NOT NULL GROUP BY {col} ORDER BY COUNT(*) DESC LIMIT {limit}"
                : $"SELECT TOP {limit} {col} AS value, COUNT(*) AS freq FROM {tbl} WHERE {col} IS NOT NULL GROUP BY {col} ORDER BY COUNT(*) DESC";
            var topRes = await _queryService.ExecuteReadOnlyAsync(ds, topSql, limit);
            if (topRes.Success)
                topValues = topRes.Data.Select(r => (object)new { value = r.TryGetValue("value", out var v) ? v : null, count = r.TryGetValue("freq", out var c) ? c : 0 }).ToList();
        }
        catch { }

        return Ok(new
        {
            supported = true,
            success = true,
            datasourceId = ds.Id,
            table,
            column,
            numeric,
            rowCount = GetVal("total_count"),
            nonNullCount = GetVal("non_null_count"),
            distinctCount = GetVal("distinct_count"),
            min = GetVal("min_val"),
            max = GetVal("max_val"),
            avg = GetVal("avg_val"),
            topValues
        });
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
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiMethod { get; set; }
    public int OrganizationId { get; set; }
    public int? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}
