namespace StrandsAgents.Core;

public interface IGuardrailEvaluator
{
    Task<GuardrailEvaluationResult> EvaluateAsync(
        string content,
        string source,
        CancellationToken ct = default);

    bool IsEnabled { get; }
    bool ShadowMode { get; }
}
