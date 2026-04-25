using System.Globalization;
using AIInsights.Models;
using CsvHelper;
using CsvHelper.Configuration;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;

namespace AIInsights.Services;

/// <summary>
/// Fetches and parses CSV / XLSX files from public anonymous share links.
/// Supports OneDrive (1drv.ms, personal, SharePoint anonymous),
/// Google Drive (/file/d/&lt;id&gt;/view), Dropbox (?dl=0), and any
/// raw HTTPS URL. Files are never written to disk — HttpClient body
/// is consumed as a stream directly into CsvHelper or ClosedXML.
/// </summary>
public sealed class FileDatasourceService : IFileDatasourceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEncryptionService _encryption;
    private readonly int _maxRows;
    private readonly int _httpTimeoutSeconds;
    private readonly long _maxBytes;

    public FileDatasourceService(
        IHttpClientFactory httpClientFactory,
        IEncryptionService encryption,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _encryption = encryption;
        _maxRows            = configuration.GetValue("FileDatasource:MaxRows",            50_000);
        _httpTimeoutSeconds = configuration.GetValue("FileDatasource:HttpTimeoutSeconds",  30);
        _maxBytes           = configuration.GetValue("FileDatasource:MaxBytes",    104_857_600L);
    }

    // ── Public interface ─────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> TestAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "File URL is required.");

        var client = CreateClient();

        string normalized;
        try { normalized = await NormalizeUrlAsync(url, client); }
        catch (Exception ex) { return (false, $"Invalid URL: {ex.Message}"); }

        try
        {
            // Use a HEAD request first to check accessibility and size without
            // downloading the full file. Many anonymous share endpoints
            // (notably the OneDrive Shares API) reject HEAD with 401/403/405
            // even though GET works fine — the share token in the URL is the
            // credential and is only honoured by the content endpoint. So we
            // always fall back to a streamed GET if HEAD doesn't succeed.
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, normalized);
            HttpResponseMessage? headResponse = null;
            try { headResponse = await client.SendAsync(headRequest); }
            catch { /* fall through to GET */ }

            if (headResponse == null || !headResponse.IsSuccessStatusCode)
            {
                using var getReq  = new HttpRequestMessage(HttpMethod.Get, normalized);
                using var getResp = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
                return ValidateResponse(getResp, normalized);
            }

            return ValidateResponse(headResponse, normalized);
        }
        catch (TaskCanceledException)
        {
            return (false, $"Connection timed out after {_httpTimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Could not reach the URL: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"File URL test failed: {ex.Message}");
        }
    }

    public async Task<QueryExecutionResult> ExecuteAsync(Datasource ds, int maxRows = 1000)
    {
        var rawUrl = _encryption.Decrypt(ds.ApiUrl ?? "");
        if (string.IsNullOrWhiteSpace(rawUrl))
            return new QueryExecutionResult { Success = false, Error = "File URL is not configured." };

        // Enforce the hard ceiling from configuration — ignore the caller's maxRows if it exceeds it.
        var effectiveMax = Math.Min(maxRows, _maxRows);

        var client = CreateClient();

        string normalized;
        try { normalized = await NormalizeUrlAsync(rawUrl, client); }
        catch (Exception ex) { return new QueryExecutionResult { Success = false, Error = $"Invalid URL: {ex.Message}" }; }

        try
        {
            using var request  = new HttpRequestMessage(HttpMethod.Get, normalized);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            var (valid, error) = ValidateResponse(response, normalized);
            if (!valid)
                return new QueryExecutionResult { Success = false, Error = error };

            // Determine format: ApiMethod field stores "Auto" / "CSV" / "XLSX"
            var fmt = ResolveFormat(ds.ApiMethod, normalized, response);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var limited = new LimitedStream(stream, _maxBytes);

            return fmt == FileFormat.Xlsx
                ? ParseXlsx(limited, effectiveMax)
                : ParseCsv(limited, effectiveMax);
        }
        catch (LimitExceededException)
        {
            return new QueryExecutionResult
            {
                Success = false,
                Error = $"File exceeds the maximum allowed size of {_maxBytes / 1_048_576} MB."
            };
        }
        catch (TaskCanceledException)
        {
            return new QueryExecutionResult
            {
                Success = false,
                Error = $"Request timed out after {_httpTimeoutSeconds} seconds."
            };
        }
        catch (Exception ex)
        {
            return new QueryExecutionResult { Success = false, Error = $"File fetch/parse failed: {ex.Message}" };
        }
    }

    // ── URL normalisation ────────────────────────────────────────────────────

    /// <summary>
    /// Async wrapper around <see cref="NormalizeUrl"/> that additionally resolves
    /// OneDrive 1drv.ms short-links to their canonical long form before encoding
    /// them for the Shares API. The Shares API requires the canonical share URL
    /// (e.g. https://onedrive.live.com/?cid=…&resid=…) — base64-encoding a
    /// short 1drv.ms link works for some tenants but fails for others, so we
    /// always resolve the redirect first.
    /// </summary>
    private async Task<string> NormalizeUrlAsync(string url, HttpClient client)
    {
        url = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only http:// or https:// URLs are supported.");

        var host = uri.Host.ToLowerInvariant();
        if (host == "1drv.ms")
        {
            try
            {
                using var probe = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead);
                var resolved = resp.RequestMessage?.RequestUri?.ToString();
                if (!string.IsNullOrWhiteSpace(resolved) &&
                    !resolved.StartsWith("https://1drv.ms", StringComparison.OrdinalIgnoreCase))
                {
                    return ToOneDriveSharesContentUrl(resolved);
                }
            }
            catch { /* fall through and try with the short URL */ }
            return ToOneDriveSharesContentUrl(url);
        }

        return NormalizeUrl(url);
    }

    /// <summary>
    /// Converts user-supplied share links to direct-download equivalents.
    /// </summary>
    internal static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only http:// or https:// URLs are supported.");

        var host = uri.Host.ToLowerInvariant();

        // ── Google Drive ────────────────────────────────────────────────────
        // https://drive.google.com/file/d/<id>/view  →  /uc?export=download&id=<id>
        if (host == "drive.google.com")
        {
            var m = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"/file/d/([^/]+)/");
            if (m.Success)
                return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}";
        }

        // ── Dropbox ──────────────────────────────────────────────────────────
        // Replace ?dl=0 with ?dl=1, or add ?dl=1 if absent
        if (host == "www.dropbox.com" || host == "dropbox.com")
        {
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            q["dl"] = "1";
            return $"https://www.dropbox.com{uri.AbsolutePath}?{q}";
        }

        // ── OneDrive short link (1drv.ms) and personal OneDrive (onedrive.live.com) ──
        // These return an HTML viewer page when fetched directly, NOT the file bytes.
        // The OneDrive Shares API resolves any anonymous share URL to the raw content
        // via:  https://api.onedrive.com/v1.0/shares/u!{base64url(url)}/root/content
        // This is the documented way to download a share link without authentication.
        if (host is "1drv.ms" or "onedrive.live.com")
            return ToOneDriveSharesContentUrl(url);

        // ── OneDrive / SharePoint "…?e=…&download=1" ───────────────────────
        // Personal OneDrive share links: embed=1 → download=1
        if (host.EndsWith(".sharepoint.com") || host.EndsWith("onedrive.com"))
        {
            // Same Shares API works for SharePoint anonymous share links and is
            // far more reliable than the &download=1 trick (which requires the
            // owner to have explicitly enabled "Allow download").
            return ToOneDriveSharesContentUrl(url);
        }

        // All other URLs (GitHub raw, S3, direct HTTPS, etc.) — return as-is
        return url;
    }

    /// <summary>
    /// Encodes a OneDrive / SharePoint share URL into the public Shares API
    /// content endpoint that returns raw file bytes. See
    /// https://learn.microsoft.com/onedrive/developer/rest-api/api/shares_get
    /// </summary>
    private static string ToOneDriveSharesContentUrl(string shareUrl)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(shareUrl);
        var b64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"https://api.onedrive.com/v1.0/shares/u!{b64}/root/content";
    }

    // ── Format detection ─────────────────────────────────────────────────────

    private enum FileFormat { Csv, Xlsx }

    private static FileFormat ResolveFormat(string? apiMethod, string url, HttpResponseMessage response)
    {
        // User-supplied override stored in ApiMethod ("CSV", "XLSX", "Auto" or empty)
        if (string.Equals(apiMethod, "XLSX", StringComparison.OrdinalIgnoreCase))
            return FileFormat.Xlsx;
        if (string.Equals(apiMethod, "CSV", StringComparison.OrdinalIgnoreCase))
            return FileFormat.Csv;

        // Auto-detect from URL extension
        var path = new Uri(url).AbsolutePath.ToLowerInvariant().Split('?')[0];
        if (path.EndsWith(".xlsx") || path.EndsWith(".xls"))
            return FileFormat.Xlsx;
        if (path.EndsWith(".csv"))
            return FileFormat.Csv;

        // Auto-detect from Content-Type header
        var ct = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        if (ct.Contains("spreadsheetml") || ct.Contains("excel") || ct.Contains("vnd.ms-excel"))
            return FileFormat.Xlsx;

        // Default to CSV
        return FileFormat.Csv;
    }

    // ── CSV parsing ───────────────────────────────────────────────────────────

    private static QueryExecutionResult ParseCsv(Stream stream, int maxRows)
    {
        var rows = new List<Dictionary<string, object>>();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,    // skip unparseable records rather than throwing
            MissingFieldFound = null
        };
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv    = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader())
            return new QueryExecutionResult { Success = true, Data = rows, RowCount = 0 };

        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (csv.Read())
        {
            if (rows.Count >= maxRows) break;
            var row = new Dictionary<string, object>(headers.Length);
            for (var i = 0; i < headers.Length; i++)
            {
                var raw = csv.GetField(i) ?? "";
                row[headers[i]] = InferValue(raw);
            }
            rows.Add(row);
        }
        return new QueryExecutionResult { Success = true, Data = rows, RowCount = rows.Count };
    }

    // ── XLSX parsing ──────────────────────────────────────────────────────────

    private static QueryExecutionResult ParseXlsx(Stream stream, int maxRows)
    {
        // ClosedXML needs a seekable stream; buffer if needed.
        Stream workingStream = stream;
        if (!stream.CanSeek)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            workingStream = ms;
        }

        var rows = new List<Dictionary<string, object>>();
        using var wb = new XLWorkbook(workingStream);
        var ws = wb.Worksheets.First();

        var usedRange = ws.RangeUsed();
        if (usedRange == null)
            return new QueryExecutionResult { Success = true, Data = rows, RowCount = 0 };

        var firstRow = usedRange.FirstRow();
        var headers  = firstRow.Cells()
            .Select(c => c.GetString().Trim())
            .ToArray();

        if (headers.Length == 0)
            return new QueryExecutionResult { Success = true, Data = rows, RowCount = 0 };

        // Skip header row — iterate remaining rows
        foreach (var xlRow in usedRange.Rows().Skip(1))
        {
            if (rows.Count >= maxRows) break;
            var row = new Dictionary<string, object>(headers.Length);
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = xlRow.Cell(i + 1);
                row[headers[i]] = CellValue(cell);
            }
            rows.Add(row);
        }
        return new QueryExecutionResult { Success = true, Data = rows, RowCount = rows.Count };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("fileds");
        client.Timeout = TimeSpan.FromSeconds(_httpTimeoutSeconds);
        // No auth headers — anonymous fetch only. URL is the credential.
        return client;
    }

    private (bool Success, string? Error) ValidateResponse(HttpResponseMessage response, string url)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return (false, "This share link is not publicly accessible. Ask the file owner to change sharing to 'Anyone with the link'.");

        if (!response.IsSuccessStatusCode)
            return (false, $"Server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase} for URL: {url}");

        // Content-length guard
        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > _maxBytes)
            return (false, $"File size ({length.Value / 1_048_576} MB) exceeds the maximum of {_maxBytes / 1_048_576} MB.");

        return (true, null);
    }

    private static object InferValue(string raw)
    {
        if (long.TryParse(raw, out var l)) return l;
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        if (bool.TryParse(raw, out var b)) return b;
        return raw;
    }

    private static object CellValue(IXLCell cell)
    {
        return cell.DataType switch
        {
            XLDataType.Number  => cell.GetDouble(),
            XLDataType.DateTime => cell.GetDateTime().ToString("o"),
            XLDataType.Boolean => cell.GetBoolean(),
            XLDataType.TimeSpan => cell.GetTimeSpan().ToString(),
            _ => cell.GetString()
        };
    }

    // ── LimitedStream ─────────────────────────────────────────────────────────

    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long   _limit;
        private long _read;

        public LimitedStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush()  => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _limit) throw new LimitExceededException();
            var allowed = (int)Math.Min(count, _limit - _read);
            var n = _inner.Read(buffer, offset, allowed);
            _read += n;
            if (_read >= _limit && n > 0) throw new LimitExceededException();
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_read >= _limit) throw new LimitExceededException();
            var allowed = (int)Math.Min(count, _limit - _read);
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, allowed), ct);
            _read += n;
            if (_read >= _limit && n > 0) throw new LimitExceededException();
            return n;
        }

        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class LimitExceededException : IOException
    {
        public LimitExceededException() : base("File size limit exceeded.") { }
    }
}
