namespace AIInsights.Models;

/// <summary>
/// Phase 34b — Server-persisted snapshot of a Report's canvas state.
/// Two kinds:
///   - "Auto"     : created automatically before each PUT /api/reports/{guid} overwrite (capped per report).
///   - "Snapshot" : user-named save point (no auto-trim).
/// </summary>
public class ReportRevision
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report? Report { get; set; }

    /// <summary>"Auto" or "Snapshot".</summary>
    public string Kind { get; set; } = "Auto";

    /// <summary>User-provided label for "Snapshot" kind; optional descriptor for "Auto".</summary>
    public string? Name { get; set; }

    /// <summary>Full canvas JSON at the time the revision was captured.</summary>
    public string? CanvasJson { get; set; }

    /// <summary>Report name at the time of capture (for display in the list).</summary>
    public string? ReportName { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
