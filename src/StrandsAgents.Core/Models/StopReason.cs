namespace StrandsAgents.Core;

/// <summary>Reason the agent loop stopped.</summary>
public enum StopReason
{
    /// <summary>Model signalled it is done.</summary>
    EndTurn,
    /// <summary>Model requested tool use (internal — loop continues).</summary>
    ToolUse,
    /// <summary>Model hit its max token limit.</summary>
    MaxTokens,
    /// <summary>A stop sequence was matched.</summary>
    StopSequence,
    /// <summary>Agent reached the configured max iterations.</summary>
    MaxIterations,
    /// <summary>A hook interrupted the loop.</summary>
    Interrupted,
    /// <summary>An unrecoverable error occurred.</summary>
    Error,
    /// <summary>A guardrail blocked the request or response.</summary>
    GuardrailBlocked
}
