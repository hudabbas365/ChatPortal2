using System.Diagnostics;
using System.Text.Json;
using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services.Transforms;

internal static class TransformWorkbenchConstants
{
    public const int MaxPreviewPageSize = 1000;
}

public record TransformValidationIssue(string Code, string Message, string? Reference = null);

public sealed class TransformValidationSummary
{
    public bool Success { get; set; }
    public List<TransformValidationIssue> Issues { get; set; } = new();
    public List<AiTransformRule> ParsedSteps { get; set; } = new();
    public string? Suggestion { get; set; }
}

public sealed class TransformPreviewSummary
{
    public bool Success { get; set; }
    public int TotalRowCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<Dictionary<string, object>> PagedRows { get; set; } = new();
    public long RenderDurationMs { get; set; }
    public List<string> Audit { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
    public List<object> StepSummary { get; set; } = new();
}

public interface ITransformDefinitionParser
{
    TransformParseResult Parse(string? toml);
}

public interface ITransformValidationService
{
    TransformValidationSummary ValidateToml(string? toml);
    TransformValidationSummary ValidateSteps(IEnumerable<AiTransformRule>? steps);
}

public interface ITransformDraftService
{
    Task<TransformDraft> SaveDraftAsync(Datasource ds, TransformDraftUpsertRequest request, string? userId);
    Task<TransformDraft?> LoadDraftAsync(int datasourceId, string draftGuid);
}

public interface ITransformPreviewService
{
    TransformPreviewSummary Preview(
        Datasource ds,
        string? toml,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceRows,
        int page,
        int pageSize);
}

public interface ITransformPublishService
{
    Task<(bool Success, string? Error)> PublishAsync(Datasource ds, string? toml, string? userId, string? draftGuid = null);
}

public interface ITransformSuggestionService
{
    string SuggestNextStep(IEnumerable<string> columns, string? validationError = null);
}

public sealed class TransformDefinitionParser : ITransformDefinitionParser
{
    private readonly IAiInsightTransformService _service;
    public TransformDefinitionParser(IAiInsightTransformService service) => _service = service;
    public TransformParseResult Parse(string? toml) => _service.Parse(toml);
}

public sealed class TransformValidationService : ITransformValidationService
{
    private static readonly HashSet<string> SupportedRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "remove_duplicates", "handle_nulls", "standardize_format", "convert_units", "apply_naming_convention", "map_codes",
        "kpi_classification", "sentiment_score", "derived_field", "aggregate", "flatten_json", "validate_schema", "referential_integrity",
        "append_rows", "merge_tables", "pivot_table", "unpivot_table", "transpose_table", "filter_rows", "sort_rows", "select_columns",
        "rename_columns", "split_column", "replace_values", "cast_types"
    };

    private readonly ITransformDefinitionParser _parser;
    private readonly ITransformSuggestionService _suggestion;

    public TransformValidationService(ITransformDefinitionParser parser, ITransformSuggestionService suggestion)
    {
        _parser = parser;
        _suggestion = suggestion;
    }

    public TransformValidationSummary ValidateToml(string? toml)
    {
        var parsed = _parser.Parse(toml);
        if (!parsed.Success || parsed.Definition == null)
        {
            var err = parsed.Error ?? "Invalid TOML.";
            return new TransformValidationSummary
            {
                Success = false,
                Issues = new() { new TransformValidationIssue("syntax", err, "toml") },
                Suggestion = _suggestion.SuggestNextStep(Array.Empty<string>(), err)
            };
        }

        return ValidateSteps(parsed.Definition.Rules);
    }

    public TransformValidationSummary ValidateSteps(IEnumerable<AiTransformRule>? steps)
    {
        var issues = new List<TransformValidationIssue>();
        var parsedSteps = (steps ?? Array.Empty<AiTransformRule>()).ToList();
        if (parsedSteps.Count == 0)
            issues.Add(new TransformValidationIssue("empty", "No transform steps were found.", "rules"));

        for (var i = 0; i < parsedSteps.Count; i++)
        {
            var s = parsedSteps[i];
            if (!SupportedRules.Contains(s.Type))
                issues.Add(new TransformValidationIssue("unsupported_rule", $"Unsupported transform type '{s.Type}'.", $"step[{i}].type"));
        }

        return new TransformValidationSummary
        {
            Success = issues.Count == 0,
            Issues = issues,
            ParsedSteps = parsedSteps,
            Suggestion = issues.Count == 0
                ? _suggestion.SuggestNextStep(Array.Empty<string>())
                : _suggestion.SuggestNextStep(Array.Empty<string>(), string.Join("; ", issues.Select(i => i.Message)))
        };
    }
}

