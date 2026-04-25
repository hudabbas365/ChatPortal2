using AIInsights.Models;

namespace AIInsights.Services;

/// <summary>
/// Fetches and parses CSV or Excel (XLSX) files from a public anonymous share
/// link (OneDrive, SharePoint, Google Drive, Dropbox, raw HTTPS URL, etc.).
/// Files are never persisted to disk or database — they are streamed and parsed
/// on demand. The URL stored in <see cref="Datasource.ApiUrl"/> acts as the
/// credential (anonymous fetch only).
/// </summary>
public interface IFileDatasourceService
{
    /// <summary>
    /// Probes the given URL: verifies it is reachable, publicly accessible,
    /// within the configured byte limit, and returns a parseable content type.
    /// </summary>
    Task<(bool Success, string? Error)> TestAsync(string url);

    /// <summary>
    /// Fetches and parses the file referenced by <paramref name="ds"/>.ApiUrl,
    /// returning up to <paramref name="maxRows"/> rows.
    /// The format is auto-detected from the URL extension and Content-Type
    /// unless the user explicitly set ds.ApiMethod to "CSV" or "XLSX".
    /// </summary>
    Task<QueryExecutionResult> ExecuteAsync(Datasource ds, int maxRows = 1000);
}
