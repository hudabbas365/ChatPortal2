using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/datasources")]
[ApiController]
public class DatasourceController : ControllerBase
{
    private static readonly List<string> DatasourceTypes = new()
    {
        "SQL Server", "PostgreSQL", "MySQL", "MariaDB", "Oracle", "SQLite", "MongoDB",
        "Redis", "Cassandra", "CouchDB", "DynamoDB", "Firebase Realtime DB", "Firestore",
        "Elasticsearch", "OpenSearch", "Solr", "InfluxDB", "TimescaleDB", "QuestDB",
        "REST API", "GraphQL API", "SOAP / WSDL", "OData", "WebSocket",
        "CSV", "Excel (XLSX)", "Google Sheets", "JSON File", "XML File",
        "Parquet", "Avro", "ORC", "Feather", "HDF5",
        "Snowflake", "BigQuery", "Amazon Redshift", "Azure Synapse", "Databricks",
        "Teradata", "IBM Db2", "SAP HANA", "Vertica", "Greenplum",
        "Amazon S3", "Azure Blob Storage", "Google Cloud Storage", "HDFS",
        "Apache Kafka", "Apache Spark", "Apache Flink", "RabbitMQ", "Azure Event Hubs",
        "Salesforce", "HubSpot", "Zendesk", "Shopify", "Stripe",
        "Google Analytics", "Mixpanel", "Amplitude", "Segment", "Heap",
        "Looker", "Tableau", "Power BI", "Metabase", "Mode Analytics",
        "Airtable", "Notion", "Coda", "Smartsheet",
        "GitHub", "GitLab", "Jira", "Confluence", "Linear",
        "Slack", "Microsoft Teams", "Discord",
        "MySQL Cluster", "CockroachDB", "PlanetScale", "Neon", "Supabase",
        "FTP / SFTP", "Email (IMAP/SMTP)", "SMS API", "Push Notification Service",
        "Custom JDBC", "Custom ODBC", "In-Memory Cache"
    };

    private readonly AppDbContext _db;

    public DatasourceController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("types")]
    public IActionResult GetTypes() => Ok(DatasourceTypes);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int organizationId)
    {
        var datasources = await _db.Datasources
            .Where(d => d.OrganizationId == organizationId)
            .ToListAsync();
        return Ok(datasources);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DatasourceRequest req)
    {
        var ds = new Datasource
        {
            Name = req.Name ?? "New Datasource",
            Type = req.Type ?? "SQL Server",
            ConnectionString = req.ConnectionString ?? "",
            OrganizationId = req.OrganizationId
        };
        _db.Datasources.Add(ds);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "datasource_created",
            Description = $"Datasource '{ds.Name}' ({ds.Type}) connected.",
            UserId = req.UserId ?? "",
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { ds.Id, ds.Name, ds.Type, ds.OrganizationId });
    }

    [HttpGet("{id}/fields")]
    public async Task<IActionResult> GetFields(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();
        // Return sample/placeholder fields; real implementation would query the datasource
        var fields = new List<string> { "id", "name", "region", "revenue", "date", "category", "quantity", "price", "status" };
        return Ok(fields);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();
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
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }
}
