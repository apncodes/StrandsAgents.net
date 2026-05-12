using System.Diagnostics;

namespace StrandsAgents.Core;

/// <summary>
/// The model-driven event loop. Calls the model, executes tool calls, and loops
/// until the model signals end_turn, max iterations is reached, or cancellation fires.
/// </summary>
internal sealed class EventLoop
{
    private readonly IModel _model;
    private readonly ToolRegistry _tools;
    private readonly AgentConfig _config;
    private readonly HookRegistry? _hooks;
    private readonly IGuardrailEvaluator? _guardrailEvaluator;

    public EventLoop(
        IModel model,
        ToolRegistry tools,
        AgentConfig config,
        HookRegistry? hooks = null,
        IGuardrailEvaluator? guardrailEvaluator = null)
    {
        _model = model;
        _tools = tools;
        _config = config;
        _hooks = hooks;
        _guardrailEvaluator = guardrailEvaluator;
    }

    /// <summary>Runs the agent loop and returns the final result.</summary>
    public async Task<(AgentResult Result, List<Message> UpdatedMessages)> RunAsync(
        List<Message> messages,
        string? systemPrompt,
        CancellationToken ct)
    {
        using var agentActivity = StrandsTelemetry.ActivitySource.StartActivity("agent.invoke");

        var sw = Stopwatch.StartNew();
        var totalUsage = TokenUsage.Zero;
        var iterations = 0;
        var toolCallCount = 0;

        while (iterations < _config.MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            // Trim the context window before each model call if a strategy is configured.
            // The trimmed list is used only for the model request — appending continues on the
            // full messages list so no history is permanently lost from the conversation.
            var contextMessages = _config.ContextWindowStrategy is not null
                ? (await _config.ContextWindowStrategy
                    .TrimAsync(messages, _config.MaxContextTokens, ct)
                    .ConfigureAwait(false)).ToList()
                : messages;

            // Fire PiiRedactionRequestEvent — handlers may replace the message list
            var piiRequestEvt = new PiiRedactionRequestEvent(contextMessages);
            if (_hooks is not null)
                await _hooks.FireAsync(piiRequestEvt, ct).ConfigureAwait(false);
            var effectiveMessages = piiRequestEvt.Messages;

            var request = new ModelRequest(
                effectiveMessages,
                systemPrompt,
                _tools.GetDefinitions(),
                new ModelParameters());

            // Fire BeforeModelCallEvent — interrupt halts the loop
            if (_hooks is not null)
            {
                var beforeEvt = new BeforeModelCallEvent(request);
                await _hooks.FireAsync(beforeEvt, ct).ConfigureAwait(false);
                if (beforeEvt.Interrupt)
                {
                    var interruptMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                    var interruptResult = new AgentResult(string.Empty, StopReason.EndTurn, totalUsage, interruptMetrics);
                    RecordInvokeMetrics(agentActivity, totalUsage, sw, interruptResult.StopReason);
                    return (interruptResult, messages);
                }
            }

            ModelResponse response;
            using (var modelActivity = StrandsTelemetry.ActivitySource.StartActivity("agent.model_call"))
            {
                response = await _model.InvokeAsync(request, ct).ConfigureAwait(false);
                modelActivity?.SetTag("model.input_tokens", response.Usage.InputTokens);
                modelActivity?.SetTag("model.output_tokens", response.Usage.OutputTokens);
            }

            totalUsage += response.Usage;

            // Fire AfterModelCallEvent
            if (_hooks is not null)
            {
                var afterEvt = new AfterModelCallEvent(request, response);
                await _hooks.FireAsync(afterEvt, ct).ConfigureAwait(false);
            }

            // Fire PiiRedactionResponseEvent — handlers may replace the response
            var piiResponseEvt = new PiiRedactionResponseEvent(response);
            if (_hooks is not null)
                await _hooks.FireAsync(piiResponseEvt, ct).ConfigureAwait(false);
            response = piiResponseEvt.Response;

            // Halt immediately if a guardrail blocked the response
            if (response.StopReason == StopReason.GuardrailBlocked)
            {
                var metrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                var result = new AgentResult(response.TextContent ?? string.Empty, StopReason.GuardrailBlocked, totalUsage, metrics);
                RecordInvokeMetrics(agentActivity, totalUsage, sw, result.StopReason);
                return (result, messages);
            }

            // Append assistant message
            var assistantContent = BuildAssistantContent(response);
            messages.Add(new Message(Role.Assistant, assistantContent));

            if (response.StopReason == StopReason.EndTurn || response.ToolCalls.Count == 0)
            {
                var metrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                var result = new AgentResult(response.TextContent ?? string.Empty, StopReason.EndTurn, totalUsage, metrics);
                RecordInvokeMetrics(agentActivity, totalUsage, sw, result.StopReason);
                return (result, messages);
            }

            if (response.StopReason == StopReason.ToolUse)
            {
                toolCallCount += response.ToolCalls.Count;

                IReadOnlyList<ToolResult> toolResults;
                bool guardrailHalted = false;

                if (_hooks is not null)
                {
                    // Per-tool hook firing — iterate individually
                    var results = new List<ToolResult>(response.ToolCalls.Count);
                    var interrupted = false;

                    foreach (var call in response.ToolCalls)
                    {
                        var beforeToolEvt = new BeforeToolCallEvent(call);
                        await _hooks.FireAsync(beforeToolEvt, ct).ConfigureAwait(false);
                        if (beforeToolEvt.Interrupt)
                        {
                            interrupted = true;
                            break;
                        }

                        var singleResult = await _tools.ExecuteAsync([call], false, ct).ConfigureAwait(false);
                        var toolResult = singleResult[0];

                        // Evaluate tool result against guardrail
                        var (evaluatedResult, shouldHalt) = await EvaluateToolResultAsync(toolResult, ct).ConfigureAwait(false);
                        results.Add(evaluatedResult);

                        var afterToolEvt = new AfterToolCallEvent(call, evaluatedResult);
                        await _hooks.FireAsync(afterToolEvt, ct).ConfigureAwait(false);

                        if (shouldHalt)
                        {
                            guardrailHalted = true;
                            break;
                        }
                    }

                    if (interrupted)
                    {
                        if (results.Count > 0)
                        {
                            var partialContent = results
                                .Select(r => (ContentBlock)new ToolResultBlock(r.ToolCallId, r.Content, r.IsError))
                                .ToList();
                            messages.Add(new Message(Role.User, partialContent));
                        }

                        var interruptMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                        var interruptResult = new AgentResult(response.TextContent ?? string.Empty, StopReason.EndTurn, totalUsage, interruptMetrics);
                        RecordInvokeMetrics(agentActivity, totalUsage, sw, interruptResult.StopReason);
                        return (interruptResult, messages);
                    }

                    toolResults = results;
                }
                else
                {
                    // No hooks — use the fast batch path, then evaluate each result
                    var batchResults = await _tools.ExecuteAsync(response.ToolCalls, _config.ParallelToolExecution, ct).ConfigureAwait(false);
                    var evaluatedList = new List<ToolResult>(batchResults.Count);
                    foreach (var tr in batchResults)
                    {
                        var (evaluatedResult, shouldHalt) = await EvaluateToolResultAsync(tr, ct).ConfigureAwait(false);
                        evaluatedList.Add(evaluatedResult);
                        if (shouldHalt)
                        {
                            guardrailHalted = true;
                            break;
                        }
                    }
                    toolResults = evaluatedList;
                }

                // Append tool results as user message
                var toolResultContent = toolResults
                    .Select(r => (ContentBlock)new ToolResultBlock(r.ToolCallId, r.Content, r.IsError))
                    .ToList();
                messages.Add(new Message(Role.User, toolResultContent));

                if (guardrailHalted)
                {
                    var haltMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                    var haltResult = new AgentResult(string.Empty, StopReason.GuardrailBlocked, totalUsage, haltMetrics);
                    RecordInvokeMetrics(agentActivity, totalUsage, sw, haltResult.StopReason);
                    return (haltResult, messages);
                }

                continue;
            }

            // MaxTokens, StopSequence, or other terminal reason
            var terminalMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
            var terminalResult = new AgentResult(response.TextContent ?? string.Empty, response.StopReason, totalUsage, terminalMetrics);
            RecordInvokeMetrics(agentActivity, totalUsage, sw, terminalResult.StopReason);
            return (terminalResult, messages);
        }

        // Max iterations reached
        var finalMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
        var maxIterResult = new AgentResult(string.Empty, StopReason.MaxIterations, totalUsage, finalMetrics);
        RecordInvokeMetrics(agentActivity, totalUsage, sw, maxIterResult.StopReason);
        return (maxIterResult, messages);
    }

