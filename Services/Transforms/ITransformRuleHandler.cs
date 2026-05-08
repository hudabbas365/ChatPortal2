namespace AIInsights.Services.Transforms;

/// <summary>
/// Thrown by an <see cref="ITransformRuleHandler"/> to abort the entire transform
/// pipeline (e.g. when <c>validate_schema</c> fails in strict mode).
/// The <see cref="AuditError"/> message is added to the transform audit log.
/// </summary>
public sealed class TransformAbortException : Exception
{
    public string AuditError { get; }
    public TransformAbortException(string auditError, string message)
        : base(message) => AuditError = auditError;
}

/// <summary>
/// Per-rule-type handler for the AI Insights 365 ETL workbench.
///
/// Pattern mirrors <c>IDatasourceTypeService</c>: handlers are registered in DI
/// and resolved by <see cref="IAiInsightTransformService"/> at run-time, so adding
/// a new rule type requires only a new implementation class and a DI registration —
/// no changes to the core switch or any allowlist.
/// </summary>
public interface ITransformRuleHandler
{
    /// <summary>Unique rule type name (matches the <c>type</c> key in TOML).</summary>
    string Type { get; }

    /// <summary>UI ribbon group this rule belongs to (e.g. "Clean", "Shape", "Combine").</summary>
    string Group { get; }

    /// <summary>
    /// Applies the rule to <paramref name="rows"/> and returns the (possibly new) row list.
    /// The handler may append messages to <paramref name="audit"/>.
    /// Throw <see cref="TransformAbortException"/> to stop the pipeline on a fatal error.
    /// </summary>
    List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit);
}
