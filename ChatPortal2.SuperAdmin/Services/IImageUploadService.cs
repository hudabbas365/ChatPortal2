using Microsoft.AspNetCore.Http;

namespace AIInsights.SuperAdmin.Services;

public interface IImageUploadService
{
    Task<(bool Success, string? ImagePath, string? ErrorMessage)> SaveImageAsync(IFormFile file, string contentTypeFolder, CancellationToken cancellationToken = default);
    bool DeleteImage(string? imagePath);
}
