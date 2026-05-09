using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace AIInsights.Services;

public class ImageUploadService : IImageUploadService
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private static readonly Dictionary<string, string> ContentTypeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    private readonly IWebHostEnvironment _environment;

    public ImageUploadService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<ImageUploadResult> SaveImageAsync(ImageUploadRequest request, string folderName, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Base64Data))
        {
            return new ImageUploadResult { ErrorMessage = "Please select an image to upload." };
        }

        var extension = ResolveExtension(request.FileName, request.ContentType);
        if (extension == null)
        {
            return new ImageUploadResult { ErrorMessage = "Only JPG, JPEG, PNG, WEBP, and GIF images are allowed." };
        }

        byte[] bytes;
        try
        {
            bytes = DecodeBase64(request.Base64Data);
        }
        catch (FormatException)
        {
            return new ImageUploadResult { ErrorMessage = "The selected image could not be processed." };
        }

        if (bytes.Length == 0)
        {
            return new ImageUploadResult { ErrorMessage = "The selected image is empty." };
        }

        if (bytes.Length > MaxImageBytes)
        {
            return new ImageUploadResult { ErrorMessage = "Images must be 5 MB or smaller." };
        }

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
        }

        var safeFolder = string.IsNullOrWhiteSpace(folderName) ? "misc" : folderName.Trim().Trim('/').Replace("..", string.Empty);
        var uploadsFolder = Path.Combine(webRoot, "uploads", safeFolder);
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}{extension}";
        var absolutePath = Path.Combine(uploadsFolder, fileName);
        await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);

        return new ImageUploadResult
        {
            Success = true,
            ImagePath = $"/uploads/{safeFolder}/{fileName}"
        };
    }

    public void DeleteImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
        }

        var trimmed = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(Path.Combine(webRoot, trimmed.Replace("uploads" + Path.DirectorySeparatorChar, string.Empty)));
        var uploadsRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads"));
        if (!absolutePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(absolutePath))
        {
            return;
        }

        File.Delete(absolutePath);
    }

    private static string? ResolveExtension(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension))
        {
            return extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(contentType) && ContentTypeToExtension.TryGetValue(contentType.Trim(), out var mapped))
        {
            return mapped;
        }

        return null;
    }

    private static byte[] DecodeBase64(string base64Data)
    {
        var data = base64Data.Trim();
        var commaIndex = data.IndexOf(',');
        if (commaIndex >= 0)
        {
            data = data[(commaIndex + 1)..];
        }

        return Convert.FromBase64String(data);
    }
}
