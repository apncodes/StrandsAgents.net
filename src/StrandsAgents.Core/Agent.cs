namespace StrandsAgents.Core;

/// <summary>
/// The main agent class. Wires together a model, tools, conversation history,
/// and session management into a single invokable unit.
/// </summary>
public sealed class Agent : IAgent
{
    private readonly IModel _model;
    private readonly string? _systemPrompt;
    private readonly IConversationManager _conversation;
    private readonly ISessionManager? _sessionManager;
    private readonly EventLoop _eventLoop;

    /// <summary>Arbitrary key-value state persisted alongside conversation history.</summary>
    public AgentState State { get; } = new();

    public Agent(
        IModel model,
        string? systemPrompt = null,
        IEnumerable<ITool>? tools = null,
        IConversationManager? conversationManager = null,
        ISessionManager? sessionManager = null,
        HookRegistry? hooks = null,
        AgentConfig? config = null,
        IGuardrailEvaluator? guardrailEvaluator = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _systemPrompt = systemPrompt;
        _conversation = conversationManager ?? new InMemoryConversationManager();
        _sessionManager = sessionManager;

        var registry = new ToolRegistry();
        if (tools is not null)
            registry.RegisterAll(tools);

        _eventLoop = new EventLoop(model, registry, config ?? new AgentConfig(), hooks, guardrailEvaluator);
    }

    /// <inheritdoc/>
    public async Task<AgentResult> InvokeAsync(string prompt, CancellationToken ct = default)
    {
        _conversation.Append(Message.User(prompt));

        var messages = _conversation.GetMessages().ToList();
        var (result, updatedMessages) = await _eventLoop.RunAsync(messages, _systemPrompt, ct).ConfigureAwait(false);

        // Sync conversation manager with any new messages added during the loop
        SyncMessages(updatedMessages);

        // Auto-trim if the conversation manager supports it (e.g. SummarizingConversationManager)
        if (_conversation is IAutoTrimConversationManager autoTrim)
            await autoTrim.TrimAfterTurnAsync(ct).ConfigureAwait(false);

        if (_sessionManager is not null)
        {
            var session = new AgentSession(
                Guid.NewGuid().ToString(),
                _conversation.GetMessages(),
                State.ToSnapshot(),
                DateTimeOffset.UtcNow);
            await _sessionManager.SaveAsync(session.SessionId, session, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _conversation.Append(Message.User(prompt));

        var messages = _conversation.GetMessages().ToList();

        await foreach (var evt in _eventLoop.StreamAsync(messages, _systemPrompt, ct).ConfigureAwait(false))
        {
            if (evt is AgentCompleteEvent)
            {
                SyncMessages(messages);

                // Auto-trim if the conversation manager supports it
                if (_conversation is IAutoTrimConversationManager autoTrim)
                    await autoTrim.TrimAfterTurnAsync(ct).ConfigureAwait(false);

                if (_sessionManager is not null)
                {
                    var session = new AgentSession(
                        Guid.NewGuid().ToString(),
                        _conversation.GetMessages(),
                        State.ToSnapshot(),
                        DateTimeOffset.UtcNow);
                    await _sessionManager.SaveAsync(session.SessionId, session, ct).ConfigureAwait(false);
                }
            }

            yield return evt;
        }
    }

    /// <summary>
    /// Invokes the agent with a schema-augmented prompt and deserializes the response to
    /// <typeparamref name="T"/> using <see cref="System.Text.Json.JsonSerializer"/>.
    /// Retries up to 3 times on parse failure, passing the error back to the model each time.
    /// </summary>
    /// <exception cref="StructuredOutputException">
    /// Thrown when the model response cannot be deserialized to <typeparamref name="T"/>
    /// after all retry attempts, including when <c>[JsonRequired]</c> properties are missing.
    /// </exception>
    public async Task<T> GetStructuredOutputAsync<T>(string prompt, CancellationToken ct = default)
    {
        var schema = JsonSchemaBuilder.GetSchema<T>();
        var schemaInstruction =
            "Respond with ONLY a raw JSON object that matches this schema exactly.\n" +
            "Do NOT wrap the JSON in markdown code fences or backticks.\n" +
            "Do NOT include any explanation, commentary, or text outside the JSON.\n" +
            "Your entire response must start with { and end with }.\n" +
            $"Schema: {schema}";

        // Build an augmented system prompt so the schema instruction doesn't pollute
        // the conversation history of this agent instance.
        var baseSystemPrompt = _systemPrompt is null
            ? schemaInstruction
            : $"{_systemPrompt}\n\n{schemaInstruction}";

        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };

        const int maxAttempts = 3;
        string? rawResponse = null;
        System.Text.Json.JsonException? lastJsonException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // On retry, amend the system prompt with the previous failure so the model
            // knows what went wrong and can self-correct.
            var systemPrompt = (attempt == 0 || rawResponse is null)
                ? baseSystemPrompt
                : $"{baseSystemPrompt}\n\nYour previous response was invalid. " +
                  $"Error: {lastJsonException?.Message}\n" +
                  $"You must respond with ONLY a raw JSON object — no markdown, no code fences, no backticks, no explanation. " +
                  $"Start your response with {{ and end with }}.";

            // Run in an isolated message list so retries don't pollute conversation history.
            var messages = new List<Message> { Message.User(prompt) };
            var (result, _) = await _eventLoop.RunAsync(messages, systemPrompt, ct).ConfigureAwait(false);
            rawResponse = result.Message;

            // Strip markdown code fences if the model wraps JSON in ```json ... ``` or ``` ... ```
            var jsonText = rawResponse.Trim();
            if (jsonText.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = jsonText.IndexOf('\n');
                if (firstNewline >= 0)
                    jsonText = jsonText[(firstNewline + 1)..];
                if (jsonText.EndsWith("```", StringComparison.Ordinal))
                    jsonText = jsonText[..^3].TrimEnd();
            }

            try
            {
                var deserialized = System.Text.Json.JsonSerializer.Deserialize<T>(jsonText, options);

                if (deserialized is null)
                {
                    // Treat null deserialization as a parse failure and retry.
                    lastJsonException = null;
                    continue;
                }

                return deserialized;
            }
            catch (System.Text.Json.JsonException ex)
            {
                lastJsonException = ex;
                // Loop continues to the next attempt.
            }
        }

        throw new StructuredOutputException(
            $"Failed to deserialize model response to '{typeof(T).Name}' after {maxAttempts} attempts: " +
            (lastJsonException?.Message ?? "model returned null"),
            rawResponse ?? string.Empty,
            lastJsonException);
    }

    private void SyncMessages(List<Message> updatedMessages)
    {
        // Append any messages added by the event loop that aren't already in the conversation
        var existing = _conversation.GetMessages();
        foreach (var msg in updatedMessages.Skip(existing.Count))
            _conversation.Append(msg);
    }
}
