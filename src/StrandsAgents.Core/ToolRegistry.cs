using System.Collections.Concurrent;
using System.Diagnostics;

namespace StrandsAgents.Core;

/// <summary>
/// Registers and resolves tools. Executes tool calls in parallel or sequentially.
/// Thread-safe: supports concurrent reads and writes via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a single tool.</summary>
    public void Register(ITool tool) => _tools[tool.Definition.Name] = tool;

    /// <summary>Registers multiple tools.</summary>
    public void RegisterAll(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools)
            Register(tool);
    }

    /// <summary>Resolves a tool by name. Returns null if not found.</summary>
    public ITool? Resolve(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>Returns tool definitions for the model request.</summary>
    public IReadOnlyList<ToolDefinition> GetDefinitions() =>
        _tools.Values.Select(t => t.Definition).ToList();

    /// <summary>Executes tool calls, optionally in parallel.</summary>
    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(
        IEnumerable<ToolCall> calls,
        bool parallel,
        CancellationToken ct = default)
    {
        var callList = calls.ToList();

        if (parallel && callList.Count > 1)
        {
            var tasks = callList.Select(call => ExecuteSingleAsync(call, ct));
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var results = new List<ToolResult>(callList.Count);
        foreach (var call in callList)
            results.Add(await ExecuteSingleAsync(call, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<ToolResult> ExecuteSingleAsync(ToolCall call, CancellationToken ct)
    {
        using var toolActivity = StrandsTelemetry.ActivitySource.StartActivity("agent.tool_call");
        toolActivity?.SetTag("tool.name", call.Name);

        var tool = Resolve(call.Name);
        if (tool is null)
        {
            toolActivity?.SetTag("tool.success", false);
            return ToolResult.Failure(call.Id, $"Tool '{call.Name}' not found.");
        }

        if (tool.Definition.ParameterConstraints is { Count: > 0 } constraints)
        {
            var validationError = ValidateParameters(call.Input, constraints);
            if (validationError is not null)
            {
                toolActivity?.SetTag("tool.success", false);
                return ToolResult.Failure(call.Id, validationError);
            }
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await tool.InvokeAsync(call.Input, ct).ConfigureAwait(false);
            // Always normalize ToolCallId to the model-assigned call ID regardless of what
            // the tool implementation returns — tools don't know their own call ID.
            result = result with { ToolCallId = call.Id };

            toolActivity?.SetTag("tool.success", !result.IsError);
            toolActivity?.SetTag("tool.duration_ms", sw.ElapsedMilliseconds);
            StrandsTelemetry.ToolCalls.Add(1);

            return result;
        }
        catch (Exception ex)
        {
            toolActivity?.SetTag("tool.success", false);
            StrandsTelemetry.ToolCalls.Add(1);
            return ToolResult.Failure(call.Id, ex.Message);
        }
    }

    private static string? ValidateParameters(
        System.Text.Json.JsonElement input,
        IReadOnlyDictionary<string, ToolParameterConstraints> constraints)
    {
        foreach (var (paramName, constraint) in constraints)
        {
            // Determine whether the parameter is present and non-null
            System.Text.Json.JsonElement value = default;
            var hasValue = input.ValueKind == System.Text.Json.JsonValueKind.Object
                && input.TryGetProperty(paramName, out value)
                && value.ValueKind != System.Text.Json.JsonValueKind.Null;

            if (constraint.Required && !hasValue)
                return $"Required parameter '{paramName}' is missing or null.";

            if (!hasValue)
                continue;

            if (value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var strValue = value.GetString()!;

                if (constraint.MinLength.HasValue && strValue.Length < constraint.MinLength.Value)
                    return $"Parameter '{paramName}' length {strValue.Length} is less than minimum {constraint.MinLength.Value}.";

                if (constraint.MaxLength.HasValue && strValue.Length > constraint.MaxLength.Value)
                    return $"Parameter '{paramName}' length {strValue.Length} exceeds maximum {constraint.MaxLength.Value}.";

                if (constraint.Pattern is not null
                    && !System.Text.RegularExpressions.Regex.IsMatch(strValue, constraint.Pattern))
                    return $"Parameter '{paramName}' does not match required pattern '{constraint.Pattern}'.";

                if (constraint.AllowedValues is { Length: > 0 }
                    && !constraint.AllowedValues.Contains(strValue, StringComparer.Ordinal))
                    return $"Parameter '{paramName}' value '{strValue}' is not in the allowed values list.";
            }
        }
        return null;
    }
}
