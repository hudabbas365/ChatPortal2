using AIInsights.Services;
using Microsoft.AspNetCore.Hosting;

namespace ChatPortal2.Tests;

public class ImageUploadServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TestWebHostEnvironment _environment;

    public ImageUploadServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chatportal2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
        _environment = new TestWebHostEnvironment(_root);
    }

    [Fact]
    public async Task SaveImageAsync_SavesValidImage()
    {
        var service = new ImageUploadService(_environment);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var payload = new ImageUploadRequest
        {
            FileName = "feature.png",
            ContentType = "image/png",
            Base64Data = Convert.ToBase64String(bytes)
        };

        var result = await service.SaveImageAsync(payload, "blogs");

        Assert.True(result.Success);
        Assert.NotNull(result.ImagePath);
        Assert.True(File.Exists(Path.Combine(_environment.WebRootPath!, result.ImagePath!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task SaveImageAsync_RejectsInvalidType()
    {
        var service = new ImageUploadService(_environment);
        var result = await service.SaveImageAsync(new ImageUploadRequest
        {
            FileName = "feature.txt",
            ContentType = "text/plain",
            Base64Data = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        }, "blogs");

        Assert.False(result.Success);
        Assert.Equal("Only JPG, JPEG, PNG, WEBP, and GIF images are allowed.", result.ErrorMessage);
    }

    [Fact]
    public async Task SaveImageAsync_RejectsOversizedImages()
    {
        var service = new ImageUploadService(_environment);
        var largeBytes = new byte[ImageUploadService.MaxImageBytes + 1];
        var result = await service.SaveImageAsync(new ImageUploadRequest
        {
            FileName = "feature.png",
            ContentType = "image/png",
            Base64Data = Convert.ToBase64String(largeBytes)
        }, "blogs");

        Assert.False(result.Success);
        Assert.Equal("Images must be 5 MB or smaller.", result.ErrorMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
