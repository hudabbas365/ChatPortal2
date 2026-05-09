using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public class OrganizationBackupService : IOrganizationBackupService
{
    internal const string SchemaVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<OrganizationBackupService> _logger;

    public OrganizationBackupService(AppDbContext db, IWebHostEnvironment environment, ILogger<OrganizationBackupService> logger)
    {
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    public async Task<OrganizationBackupArtifact> CreateBackupAsync(int organizationId, string? performedByUserId, bool includeAttachments, bool jsonOnly, CancellationToken cancellationToken = default)
    {
        var package = await BuildPackageAsync(organizationId, cancellationToken);
        var dataBytes = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var extension = jsonOnly ? ".json" : ".zip";
        var fileName = $"org-backup-{timestamp}{extension}";

        byte[] fileBytes;
        var manifestFiles = new List<BackupManifestFile>
        {
            new() { Path = "data.json", Sha256 = ComputeSha256(dataBytes) }
        };

        if (jsonOnly)
        {
            fileBytes = dataBytes;
        }
        else
        {
            var attachments = includeAttachments ? CollectAttachmentBytes(package) : new Dictionary<string, byte[]>();
            foreach (var attachment in attachments)
            {
                manifestFiles.Add(new BackupManifestFile { Path = attachment.Key, Sha256 = ComputeSha256(attachment.Value) });
            }

            var manifest = new BackupManifest
            {
                SchemaVersion = SchemaVersion,
                AppVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                GeneratedAtUtc = DateTime.UtcNow,
                ExportingAdminId = performedByUserId,
                OrganizationId = organizationId,
                Files = manifestFiles
            };

            await using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var dataEntry = archive.CreateEntry("data.json", CompressionLevel.Fastest);
                await using (var dataStream = dataEntry.Open())
                {
                    await dataStream.WriteAsync(dataBytes, cancellationToken);
                }

                foreach (var attachment in attachments)
                {
                    var attachmentEntry = archive.CreateEntry(attachment.Key, CompressionLevel.Fastest);
                    await using var entryStream = attachmentEntry.Open();
                    await entryStream.WriteAsync(attachment.Value, cancellationToken);
                }

                var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using var manifestStream = manifestEntry.Open();
                await manifestStream.WriteAsync(manifestBytes, cancellationToken);
            }

            fileBytes = ms.ToArray();
        }

        var savedPath = GetBackupFilePath(organizationId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(savedPath)!);
        await File.WriteAllBytesAsync(savedPath, fileBytes, cancellationToken);

        _db.OrganizationBackupAudits.Add(new OrganizationBackupAudit
        {
            Action = "Backup",
            Mode = jsonOnly ? "JsonOnly" : "FullZip",
            FileName = fileName,
            OrganizationId = organizationId,
            PerformedByUserId = performedByUserId,
            PerformedAt = DateTime.UtcNow,
            Notes = includeAttachments ? "Attachments included." : "Attachments excluded.",
            FileSizeBytes = fileBytes.LongLength
        });
        await _db.SaveChangesAsync(cancellationToken);

        return new OrganizationBackupArtifact
        {
            FileName = fileName,
            ContentType = jsonOnly ? "application/json" : "application/zip",
            Bytes = fileBytes,
            SavedPath = savedPath
        };
    }

    public async Task<OrganizationRestoreResult> RestoreAsync(int organizationId, IFormFile backupFile, string mode, string? performedByUserId, string? confirmationText, string? confirmationOrganizationName, CancellationToken cancellationToken = default)
    {
        if (backupFile == null || backupFile.Length == 0)
        {
            return new OrganizationRestoreResult { ErrorMessage = "Please choose a backup file to restore." };
        }

        var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        if (organization == null)
        {
            return new OrganizationRestoreResult { ErrorMessage = "Organization not found." };
        }

        if (mode.Equals("Replace", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(confirmationText, "REPLACE", StringComparison.Ordinal) || !string.Equals(confirmationOrganizationName, organization.Name, StringComparison.Ordinal))
            {
                return new OrganizationRestoreResult { ErrorMessage = "Replace mode requires typing REPLACE and the exact organization name." };
            }
        }

        OrganizationBackupPackage package;
        Dictionary<string, byte[]> attachments;
        try
        {
            (package, attachments) = await ReadPackageAsync(backupFile, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return new OrganizationRestoreResult { ErrorMessage = ex.Message };
        }

        if (package.Organization.Id != organizationId)
        {
            return new OrganizationRestoreResult { ErrorMessage = "This backup belongs to a different organization." };
        }

        IDbContextTransactionWrapper? transaction = null;
        if (_db.Database.IsRelational())
        {
            transaction = new EfCoreTransactionWrapper(await _db.Database.BeginTransactionAsync(cancellationToken));
        }

        try
        {
            if (mode.Equals("Replace", StringComparison.OrdinalIgnoreCase))
            {
                await WipeOrganizationDataAsync(organizationId, cancellationToken);
            }

            await RestoreOrganizationAsync(package, mode, cancellationToken);
            RestoreAttachments(attachments);

            _db.OrganizationBackupAudits.Add(new OrganizationBackupAudit
            {
                Action = "Restore",
                Mode = mode,
                FileName = backupFile.FileName,
                OrganizationId = organizationId,
                PerformedByUserId = performedByUserId,
                PerformedAt = DateTime.UtcNow,
                Notes = $"Restored {package.Users.Count} users, {package.BlogPosts.Count} blog posts, and {package.Documents.Count} documents.",
                FileSizeBytes = backupFile.Length
            });
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new OrganizationRestoreResult { Success = true, Notes = "Backup restored successfully." };
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            _logger.LogError(ex, "Organization restore failed for organization {OrganizationId}.", organizationId);
            return new OrganizationRestoreResult { ErrorMessage = "The backup could not be restored. No changes were applied." };
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<OrganizationBackupHistoryItem>> GetHistoryAsync(int organizationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _db.OrganizationBackupAudits
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.PerformedAt)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .Select(a => new OrganizationBackupHistoryItem
            {
                FileName = a.FileName,
                Mode = a.Mode,
                PerformedAt = a.PerformedAt,
                PerformedByUserId = a.PerformedByUserId,
                FileSizeBytes = a.FileSizeBytes,
                Notes = a.Notes
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteBackupAsync(int organizationId, string fileName, CancellationToken cancellationToken = default)
    {
        var path = GetBackupFilePath(organizationId, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        var audits = await _db.OrganizationBackupAudits
            .Where(a => a.OrganizationId == organizationId && a.FileName == fileName)
            .ToListAsync(cancellationToken);
        if (audits.Count > 0)
        {
            _db.OrganizationBackupAudits.RemoveRange(audits);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public string GetBackupFilePath(int organizationId, string fileName)
    {
        var root = Path.Combine(_environment.ContentRootPath, "App_Data", "backups", "org", organizationId.ToString());
        return Path.Combine(root, Path.GetFileName(fileName));
    }

    private async Task<OrganizationBackupPackage> BuildPackageAsync(int organizationId, CancellationToken cancellationToken)
    {
        var organization = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == organizationId, cancellationToken);
        var users = await _db.Users.AsNoTracking().Where(u => u.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var userIds = users.Select(u => u.Id).ToList();
        var subscriptions = await _db.SubscriptionPlans.AsNoTracking().Where(s => userIds.Contains(s.UserId)).ToListAsync(cancellationToken);
        var blogPosts = await _db.BlogPosts.AsNoTracking().Include(b => b.BlogImages).Include(b => b.BlogSubscriptions).ToListAsync(cancellationToken);
        var docArticles = await _db.DocArticles.AsNoTracking().Include(d => d.DocumentImages).ToListAsync(cancellationToken);

        return new OrganizationBackupPackage
        {
            SchemaVersion = SchemaVersion,
            Organization = new BackupOrganizationDto
            {
                Id = organization.Id,
                OrganizationGuid = organization.OrganizationGuid,
                Name = organization.Name,
                LogoUrl = organization.LogoUrl,
                Plan = organization.Plan,
                CreatedAt = organization.CreatedAt,
                IsActive = organization.IsActive,
                IsDeleted = organization.IsDeleted
            },
            Users = users.Select(u => new BackupUserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                UserName = u.UserName ?? string.Empty,
                FullName = u.FullName,
                Role = u.Role,
                Status = u.Status,
                IsSubscribedToAnnouncements = u.IsSubscribedToAnnouncements,
                EmailConfirmed = u.EmailConfirmed
            }).ToList(),
            Subscriptions = subscriptions.Select(s => new BackupSubscriptionDto
            {
                UserId = s.UserId,
                Plan = s.Plan,
                TrialStartDate = s.TrialStartDate,
                TrialEndDate = s.TrialEndDate,
                HasUsedTrial = s.HasUsedTrial,
                CreatedAt = s.CreatedAt
            }).ToList(),
            BlogPosts = blogPosts.Select(b => new BackupBlogPostDto
            {
                Title = b.Title,
                Slug = b.Slug,
                Summary = b.Summary,
                Content = b.Content,
                Author = b.Author,
                ImageUrl = b.ImageUrl,
                FeaturedImagePath = b.FeaturedImagePath,
                SeoKeywords = b.SeoKeywords,
                IsFeatureAnnouncement = b.IsFeatureAnnouncement,
                EmailSubject = b.EmailSubject,
                SendToAllSubscribers = b.SendToAllSubscribers,
                PublishedAt = b.PublishedAt,
                IsPublished = b.IsPublished,
                BlogImages = b.BlogImages.Select(i => new BackupImageDto { ImagePath = i.ImagePath, SortOrder = i.SortOrder, AltText = i.AltText, CreatedAt = i.CreatedAt }).ToList(),
                SubscriptionIds = b.BlogSubscriptions.Select(s => s.SubscriptionId).ToList()
            }).ToList(),
            Documents = docArticles.Select(d => new BackupDocumentDto
            {
                Title = d.Title,
                Slug = d.Slug,
                Summary = d.Summary,
                Content = d.Content,
                Author = d.Author,
                FeaturedImagePath = d.FeaturedImagePath,
                SortOrder = d.SortOrder,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                IsPublished = d.IsPublished,
                DocumentImages = d.DocumentImages.Select(i => new BackupImageDto { ImagePath = i.ImagePath, SortOrder = i.SortOrder, AltText = i.AltText, CreatedAt = i.CreatedAt }).ToList()
            }).ToList()
        };
    }

    private Dictionary<string, byte[]> CollectAttachmentBytes(OrganizationBackupPackage package)
    {
        var allPaths = package.BlogPosts
            .SelectMany(post => post.BlogImages.Select(i => i.ImagePath).Concat(new[] { post.FeaturedImagePath, post.ImageUrl }))
            .Concat(package.Documents.SelectMany(doc => doc.DocumentImages.Select(i => i.ImagePath).Concat(new[] { doc.FeaturedImagePath })))
            .Where(path => !string.IsNullOrWhiteSpace(path) && path!.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allPaths)
        {
            var relative = path.TrimStart('/');
            var absolute = Path.Combine(_environment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            result[$"uploads/{relative["uploads/".Length..]}"] = File.ReadAllBytes(absolute);
        }

        return result;
    }

    private async Task<(OrganizationBackupPackage Package, Dictionary<string, byte[]> Attachments)> ReadPackageAsync(IFormFile backupFile, CancellationToken cancellationToken)
    {
        await using var input = backupFile.OpenReadStream();
        if (backupFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms, cancellationToken);
            var package = JsonSerializer.Deserialize<OrganizationBackupPackage>(ms.ToArray(), JsonOptions)
                ?? throw new InvalidDataException("The backup file is empty or invalid.");
            ValidatePackage(package);
            return (package, new Dictionary<string, byte[]>());
        }

        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup manifest.json is missing.");
        var dataEntry = zip.GetEntry("data.json") ?? throw new InvalidDataException("Backup data.json is missing.");

        BackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Backup manifest is invalid.");
        }

        if (!string.Equals(manifest.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Backup schema version is not supported.");
        }

        using var dataMs = new MemoryStream();
        await using (var dataStream = dataEntry.Open())
        {
            await dataStream.CopyToAsync(dataMs, cancellationToken);
        }
        var dataBytes = dataMs.ToArray();

        var expectedData = manifest.Files.FirstOrDefault(f => f.Path == "data.json")?.Sha256;
        if (!string.Equals(expectedData, ComputeSha256(dataBytes), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Backup data checksum validation failed.");
        }

        var package = JsonSerializer.Deserialize<OrganizationBackupPackage>(dataBytes, JsonOptions)
            ?? throw new InvalidDataException("Backup payload is invalid.");
        ValidatePackage(package);

        var attachments = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files.Where(f => f.Path.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateZipPath(file.Path);
            var entry = zip.GetEntry(file.Path) ?? throw new InvalidDataException($"Backup attachment '{file.Path}' is missing.");
            using var ms = new MemoryStream();
            await using var entryStream = entry.Open();
            await entryStream.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            if (!string.Equals(file.Sha256, ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Checksum validation failed for '{file.Path}'.");
            }
            attachments[file.Path] = bytes;
        }

        return (package, attachments);
    }

    private static void ValidatePackage(OrganizationBackupPackage package)
    {
        if (!string.Equals(package.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Backup schema version is not supported.");
        }

        if (package.Organization == null || string.IsNullOrWhiteSpace(package.Organization.Name))
        {
            throw new InvalidDataException("Backup organization data is incomplete.");
        }
    }

    private async Task WipeOrganizationDataAsync(int organizationId, CancellationToken cancellationToken)
    {
        var userIds = await _db.Users.Where(u => u.OrganizationId == organizationId).Select(u => u.Id).ToListAsync(cancellationToken);
        var subscriptions = await _db.SubscriptionPlans.Where(s => userIds.Contains(s.UserId)).ToListAsync(cancellationToken);
        var users = await _db.Users.Where(u => u.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var blogs = await _db.BlogPosts.Include(b => b.BlogImages).Include(b => b.BlogSubscriptions).ToListAsync(cancellationToken);
        var docs = await _db.DocArticles.Include(d => d.DocumentImages).ToListAsync(cancellationToken);

        _db.SubscriptionPlans.RemoveRange(subscriptions);
        _db.Users.RemoveRange(users);
        _db.BlogPosts.RemoveRange(blogs);
        _db.DocArticles.RemoveRange(docs);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreOrganizationAsync(OrganizationBackupPackage package, string mode, CancellationToken cancellationToken)
    {
        var organization = await _db.Organizations.FirstAsync(o => o.Id == package.Organization.Id, cancellationToken);
        organization.Name = package.Organization.Name;
        organization.LogoUrl = package.Organization.LogoUrl;
        organization.Plan = package.Organization.Plan;
        organization.IsActive = package.Organization.IsActive;
        organization.IsDeleted = package.Organization.IsDeleted;
        organization.OrganizationGuid = package.Organization.OrganizationGuid;

        foreach (var userDto in package.Users)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == userDto.Email, cancellationToken);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    Id = string.IsNullOrWhiteSpace(userDto.Id) ? Guid.NewGuid().ToString("N") : userDto.Id,
                    Email = userDto.Email,
                    UserName = string.IsNullOrWhiteSpace(userDto.UserName) ? userDto.Email : userDto.UserName,
                    NormalizedEmail = userDto.Email.ToUpperInvariant(),
                    NormalizedUserName = (string.IsNullOrWhiteSpace(userDto.UserName) ? userDto.Email : userDto.UserName).ToUpperInvariant(),
                    CreatedAt = DateTime.UtcNow
                };
                _db.Users.Add(user);
            }

            user.OrganizationId = organization.Id;
            user.FullName = userDto.FullName;
            user.Role = userDto.Role;
            user.Status = userDto.Status;
            user.IsSubscribedToAnnouncements = userDto.IsSubscribedToAnnouncements;
            user.EmailConfirmed = userDto.EmailConfirmed;
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var subscriptionDto in package.Subscriptions)
        {
            var user = await _db.Users.FirstAsync(u => u.Id == subscriptionDto.UserId || u.Email == package.Users.First(p => p.Id == subscriptionDto.UserId).Email, cancellationToken);
            var subscription = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken);
            if (subscription == null)
            {
                subscription = new SubscriptionPlan { UserId = user.Id };
                _db.SubscriptionPlans.Add(subscription);
            }

            subscription.Plan = subscriptionDto.Plan;
            subscription.TrialStartDate = subscriptionDto.TrialStartDate;
            subscription.TrialEndDate = subscriptionDto.TrialEndDate;
            subscription.HasUsedTrial = subscriptionDto.HasUsedTrial;
            subscription.CreatedAt = subscriptionDto.CreatedAt;
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var blogDto in package.BlogPosts)
        {
            var blog = await _db.BlogPosts.Include(b => b.BlogImages).Include(b => b.BlogSubscriptions).FirstOrDefaultAsync(b => b.Slug == blogDto.Slug, cancellationToken);
            if (blog == null)
            {
                blog = new BlogPost { Slug = blogDto.Slug };
                _db.BlogPosts.Add(blog);
            }

            blog.Title = blogDto.Title;
            blog.Summary = blogDto.Summary;
            blog.Content = blogDto.Content;
            blog.Author = blogDto.Author;
            blog.ImageUrl = blogDto.ImageUrl;
            blog.FeaturedImagePath = blogDto.FeaturedImagePath;
            blog.SeoKeywords = blogDto.SeoKeywords;
            blog.IsFeatureAnnouncement = blogDto.IsFeatureAnnouncement;
            blog.EmailSubject = blogDto.EmailSubject;
            blog.SendToAllSubscribers = blogDto.SendToAllSubscribers;
            blog.IsPublished = blogDto.IsPublished;
            blog.PublishedAt = blogDto.PublishedAt;

            blog.BlogImages.Clear();
            foreach (var image in blogDto.BlogImages.OrderBy(i => i.SortOrder))
            {
                blog.BlogImages.Add(new BlogImage { ImagePath = image.ImagePath, SortOrder = image.SortOrder, AltText = image.AltText, CreatedAt = image.CreatedAt });
            }

            blog.BlogSubscriptions.Clear();
            foreach (var subscriptionId in blogDto.SubscriptionIds.Distinct())
            {
                blog.BlogSubscriptions.Add(new BlogSubscription { SubscriptionId = subscriptionId });
            }
        }

        foreach (var docDto in package.Documents)
        {
            var document = await _db.DocArticles.Include(d => d.DocumentImages).FirstOrDefaultAsync(d => d.Slug == docDto.Slug, cancellationToken);
            if (document == null)
            {
                document = new DocArticle { Slug = docDto.Slug };
                _db.DocArticles.Add(document);
            }

            document.Title = docDto.Title;
            document.Summary = docDto.Summary;
            document.Content = docDto.Content;
            document.Author = docDto.Author;
            document.FeaturedImagePath = docDto.FeaturedImagePath;
            document.SortOrder = docDto.SortOrder;
            document.CreatedAt = docDto.CreatedAt;
            document.UpdatedAt = docDto.UpdatedAt;
            document.IsPublished = docDto.IsPublished;

            document.DocumentImages.Clear();
            foreach (var image in docDto.DocumentImages.OrderBy(i => i.SortOrder))
            {
                document.DocumentImages.Add(new DocumentImage { ImagePath = image.ImagePath, SortOrder = image.SortOrder, AltText = image.AltText, CreatedAt = image.CreatedAt });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void RestoreAttachments(Dictionary<string, byte[]> attachments)
    {
        foreach (var attachment in attachments)
        {
            ValidateZipPath(attachment.Key);
            var destination = Path.GetFullPath(Path.Combine(_environment.WebRootPath, attachment.Key.Replace("uploads/", "uploads" + Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.WebRootPath, "uploads"));
            if (!destination.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Backup attachment path is invalid.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, attachment.Value);
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void ValidateZipPath(string path)
    {
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException("Backup contains an invalid file path.");
        }
    }

    private interface IDbContextTransactionWrapper : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken);
        Task RollbackAsync(CancellationToken cancellationToken);
    }

    private sealed class EfCoreTransactionWrapper : IDbContextTransactionWrapper
    {
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _transaction;

        public EfCoreTransactionWrapper(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken) => _transaction.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }

    internal class OrganizationBackupPackage
    {
        public string SchemaVersion { get; set; } = SchemaVersion;
        public BackupOrganizationDto Organization { get; set; } = new();
        public List<BackupUserDto> Users { get; set; } = new();
        public List<BackupSubscriptionDto> Subscriptions { get; set; } = new();
        public List<BackupBlogPostDto> BlogPosts { get; set; } = new();
        public List<BackupDocumentDto> Documents { get; set; } = new();
    }

    internal class BackupOrganizationDto
    {
        public int Id { get; set; }
        public Guid OrganizationGuid { get; set; }
        public string Name { get; set; } = "";
        public string? LogoUrl { get; set; }
        public PlanType Plan { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
    }

    internal class BackupUserDto
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "User";
        public string Status { get; set; } = "Active";
        public bool IsSubscribedToAnnouncements { get; set; }
        public bool EmailConfirmed { get; set; }
    }

    internal class BackupSubscriptionDto
    {
        public string UserId { get; set; } = "";
        public PlanType Plan { get; set; }
        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public bool HasUsedTrial { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal class BackupBlogPostDto
    {
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Author { get; set; }
        public string? ImageUrl { get; set; }
        public string? FeaturedImagePath { get; set; }
        public string? SeoKeywords { get; set; }
        public bool IsFeatureAnnouncement { get; set; }
        public string? EmailSubject { get; set; }
        public bool SendToAllSubscribers { get; set; }
        public DateTime PublishedAt { get; set; }
        public bool IsPublished { get; set; }
        public List<BackupImageDto> BlogImages { get; set; } = new();
        public List<int> SubscriptionIds { get; set; } = new();
    }

    internal class BackupDocumentDto
    {
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Author { get; set; }
        public string? FeaturedImagePath { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsPublished { get; set; }
        public List<BackupImageDto> DocumentImages { get; set; } = new();
    }

    internal class BackupImageDto
    {
        public string ImagePath { get; set; } = "";
        public int SortOrder { get; set; }
        public string? AltText { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal class BackupManifest
    {
        public string SchemaVersion { get; set; } = SchemaVersion;
        public string AppVersion { get; set; } = "unknown";
        public DateTime GeneratedAtUtc { get; set; }
        public string? ExportingAdminId { get; set; }
        public int OrganizationId { get; set; }
        public List<BackupManifestFile> Files { get; set; } = new();
    }

    internal class BackupManifestFile
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }
}

public class OrganizationBackupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrganizationBackupScheduler> _logger;

    public OrganizationBackupScheduler(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OrganizationBackupScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("OrganizationBackup:Enabled", true);
        if (!enabled)
        {
            return;
        }

        var intervalHours = Math.Max(1, _configuration.GetValue("OrganizationBackup:IntervalHours", 24));
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IOrganizationBackupService>();
                var includeAttachments = _configuration.GetValue("OrganizationBackup:IncludeAttachments", true);
                var retention = Math.Max(1, _configuration.GetValue("OrganizationBackup:Retention", 30));
                var orgIds = await db.Organizations.Where(o => !o.IsDeleted).Select(o => o.Id).ToListAsync(stoppingToken);
                foreach (var orgId in orgIds)
                {
                    await service.CreateBackupAsync(orgId, "system", includeAttachments, jsonOnly: false, stoppingToken);
                    var history = await service.GetHistoryAsync(orgId, 1, 200, stoppingToken);
                    foreach (var oldItem in history.Skip(retention))
                    {
                        await service.DeleteBackupAsync(orgId, oldItem.FileName, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduled organization backup failed.");
            }
        }
    }
}
