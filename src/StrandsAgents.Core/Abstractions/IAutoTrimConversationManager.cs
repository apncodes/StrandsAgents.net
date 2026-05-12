namespace StrandsAgents.Core;

/// <summary>
/// Extends <see cref="IConversationManager"/> with an async trim hook that the
/// <see cref="Agent"/> calls automatically after every turn completes.
/// </summary>
/// <remarks>
/// Implement this interface (or use <see cref="AutoTrimConversationManagerDecorator"/>)
/// to opt into automatic context compaction without adding manual <c>TrimAsync</c> calls
/// after every <c>InvokeAsync</c> / <c>StreamAsync</c> invocation.
/// </remarks>
public interface IAutoTrimConversationManager : IConversationManager
{
    /// <summary>
    /// Called by <see cref="Agent"/> after each turn completes.
    /// Implementations may summarize, compact, or otherwise trim the conversation history.
    /// When the history is already within budget this should be a no-op.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task TrimAfterTurnAsync(CancellationToken ct = default);
}
