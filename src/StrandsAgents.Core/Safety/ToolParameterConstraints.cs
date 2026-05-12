namespace StrandsAgents.Core;

/// <summary>
/// Runtime-accessible validation constraints for a tool parameter.
/// Populated at compile time by the source generator from <see cref="ToolParameterValidationAttribute"/>.
/// </summary>
public sealed record ToolParameterConstraints(
    bool Required = false,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    string[]? AllowedValues = null);
