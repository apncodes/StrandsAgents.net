namespace StrandsAgents.Core;

/// <summary>
/// Wraps a <see cref="SummarizingConversationManager"/> and implements
/// <see cref="IAutoTrimConversationManager"/> so that the <see cref="Agent"/> can call
/// <see cref="TrimAfterTurnAsync"/> automatically after each turn.
/// </summary>
/// <remarks>
/// Use this decorator when you already hold a <see cref="SummarizingConversationManager"/>
/// reference and want to opt into auto-trim without changing the declared type.
/// If you are constructing the manager fresh, you can pass a
/// <see cref="SummarizingConversationManager"/> directly — it already implements
/// <see cref="IAutoTrimConversationManager"/>.
/// </remarks>
public sealed class AutoTrimConversationManagerDecorator : IAutoTrimConversationManager
{
    private readonly SummarizingConversationManager _inner;

    /// <summary>
    /// Initializes a new <see cref="AutoTrimConversationManagerDecorator"/>.
    /// </summary>
    /// <param name="inner">The <see cref="SummarizingConversationManager"/> to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
    public AutoTrimConversationManagerDecorator(SummarizingConversationManager inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public IReadOnlyList<Message> GetMessages() => _inner.GetMessages();

    /// <inheritdoc/>
    public void Append(Message message) => _inner.Append(message);

    /// <inheritdoc/>
    public void Trim() => _inner.Trim();

    /// <inheritdoc/>
    /// <remarks>Delegates to <see cref="SummarizingConversationManager.TrimAsync"/>.</remarks>
    public Task TrimAfterTurnAsync(CancellationToken ct = default) => _inner.TrimAsync(ct);
}
