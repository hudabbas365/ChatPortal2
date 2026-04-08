namespace ChatPortal2.Models;

public class Datasource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = "";
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
