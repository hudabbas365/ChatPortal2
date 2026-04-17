using System.Data;
using System.Data.Common;
using AIInsights.Models;
using Microsoft.Data.SqlClient;
using Npgsql;
using MySqlConnector;

namespace AIInsights.Services;

public interface IQueryExecutionService
{
    Task<QueryExecutionResult> ExecuteReadOnlyAsync(Datasource ds, string sql, int maxRows = 1000);
}

public class QueryExecutionResult
{
    public bool Success { get; set; }
    public List<Dictionary<string, object>> Data { get; set; } = new();
    public int RowCount { get; set; }
    public string? Error { get; set; }
}

public class QueryExecutionService : IQueryExecutionService
{
    private readonly IEncryptionService _encryption;
    private readonly IPowerBiService _powerBi;

    public QueryExecutionService(IEncryptionService encryption, IPowerBiService powerBi)
    {
        _encryption = encryption;
        _powerBi = powerBi;
    }

    private static readonly HashSet<string> SqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL Server", "SqlServer", "MSSQL"
    };

    public static readonly HashSet<string> PowerBiTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Power BI", "PowerBI"
    };

    private static readonly HashSet<string> PgTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostgreSQL", "Postgres"
    };

    private static readonly HashSet<string> MySqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MySQL", "MariaDB"
    };

    private static readonly string[] BlockedSqlPatterns = new[]
    {
        @"(/\*[\s\S]*?\*/)",       // Block inline comments used to break keywords
        @"(--[^\r\n]*)",           // Block line comments
        @"\bXP_\w+",               // xp_ extended procs
        @"\bSP_\w+",               // sp_ system procs
        @"\bOPENROWSET\b",
        @"\bOPENDATASOURCE\b",
        @"\bBULK\s+INSERT\b",
        @"\bSHUTDOWN\b",
        @"\bSYSTEM_USER\b",
        @"\bCONVERT\s*\(",         // Often used in blind injections
    };

    public static bool IsSafeQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var upper = sql.ToUpperInvariant().Trim();
        // Must start with SELECT, WITH (CTE), or EVALUATE (DAX)
        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH") && !upper.StartsWith("EVALUATE"))
            return false;
        // Block stacked queries (multiple statements via semicolons) — but not for DAX/EVALUATE
        if (!upper.StartsWith("EVALUATE"))
        {
            // Strip trailing semicolons, then check for any remaining ones (stacked statements)
            var trimmed = upper.TrimEnd(';', ' ', '\r', '\n', '\t');
            if (trimmed.Contains(';'))
                return false;
        }
        foreach (var pattern in BlockedSqlPatterns)
            if (System.Text.RegularExpressions.Regex.IsMatch(sql, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return false;
        return true;
    }

    public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(Datasource ds, string sql, int maxRows = 1000)
    {
        // Safety gate: only allow safe read-only queries
        if (!IsSafeQuery(sql))
            return new QueryExecutionResult { Success = false, Error = "Query blocked by security policy. Only read-only SELECT/EVALUATE queries are allowed." };

        // Route Power BI datasources to the ADOMD-based service (DAX / DMV)
        if (PowerBiTypes.Contains(ds.Type))
        {
            var isDmv = sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                      && sql.Contains("$SYSTEM.", StringComparison.OrdinalIgnoreCase);
            return isDmv
                ? await _powerBi.ExecuteDmvAsync(ds, sql, maxRows)
                : await _powerBi.ExecuteDaxAsync(ds, sql, maxRows);
        }

        DbConnection? conn = null;
        try
        {
            var connStr = BuildConnectionString(ds);
            conn = CreateConnection(ds.Type, connStr);
            if (conn == null)
                return new QueryExecutionResult { Success = false, Error = $"Unsupported datasource type: {ds.Type}. Only SQL Server, Power BI, PostgreSQL, and MySQL/MariaDB are supported for live queries." };

            // Normalize SQL dialect differences before execution
            sql = NormalizeSql(ds.Type, sql);

            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 30;

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult);
            var results = new List<Dictionary<string, object>>();
            var fieldCount = reader.FieldCount;
            var columns = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                columns[i] = reader.GetName(i);

            while (await reader.ReadAsync() && results.Count < maxRows)
            {
                var row = new Dictionary<string, object>(fieldCount);
                for (int i = 0; i < fieldCount; i++)
                {
                    var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columns[i]] = val ?? (object)"NULL";
                }
                results.Add(row);
            }

            return new QueryExecutionResult
            {
                Success = true,
                Data = results,
                RowCount = results.Count
            };
        }
        catch (Exception ex)
        {
            return new QueryExecutionResult
            {
                Success = false,
                Error = $"Query execution failed: {ex.Message}"
            };
        }
        finally
        {
            if (conn != null)
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }
        }
    }

    private string BuildConnectionString(Datasource ds)
    {
        var connStr = _encryption.Decrypt(ds.ConnectionString ?? "");

        // Validate connection string has proper key=value format.
        // If the first segment lacks '=' it's likely a bare server name — prepend 'Server='.
        connStr = NormalizeConnectionString(ds.Type, connStr);

        var dbUser = _encryption.Decrypt(ds.DbUser ?? "");
        var dbPassword = _encryption.Decrypt(ds.DbPassword ?? "");

        if (string.IsNullOrEmpty(dbUser) && string.IsNullOrEmpty(dbPassword))
            return connStr;

        // Inject credentials if not already present in the connection string
        var upper = connStr.ToUpperInvariant();
        var hasUser = upper.Contains("USER ID=") || upper.Contains("UID=") ||
                      upper.Contains("USERNAME=") || upper.Contains("USER=");

        if (!hasUser)
        {
            if (!string.IsNullOrEmpty(dbUser))
            {
                connStr = connStr.TrimEnd(';') + $";User ID={dbUser}";
            }
            if (!string.IsNullOrEmpty(dbPassword))
            {
                connStr = connStr.TrimEnd(';') + $";Password={dbPassword}";
            }
        }

        return connStr;
    }

    /// <summary>
    /// Ensures the connection string has proper key=value format.
    /// Fixes common issues like missing 'Server=' prefix.
    /// </summary>
    private static string NormalizeConnectionString(string type, string connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return connStr;

        var trimmed = connStr.Trim();

        // Check if the first segment before ';' contains '=' (i.e. is a proper key=value pair)
        var firstSemicolon = trimmed.IndexOf(';');
        var firstSegment = firstSemicolon >= 0 ? trimmed[..firstSemicolon] : trimmed;

        if (!firstSegment.Contains('='))
        {
            // The first segment has no '=' — it's likely a bare server name.
            // Prepend the appropriate server keyword for the database type.
            if (PgTypes.Contains(type))
                trimmed = $"Host={trimmed}";
            else if (MySqlTypes.Contains(type))
                trimmed = $"Server={trimmed}";
            else
                trimmed = $"Server={trimmed}";
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes SQL syntax differences between database engines.
    /// Converts LIMIT N to TOP N for SQL Server, etc.
    /// </summary>
    private static string NormalizeSql(string type, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // SQL Server does not support LIMIT — convert to TOP
        if (SqlTypes.Contains(type))
        {
            var limitMatch = System.Text.RegularExpressions.Regex.Match(
                sql, @"\bLIMIT\s+(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (limitMatch.Success)
            {
                var n = limitMatch.Groups[1].Value;
                sql = sql[..limitMatch.Index].TrimEnd();
                sql = System.Text.RegularExpressions.Regex.Replace(
                    sql, @"^\s*SELECT\b", $"SELECT TOP {n}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }
        // MySQL does not support square-bracket quoting — strip brackets
        else if (MySqlTypes.Contains(type))
        {
            sql = sql.Replace("[", "`").Replace("]", "`");
        }

        return sql;
    }

    private static DbConnection? CreateConnection(string type, string connectionString)
    {
        if (SqlTypes.Contains(type))
            return new SqlConnection(connectionString);

        if (PgTypes.Contains(type))
            return new NpgsqlConnection(connectionString);

        if (MySqlTypes.Contains(type))
            return new MySqlConnection(connectionString);

        return null;
    }
}