    /// <summary>Runs the agent loop and yields streaming events.</summary>
    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        List<Message> messages,
        string? systemPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var agentActivity = StrandsTelemetry.ActivitySource.StartActivity("agent.stream");

        var sw = Stopwatch.StartNew();
        var totalUsage = TokenUsage.Zero;
        var iterations = 0;
        var toolCallCount = 0;
        var textBuilder = new System.Text.StringBuilder();

        while (iterations < _config.MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            // Trim the context window before each model call if a strategy is configured.
            var contextMessages = _config.ContextWindowStrategy is not null
                ? (await _config.ContextWindowStrategy
                    .TrimAsync(messages, _config.MaxContextTokens, ct)
                    .ConfigureAwait(false)).ToList()
                : messages;

            // Fire PiiRedactionRequestEvent — handlers may replace the message list
            var piiRequestEvt = new PiiRedactionRequestEvent(contextMessages);
            if (_hooks is not null)
                await _hooks.FireAsync(piiRequestEvt, ct).ConfigureAwait(false);
            var effectiveMessages = piiRequestEvt.Messages;

            var request = new ModelRequest(
                effectiveMessages,
                systemPrompt,
                _tools.GetDefinitions(),
                new ModelParameters());

            // Fire BeforeModelCallEvent — interrupt halts the loop
            if (_hooks is not null)
            {
                var beforeEvt = new BeforeModelCallEvent(request);
                await _hooks.FireAsync(beforeEvt, ct).ConfigureAwait(false);
                if (beforeEvt.Interrupt)
                {
                    var interruptMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                    var interruptResult = new AgentResult(textBuilder.ToString(), StopReason.EndTurn, totalUsage, interruptMetrics);
                    RecordInvokeMetrics(agentActivity, totalUsage, sw, interruptResult.StopReason);
                    yield return new AgentCompleteEvent(interruptResult);
                    yield break;
                }
            }

            ModelResponse? finalResponse = null;
            var pendingToolCalls = new Dictionary<string, (string Name, System.Text.StringBuilder Input)>();

            using (var modelActivity = StrandsTelemetry.ActivitySource.StartActivity("agent.model_call"))
            {
                await foreach (var chunk in _model.StreamAsync(request, ct).ConfigureAwait(false))
                {
                    switch (chunk)
                    {
                        case TextDeltaModelEvent textDelta:
                            textBuilder.Append(textDelta.Delta);
                            yield return new TextDeltaEvent(textDelta.Delta);
                            break;

                        case ToolCallStartModelEvent toolStart:
                            pendingToolCalls[toolStart.Id] = (toolStart.Name, new System.Text.StringBuilder());
                            yield return new ToolCallStartEvent(toolStart.Name);
                            break;

                        case ToolCallInputDeltaModelEvent inputDelta:
                            if (pendingToolCalls.TryGetValue(inputDelta.Id, out var pending))
                                pending.Input.Append(inputDelta.Delta);
                            break;

                        case ModelCompleteEvent complete:
                            finalResponse = complete.Response;
                            totalUsage += complete.Response.Usage;
                            break;
                    }
                }

                modelActivity?.SetTag("model.input_tokens", finalResponse?.Usage.InputTokens ?? 0);
                modelActivity?.SetTag("model.output_tokens", finalResponse?.Usage.OutputTokens ?? 0);
            }

            if (finalResponse is null)
                yield break;

            // Fire AfterModelCallEvent
            if (_hooks is not null)
            {
                var afterEvt = new AfterModelCallEvent(request, finalResponse);
                await _hooks.FireAsync(afterEvt, ct).ConfigureAwait(false);
            }

            // Fire PiiRedactionResponseEvent — handlers may replace the response
            var piiResponseEvt = new PiiRedactionResponseEvent(finalResponse);
            if (_hooks is not null)
                await _hooks.FireAsync(piiResponseEvt, ct).ConfigureAwait(false);
            finalResponse = piiResponseEvt.Response;

            // Halt immediately if a guardrail blocked the response
            if (finalResponse.StopReason == StopReason.GuardrailBlocked)
            {
                var metrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                var result = new AgentResult(finalResponse.TextContent ?? string.Empty, StopReason.GuardrailBlocked, totalUsage, metrics);
                RecordInvokeMetrics(agentActivity, totalUsage, sw, result.StopReason);
                yield return new AgentCompleteEvent(result);
                yield break;
            }

            var assistantContent = BuildAssistantContent(finalResponse);
            messages.Add(new Message(Role.Assistant, assistantContent));

            if (finalResponse.StopReason == StopReason.EndTurn || finalResponse.ToolCalls.Count == 0)
            {
                var metrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                var result = new AgentResult(textBuilder.ToString(), StopReason.EndTurn, totalUsage, metrics);
                RecordInvokeMetrics(agentActivity, totalUsage, sw, result.StopReason);
                yield return new AgentCompleteEvent(result);
                yield break;
            }

            if (finalResponse.StopReason == StopReason.ToolUse)
            {
                toolCallCount += finalResponse.ToolCalls.Count;

                IReadOnlyList<ToolResult> toolResults;
                bool guardrailHalted = false;

                if (_hooks is not null)
                {
                    // Per-tool hook firing — iterate individually
                    var results = new List<ToolResult>(finalResponse.ToolCalls.Count);
                    var interrupted = false;

                    foreach (var call in finalResponse.ToolCalls)
                    {
                        var beforeToolEvt = new BeforeToolCallEvent(call);
                        await _hooks.FireAsync(beforeToolEvt, ct).ConfigureAwait(false);
                        if (beforeToolEvt.Interrupt)
                        {
                            interrupted = true;
                            break;
                        }

                        var singleResult = await _tools.ExecuteAsync([call], false, ct).ConfigureAwait(false);
                        var toolResult = singleResult[0];

                        // Evaluate tool result against guardrail
                        var (evaluatedResult, shouldHalt) = await EvaluateToolResultAsync(toolResult, ct).ConfigureAwait(false);
                        results.Add(evaluatedResult);

                        yield return new ToolCallResultEvent(evaluatedResult.ToolCallId, evaluatedResult);

                        var afterToolEvt = new AfterToolCallEvent(call, evaluatedResult);
                        await _hooks.FireAsync(afterToolEvt, ct).ConfigureAwait(false);

                        if (shouldHalt)
                        {
                            guardrailHalted = true;
                            break;
                        }
                    }

                    if (interrupted)
                    {
                        if (results.Count > 0)
                        {
                            var partialContent = results
                                .Select(r => (ContentBlock)new ToolResultBlock(r.ToolCallId, r.Content, r.IsError))
                                .ToList();
                            messages.Add(new Message(Role.User, partialContent));
                        }

                        var interruptMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                        var interruptResult = new AgentResult(textBuilder.ToString(), StopReason.EndTurn, totalUsage, interruptMetrics);
                        RecordInvokeMetrics(agentActivity, totalUsage, sw, interruptResult.StopReason);
                        yield return new AgentCompleteEvent(interruptResult);
                        yield break;
                    }

                    toolResults = results;
                }
                else
                {
                    // No hooks — use the fast batch path, then evaluate each result
                    var batchResults = await _tools.ExecuteAsync(finalResponse.ToolCalls, _config.ParallelToolExecution, ct).ConfigureAwait(false);
                    var evaluatedList = new List<ToolResult>(batchResults.Count);
                    foreach (var tr in batchResults)
                    {
                        var (evaluatedResult, shouldHalt) = await EvaluateToolResultAsync(tr, ct).ConfigureAwait(false);
                        evaluatedList.Add(evaluatedResult);
                        yield return new ToolCallResultEvent(evaluatedResult.ToolCallId, evaluatedResult);
                        if (shouldHalt)
                        {
                            guardrailHalted = true;
                            break;
                        }
                    }
                    toolResults = evaluatedList;
                }

                var toolResultContent = toolResults
                    .Select(r => (ContentBlock)new ToolResultBlock(r.ToolCallId, r.Content, r.IsError))
                    .ToList();
                messages.Add(new Message(Role.User, toolResultContent));

                if (guardrailHalted)
                {
                    var haltMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
                    var haltResult = new AgentResult(string.Empty, StopReason.GuardrailBlocked, totalUsage, haltMetrics);
                    RecordInvokeMetrics(agentActivity, totalUsage, sw, haltResult.StopReason);
                    yield return new AgentCompleteEvent(haltResult);
                    yield break;
                }

                continue;
            }

            // Terminal stop
            var termMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
            var termResult = new AgentResult(textBuilder.ToString(), finalResponse.StopReason, totalUsage, termMetrics);
            RecordInvokeMetrics(agentActivity, totalUsage, sw, termResult.StopReason);
            yield return new AgentCompleteEvent(termResult);
            yield break;
        }

