namespace StrandsAgents.Core;

/// <summary>Base type for all hook events fired by the agent event loop.</summary>
public abstract record HookEvent;

/// <summary>Fired before each model call. Set <see cref="Interrupt"/> to <c>true</c> to halt the agent loop.</summary>
public record BeforeModelCallEvent(ModelRequest Request) : HookEvent
{
    /// <summary>When set to <c>true</c>, the agent loop will halt after this hook completes.</summary>
    public bool Interrupt { get; set; }
}

/// <summary>Fired after each model call.</summary>
public record AfterModelCallEvent(ModelRequest Request, ModelResponse Response) : HookEvent;

/// <summary>Fired before each tool call. Set <see cref="Interrupt"/> to <c>true</c> to halt the agent loop.</summary>
public record BeforeToolCallEvent(ToolCall Call) : HookEvent
{
    /// <summary>When set to <c>true</c>, the agent loop will halt after this hook completes.</summary>
    public bool Interrupt { get; set; }
}

/// <summary>Fired after each tool call.</summary>
public record AfterToolCallEvent(ToolCall Call, ToolResult Result) : HookEvent;

/// <summary>Fired when a guardrail intervenes or blocks a request, response, or tool result.</summary>
public sealed record GuardrailViolationEvent(
    string GuardrailId,
    string GuardrailVersion,
    GuardrailAction Action,
    GuardrailSource Source,
    string? RedactedContent) : HookEvent;

/// <summary>
/// Fired before each model call. Handlers may replace <see cref="Messages"/> with a redacted list.
/// </summary>
public sealed record PiiRedactionRequestEvent : HookEvent
{
    /// <summary>The outbound messages. Handlers may replace this with a redacted list.</summary>
    public IReadOnlyList<Message> Messages { get; set; }

    public PiiRedactionRequestEvent(IReadOnlyList<Message> messages) => Messages = messages;
}

/// <summary>
/// Fired after each model call. Handlers may replace <see cref="Response"/> with a redacted response.
/// </summary>
public sealed record PiiRedactionResponseEvent : HookEvent
{
    /// <summary>The model response. Handlers may replace this with a redacted response.</summary>
    public ModelResponse Response { get; set; }

    public PiiRedactionResponseEvent(ModelResponse response) => Response = response;
}
