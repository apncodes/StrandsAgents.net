namespace StrandsAgents.Models.Bedrock;

/// <summary>
/// Configuration for Amazon Bedrock Guardrails on a <see cref="BedrockModel"/> instance.
/// </summary>
public sealed record BedrockGuardrailConfig(
    string GuardrailId,
    string GuardrailVersion)
{
    /// <summary>Enable guardrail trace in Bedrock responses for debugging. Default: false.</summary>
    public bool Trace { get; init; } = false;

    /// <summary>Replace blocked input content in conversation history. Default: true.</summary>
    public bool RedactInput { get; init; } = true;

    /// <summary>Custom message for redacted input. Null uses SDK default placeholder.</summary>
    public string? RedactInputMessage { get; init; } = null;

    /// <summary>Replace blocked output content in conversation history. Default: true.</summary>
    public bool RedactOutput { get; init; } = true;

    /// <summary>Custom message for redacted output. Null uses SDK default placeholder.</summary>
    public string? RedactOutputMessage { get; init; } = null;

    /// <summary>
    /// When true, only the last user message is wrapped in a guardContent block for evaluation,
    /// reducing cost and latency on multi-turn conversations. Default: true.
    /// </summary>
    public bool EvaluateLatestMessageOnly { get; init; } = true;

    /// <summary>
    /// When true, calls ApplyGuardrailAsync before each Converse call but never blocks the loop.
    /// Violations fire GuardrailViolationEvent only. Default: false.
    /// </summary>
    public bool ShadowMode { get; init; } = false;

    /// <summary>Controls whether guardrail assessment completes before streaming begins. Default: Synchronous.</summary>
    public GuardrailStreamProcessingMode StreamProcessingMode { get; init; } = GuardrailStreamProcessingMode.Synchronous;
}
