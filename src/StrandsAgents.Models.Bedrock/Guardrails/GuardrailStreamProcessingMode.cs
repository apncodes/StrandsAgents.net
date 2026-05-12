namespace StrandsAgents.Models.Bedrock;

/// <summary>Controls whether guardrail assessment completes before streaming begins.</summary>
public enum GuardrailStreamProcessingMode
{
    /// <summary>Guardrail assessment completes before any stream tokens are returned.</summary>
    Synchronous,
    /// <summary>Tokens stream while guardrail assessment runs in the background.</summary>
    Asynchronous
}