public sealed class TransformPreviewService : ITransformPreviewService
{
    private readonly IAiInsightTransformService _transformService;

    public TransformPreviewService(IAiInsightTransformService transformService) => _transformService = transformService;

    public TransformPreviewSummary Preview(
        Datasource ds,
        string? toml,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceRows,
        int page,
        int pageSize)
    {
        var sw = Stopwatch.StartNew();
        var tempDatasource = new Datasource
        {
            Id = ds.Id,
            Guid = ds.Guid,
            Name = ds.Name,
            Type = ds.Type,
            TransformEnabled = true,
            TransformToml = toml ?? ds.TransformToml
        };

        var result = _transformService.ApplyWithSources(tempDatasource, new QueryExecutionResult
        {
            Success = true,
            Data = rows,
            RowCount = rows.Count
        }, sourceRows);
        sw.Stop();

        var safePageSize = Math.Clamp(pageSize <= 0 ? TransformWorkbenchConstants.MaxPreviewPageSize : pageSize, 1, TransformWorkbenchConstants.MaxPreviewPageSize);
        var safePage = Math.Max(page, 1);
        var totalRows = result.RowCount;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRows / (double)safePageSize));
        var pageRows = result.Data.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToList();

        return new TransformPreviewSummary
        {
            Success = result.Success,
            TotalRowCount = totalRows,
            Page = safePage,
            PageSize = safePageSize,
            TotalPages = totalPages,
            PagedRows = pageRows,
            RenderDurationMs = sw.ElapsedMilliseconds,
            Audit = result.TransformAudit,
            Error = result.Error,
            Warnings = result.Success ? new List<string>() : new List<string> { result.Error ?? "Transform failed." },
            StepSummary = BuildStepSummary(toml)
        };
    }

    private static List<object> BuildStepSummary(string? toml)
    {
        if (string.IsNullOrWhiteSpace(toml)) return new();
        try
        {
            var parsed = Tomlyn.Toml.ToModel(toml) as IDictionary<string, object>;
            if (parsed == null || !parsed.TryGetValue("rules", out var r) || r is not IEnumerable<object> rules)
                return new();

            var list = new List<object>();
            var index = 0;
            foreach (var rule in rules)
            {
                if (rule is not IDictionary<string, object> rr) continue;
                list.Add(new
                {
                    step = ++index,
                    type = rr.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "",
                    enabled = rr.TryGetValue("enabled", out var en) ? bool.TryParse(en?.ToString(), out var b) ? b : true : true
                });
            }
            return list;
        }
        catch
        {
            return new();
        }
    }
}

public sealed class TransformDraftService : ITransformDraftService
{
    private readonly AppDbContext _db;

    public TransformDraftService(AppDbContext db) => _db = db;

