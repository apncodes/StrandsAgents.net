namespace StrandsAgents.Core;

public sealed record GuardrailEvaluationResult(
    GuardrailAction Action,
    string? BlockedMessage,
    string? GuardrailId,
    string? GuardrailVersion);
