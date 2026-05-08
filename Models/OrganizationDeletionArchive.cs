namespace AIInsights.Models;

public class OrganizationDeletionArchive
{
    // TODO: Add a daily background purge job for expired archive rows.
    public int Id { get; set; }
    public int OriginalOrganizationId { get; set; }
    public Guid OriginalOrganizationGuid { get; set; }
    public string Name { get; set; } = "";
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
    public string? DeletedByUserId { get; set; }
    public string? DeletedByDisplayName { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RestoredAt { get; set; }
    public string Snapshot { get; set; } = "";
    public int SizeBytes { get; set; }
}
