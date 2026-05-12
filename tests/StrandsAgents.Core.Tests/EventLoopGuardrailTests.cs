using Moq;
using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Unit tests for EventLoop guardrail halt and tool result evaluation (Task 8.1).
/// Requirements: 2.3, 2b.1, 2b.2, 2b.3, 2b.4, 2b.5, 3.3
/// </summary>
public class EventLoopGuardrailTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ToolRegistry EmptyRegistry() => new();

    private static ToolRegistry RegistryWith(params ITool[] tools)
    {
        var r = new ToolRegistry();
        r.RegisterAll(tools);
        return r;
    }

    private static ModelResponse GuardrailBlockedResponse(string text = "[blocked]") =>
        new(text, [], StopReason.GuardrailBlocked, TokenUsage.Zero);

    private static ModelResponse ToolUseResponse(params ToolCall[] calls) =>
        new(null, [.. calls], StopReason.ToolUse, TokenUsage.Zero);

    private static ModelResponse EndTurnResponse(string text = "done") =>
        new(text, [], StopReason.EndTurn, TokenUsage.Zero);

    private static ToolCall MakeToolCall(string id = "tc1", string name = "myTool") =>
        new(id, name, JsonDocument.Parse("{}").RootElement);

    private static GuardrailEvaluationResult BlockedEval(string msg = "[redacted]") =>
        new(GuardrailAction.Blocked, msg, "g-id", "1");

    private static GuardrailEvaluationResult IntervenedEval(string msg = "[intervened]") =>
        new(GuardrailAction.Intervened, msg, "g-id", "1");

    private static GuardrailEvaluationResult NoneEval() =>
        new(GuardrailAction.None, null, "g-id", "1");

    // ── RunAsync: model returns GuardrailBlocked ──────────────────────────────

    [Fact]
    public async Task RunAsync_ModelReturnsGuardrailBlocked_ReturnsGuardrailBlockedResult()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(GuardrailBlockedResponse("[blocked by guardrail]"));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        var (result, _) = await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        Assert.Equal(StopReason.GuardrailBlocked, result.StopReason);
        Assert.Equal("[blocked by guardrail]", result.Message);
    }

    [Fact]
    public async Task RunAsync_ModelReturnsGuardrailBlocked_NoFurtherModelCalls()
    {
        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return GuardrailBlockedResponse();
             });

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        // Model should only be called once — loop halts immediately on GuardrailBlocked
        Assert.Equal(1, callCount);
    }

    // ── StreamAsync: model returns GuardrailBlocked ───────────────────────────

    [Fact]
    public async Task StreamAsync_ModelReturnsGuardrailBlocked_FinalEventIsGuardrailBlocked()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns(FakeStream(new ModelCompleteEvent(GuardrailBlockedResponse("[stream blocked]"))));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        var events = new List<StreamEvent>();
        await foreach (var evt in loop.StreamAsync([Message.User("Hi")], null, CancellationToken.None))
            events.Add(evt);

        var complete = Assert.IsType<AgentCompleteEvent>(events.Last());
        Assert.Equal(StopReason.GuardrailBlocked, complete.Result.StopReason);
    }

    [Fact]
    public async Task StreamAsync_ModelReturnsGuardrailBlocked_NoFurtherModelCalls()
    {
        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 return FakeStream(new ModelCompleteEvent(GuardrailBlockedResponse()));
             });

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        await foreach (var _ in loop.StreamAsync([Message.User("Hi")], null, CancellationToken.None)) { }

        Assert.Equal(1, callCount);
    }

    // ── Tool result: evaluator returns Blocked ────────────────────────────────

    [Fact]
    public async Task RunAsync_EvaluatorBlocksToolResult_LoopHaltsWithGuardrailBlocked()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "sensitive content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? ToolUseResponse(toolCall)
                     : EndTurnResponse();
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(BlockedEval());

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, _) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        Assert.Equal(StopReason.GuardrailBlocked, result.StopReason);
        // Model should only be called once — loop halts after blocked tool result
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RunAsync_EvaluatorBlocksToolResult_RedactedContentInMessages()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "original content"));

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(ToolUseResponse(toolCall));

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(BlockedEval("[redacted by guardrail]"));

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (_, messages) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        // The tool result message should contain the redacted content, not the original
        var toolResultMsg = messages.FirstOrDefault(m => m.Content.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var block = toolResultMsg!.Content.OfType<ToolResultBlock>().First();
        Assert.Equal("[redacted by guardrail]", block.Content);
    }

    // ── Tool result: evaluator returns Intervened ─────────────────────────────

    [Fact]
    public async Task RunAsync_EvaluatorIntervenesToolResult_LoopContinuesWithReplacedContent()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "original content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? ToolUseResponse(toolCall)
                     : EndTurnResponse("completed");
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(IntervenedEval("[intervened content]"));

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, messages) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        // Loop should complete normally (not GuardrailBlocked)
        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Equal(2, callCount);

        // Tool result should contain replaced content
        var toolResultMsg = messages.FirstOrDefault(m => m.Content.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var block = toolResultMsg!.Content.OfType<ToolResultBlock>().First();
        Assert.Equal("[intervened content]", block.Content);
    }

    // ── Tool result: evaluator throws ─────────────────────────────────────────

    [Fact]
    public async Task RunAsync_EvaluatorThrows_LoopContinuesWithOriginalContent()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "original content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? ToolUseResponse(toolCall)
                     : EndTurnResponse("completed");
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Guardrail service unavailable"));

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, messages) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        // Loop should complete normally despite evaluator throwing
        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Equal(2, callCount);

        // Tool result should contain original content
        var toolResultMsg = messages.FirstOrDefault(m => m.Content.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var block = toolResultMsg!.Content.OfType<ToolResultBlock>().First();
        Assert.Equal("original content", block.Content);
    }

    // ── Tool result: shadow mode ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ShadowMode_EvaluatorBlockedAction_LoopCompletesNormally()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? ToolUseResponse(toolCall)
                     : EndTurnResponse("done");
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(true); // shadow mode
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(BlockedEval());

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, _) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        // Shadow mode: loop should complete normally even when evaluator returns Blocked
        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RunAsync_ShadowMode_OriginalContentPreserved()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "original content"));

        var model = new Mock<IModel>();
        var callCount = 0;
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1 ? ToolUseResponse(toolCall) : EndTurnResponse();
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(true);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(BlockedEval("[would be redacted]"));

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (_, messages) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        // Shadow mode: original content should be preserved in messages
        var toolResultMsg = messages.FirstOrDefault(m => m.Content.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var block = toolResultMsg!.Content.OfType<ToolResultBlock>().First();
        Assert.Equal("original content", block.Content);
    }

    // ── Tool result: evaluator disabled / null content ────────────────────────

    [Fact]
    public async Task RunAsync_EvaluatorDisabled_SkipsEvaluation()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1 ? ToolUseResponse(toolCall) : EndTurnResponse();
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(false); // disabled
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, _) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        Assert.Equal(StopReason.EndTurn, result.StopReason);
        // EvaluateAsync should never be called when IsEnabled=false
        evaluator.Verify(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_EmptyToolResultContent_SkipsEvaluation()
    {
        var toolCall = MakeToolCall();
        // Tool returns empty content
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", string.Empty));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1 ? ToolUseResponse(toolCall) : EndTurnResponse();
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var (result, _) = await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        Assert.Equal(StopReason.EndTurn, result.StopReason);
        // EvaluateAsync should not be called for empty content
        evaluator.Verify(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GuardrailViolationEvent fired on tool result intervention ─────────────

    [Fact]
    public async Task RunAsync_EvaluatorBlocked_FiresGuardrailViolationEvent()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "content"));

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(ToolUseResponse(toolCall));

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new GuardrailEvaluationResult(GuardrailAction.Blocked, "[blocked]", "guardrail-123", "v1"));

        GuardrailViolationEvent? capturedEvent = null;
        var hooks = new HookRegistry();
        hooks.Register<GuardrailViolationEvent>(evt =>
        {
            capturedEvent = evt;
            return Task.CompletedTask;
        });

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), hooks, evaluator.Object);
        await loop.RunAsync([Message.User("Go")], null, CancellationToken.None);

        Assert.NotNull(capturedEvent);
        Assert.Equal(GuardrailAction.Blocked, capturedEvent!.Action);
        Assert.Equal(GuardrailSource.ToolResult, capturedEvent.Source);
        Assert.Equal("guardrail-123", capturedEvent.GuardrailId);
        Assert.Equal("v1", capturedEvent.GuardrailVersion);
    }

    // ── StreamAsync: tool result blocked ─────────────────────────────────────

    [Fact]
    public async Task StreamAsync_EvaluatorBlocksToolResult_FinalEventIsGuardrailBlocked()
    {
        var toolCall = MakeToolCall();
        var fakeTool = new FakeTool("myTool", ToolResult.Success("tc1", "content"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? FakeStream(new ModelCompleteEvent(ToolUseResponse(toolCall)))
                     : FakeStream(new ModelCompleteEvent(EndTurnResponse()));
             });

        var evaluator = new Mock<IGuardrailEvaluator>();
        evaluator.SetupGet(e => e.IsEnabled).Returns(true);
        evaluator.SetupGet(e => e.ShadowMode).Returns(false);
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(BlockedEval());

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig(), null, evaluator.Object);
        var events = new List<StreamEvent>();
        await foreach (var evt in loop.StreamAsync([Message.User("Go")], null, CancellationToken.None))
            events.Add(evt);

        var complete = Assert.IsType<AgentCompleteEvent>(events.Last());
        Assert.Equal(StopReason.GuardrailBlocked, complete.Result.StopReason);
        // Model should only be called once
        Assert.Equal(1, callCount);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ModelStreamEvent> FakeStream(params ModelStreamEvent[] events)
    {
        foreach (var e in events)
            yield return e;
        await Task.CompletedTask;
    }
}
