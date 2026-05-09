using Microsoft.AspNetCore.Http;

namespace AIInsights.Services;

public interface IImageUploadService
{
    Task<ImageUploadResult> SaveImageAsync(ImageUploadRequest request, string folderName, CancellationToken cancellationToken = default);
    void DeleteImage(string? relativePath);
}

public class ImageUploadRequest
{
    public string FileName { get; set; } = "image";
    public string? ContentType { get; set; }
    public string Base64Data { get; set; } = "";
}

public class ImageUploadResult
{
    public bool Success { get; set; }
    public string? ImagePath { get; set; }
    public string? ErrorMessage { get; set; }
}
