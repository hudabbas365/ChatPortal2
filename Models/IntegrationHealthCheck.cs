namespace AIInsights.Models;

public class IntegrationHealthCheck
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";   // Cohere, Smtp, PayPal
    public string Status { get; set; } = "";     // Up, Degraded, Down
    public int LatencyMs { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
