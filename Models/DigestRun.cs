namespace AIInsights.Models;

public class DigestRun
{
    public int Id { get; set; }
    public string RunWeekIso { get; set; } = "";  // e.g. "2026-W17"
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