    public async Task<TransformDraft> SaveDraftAsync(Datasource ds, TransformDraftUpsertRequest request, string? userId)
    {
        TransformDraft? draft = null;
        if (!string.IsNullOrWhiteSpace(request.DraftGuid))
            draft = await _db.TransformDrafts.Include(d => d.Sources).Include(d => d.Steps)
                .FirstOrDefaultAsync(d => d.Guid == request.DraftGuid && d.DatasourceId == ds.Id);

        if (draft == null)
        {
            draft = new TransformDraft
            {
                DatasourceId = ds.Id,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.TransformDrafts.Add(draft);
        }

        draft.Name = string.IsNullOrWhiteSpace(request.Name) ? draft.Name : request.Name.Trim();
        draft.TomlDefinition = request.Toml ?? "";
        draft.StepsJson = request.StepsJson ?? "[]";
        draft.DatasourceGuidsCsv = request.DatasourceGuids?.Count > 0 ? string.Join(',', request.DatasourceGuids.Distinct()) : ds.Guid;
        draft.UpdatedByUserId = userId;
        draft.UpdatedAt = DateTime.UtcNow;
        draft.Status = "draft";
        draft.AuditMetadataJson = request.AuditMetadataJson;

        var sourceGuids = request.DatasourceGuids?.Count > 0
            ? request.DatasourceGuids
            : new List<string> { ds.Guid };
        var sourceIds = await _db.Datasources
            .Where(d => sourceGuids.Contains(d.Guid))
            .Select(d => new { d.Id, d.Guid })
            .ToListAsync();

        var existingSources = await _db.TransformSources.Where(s => s.TransformDraftId == draft.Id).ToListAsync();
        _db.TransformSources.RemoveRange(existingSources);
        var order = 0;
        foreach (var s in sourceIds)
        {
            _db.TransformSources.Add(new TransformSource
            {
                TransformDraft = draft,
                DatasourceId = s.Id,
                Alias = s.Guid,
                SortOrder = order++
            });
        }

        await _db.SaveChangesAsync();
        return draft;
    }

    public Task<TransformDraft?> LoadDraftAsync(int datasourceId, string draftGuid) =>
        _db.TransformDrafts
            .Include(d => d.Sources)
            .Include(d => d.Steps)
            .FirstOrDefaultAsync(d => d.DatasourceId == datasourceId && d.Guid == draftGuid);
}

public sealed class TransformPublishService : ITransformPublishService
{
    private readonly AppDbContext _db;
    private readonly ITransformValidationService _validation;

    public TransformPublishService(AppDbContext db, ITransformValidationService validation)
    {
        _db = db;
        _validation = validation;
    }

    public async Task<(bool Success, string? Error)> PublishAsync(Datasource ds, string? toml, string? userId, string? draftGuid = null)
    {
        var validation = _validation.ValidateToml(toml);
        if (!validation.Success)
            return (false, string.Join(" | ", validation.Issues.Select(i => i.Message)));

        ds.TransformEnabled = true;
        ds.TransformToml = toml ?? "";

        if (!string.IsNullOrWhiteSpace(draftGuid))
        {
            var draft = await _db.TransformDrafts.FirstOrDefaultAsync(d => d.Guid == draftGuid && d.DatasourceId == ds.Id);
            if (draft != null)
            {
                draft.Status = "published";
                draft.PublishedAt = DateTime.UtcNow;
                draft.UpdatedByUserId = userId;
                draft.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }
}

public sealed class TransformSuggestionService : ITransformSuggestionService
{
    public string SuggestNextStep(IEnumerable<string> columns, string? validationError = null)
    {
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            if (validationError.Contains("Unsupported transform type", StringComparison.OrdinalIgnoreCase))
                return "Use a supported function such as filter_rows, sort_rows, select_columns, append_rows, or merge_tables.";
            if (validationError.Contains("parse", StringComparison.OrdinalIgnoreCase) || validationError.Contains("syntax", StringComparison.OrdinalIgnoreCase))
                return "Check TOML brackets and ensure each [[rules]] block has a valid type field.";
            return "Validate TOML first, then add one step at a time and preview after each step.";
        }

        var cols = columns.Select(c => c.ToLowerInvariant()).ToList();
        if (cols.Any(c => c.Contains("date") || c.Contains("time")))
            return "Try sort_rows by your date column, then aggregate or pivot_table for reporting.";
        if (cols.Any(c => c.Contains("id") || c.Contains("code")))
            return "Try remove_duplicates on ID columns, then map_codes or merge_tables.";
        return "Start with handle_nulls and select_columns, then preview and add aggregate/pivot as needed.";
    }
}

public sealed class TransformDraftUpsertRequest
{
    public string? DraftGuid { get; set; }
    public string? Name { get; set; }
    public string? Toml { get; set; }
    public string? StepsJson { get; set; }
    public List<string>? DatasourceGuids { get; set; }
    public string? AuditMetadataJson { get; set; }
}
