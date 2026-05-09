using Microsoft.AspNetCore.Http;

namespace AIInsights.SuperAdmin.Services;

public class ImageUploadService : IImageUploadService
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private readonly IWebHostEnvironment _env;

    public ImageUploadService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(bool Success, string? ImagePath, string? ErrorMessage)> SaveImageAsync(
        IFormFile file,
        string contentTypeFolder,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return (false, null, "Please select an image file to upload.");

        if (file.Length > MaxImageBytes)
            return (false, null, "Image size must be 5 MB or less.");

        var extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            return (false, null, "Only JPG, JPEG, PNG, WEBP, and GIF files are allowed.");

        var folder = contentTypeFolder.Equals("documents", StringComparison.OrdinalIgnoreCase)
            ? "documents"
            : "blogs";

        var uploadsRoot = ResolveUploadsRoot();
        var destinationFolder = Path.Combine(uploadsRoot, folder);
        Directory.CreateDirectory(destinationFolder);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var physicalPath = Path.Combine(destinationFolder, fileName);
        await using (var stream = new FileStream(physicalPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return (true, $"/uploads/{folder}/{fileName}", null);
    }

    public bool DeleteImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return false;
        var relative = imagePath.Replace('\\', '/').Trim();
        if (!relative.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return false;
        var sanitizedRelative = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(ResolveWebRoot(), sanitizedRelative);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string ResolveUploadsRoot() => Path.Combine(ResolveWebRoot(), "uploads");

    private string ResolveWebRoot()
    {
        var siblingWebRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "wwwroot"));
        if (Directory.Exists(siblingWebRoot)) return siblingWebRoot;
        var localWebRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(localWebRoot);
        return localWebRoot;
    }
}
