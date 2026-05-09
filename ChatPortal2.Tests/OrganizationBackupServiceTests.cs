using System.IO.Compression;
using System.Text;
using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatPortal2.Tests;

public class OrganizationBackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TestWebHostEnvironment _environment;

    public OrganizationBackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chatportal2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot", "uploads", "blogs"));
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot", "uploads", "documents"));
        _environment = new TestWebHostEnvironment(_root);
    }

    [Fact]
    public async Task BackupRestore_Merge_RoundTripsCoreData()
    {
        await using var db = CreateDb();
        SeedBackupData(db, _environment);
        var service = new OrganizationBackupService(db, _environment, NullLogger<OrganizationBackupService>.Instance);

        var artifact = await service.CreateBackupAsync(1, "admin", includeAttachments: true, jsonOnly: false);

        var blog = db.BlogPosts.Single();
        blog.Title = "Changed title";
        db.DocArticles.Remove(db.DocArticles.Single());
        await db.SaveChangesAsync();

        var restore = await service.RestoreAsync(1, CreateFormFile(artifact.Bytes, artifact.FileName), "Merge", "admin", null, null);

        Assert.True(restore.Success, restore.ErrorMessage);
        Assert.Equal("Feature launch", db.BlogPosts.Single().Title);
        Assert.Single(db.DocArticles);
        Assert.True(File.Exists(Path.Combine(_environment.WebRootPath!, "uploads", "blogs", "feature.png")));
    }

    [Fact]
    public async Task BackupRestore_Replace_RemovesRowsNotInBackup()
    {
        await using var db = CreateDb();
        SeedBackupData(db, _environment);
        var service = new OrganizationBackupService(db, _environment, NullLogger<OrganizationBackupService>.Instance);
        var artifact = await service.CreateBackupAsync(1, "admin", includeAttachments: true, jsonOnly: false);

        db.BlogPosts.Add(new BlogPost { Title = "Extra", Slug = "extra", Summary = "x", Content = "x", IsPublished = true });
        await db.SaveChangesAsync();

        var restore = await service.RestoreAsync(1, CreateFormFile(artifact.Bytes, artifact.FileName), "Replace", "admin", "REPLACE", "Acme");

        Assert.True(restore.Success, restore.ErrorMessage);
        Assert.Single(db.BlogPosts);
        Assert.Equal("Feature launch", db.BlogPosts.Single().Title);
    }

    [Fact]
    public async Task BackupRestore_InvalidManifest_IsRejected()
    {
        await using var db = CreateDb();
        SeedBackupData(db, _environment);
        var service = new OrganizationBackupService(db, _environment, NullLogger<OrganizationBackupService>.Instance);
        var artifact = await service.CreateBackupAsync(1, "admin", includeAttachments: true, jsonOnly: false);
        var tampered = CorruptManifest(artifact.Bytes);

        var restore = await service.RestoreAsync(1, CreateFormFile(tampered, artifact.FileName), "Merge", "admin", null, null);

        Assert.False(restore.Success);
        Assert.Contains("checksum", restore.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedBackupData(AppDbContext db, TestWebHostEnvironment environment)
    {
        File.WriteAllText(Path.Combine(environment.WebRootPath!, "uploads", "blogs", "feature.png"), "image");
        File.WriteAllText(Path.Combine(environment.WebRootPath!, "uploads", "documents", "doc.png"), "image");

        var org = new Organization { Id = 1, Name = "Acme", Plan = PlanType.Professional };
        var user = new ApplicationUser { Id = "user-1", Email = "user@example.com", UserName = "user@example.com", NormalizedEmail = "USER@EXAMPLE.COM", NormalizedUserName = "USER@EXAMPLE.COM", FullName = "Example User", OrganizationId = 1, EmailConfirmed = true, IsSubscribedToAnnouncements = true };
        var subscription = new SubscriptionPlan { UserId = "user-1", Plan = PlanType.Professional, CreatedAt = DateTime.UtcNow };
        var blog = new BlogPost
        {
            Id = 1,
            Title = "Feature launch",
            Slug = "feature-launch",
            Summary = "Summary",
            Content = "Body",
            FeaturedImagePath = "/uploads/blogs/feature.png",
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            BlogImages = new List<BlogImage> { new() { Id = 1, ImagePath = "/uploads/blogs/feature.png", SortOrder = 0, AltText = "Feature" } },
            BlogSubscriptions = new List<BlogSubscription> { new() { BlogId = 1, SubscriptionId = (int)PlanType.Professional } }
        };
        var doc = new DocArticle
        {
            Id = 1,
            Title = "Doc title",
            Slug = "doc-title",
            Summary = "Doc summary",
            Content = "Doc body",
            FeaturedImagePath = "/uploads/documents/doc.png",
            SortOrder = 1,
            IsPublished = true,
            DocumentImages = new List<DocumentImage> { new() { Id = 1, ImagePath = "/uploads/documents/doc.png", SortOrder = 0, AltText = "Doc" } }
        };
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.SubscriptionPlans.Add(subscription);
        db.BlogPosts.Add(blog);
        db.DocArticles.Add(doc);
        db.SaveChanges();
    }

    private static FormFile CreateFormFile(byte[] bytes, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    private static byte[] CorruptManifest(byte[] zipBytes)
    {
        using var input = new MemoryStream(zipBytes);
        using var output = new MemoryStream();
        using (var source = new ZipArchive(input, ZipArchiveMode.Read, true))
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                var newEntry = destination.CreateEntry(entry.FullName);
                using var sourceStream = entry.Open();
                using var targetStream = newEntry.Open();
                if (entry.FullName == "manifest.json")
                {
                    using var reader = new StreamReader(sourceStream, Encoding.UTF8);
                    var manifest = reader.ReadToEnd().Replace("data.json", "data-bad.json");
                    using var writer = new StreamWriter(targetStream, Encoding.UTF8, leaveOpen: true);
                    writer.Write(manifest);
                    writer.Flush();
                }
                else
                {
                    sourceStream.CopyTo(targetStream);
                }
            }
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
