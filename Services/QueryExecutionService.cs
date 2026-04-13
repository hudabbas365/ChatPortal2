using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using ChatPortal2.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Npgsql;
using MySqlConnector;

namespace ChatPortal2.Services;

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
    private static readonly HashSet<string> SqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL Server", "SqlServer", "MSSQL", "Power BI"
    };

    private static readonly HashSet<string> PgTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostgreSQL", "Postgres"
    };

    private static readonly HashSet<string> MySqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MySQL", "MariaDB"
    };

    private static readonly string[] WriteKeywords =
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
        "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "GRANT", "REVOKE",
        "REPLACE", "UPSERT", "ATTACH", "DETACH", "CALL"
    };

    private readonly IDataProtectionService _dataProtection;

    public QueryExecutionService(IDataProtectionService dataProtection)
    {
        _dataProtection = dataProtection;
    }

    public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(Datasource ds, string sql, int maxRows = 1000)
    {
        // Block write operations before executing
        var normalized = Regex.Replace(sql.ToUpperInvariant(), @"\s+", " ").Trim();
        foreach (var keyword in WriteKeywords)
        {
            if (Regex.IsMatch(normalized, $@"\b{keyword}\b"))
                return new QueryExecutionResult { Success = false, Error = $"Write operation '{keyword}' is not permitted. Only SELECT queries are allowed." };
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
        var connStr = _dataProtection.Unprotect(ds.ConnectionString ?? "");
        var dbUser = string.IsNullOrEmpty(ds.DbUser) ? null : _dataProtection.Unprotect(ds.DbUser);
        var dbPassword = string.IsNullOrEmpty(ds.DbPassword) ? null : _dataProtection.Unprotect(ds.DbPassword);

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
    /// Normalizes SQL syntax differences between database engines.
    /// Converts LIMIT N to TOP N for SQL Server, etc.
    /// </summary>
    private static string NormalizeSql(string type, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // SQL Server does not support LIMIT — convert to TOP
        if (SqlTypes.Contains(type))
        {
            var limitMatch = Regex.Match(
                sql, @"\bLIMIT\s+(\d+)\s*$", RegexOptions.IgnoreCase);
            if (limitMatch.Success)
            {
                var n = limitMatch.Groups[1].Value;
                sql = sql[..limitMatch.Index].TrimEnd();
                sql = Regex.Replace(
                    sql, @"^\s*SELECT\b", $"SELECT TOP {n}",
                    RegexOptions.IgnoreCase);
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
