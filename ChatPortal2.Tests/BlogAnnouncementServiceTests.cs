using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatPortal2.Tests;

public class BlogAnnouncementServiceTests
{
    [Fact]
    public async Task QueueAnnouncementAsync_IsIdempotent_UnlessResendingFailed()
    {
        await using var db = CreateDb();
        SeedAnnouncementData(db);
        var sender = new FakeAnnouncementEmailSender { NextResults = new Queue<bool>(new[] { false, false, false, true }) };
        var service = new BlogAnnouncementService(db, sender, BuildConfiguration(), NullLogger<BlogAnnouncementService>.Instance);

        var initial = await service.QueueAnnouncementAsync(1);
        var second = await service.QueueAnnouncementAsync(1);

        Assert.True(initial.Success);
        Assert.False(second.Success);

        await service.ProcessQueuedEmailsAsync();
        await service.ProcessQueuedEmailsAsync();
        await service.ProcessQueuedEmailsAsync();

        Assert.Equal("Failed", db.BlogAnnouncementEmailLogs.Single().Status);

        var resend = await service.QueueAnnouncementAsync(1, resendFailedOnly: true);
        Assert.True(resend.Success);

        await service.ProcessQueuedEmailsAsync();
        Assert.Equal("Sent", db.BlogAnnouncementEmailLogs.Single().Status);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedAnnouncementData(AppDbContext db)
    {
        var org = new Organization { Id = 1, Name = "Acme", Plan = PlanType.Professional };
        var user = new ApplicationUser { Id = "user-1", Email = "user@example.com", UserName = "user@example.com", NormalizedEmail = "USER@EXAMPLE.COM", NormalizedUserName = "USER@EXAMPLE.COM", FullName = "Example User", OrganizationId = 1, IsSubscribedToAnnouncements = true };
        var subscription = new SubscriptionPlan { UserId = "user-1", Plan = PlanType.Professional, CreatedAt = DateTime.UtcNow };
        var blog = new BlogPost { Id = 1, Title = "New dashboard filters", Slug = "new-dashboard-filters", Summary = "Feature summary", Content = "Feature content", IsFeatureAnnouncement = true, SendToAllSubscribers = true, IsPublished = true, PublishedAt = DateTime.UtcNow };
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.SubscriptionPlans.Add(subscription);
        db.BlogPosts.Add(blog);
        db.SaveChanges();
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://example.test" })
        .Build();
}
