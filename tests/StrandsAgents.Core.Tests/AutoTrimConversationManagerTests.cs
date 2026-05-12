using Moq;
using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Tests for IAutoTrimConversationManager, SummarizingConversationManager auto-trim,
/// AutoTrimConversationManagerDecorator, and Agent auto-trim integration.
/// </summary>
public sealed class AutoTrimConversationManagerTests
{
    // ── SummarizingConversationManager implements IAutoTrimConversationManager ──

    [Fact]
    public void SummarizingConversationManager_ImplementsIAutoTrimConversationManager()
    {
        var model = new Mock<IModel>().Object;
        var manager = new SummarizingConversationManager(model);

        Assert.IsAssignableFrom<IAutoTrimConversationManager>(manager);
    }

    [Fact]
    public async Task SummarizingConversationManager_TrimAfterTurnAsync_BelowThreshold_IsNoOp()
    {
        var model = new Mock<IModel>();
        var manager = new SummarizingConversationManager(model.Object, threshold: 10, keepRecentCount: 3);

        manager.Append(Message.User("hello"));
        manager.Append(Message.Assistant("hi"));

        await ((IAutoTrimConversationManager)manager).TrimAfterTurnAsync();

        // Below threshold — model should NOT have been called for summarization
        model.Verify(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(2, manager.GetMessages().Count);
    }

    [Fact]
    public async Task SummarizingConversationManager_TrimAfterTurnAsync_AboveThreshold_Summarizes()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse("Summary of old messages.", [], StopReason.EndTurn, TokenUsage.Zero));

        var manager = new SummarizingConversationManager(model.Object, threshold: 4, keepRecentCount: 2);

        // Add 5 messages — exceeds threshold of 4
        for (var i = 0; i < 5; i++)
            manager.Append(i % 2 == 0 ? Message.User($"msg {i}") : Message.Assistant($"reply {i}"));

        var countBefore = manager.GetMessages().Count;
        await ((IAutoTrimConversationManager)manager).TrimAfterTurnAsync();

