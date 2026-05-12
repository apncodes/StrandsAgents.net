using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Unit tests for ToolRegistry parameter validation (Task 6 / Requirements 8.2–8.6).
/// </summary>
public class ToolRegistryValidationTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ToolCall whose Input is a JSON object with the supplied key/value pairs.
    /// </summary>
    private static ToolCall MakeCall(string id, string toolName, object? inputObj = null)
    {
        var json = inputObj is null ? "{}" : System.Text.Json.JsonSerializer.Serialize(inputObj);
        return new ToolCall(id, toolName, JsonDocument.Parse(json).RootElement);
    }

    /// <summary>
    /// Creates a constrained tool with a single parameter named "value" and the given constraints.
    /// </summary>
    private static (ToolRegistry registry, TrackingTool tool) MakeRegistry(
        ToolParameterConstraints constraints,
        string paramName = "value")
    {
        var tool = new TrackingTool("myTool", paramName, constraints);
        var registry = new ToolRegistry();
        registry.Register(tool);
        return (registry, tool);
    }

    // ── Required parameter ────────────────────────────────────────────────────

    [Fact]
    public async Task MissingRequiredParameter_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(Required: true));

        // Input object has no "value" key at all
        var call = MakeCall("c1", "myTool", new { });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.Single(results);
        Assert.True(results[0].IsError);
        Assert.Contains("value", results[0].Content);   // message names the param
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task NullRequiredParameter_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(Required: true));

        // Input has the key but with a JSON null value
        var call = MakeCall("c1", "myTool",
            JsonDocument.Parse("{\"value\":null}").RootElement);
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Equal(0, tool.InvokeCount);
    }

    // ── MinLength ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StringBelowMinLength_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(MinLength: 5));

        var call = MakeCall("c1", "myTool", new { value = "hi" }); // length 2 < 5
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Contains("value", results[0].Content);
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task StringAtExactMinLength_Succeeds_InvokeCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(MinLength: 3));

        var call = MakeCall("c1", "myTool", new { value = "abc" }); // length 3 == 3
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal(1, tool.InvokeCount);
    }

    // ── MaxLength ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StringAboveMaxLength_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(MaxLength: 3));

        var call = MakeCall("c1", "myTool", new { value = "toolong" }); // length 7 > 3
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Contains("value", results[0].Content);
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task StringAtExactMaxLength_Succeeds_InvokeCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(MaxLength: 5));

        var call = MakeCall("c1", "myTool", new { value = "hello" }); // length 5 == 5
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal(1, tool.InvokeCount);
    }

    // ── Pattern ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task StringNotMatchingPattern_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(Pattern: @"^\d+$"));

        var call = MakeCall("c1", "myTool", new { value = "abc" }); // not digits
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Contains("value", results[0].Content);
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task StringMatchingPattern_Succeeds_InvokeCalled()
    {
        var (registry, tool) = MakeRegistry(new ToolParameterConstraints(Pattern: @"^\d+$"));

        var call = MakeCall("c1", "myTool", new { value = "12345" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal(1, tool.InvokeCount);
    }

    // ── AllowedValues ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ValueNotInAllowedValues_ReturnsError_InvokeNotCalled()
    {
        var (registry, tool) = MakeRegistry(
            new ToolParameterConstraints(AllowedValues: ["red", "green", "blue"]));

        var call = MakeCall("c1", "myTool", new { value = "yellow" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Contains("value", results[0].Content);
        Assert.Equal(0, tool.InvokeCount);
    }

    [Fact]
    public async Task ValueInAllowedValues_Succeeds_InvokeCalled()
    {
        var (registry, tool) = MakeRegistry(
            new ToolParameterConstraints(AllowedValues: ["red", "green", "blue"]));

        var call = MakeCall("c1", "myTool", new { value = "green" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal(1, tool.InvokeCount);
    }

    [Fact]
    public async Task AllowedValues_IsCaseSensitive()
    {
        var (registry, tool) = MakeRegistry(
            new ToolParameterConstraints(AllowedValues: ["Red"]));

        // "red" != "Red" (ordinal comparison)
        var call = MakeCall("c1", "myTool", new { value = "red" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.True(results[0].IsError);
        Assert.Equal(0, tool.InvokeCount);
    }

    // ── All constraints satisfied ─────────────────────────────────────────────

    [Fact]
    public async Task AllConstraintsSatisfied_InvokeCalledAndResultReturned()
    {
        var constraints = new ToolParameterConstraints(
            Required: true,
            MinLength: 3,
            MaxLength: 10,
            Pattern: @"^[a-z]+$",
            AllowedValues: ["hello", "world"]);

        var (registry, tool) = MakeRegistry(constraints);

        var call = MakeCall("c1", "myTool", new { value = "hello" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal("ok", results[0].Content);
        Assert.Equal(1, tool.InvokeCount);
    }

    // ── No constraints (null / empty) — existing tools unaffected ────────────

    [Fact]
    public async Task NoConstraints_InvokeCalledNormally()
    {
        // FakeTool from EventLoopTests has null ParameterConstraints
        var tool = new FakeTool("noConstraints", ToolResult.Success("c1", "fine"));
        var registry = new ToolRegistry();
        registry.Register(tool);

        var call = MakeCall("c1", "noConstraints", new { anything = "goes" });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.False(results[0].IsError);
        Assert.Equal(1, tool.InvokeCount);
    }

    // ── ToolCallId is preserved on validation failure ─────────────────────────

    [Fact]
    public async Task ValidationFailure_ToolCallIdMatchesCallId()
    {
        var (registry, _) = MakeRegistry(new ToolParameterConstraints(Required: true));

        var call = MakeCall("my-unique-id", "myTool", new { });
        var results = await registry.ExecuteAsync([call], parallel: false);

        Assert.Equal("my-unique-id", results[0].ToolCallId);
    }
}

// ── Test helper ───────────────────────────────────────────────────────────────

/// <summary>
/// An ITool implementation that tracks how many times InvokeAsync was called.
/// Accepts a ToolDefinition with ParameterConstraints for validation testing.
/// </summary>
internal sealed class TrackingTool : ITool
{
    public int InvokeCount { get; private set; }

    public TrackingTool(string name, string paramName, ToolParameterConstraints constraints)
    {
        Definition = new ToolDefinition(
            name,
            "tracking tool",
            JsonDocument.Parse("{}").RootElement,
            ParameterConstraints: new Dictionary<string, ToolParameterConstraints>
            {
                [paramName] = constraints
            });
    }

    public ToolDefinition Definition { get; }

    public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        InvokeCount++;
        return Task.FromResult(ToolResult.Success(string.Empty, "ok"));
    }
}

// Overload that accepts a pre-built ToolCall JsonElement directly
file static class ToolRegistryValidationTestsExtensions
{
    public static ToolCall MakeCall(string id, string toolName, JsonElement input)
        => new(id, toolName, input);
}
