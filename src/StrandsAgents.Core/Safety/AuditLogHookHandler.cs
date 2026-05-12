using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StrandsAgents.Core;

/// <summary>
/// Registers audit log hooks that record every tool call and its result to a structured log.
/// Logs only metadata (tool name, call ID, timestamp, elapsed, IsError) — never tool input or output content.
/// </summary>
public sealed class AuditLogHookHandler
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, long> _startTicks = new();

    public AuditLogHookHandler(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Registers BeforeToolCallEvent and AfterToolCallEvent handlers on the provided registry.</summary>
    public void Register(HookRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register<BeforeToolCallEvent>(OnBeforeToolCallAsync);
        registry.Register<AfterToolCallEvent>(OnAfterToolCallAsync);
    }

    private Task OnBeforeToolCallAsync(BeforeToolCallEvent evt)
    {
        _startTicks[evt.Call.Id] = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "Tool call started. ToolName={ToolName} CallId={CallId} Timestamp={Timestamp:O}",
            evt.Call.Name,
            evt.Call.Id,
            DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    private Task OnAfterToolCallAsync(AfterToolCallEvent evt)
    {
        var elapsed = _startTicks.TryRemove(evt.Result.ToolCallId, out var startTick)
            ? Stopwatch.GetElapsedTime(startTick)
            : TimeSpan.Zero;

        if (evt.Result.IsError)
        {
            _logger.LogWarning(
                "Tool call failed. ToolName={ToolName} CallId={CallId} IsError=true ElapsedMs={ElapsedMs}",
                evt.Call.Name,
                evt.Result.ToolCallId,
                elapsed.TotalMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "Tool call completed. ToolName={ToolName} CallId={CallId} IsError=false ElapsedMs={ElapsedMs}",
                evt.Call.Name,
                evt.Result.ToolCallId,
                elapsed.TotalMilliseconds);
        }

        return Task.CompletedTask;
    }
}
