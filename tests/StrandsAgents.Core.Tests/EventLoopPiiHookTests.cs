using Moq;
using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Unit tests for EventLoop PII redaction hook events (Task 8.2).
/// Requirements: 6.3, 6.4, 6.5
/// </summary>
public class EventLoopPiiHookTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ToolRegistry EmptyRegistry() => new();

    private static ModelResponse EndTurnResponse(string text = "response") =>
        new(text, [], StopReason.EndTurn, TokenUsage.Zero);

    // ── PiiRedactionRequestEvent ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PiiRequestHandler_ModelReceivesReplacedMessages()
    {
        // Arrange: a handler that replaces the message list with a redacted version
        var redactedMessages = new List<Message> { Message.User("[REDACTED]") };

        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionRequestEvent>(evt =>
        {
            evt.Messages = redactedMessages;
            return Task.CompletedTask;
        });

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ModelRequest req, CancellationToken _) =>
             {
                 capturedMessages = req.Messages;
                 return EndTurnResponse();
             });

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);

        // Act
        await loop.RunAsync([Message.User("original sensitive message")], null, CancellationToken.None);

        // Assert: model received the replaced (redacted) messages
        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages!);
        Assert.Equal("[REDACTED]", ((TextBlock)capturedMessages![0].Content[0]).Text);
    }

    [Fact]
    public async Task StreamAsync_PiiRequestHandler_ModelReceivesReplacedMessages()
    {
        var redactedMessages = new List<Message> { Message.User("[REDACTED]") };

        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionRequestEvent>(evt =>
        {
            evt.Messages = redactedMessages;
            return Task.CompletedTask;
        });

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns((ModelRequest req, CancellationToken _) =>
             {
                 capturedMessages = req.Messages;
                 return FakeStream(new ModelCompleteEvent(EndTurnResponse()));
             });

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        await foreach (var _ in loop.StreamAsync([Message.User("original sensitive message")], null, CancellationToken.None)) { }

        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages!);
        Assert.Equal("[REDACTED]", ((TextBlock)capturedMessages![0].Content[0]).Text);
    }

    [Fact]
    public async Task RunAsync_PiiRequestHandler_CanReplaceWithMultipleMessages()
    {
        // Handler replaces with a multi-message list (e.g. system + user)
        var replacedList = new List<Message>
        {
            Message.User("[msg1 redacted]"),
            Message.User("[msg2 redacted]"),
        };

        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionRequestEvent>(evt =>
        {
            evt.Messages = replacedList;
            return Task.CompletedTask;
        });

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ModelRequest req, CancellationToken _) =>
             {
                 capturedMessages = req.Messages;
                 return EndTurnResponse();
             });

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        await loop.RunAsync([Message.User("original")], null, CancellationToken.None);

        Assert.Equal(2, capturedMessages!.Count);
    }

    // ── PiiRedactionResponseEvent ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PiiResponseHandler_SubsequentProcessingUsesReplacedResponse()
    {
        // Handler replaces the response text with a redacted version
        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionResponseEvent>(evt =>
        {
            evt.Response = new ModelResponse(
                "[response redacted]",
                evt.Response.ToolCalls,
                evt.Response.StopReason,
                evt.Response.Usage);
            return Task.CompletedTask;
        });

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(EndTurnResponse("original sensitive response"));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        var (result, _) = await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        // The AgentResult should contain the replaced (redacted) response text
        Assert.Equal("[response redacted]", result.Message);
    }

    [Fact]
    public async Task StreamAsync_PiiResponseHandler_GuardrailBlockedResponseRespected()
    {
        // In StreamAsync, the AgentResult.Message is built from the accumulated textBuilder,
        // not from finalResponse.TextContent. However, the StopReason IS taken from the
        // replaced response. Verify that a handler replacing StopReason is respected.
        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionResponseEvent>(evt =>
        {
            evt.Response = new ModelResponse(
                "[stream response redacted]",
                evt.Response.ToolCalls,
                StopReason.GuardrailBlocked, // replace stop reason
                evt.Response.Usage);
            return Task.CompletedTask;
        });

        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns(FakeStream(
                 new ModelCompleteEvent(EndTurnResponse("original response"))));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        var events = new List<StreamEvent>();
        await foreach (var evt in loop.StreamAsync([Message.User("Hi")], null, CancellationToken.None))
            events.Add(evt);

        var complete = Assert.IsType<AgentCompleteEvent>(events.Last());
        // The replaced StopReason should be respected — loop halts with GuardrailBlocked
        Assert.Equal(StopReason.GuardrailBlocked, complete.Result.StopReason);
    }

    [Fact]
    public async Task RunAsync_PiiResponseHandler_GuardrailBlockedResponseRespected()
    {
        // Handler replaces response with a GuardrailBlocked response
        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionResponseEvent>(evt =>
        {
            evt.Response = new ModelResponse(
                "[blocked]",
                [],
                StopReason.GuardrailBlocked,
                evt.Response.Usage);
            return Task.CompletedTask;
        });

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(EndTurnResponse("normal response"));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        var (result, _) = await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        // EventLoop should respect the replaced response's StopReason
        Assert.Equal(StopReason.GuardrailBlocked, result.StopReason);
    }

    // ── No handler registered ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoHandlerRegistered_ModelReceivesOriginalMessages()
    {
        var originalMessages = new List<Message> { Message.User("original message") };

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ModelRequest req, CancellationToken _) =>
             {
                 // Capture a snapshot of the messages at call time
                 capturedMessages = req.Messages.ToList();
                 return EndTurnResponse();
             });

        // No PII handler registered — use an empty HookRegistry
        var hooks = new HookRegistry();
        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        await loop.RunAsync(originalMessages, null, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages!);
        Assert.Equal("original message", ((TextBlock)capturedMessages![0].Content[0]).Text);
    }

    [Fact]
    public async Task RunAsync_NullHookRegistry_ModelReceivesOriginalMessages()
    {
        var originalMessages = new List<Message> { Message.User("original message") };

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((ModelRequest req, CancellationToken _) =>
             {
                 // Capture a snapshot of the messages at call time
                 capturedMessages = req.Messages.ToList();
                 return EndTurnResponse();
             });

        // No hooks at all (null)
        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        await loop.RunAsync(originalMessages, null, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages!);
        Assert.Equal("original message", ((TextBlock)capturedMessages![0].Content[0]).Text);
    }

    [Fact]
    public async Task RunAsync_NoHandlerRegistered_ResultContainsOriginalResponse()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(EndTurnResponse("original response text"));

        // No PII handler registered
        var hooks = new HookRegistry();
        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        var (result, _) = await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        Assert.Equal("original response text", result.Message);
    }

    [Fact]
    public async Task StreamAsync_NoHandlerRegistered_ModelReceivesOriginalMessages()
    {
        var originalMessages = new List<Message> { Message.User("original message") };

        IReadOnlyList<Message>? capturedMessages = null;
        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns((ModelRequest req, CancellationToken _) =>
             {
                 // Capture a snapshot of the messages at call time
                 capturedMessages = req.Messages.ToList();
                 return FakeStream(new ModelCompleteEvent(EndTurnResponse()));
             });

        var hooks = new HookRegistry();
        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        await foreach (var _ in loop.StreamAsync(originalMessages, null, CancellationToken.None)) { }

        Assert.NotNull(capturedMessages);
        Assert.Single(capturedMessages!);
        Assert.Equal("original message", ((TextBlock)capturedMessages![0].Content[0]).Text);
    }

    // ── Multiple handlers — chained replacement ───────────────────────────────

    [Fact]
    public async Task RunAsync_MultipleRequestHandlers_EachSeesUpdatedMessages()
    {
        // First handler replaces messages; second handler sees the first handler's replacement
        IReadOnlyList<Message>? secondHandlerSaw = null;

        var hooks = new HookRegistry();
        hooks.Register<PiiRedactionRequestEvent>(evt =>
        {
            evt.Messages = [Message.User("[first redaction]")];
            return Task.CompletedTask;
        });
        hooks.Register<PiiRedactionRequestEvent>(evt =>
        {
            secondHandlerSaw = evt.Messages;
            return Task.CompletedTask;
        });

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(EndTurnResponse());

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig(), hooks);
        await loop.RunAsync([Message.User("original")], null, CancellationToken.None);

        // Second handler should see the first handler's replacement
        Assert.NotNull(secondHandlerSaw);
        Assert.Single(secondHandlerSaw!);
        Assert.Equal("[first redaction]", ((TextBlock)secondHandlerSaw![0].Content[0]).Text);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ModelStreamEvent> FakeStream(params ModelStreamEvent[] events)
    {
        foreach (var e in events)
            yield return e;
        await Task.CompletedTask;
    }
}
