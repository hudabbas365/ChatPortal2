namespace AIInsights.Models;

public class SharedReport
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report? Report { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}
