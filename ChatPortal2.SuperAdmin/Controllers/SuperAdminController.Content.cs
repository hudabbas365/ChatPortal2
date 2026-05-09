using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.SuperAdmin.Controllers;

public partial class SuperAdminController
{
    public class ImageUploadDto
    {
        public string FileName { get; set; } = "";
        public string? ContentType { get; set; }
        public string? Base64Data { get; set; }
    }

    public class ContentImageDto
    {
        public int? Id { get; set; }
        public string? ImagePath { get; set; }
        public int SortOrder { get; set; }
        public string? AltText { get; set; }
        public ImageUploadDto? Upload { get; set; }
    }

    public class SuggestKeywordsRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
    }

    public class PreviewAnnouncementRequest
    {
        public bool SendToAllSubscribers { get; set; }
        public List<int> SubscriptionIds { get; set; } = new();
    }

    public class ExportBackupRequest
    {
        public int OrganizationId { get; set; }
        public bool IncludeAttachments { get; set; } = true;
        public bool JsonOnly { get; set; }
    }

    [HttpPost("/api/superadmin/blog/suggest-keywords")]
    public async Task<IActionResult> SuggestBlogKeywords([FromBody] SuggestKeywordsRequest request)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var keywords = _keywordSuggestionService.SuggestKeywords(request?.Title, request?.Content);
        return Ok(new { success = true, keywords, count = keywords.Count });
    }

    [HttpPost("/api/superadmin/blog/preview-announcement")]
    public async Task<IActionResult> PreviewAnnouncementRecipients([FromBody] PreviewAnnouncementRequest request)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var preview = await _blogAnnouncementService.PreviewRecipientsAsync(request.SendToAllSubscribers, request.SubscriptionIds);
        return Ok(new { success = true, recipientCount = preview.RecipientCount, sampleRecipients = preview.SampleRecipients });
    }

    [HttpPost("/api/superadmin/blog/{id}/announcement/resend-failed")]
    public async Task<IActionResult> ResendFailedAnnouncementEmails(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var result = await _blogAnnouncementService.QueueAnnouncementAsync(id, resendFailedOnly: true);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { success = true, recipientCount = result.RecipientCount });
    }

    [HttpGet("/superadmin/organizations/backup-restore")]
    public async Task<IActionResult> BackupRestore([FromQuery] int? organizationId, [FromQuery] int page = 1)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        ViewData["ActivePage"] = "org-backups";
        var organizations = await _db.Organizations
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.Name)
            .ToListAsync();
        ViewBag.Organizations = organizations;
        ViewBag.SelectedOrganizationId = organizationId ?? organizations.FirstOrDefault()?.Id;
        ViewBag.History = ViewBag.SelectedOrganizationId is int orgId
            ? await _organizationBackupService.GetHistoryAsync(orgId, Math.Max(page, 1), 20)
            : Array.Empty<OrganizationBackupHistoryItem>();
        return View("~/Views/Admin/BackupRestore.cshtml");
    }

    [HttpPost("/api/superadmin/backups/export")]
    public async Task<IActionResult> ExportOrganizationBackup([FromBody] ExportBackupRequest request)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var artifact = await _organizationBackupService.CreateBackupAsync(
            request.OrganizationId,
            GetCurrentUserId(),
            request.IncludeAttachments,
            request.JsonOnly);
        return Ok(new
        {
            success = true,
            fileName = artifact.FileName,
            size = artifact.FileSizeBytes,
            downloadUrl = $"/api/superadmin/backups/download?organizationId={request.OrganizationId}&fileName={Uri.EscapeDataString(artifact.FileName)}"
        });
    }

    [HttpGet("/api/superadmin/backups/history")]
    public async Task<IActionResult> BackupHistory([FromQuery] int organizationId, [FromQuery] int page = 1)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var history = await _organizationBackupService.GetHistoryAsync(organizationId, Math.Max(page, 1), 20);
        return Ok(new { success = true, items = history });
    }

    [HttpGet("/api/superadmin/backups/download")]
    public async Task<IActionResult> DownloadBackup([FromQuery] int organizationId, [FromQuery] string fileName)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var path = _organizationBackupService.GetBackupFilePath(organizationId, fileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        var contentType = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/zip";
        return File(bytes, contentType, Path.GetFileName(fileName));
    }

    [HttpDelete("/api/superadmin/backups")]
    public async Task<IActionResult> DeleteBackup([FromQuery] int organizationId, [FromQuery] string fileName)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var deleted = await _organizationBackupService.DeleteBackupAsync(organizationId, fileName);
        return Ok(new { success = deleted });
    }

    [HttpPost("/api/superadmin/backups/import")]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
    public async Task<IActionResult> ImportBackup([FromForm] int organizationId, [FromForm] string mode, [FromForm] string? confirmationText, [FromForm] string? confirmationOrganizationName, [FromForm] IFormFile file)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var result = await _organizationBackupService.RestoreAsync(organizationId, file, mode, GetCurrentUserId(), confirmationText, confirmationOrganizationName);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { success = true, notes = result.Notes });
    }

    private static string Slugify(string? slug, string title)
    {
        var input = string.IsNullOrWhiteSpace(slug) ? title : slug!;
        var normalized = input.Trim().ToLowerInvariant();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(ch.ToString(), string.Empty, StringComparison.Ordinal);
        }

        return string.Join('-', normalized
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Replace("--", "-");
    }

    private async Task<string?> SaveFeaturedImageAsync(ImageUploadDto? featuredImage, string folderName, string? existingPath = null)
    {
        if (featuredImage == null)
        {
            return existingPath;
        }

        if (string.IsNullOrWhiteSpace(featuredImage.Base64Data))
        {
            if (!string.IsNullOrWhiteSpace(existingPath))
            {
                _imageUploadService.DeleteImage(existingPath);
            }
            return null;
        }

        var result = await _imageUploadService.SaveImageAsync(new ImageUploadRequest
        {
            FileName = featuredImage.FileName,
            ContentType = featuredImage.ContentType,
            Base64Data = featuredImage.Base64Data
        }, folderName);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(existingPath) && !string.Equals(existingPath, result.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            _imageUploadService.DeleteImage(existingPath);
        }

        return result.ImagePath;
    }

    private async Task SyncBlogImagesAsync(BlogPost post, IEnumerable<ContentImageDto>? galleryImages)
    {
        var requestedImages = galleryImages?.OrderBy(i => i.SortOrder).ToList() ?? new List<ContentImageDto>();
        var keepIds = requestedImages.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
        var removedImages = post.BlogImages.Where(i => !keepIds.Contains(i.Id)).ToList();
        foreach (var removed in removedImages)
        {
            _imageUploadService.DeleteImage(removed.ImagePath);
            post.BlogImages.Remove(removed);
        }

        foreach (var image in requestedImages)
        {
            BlogImage target;
            if (image.Id.HasValue)
            {
                target = post.BlogImages.First(i => i.Id == image.Id.Value);
                target.SortOrder = image.SortOrder;
                target.AltText = image.AltText;
            }
            else
            {
                if (image.Upload == null || string.IsNullOrWhiteSpace(image.Upload.Base64Data)) continue;
                var upload = await _imageUploadService.SaveImageAsync(new ImageUploadRequest
                {
                    FileName = image.Upload.FileName,
                    ContentType = image.Upload.ContentType,
                    Base64Data = image.Upload.Base64Data
                }, "blogs");
                if (!upload.Success || string.IsNullOrWhiteSpace(upload.ImagePath))
                {
                    throw new InvalidOperationException(upload.ErrorMessage);
                }

                target = new BlogImage
                {
                    ImagePath = upload.ImagePath,
                    SortOrder = image.SortOrder,
                    AltText = image.AltText,
                    CreatedAt = DateTime.UtcNow
                };
                post.BlogImages.Add(target);
            }
        }
    }

    private async Task SyncDocumentImagesAsync(DocArticle document, IEnumerable<ContentImageDto>? galleryImages)
    {
        var requestedImages = galleryImages?.OrderBy(i => i.SortOrder).ToList() ?? new List<ContentImageDto>();
        var keepIds = requestedImages.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
        var removedImages = document.DocumentImages.Where(i => !keepIds.Contains(i.Id)).ToList();
        foreach (var removed in removedImages)
        {
            _imageUploadService.DeleteImage(removed.ImagePath);
            document.DocumentImages.Remove(removed);
        }

        foreach (var image in requestedImages)
        {
            DocumentImage target;
            if (image.Id.HasValue)
            {
                target = document.DocumentImages.First(i => i.Id == image.Id.Value);
                target.SortOrder = image.SortOrder;
                target.AltText = image.AltText;
            }
            else
            {
                if (image.Upload == null || string.IsNullOrWhiteSpace(image.Upload.Base64Data)) continue;
                var upload = await _imageUploadService.SaveImageAsync(new ImageUploadRequest
                {
                    FileName = image.Upload.FileName,
                    ContentType = image.Upload.ContentType,
                    Base64Data = image.Upload.Base64Data
                }, "documents");
                if (!upload.Success || string.IsNullOrWhiteSpace(upload.ImagePath))
                {
                    throw new InvalidOperationException(upload.ErrorMessage);
                }

                target = new DocumentImage
                {
                    ImagePath = upload.ImagePath,
                    SortOrder = image.SortOrder,
                    AltText = image.AltText,
                    CreatedAt = DateTime.UtcNow
                };
                document.DocumentImages.Add(target);
            }
        }
    }

    private static string? NormalizeKeywords(string? csv)
    {
        var keywords = SplitKeywords(csv);
        return keywords.Count == 0 ? null : string.Join(", ", keywords);
    }

    private static List<string> SplitKeywords(string? csv)
    {
        return (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SyncBlogSubscriptions(BlogPost post, IEnumerable<int>? subscriptionIds)
    {
        var selectedIds = (subscriptionIds ?? Enumerable.Empty<int>()).Distinct().ToHashSet();
        var removed = post.BlogSubscriptions.Where(subscription => !selectedIds.Contains(subscription.SubscriptionId)).ToList();
        foreach (var item in removed)
        {
            post.BlogSubscriptions.Remove(item);
        }

        foreach (var selectedId in selectedIds)
        {
            if (post.BlogSubscriptions.All(subscription => subscription.SubscriptionId != selectedId))
            {
                post.BlogSubscriptions.Add(new BlogSubscription { SubscriptionId = selectedId });
            }
        }
    }
}