        // Max iterations
        var maxMetrics = new AgentMetrics(sw.Elapsed, iterations, toolCallCount, totalUsage);
        var maxResult = new AgentResult(string.Empty, StopReason.MaxIterations, totalUsage, maxMetrics);
        RecordInvokeMetrics(agentActivity, totalUsage, sw, maxResult.StopReason);
        yield return new AgentCompleteEvent(maxResult);
    }

    /// <summary>
    /// Evaluates a tool result against the configured guardrail evaluator.
    /// Returns the (possibly replaced) result and a flag indicating whether the loop should halt.
    /// </summary>
    private async Task<(ToolResult Result, bool ShouldHalt)> EvaluateToolResultAsync(
        ToolResult toolResult,
        CancellationToken ct)
    {
        if (_guardrailEvaluator is null || !_guardrailEvaluator.IsEnabled || string.IsNullOrEmpty(toolResult.Content))
            return (toolResult, false);

        try
        {
            var evalResult = await _guardrailEvaluator
                .EvaluateAsync(toolResult.Content, "OUTPUT", ct)
                .ConfigureAwait(false);

            if (evalResult.Action == GuardrailAction.None)
                return (toolResult, false);

            // Fire violation event
            var violationEvt = new GuardrailViolationEvent(
                evalResult.GuardrailId ?? string.Empty,
                evalResult.GuardrailVersion ?? string.Empty,
                evalResult.Action,
                GuardrailSource.ToolResult,
                evalResult.BlockedMessage);
            if (_hooks is not null)
                await _hooks.FireAsync(violationEvt, ct).ConfigureAwait(false);

            var replacedContent = evalResult.BlockedMessage ?? "[Tool result redacted by guardrail]";
            var replacedResult = toolResult with { Content = replacedContent };

            if (_guardrailEvaluator.ShadowMode)
            {
                // Shadow mode: fire event but never replace content or halt
                return (toolResult, false);
            }

            if (evalResult.Action == GuardrailAction.Blocked)
            {
                // Enforcing mode + Blocked: replace content and halt
                return (replacedResult, true);
            }

            // Enforcing mode + Intervened: replace content, continue loop
            return (replacedResult, false);
        }
        catch (Exception ex)
        {
            // Log and continue with original content
            System.Diagnostics.Debug.WriteLine($"[EventLoop] Guardrail evaluation of tool result failed: {ex.Message}");
            return (toolResult, false);
        }
    }

    private static void RecordInvokeMetrics(Activity? activity, TokenUsage usage, Stopwatch sw, StopReason stopReason)
    {
        StrandsTelemetry.TokensInput.Add(usage.InputTokens);
        StrandsTelemetry.TokensOutput.Add(usage.OutputTokens);
        StrandsTelemetry.AgentLatency.Record(sw.Elapsed.TotalMilliseconds);
        activity?.SetTag("agent.stop_reason", stopReason.ToString());
        activity?.SetTag("agent.input_tokens", usage.InputTokens);
        activity?.SetTag("agent.output_tokens", usage.OutputTokens);
    }

    private static IReadOnlyList<ContentBlock> BuildAssistantContent(ModelResponse response)
    {
        var blocks = new List<ContentBlock>();
        if (response.TextContent is not null)
            blocks.Add(new TextBlock(response.TextContent));
        foreach (var tc in response.ToolCalls)
            blocks.Add(new ToolUseBlock(tc.Id, tc.Name, tc.Input));
        return blocks;
    }
}
