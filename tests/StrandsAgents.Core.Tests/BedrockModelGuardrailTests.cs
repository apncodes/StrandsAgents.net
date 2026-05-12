using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Moq;
using StrandsAgents.Models.Bedrock;
using System.Text.Json;
using Xunit;

// Aliases to avoid ambiguity between StrandsAgents.Core and Amazon.BedrockRuntime types
using CoreGuardrailAction = StrandsAgents.Core.GuardrailAction;
using CoreGuardrailSource = StrandsAgents.Core.GuardrailSource;
using CoreStopReason = StrandsAgents.Core.StopReason;
using CoreModelRequest = StrandsAgents.Core.ModelRequest;
using CoreModelParameters = StrandsAgents.Core.ModelParameters;
using CoreMessage = StrandsAgents.Core.Message;
using CoreHookRegistry = StrandsAgents.Core.HookRegistry;
using CoreGuardrailViolationEvent = StrandsAgents.Core.GuardrailViolationEvent;
using BRContentBlock = Amazon.BedrockRuntime.Model.ContentBlock;
using BRMessage = Amazon.BedrockRuntime.Model.Message;
using BRTokenUsage = Amazon.BedrockRuntime.Model.TokenUsage;
using BRGuardrailAction = Amazon.BedrockRuntime.GuardrailAction;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Unit tests for BedrockModel guardrail config (Task 11.1).
/// Requirements: 1.3, 1.4, 1.7, 1.8, 2.2, 5.2, 5.5
/// </summary>
public class BedrockModelGuardrailTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static ConverseResponse MakeConverseResponse(
        Amazon.BedrockRuntime.StopReason? stopReason = null,
        string text = "ok")
    {
        stopReason ??= Amazon.BedrockRuntime.StopReason.End_turn;
        return new ConverseResponse
        {
            StopReason = stopReason,
            Output = new ConverseOutput
            {
                Message = new BRMessage
                {
                    Role = ConversationRole.Assistant,
                    Content = [new BRContentBlock { Text = text }]
                }
            },
            Usage = new BRTokenUsage { InputTokens = 1, OutputTokens = 1 }
        };
    }

    private static ApplyGuardrailResponse MakeApplyGuardrailResponse(
        BRGuardrailAction action,
        string? outputText = null)
    {
        var resp = new ApplyGuardrailResponse { Action = action };
        if (outputText is not null)
            resp.Outputs = [new GuardrailOutputContent { Text = outputText }];
        return resp;
    }

    private static CoreModelRequest MakeRequest(params string[] userMessages)
    {
        var messages = userMessages
            .Select(t => CoreMessage.User(t))
            .ToList();
        return new CoreModelRequest(
            messages,
            null,
            [],
            new CoreModelParameters());
    }

    // ── 1. BuildConverseRequest includes GuardrailConfig when config non-null ─

    [Fact]
    public async Task InvokeAsync_WithGuardrailConfig_ConverseRequestIncludesGuardrailConfig()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-123", "DRAFT");
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.GuardrailConfig);
        Assert.Equal("g-123", captured.GuardrailConfig.GuardrailIdentifier);
        Assert.Equal("DRAFT", captured.GuardrailConfig.GuardrailVersion);
    }

    // ── 2. BuildConverseRequest omits GuardrailConfig when config is null ─────

    [Fact]
    public async Task InvokeAsync_WithoutGuardrailConfig_ConverseRequestOmitsGuardrailConfig()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var model = new BedrockModel(clientOverride: client.Object); // no guardrailConfig

        await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured!.GuardrailConfig);
    }

    // ── 3. EvaluateLatestMessageOnly=true wraps only last user message ─────────

    [Fact]
    public async Task InvokeAsync_EvaluateLatestMessageOnlyTrue_WrapsOnlyLastUserMessage()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { EvaluateLatestMessageOnly = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        // Two user messages — only the last should be wrapped
        var request = new CoreModelRequest(
            [
                CoreMessage.User("First message"),
                CoreMessage.User("Second message")
            ],
            null, [], new CoreModelParameters());

        await model.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(captured);
        var messages = captured!.Messages;
        Assert.Equal(2, messages.Count);

        // First message: plain text block (no guardContent)
        var firstMsg = messages[0];
        Assert.All(firstMsg.Content, block => Assert.Null(block.GuardContent));
        Assert.Contains(firstMsg.Content, block => block.Text == "First message");

        // Last message: guardContent block
        var lastMsg = messages[1];
        Assert.Contains(lastMsg.Content, block =>
            block.GuardContent?.Text?.Text == "Second message");
    }

    // ── 4. EvaluateLatestMessageOnly=false sends all messages as plain text ───

    [Fact]
    public async Task InvokeAsync_EvaluateLatestMessageOnlyFalse_AllMessagesArePlainText()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { EvaluateLatestMessageOnly = false };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        var request = new CoreModelRequest(
            [
                CoreMessage.User("First message"),
                CoreMessage.User("Second message")
            ],
            null, [], new CoreModelParameters());

        await model.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(captured);
        // All messages should have plain text blocks, no guardContent
        foreach (var msg in captured!.Messages)
        {
            Assert.All(msg.Content, block => Assert.Null(block.GuardContent));
        }
    }

    // ── 5. MapStopReason maps guardrail_intervened to GuardrailBlocked ─────────

    [Fact]
    public async Task InvokeAsync_GuardrailIntervenedStopReason_MapsToGuardrailBlocked()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse(Amazon.BedrockRuntime.StopReason.Guardrail_intervened, "[blocked]"));

        var config = new BedrockGuardrailConfig("g-1", "1");
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(CoreStopReason.GuardrailBlocked, response.StopReason);
    }

    [Fact]
    public async Task InvokeAsync_NonGuardrailStopReason_DoesNotMapToGuardrailBlocked()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse(Amazon.BedrockRuntime.StopReason.End_turn));

        var model = new BedrockModel(clientOverride: client.Object);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.NotEqual(CoreStopReason.GuardrailBlocked, response.StopReason);
    }

    // ── 6. Shadow mode calls ApplyGuardrailAsync before ConverseAsync ──────────

    [Fact]
    public async Task InvokeAsync_ShadowMode_CallsApplyGuardrailBeforeConverse()
    {
        var callOrder = new List<string>();

        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
              .Callback(() => callOrder.Add("ApplyGuardrail"))
              .ReturnsAsync(MakeApplyGuardrailResponse(BRGuardrailAction.NONE));
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((_, _) => callOrder.Add("Converse"))
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { ShadowMode = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(2, callOrder.Count);
        Assert.Equal("ApplyGuardrail", callOrder[0]);
        Assert.Equal("Converse", callOrder[1]);
    }

    [Fact]
    public async Task InvokeAsync_ShadowMode_IntervenedAction_FiresViolationEventAndProceedsWithConverse()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeApplyGuardrailResponse(BRGuardrailAction.GUARDRAIL_INTERVENED, "[shadow blocked]"));
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse());

        CoreGuardrailViolationEvent? capturedEvent = null;
        var hooks = new CoreHookRegistry();
        hooks.Register<CoreGuardrailViolationEvent>(evt =>
        {
            capturedEvent = evt;
            return Task.CompletedTask;
        });

        var config = new BedrockGuardrailConfig("g-1", "1") { ShadowMode = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config, hooks: hooks);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        // Converse should still proceed — shadow mode never blocks
        Assert.Equal(CoreStopReason.EndTurn, response.StopReason);
        // Violation event should have been fired
        Assert.NotNull(capturedEvent);
        Assert.Equal(CoreGuardrailAction.Intervened, capturedEvent!.Action);
        Assert.Equal(CoreGuardrailSource.Input, capturedEvent.Source);
        // Converse was called
        client.Verify(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 7. Shadow mode exception is swallowed and Converse proceeds ────────────

    [Fact]
    public async Task InvokeAsync_ShadowMode_ApplyGuardrailThrows_ExceptionSwallowedAndConverseProceeds()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Guardrail service unavailable"));
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { ShadowMode = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        // Should not throw — exception from ApplyGuardrail is swallowed
        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(CoreStopReason.EndTurn, response.StopReason);
        // Converse should still have been called
        client.Verify(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 8. IsEnabled and ShadowMode properties ────────────────────────────────

    [Fact]
    public void IsEnabled_WithGuardrailConfig_ReturnsTrue()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        var config = new BedrockGuardrailConfig("g-1", "1");
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        Assert.True(model.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WithoutGuardrailConfig_ReturnsFalse()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        var model = new BedrockModel(clientOverride: client.Object);

        Assert.False(model.IsEnabled);
    }

    [Fact]
    public void ShadowMode_WithShadowModeTrue_ReturnsTrue()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        var config = new BedrockGuardrailConfig("g-1", "1") { ShadowMode = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        Assert.True(model.ShadowMode);
    }

    [Fact]
    public void ShadowMode_WithoutGuardrailConfig_ReturnsFalse()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        var model = new BedrockModel(clientOverride: client.Object);

        Assert.False(model.ShadowMode);
    }

    // ── 9. EvaluateAsync returns None when no guardrail config ────────────────

    [Fact]
    public async Task EvaluateAsync_NoGuardrailConfig_ReturnsNoneAction()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        var model = new BedrockModel(clientOverride: client.Object);

        var result = await model.EvaluateAsync("some content", "INPUT");

        Assert.Equal(CoreGuardrailAction.None, result.Action);
        Assert.Null(result.GuardrailId);
        // ApplyGuardrailAsync should never be called
        client.Verify(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 10. GuardrailBlocked response applies output redaction ────────────────

    [Fact]
    public async Task InvokeAsync_GuardrailBlocked_RedactOutputTrue_ReplacesTextWithRedactOutputMessage()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse(Amazon.BedrockRuntime.StopReason.Guardrail_intervened, "original text"));

        var config = new BedrockGuardrailConfig("g-1", "1")
        {
            RedactOutput = true,
            RedactOutputMessage = "[custom redacted]"
        };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(CoreStopReason.GuardrailBlocked, response.StopReason);
        Assert.Equal("[custom redacted]", response.TextContent);
    }

    [Fact]
    public async Task InvokeAsync_GuardrailBlocked_RedactOutputTrue_NullMessage_UsesDefaultPlaceholder()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse(Amazon.BedrockRuntime.StopReason.Guardrail_intervened, "original text"));

        var config = new BedrockGuardrailConfig("g-1", "1")
        {
            RedactOutput = true,
            RedactOutputMessage = null  // use default
        };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(CoreStopReason.GuardrailBlocked, response.StopReason);
        Assert.Equal("[Output redacted by guardrail]", response.TextContent);
    }

    // ── 11. GuardrailTrace is set correctly ───────────────────────────────────

    [Fact]
    public async Task InvokeAsync_TraceTrue_SetsGuardrailTraceEnabled()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { Trace = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.NotNull(captured?.GuardrailConfig);
        Assert.Equal(GuardrailTrace.Enabled, captured!.GuardrailConfig.Trace);
    }

    [Fact]
    public async Task InvokeAsync_TraceFalse_SetsGuardrailTraceDisabled()
    {
        ConverseRequest? captured = null;
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
              .ReturnsAsync(MakeConverseResponse());

        var config = new BedrockGuardrailConfig("g-1", "1") { Trace = false };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.NotNull(captured?.GuardrailConfig);
        Assert.Equal(GuardrailTrace.Disabled, captured!.GuardrailConfig.Trace);
    }

    // ── 12. Shadow mode with no hooks — no NullReferenceException ─────────────

    [Fact]
    public async Task InvokeAsync_ShadowMode_NoHooks_IntervenedAction_DoesNotThrow()
    {
        var client = new Mock<IAmazonBedrockRuntime>();
        client.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeApplyGuardrailResponse(BRGuardrailAction.GUARDRAIL_INTERVENED));
        client.Setup(c => c.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(MakeConverseResponse());

        // No hooks passed — should not throw
        var config = new BedrockGuardrailConfig("g-1", "1") { ShadowMode = true };
        var model = new BedrockModel(clientOverride: client.Object, guardrailConfig: config);

        var response = await model.InvokeAsync(MakeRequest("Hello"), CancellationToken.None);

        Assert.Equal(CoreStopReason.EndTurn, response.StopReason);
    }
}
