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
    Task<(bool Success, string? Error)> TestConnectionAsync(string type, string connectionString, string? dbUser = null, string? dbPassword = null, string? xmlaEndpoint = null, string? tenantId = null);
    Task<(bool Success, string? Error)> TestRestApiAsync(string? apiUrl, string? apiKey, string? apiMethod = null);
    Task<QueryExecutionResult> ExecuteRestApiAsync(Datasource ds, int maxRows = 1000);
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
    private readonly IHttpClientFactory _httpClientFactory;

    public QueryExecutionService(IEncryptionService encryption, IPowerBiService powerBi, IHttpClientFactory httpClientFactory)
    {
        _encryption = encryption;
        _powerBi = powerBi;
        _httpClientFactory = httpClientFactory;
    }

    private static readonly HashSet<string> SqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL Server", "SqlServer", "MSSQL"
    };

    public static readonly HashSet<string> PowerBiTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Power BI", "PowerBI"
    };

    public static readonly HashSet<string> RestApiTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "REST API", "RestApi"
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

    /// <summary>
    /// Strips SQL line comments (--) and block comments (/* */) and collapses
    /// the result so that AI-generated queries pass the safety gate.
    /// </summary>
    public static string StripSqlComments(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        // Remove block comments
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ");
        // Remove line comments
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"--[^\r\n]*", " ");
        // Collapse whitespace
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ").Trim();
        return sql;
    }

    /// <summary>
    /// Defence-in-depth: ensures any table referenced after FROM/JOIN is in the
    /// datasource's SelectedTables whitelist. Returns null when the query is allowed,
    /// or an error message when it references a table outside the whitelist.
    /// </summary>
    private static string? ValidateTableWhitelist(Datasource ds, string sql)
    {
        if (ds == null || string.IsNullOrWhiteSpace(ds.SelectedTables)) return null;
        // REST/PBI types don't use SQL parsing here.
        if (RestApiTypes.Contains(ds.Type)) return null;
        if (PowerBiTypes.Contains(ds.Type)) return null;
        if (string.IsNullOrWhiteSpace(sql)) return null;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = StripIdentifierWrappers(t);
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0) name = name[(lastDot + 1)..];
            name = StripIdentifierWrappers(name);
            if (!string.IsNullOrEmpty(name)) allowed.Add(name);
        }
        if (allowed.Count == 0) return null;

        // Match any identifier following FROM or JOIN, including [bracketed], "quoted",
        // `backticked`, and schema-qualified names.
        var pattern = @"\b(?:FROM|JOIN)\s+([\[\""`]?[\w]+[\]\""`]?(?:\s*\.\s*[\[\""`]?[\w]+[\]\""`]?)*)";
        var matches = System.Text.RegularExpressions.Regex.Matches(
            sql, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var raw = m.Groups[1].Value;
            var name = raw;
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0) name = name[(lastDot + 1)..];
            name = StripIdentifierWrappers(name);
            if (string.IsNullOrEmpty(name)) continue;
            if (!allowed.Contains(name))
                return $"Query references table '{name}' which is not in the agent's allowed tables. Allowed: {string.Join(", ", allowed)}.";
        }
        return null;
    }

    private static string StripIdentifierWrappers(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Trim();
        if (s.Length >= 2)
        {
            var first = s[0];
            var last = s[^1];
            if ((first == '[' && last == ']') ||
                (first == '"' && last == '"') ||
                (first == '`' && last == '`'))
            {
                s = s.Substring(1, s.Length - 2);
            }
        }
        return s.Trim();
    }

    public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(Datasource ds, string sql, int maxRows = 1000)
    {
        // Strip comments so AI-generated SQL with -- or /* */ annotations is not rejected
        sql = StripSqlComments(sql);

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

        // SECURITY: when the datasource has a SelectedTables whitelist, ensure the query
        // only touches tables in that whitelist. Closes the door on a tampered system
        // prompt (or a hand-crafted call) that asks the LLM to query tables the workspace
        // owner never granted to the agent.
        var whitelistError = ValidateTableWhitelist(ds, sql);
        if (whitelistError != null)
            return new QueryExecutionResult { Success = false, Error = whitelistError };

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

    public async Task<(bool Success, string? Error)> TestConnectionAsync(string type, string connectionString, string? dbUser = null, string? dbPassword = null, string? xmlaEndpoint = null, string? tenantId = null)
    {
        if (PowerBiTypes.Contains(type))
        {
            // Genuinely validate Power BI credentials by acquiring an MSAL token
            // (clientId is stored in DbUser, clientSecret in DbPassword, catalog in connectionString)
            return await _powerBi.TestCredentialsAsync(
                tenantId ?? "",
                dbUser ?? "",
                dbPassword ?? "",
                xmlaEndpoint ?? "",
                connectionString);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "Connection string is required.");

        var connStr = NormalizeConnectionString(type, connectionString);

        // Inject credentials
        if (!string.IsNullOrEmpty(dbUser))
        {
            var upper = connStr.ToUpperInvariant();
            if (!upper.Contains("USER ID=") && !upper.Contains("UID=") && !upper.Contains("USERNAME=") && !upper.Contains("USER="))
            {
                connStr = connStr.TrimEnd(';') + $";User ID={dbUser}";
                if (!string.IsNullOrEmpty(dbPassword))
                    connStr = connStr.TrimEnd(';') + $";Password={dbPassword}";
            }
        }

        DbConnection? conn = null;
        try
        {
            conn = CreateConnection(type, connStr);
            if (conn == null)
                return (false, $"Unsupported datasource type: {type}.");

            await conn.OpenAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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

    public async Task<(bool Success, string? Error)> TestRestApiAsync(string? apiUrl, string? apiKey, string? apiMethod = null)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            return (false, "API URL is required.");

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var method = ResolveHttpMethod(apiMethod);
            var request = new HttpRequestMessage(method, apiUrl);
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await client.SendAsync(request);

            // Auto-retry with alternate method on 405 Method Not Allowed
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                var altMethod = method == HttpMethod.Get ? HttpMethod.Post : HttpMethod.Get;
                var retryRequest = new HttpRequestMessage(altMethod, apiUrl);
                if (!string.IsNullOrEmpty(apiKey))
                    retryRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                if (altMethod == HttpMethod.Post)
                    retryRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                response = await client.SendAsync(retryRequest);
            }

            if (!response.IsSuccessStatusCode)
                return (false, $"API returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Tried method: {method.Method}.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"REST API test failed: {ex.Message}");
        }
    }

    public async Task<QueryExecutionResult> ExecuteRestApiAsync(Datasource ds, int maxRows = 1000)
    {
        var apiUrl = _encryption.Decrypt(ds.ApiUrl ?? "");
        var apiKey = _encryption.Decrypt(ds.ApiKey ?? "");

        if (string.IsNullOrWhiteSpace(apiUrl))
            return new QueryExecutionResult { Success = false, Error = "API URL is not configured." };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var method = ResolveHttpMethod(ds.ApiMethod);
            var request = new HttpRequestMessage(method, apiUrl);
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await client.SendAsync(request);

            // Auto-retry with alternate method on 405 Method Not Allowed
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                var altMethod = method == HttpMethod.Get ? HttpMethod.Post : HttpMethod.Get;
                var retryRequest = new HttpRequestMessage(altMethod, apiUrl);
                if (!string.IsNullOrEmpty(apiKey))
                    retryRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                if (altMethod == HttpMethod.Post)
                    retryRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                response = await client.SendAsync(retryRequest);
            }

            if (!response.IsSuccessStatusCode)
                return new QueryExecutionResult { Success = false, Error = $"API returned HTTP {(int)response.StatusCode}. URL: {apiUrl}, Method: {method.Method}." };

            var json = await response.Content.ReadAsStringAsync();
            var rows = ParseJsonToRows(json, maxRows);

            return new QueryExecutionResult { Success = true, Data = rows, RowCount = rows.Count };
        }
        catch (Exception ex)
        {
            return new QueryExecutionResult { Success = false, Error = $"REST API call failed: {ex.Message}" };
        }
    }

    private static List<Dictionary<string, object>> ParseJsonToRows(string json, int maxRows)
    {
        var rows = new List<Dictionary<string, object>>();
        if (string.IsNullOrWhiteSpace(json)) return rows;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // If root is an array, each element is a row
        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                if (rows.Count >= maxRows) break;
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                    rows.Add(FlattenJsonObject(element));
            }
        }
        // If root is an object, look for the first array property (common pattern: { "data": [...] })
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var element in prop.Value.EnumerateArray())
                    {
                        if (rows.Count >= maxRows) break;
                        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                            rows.Add(FlattenJsonObject(element));
                    }
                    if (rows.Count > 0) break;
                }
            }
            // If no array found, treat the object itself as a single row
            if (rows.Count == 0)
                rows.Add(FlattenJsonObject(root));
        }

        return rows;
    }

    private static Dictionary<string, object> FlattenJsonObject(System.Text.Json.JsonElement element)
    {
        var row = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            row[prop.Name] = prop.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => (object)(prop.Value.GetString() ?? ""),
                System.Text.Json.JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => "NULL",
                _ => prop.Value.GetRawText()
            };
        }
        return row;
    }

    private static HttpMethod ResolveHttpMethod(string? method)
    {
        return (method?.Trim().ToUpperInvariant()) switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            _ => HttpMethod.Get
        };
    }
}