        // After trim: 1 summary message + 2 recent = 3 messages
        Assert.True(manager.GetMessages().Count < countBefore);
        model.Verify(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AutoTrimConversationManagerDecorator ─────────────────────────────────

    [Fact]
    public void Decorator_ImplementsIAutoTrimConversationManager()
    {
        var model = new Mock<IModel>().Object;
        var inner = new SummarizingConversationManager(model);
        var decorator = new AutoTrimConversationManagerDecorator(inner);

        Assert.IsAssignableFrom<IAutoTrimConversationManager>(decorator);
    }

    [Fact]
    public void Decorator_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AutoTrimConversationManagerDecorator(null!));
    }

    [Fact]
    public void Decorator_GetMessages_DelegatesToInner()
    {
        var model = new Mock<IModel>().Object;
        var inner = new SummarizingConversationManager(model);
        inner.Append(Message.User("hello"));

        var decorator = new AutoTrimConversationManagerDecorator(inner);

        Assert.Equal(inner.GetMessages(), decorator.GetMessages());
    }

    [Fact]
    public void Decorator_Append_DelegatesToInner()
    {
        var model = new Mock<IModel>().Object;
        var inner = new SummarizingConversationManager(model);
        var decorator = new AutoTrimConversationManagerDecorator(inner);

        decorator.Append(Message.User("test"));

        Assert.Single(inner.GetMessages());
        Assert.Equal("test", ((TextBlock)inner.GetMessages()[0].Content[0]).Text);
    }

    [Fact]
    public void Decorator_Trim_DelegatesToInner()
    {
        var model = new Mock<IModel>().Object;
        var inner = new SummarizingConversationManager(model);
        inner.Append(Message.User("hello"));
        var decorator = new AutoTrimConversationManagerDecorator(inner);

        // SummarizingConversationManager.Trim() is a no-op — just verify it doesn't throw
        decorator.Trim();

        Assert.Single(inner.GetMessages()); // unchanged
    }

    [Fact]
    public async Task Decorator_TrimAfterTurnAsync_DelegatesToInnerTrimAsync()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse("Summary.", [], StopReason.EndTurn, TokenUsage.Zero));

        var inner = new SummarizingConversationManager(model.Object, threshold: 3, keepRecentCount: 1);
        var decorator = new AutoTrimConversationManagerDecorator(inner);

        // Add 4 messages — exceeds threshold of 3
        for (var i = 0; i < 4; i++)
            decorator.Append(i % 2 == 0 ? Message.User($"u{i}") : Message.Assistant($"a{i}"));

        await decorator.TrimAfterTurnAsync();

        // Model was called for summarization
        model.Verify(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Decorator_GetMessages_AlwaysMatchesInner()
    {
        var model = new Mock<IModel>().Object;
        var inner = new SummarizingConversationManager(model);
        var decorator = new AutoTrimConversationManagerDecorator(inner);

        // Before any messages
        Assert.Equal(inner.GetMessages().Count, decorator.GetMessages().Count);

        // After appending via decorator
        decorator.Append(Message.User("a"));
        decorator.Append(Message.Assistant("b"));
        Assert.Equal(inner.GetMessages().Count, decorator.GetMessages().Count);
        // Both return the same underlying data (equal content, not necessarily same instance)
        Assert.Equal(inner.GetMessages(), decorator.GetMessages());
    }

    // ── Agent auto-trim integration ───────────────────────────────────────────

    [Fact]
    public async Task Agent_InvokeAsync_CallsTrimAfterTurnAsync_WhenConversationIsAutoTrim()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse("Done.", [], StopReason.EndTurn, TokenUsage.Zero));

        var autoTrimMock = new Mock<IAutoTrimConversationManager>();
        autoTrimMock.Setup(c => c.GetMessages()).Returns([]);
        autoTrimMock.Setup(c => c.TrimAfterTurnAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var agent = new Agent(model.Object, conversationManager: autoTrimMock.Object);

        await agent.InvokeAsync("hello");

        autoTrimMock.Verify(c => c.TrimAfterTurnAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Agent_InvokeAsync_DoesNotCallTrimAfterTurnAsync_WhenConversationIsNotAutoTrim()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse("Done.", [], StopReason.EndTurn, TokenUsage.Zero));

        // Plain InMemoryConversationManager does NOT implement IAutoTrimConversationManager
        var conversation = new InMemoryConversationManager();
        var agent = new Agent(model.Object, conversationManager: conversation);

        // Should complete without error — no trim called
        var result = await agent.InvokeAsync("hello");

        Assert.Equal("Done.", result.Message);
    }

    [Fact]
    public async Task Agent_StreamAsync_CallsTrimAfterTurnAsync_WhenConversationIsAutoTrim()
    {
        var agentResult = new AgentResult("Done.", StopReason.EndTurn, TokenUsage.Zero,
            new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero));

        var model = new Mock<IModel>();
        model.Setup(m => m.StreamAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .Returns(AsyncModelEvents(
                 new TextDeltaModelEvent("Done."),
                 new ModelCompleteEvent(new ModelResponse("Done.", [], StopReason.EndTurn, TokenUsage.Zero))));

        var autoTrimMock = new Mock<IAutoTrimConversationManager>();
        autoTrimMock.Setup(c => c.GetMessages()).Returns([]);
        autoTrimMock.Setup(c => c.TrimAfterTurnAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var agent = new Agent(model.Object, conversationManager: autoTrimMock.Object);

        // Consume the full stream
        await foreach (var _ in agent.StreamAsync("hello")) { }

        autoTrimMock.Verify(c => c.TrimAfterTurnAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ModelStreamEvent> AsyncModelEvents(
        params ModelStreamEvent[] events)
    {
        foreach (var e in events)
        {
            yield return e;
            await Task.Yield();
        }
    }
}
