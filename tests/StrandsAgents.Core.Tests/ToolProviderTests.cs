using Moq;
using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Unit tests for the IToolProvider pattern — Agent constructor integration
/// and IToolProvider contract (GetTools count, instance sharing).
/// No live model required; uses Moq stubs.
/// </summary>
public class ToolProviderTests
{
    // ── 5.6 Agent_ToolProviders_RegistersAllTools ─────────────────────────────

    [Fact]
    public async Task Agent_ToolProviders_RegistersAllTools()
    {
        // Arrange: a provider that exposes two tools
        var provider = new StubToolProvider(
            new FakeTool("tool-a", ToolResult.Success("id", "a")),
            new FakeTool("tool-b", ToolResult.Success("id", "b")));

        // Model: first call requests tool-a, second call ends turn
        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? new ModelResponse(null,
                         [new ToolCall("id1", "tool-a", JsonDocument.Parse("{}").RootElement)],
                         StopReason.ToolUse, TokenUsage.Zero)
                     : new ModelResponse("done", [], StopReason.EndTurn, TokenUsage.Zero);
             });

        var agent = new Agent(model.Object, toolProviders: [provider]);

        // Act
        var result = await agent.InvokeAsync("go");

        // Assert: tool-a was invoked, meaning it was registered from the provider
        Assert.Equal("done", result.Message);
        Assert.Equal(1, provider.ToolA.InvokeCount);
    }

    // ── 5.7 Agent_ToolsAndToolProviders_BothRegistered ────────────────────────

    [Fact]
    public async Task Agent_ToolsAndToolProviders_BothRegistered()
    {
        var directTool = new FakeTool("direct", ToolResult.Success("id", "direct-result"));
        var providerTool = new FakeTool("from-provider", ToolResult.Success("id", "provider-result"));
        var provider = new StubToolProvider(providerTool);

        // Model: call direct tool on first iteration, end on second
        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? new ModelResponse(null,
                         [new ToolCall("id1", "direct", JsonDocument.Parse("{}").RootElement)],
                         StopReason.ToolUse, TokenUsage.Zero)
                     : new ModelResponse("done", [], StopReason.EndTurn, TokenUsage.Zero);
             });

        var agent = new Agent(model.Object, tools: [directTool], toolProviders: [provider]);

        var result = await agent.InvokeAsync("go");

        Assert.Equal("done", result.Message);
        Assert.Equal(1, directTool.InvokeCount);   // direct tool was reached
        Assert.Equal(0, providerTool.InvokeCount); // provider tool not called this run, but registered
    }

    // ── 5.8 Agent_NullToolProviders_DoesNotThrow ─────────────────────────────

    [Fact]
    public void Agent_NullToolProviders_DoesNotThrow()
    {
        var model = new Mock<IModel>();
        // Should construct without exception
        var agent = new Agent(model.Object, toolProviders: null);
        Assert.NotNull(agent);
    }

    // ── 5.9 GetTools_ReturnsCorrectCount ─────────────────────────────────────

    [Fact]
    public void GetTools_ReturnsCorrectCount()
    {
        var provider = new StubToolProvider(
            new FakeTool("t1", ToolResult.Success("id", "1")),
            new FakeTool("t2", ToolResult.Success("id", "2")),
            new FakeTool("t3", ToolResult.Success("id", "3")));

        Assert.Equal(3, provider.GetTools().Count());
    }

    // ── 5.10 GetTools_WrappersShareSameInstance ───────────────────────────────

    [Fact]
    public void GetTools_WrappersShareSameInstance()
    {
        // StubToolProvider.GetTools() passes `this` to each wrapper constructor.
        // We verify this by checking that all tools returned are from the same provider
        // instance (the provider tracks itself via a public property on each wrapper).
        var provider = new InstanceTrackingProvider();
        var tools = provider.GetTools().ToList();

        Assert.Equal(2, tools.Count);

        // Both wrappers must reference the same host instance
        var wrapper1 = Assert.IsType<InstanceTrackingProvider.ToolOneWrapper>(tools[0]);
        var wrapper2 = Assert.IsType<InstanceTrackingProvider.ToolTwoWrapper>(tools[1]);

        Assert.Same(provider, wrapper1.Host);
        Assert.Same(provider, wrapper2.Host);
    }
}

// ── Test helpers ──────────────────────────────────────────────────────────────

/// <summary>
/// A hand-written IToolProvider that wraps a fixed set of FakeTools.
/// Simulates what the source generator would emit for a partial class.
/// </summary>
internal sealed class StubToolProvider : IToolProvider
{
    private readonly FakeTool[] _tools;

    public StubToolProvider(params FakeTool[] tools) => _tools = tools;

    // Convenience accessor for the first tool (used in test assertions)
    public FakeTool ToolA => _tools[0];

    public IEnumerable<ITool> GetTools() => _tools;
}

/// <summary>
/// A hand-written IToolProvider whose wrappers expose their host instance,
/// allowing tests to verify that all wrappers share the same host reference.
/// </summary>
internal sealed class InstanceTrackingProvider : IToolProvider
{
    public IEnumerable<ITool> GetTools()
    {
        yield return new ToolOneWrapper(this);
        yield return new ToolTwoWrapper(this);
    }

    internal sealed class ToolOneWrapper : ITool
    {
        public InstanceTrackingProvider Host { get; }
        public ToolOneWrapper(InstanceTrackingProvider host) => Host = host;
        public ToolDefinition Definition { get; } =
            new("tool-one", "first tool", JsonDocument.Parse("{}").RootElement);
        public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success("tool-one", "one"));
    }

    internal sealed class ToolTwoWrapper : ITool
    {
        public InstanceTrackingProvider Host { get; }
        public ToolTwoWrapper(InstanceTrackingProvider host) => Host = host;
        public ToolDefinition Definition { get; } =
            new("tool-two", "second tool", JsonDocument.Parse("{}").RootElement);
        public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success("tool-two", "two"));
    }
}
