namespace StrandsAgents.Core;

/// <summary>
/// Declares validation constraints on a tool method parameter.
/// The source generator reads this attribute and embeds the constraints into
/// <see cref="ToolDefinition.ParameterConstraints"/> at compile time.
/// </summary>
/// <remarks>
/// <c>MinLength</c> and <c>MaxLength</c> use <c>-1</c> as the "not set" sentinel because
/// nullable value types (<c>int?</c>) are not valid named attribute arguments in C#.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ToolParameterValidationAttribute : Attribute
{
    /// <summary>
    /// Minimum string length. Use -1 (default) to indicate no minimum.
    /// </summary>
    public int MinLength { get; init; } = -1;

    /// <summary>
    /// Maximum string length. Use -1 (default) to indicate no maximum.
    /// </summary>
    public int MaxLength { get; init; } = -1;

    /// <summary>Regex pattern the value must match. Null means no pattern check.</summary>
    public string? Pattern { get; init; }

    /// <summary>Allowed string values. Null or empty means any value is allowed.</summary>
    public string[]? AllowedValues { get; init; }

    /// <summary>When true, the parameter must be present and non-null.</summary>
    public bool Required { get; init; }
}
