using AIInsights.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ChatPortal2.Tests;

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public TestWebHostEnvironment(string contentRoot)
    {
        ContentRootPath = contentRoot;
        WebRootPath = Path.Combine(contentRoot, "wwwroot");
        ContentRootFileProvider = new PhysicalFileProvider(contentRoot);
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
        ApplicationName = "ChatPortal2.Tests";
        EnvironmentName = "Development";
    }

    public string ApplicationName { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

internal sealed class FakeAnnouncementEmailSender : IAnnouncementEmailSender
{
    public Queue<bool> NextResults { get; set; } = new();

    public Task<bool> SendAsync(AnnouncementEmailMessage message, CancellationToken cancellationToken = default)
    {
        if (NextResults.Count == 0)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(NextResults.Dequeue());
    }
}
