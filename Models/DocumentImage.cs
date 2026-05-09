namespace AIInsights.Models;

public class DocumentImage
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public DocArticle? Document { get; set; }
    public string ImagePath { get; set; } = "";
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
