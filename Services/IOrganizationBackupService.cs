using Microsoft.AspNetCore.Http;

namespace AIInsights.Services;

public interface IOrganizationBackupService
{
    Task<OrganizationBackupArtifact> CreateBackupAsync(int organizationId, string? performedByUserId, bool includeAttachments, bool jsonOnly, CancellationToken cancellationToken = default);
    Task<OrganizationRestoreResult> RestoreAsync(int organizationId, IFormFile backupFile, string mode, string? performedByUserId, string? confirmationText, string? confirmationOrganizationName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationBackupHistoryItem>> GetHistoryAsync(int organizationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> DeleteBackupAsync(int organizationId, string fileName, CancellationToken cancellationToken = default);
    string GetBackupFilePath(int organizationId, string fileName);
}

public class OrganizationBackupArtifact
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string SavedPath { get; set; } = "";
    public long FileSizeBytes => Bytes.LongLength;
}

public class OrganizationRestoreResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Notes { get; set; }
}

public class OrganizationBackupHistoryItem
{
    public string FileName { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTime PerformedAt { get; set; }
    public string? PerformedByUserId { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Notes { get; set; }
}
