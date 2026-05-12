namespace StrandsAgents.Core;

/// <summary>
/// Summarizes older messages via a model call when the message count exceeds a threshold,
/// replacing them with a single summary message to reduce token usage.
/// </summary>
/// <remarks>
/// The synchronous <see cref="Trim"/> method is a no-op for this manager.
/// Callers should invoke <see cref="TrimAsync"/> to trigger summarization.
/// </remarks>
public sealed class SummarizingConversationManager : IAutoTrimConversationManager
{
    private readonly IModel _model;
    private readonly List<Message> _messages = [];

    /// <summary>Message count threshold that triggers summarization.</summary>
    public int Threshold { get; }

    /// <summary>Number of recent messages to keep verbatim after summarization.</summary>
    public int KeepRecentCount { get; }

    /// <summary>
    /// Initializes a new <see cref="SummarizingConversationManager"/>.
    /// </summary>
    /// <param name="model">Model used to generate the summary.</param>
    /// <param name="threshold">When message count exceeds this value, summarization is triggered.</param>
    /// <param name="keepRecentCount">Number of recent messages to keep verbatim after summarization.</param>
    public SummarizingConversationManager(IModel model, int threshold = 20, int keepRecentCount = 5)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        Threshold = threshold;
        KeepRecentCount = keepRecentCount;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Message> GetMessages() => _messages.AsReadOnly();

    /// <inheritdoc/>
    public void Append(Message message) => _messages.Add(message);

    /// <inheritdoc/>
    /// <remarks>No-op — use <see cref="TrimAsync"/> for summarization.</remarks>
    public void Trim() { /* No-op — async summarization requires TrimAsync */ }

    /// <inheritdoc/>
    /// <remarks>Delegates to <see cref="TrimAsync"/>.</remarks>
    public Task TrimAfterTurnAsync(CancellationToken ct = default) => TrimAsync(ct);

    /// <summary>
    /// Summarizes older messages via a model call when the message count exceeds <see cref="Threshold"/>.
    /// Older messages (all except the most recent <see cref="KeepRecentCount"/>) are replaced with
    /// a single summary message.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task TrimAsync(CancellationToken ct = default)
    {
        if (_messages.Count <= Threshold)
            return;

        var oldMessages = _messages.GetRange(0, _messages.Count - KeepRecentCount);
        var recentMessages = _messages.GetRange(_messages.Count - KeepRecentCount, KeepRecentCount);

        var summary = await SummarizeAsync(oldMessages, ct);

        _messages.Clear();
        _messages.Add(new Message(Role.User, [new TextBlock($"Previous conversation summary: {summary}")]));
        _messages.AddRange(recentMessages);
    }

    private async Task<string> SummarizeAsync(IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var promptText = BuildSummarizationPrompt(messages);

        var summarizationRequest = new ModelRequest(
            Messages: [new Message(Role.User, [new TextBlock(promptText)])],
            SystemPrompt: null,
            Tools: [],
            Parameters: new ModelParameters());

        var response = await _model.InvokeAsync(summarizationRequest, ct).ConfigureAwait(false);
        return response.TextContent ?? string.Empty;
    }

    private static string BuildSummarizationPrompt(IReadOnlyList<Message> messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Summarize the following conversation history concisely, preserving key facts and decisions:");
        sb.AppendLine();

        foreach (var message in messages)
        {
            var role = message.Role == Role.User ? "user" : "assistant";
            var text = ExtractText(message);
            sb.AppendLine($"[{role}]: {text}");
        }

        return sb.ToString();
    }

    private static string ExtractText(Message message)
    {
        var parts = message.Content
            .OfType<TextBlock>()
            .Select(b => b.Text);
        return string.Join(" ", parts);
    }
}
