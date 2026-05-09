namespace AIInsights.Models;

public class BlogImage
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string ImagePath { get; set; } = "";
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public BlogPost? Blog { get; set; }
}
