namespace AIInsights.Models;

public class TransformSource
{
    public int Id { get; set; }
    public int TransformDraftId { get; set; }
    public TransformDraft? TransformDraft { get; set; }
    public int DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public string Alias { get; set; } = "source";
    public int SortOrder { get; set; }
}
