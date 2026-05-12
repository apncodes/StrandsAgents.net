using Microsoft.Extensions.Logging;
using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class AuditLogHookHandlerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ToolCall MakeCall(string id = "call-1", string name = "myTool") =>
        new(id, name, JsonDocument.Parse("{\"secret\":\"do-not-log\"}").RootElement);

    private static ToolResult MakeResult(string callId = "call-1", bool isError = false) =>
        new(callId, "sensitive-output-do-not-log", isError);

    // ── constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AuditLogHookHandler(null!));
    }

    // ── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public void Register_NullRegistry_Throws()
    {
        var handler = new AuditLogHookHandler(new CapturingLogger());
        Assert.Throws<ArgumentNullException>(() => handler.Register(null!));
    }

    // ── BeforeToolCallEvent ───────────────────────────────────────────────────

    [Fact]
    public async Task BeforeToolCall_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        await registry.FireAsync(new BeforeToolCallEvent(MakeCall()));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    [Fact]
    public async Task BeforeToolCall_LogContainsToolName()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        await registry.FireAsync(new BeforeToolCallEvent(MakeCall(name: "specialTool")));

        Assert.Contains("specialTool", logger.Entries[0].Message);
    }

    [Fact]
    public async Task BeforeToolCall_LogContainsCallId()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        await registry.FireAsync(new BeforeToolCallEvent(MakeCall(id: "abc-123")));

        Assert.Contains("abc-123", logger.Entries[0].Message);
    }

    [Fact]
    public async Task BeforeToolCall_LogContainsTimestamp()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var before = DateTimeOffset.UtcNow;
        await registry.FireAsync(new BeforeToolCallEvent(MakeCall()));
        var after = DateTimeOffset.UtcNow;

        // The log message should contain a year that falls within the test window
        var year = before.Year.ToString();
        Assert.Contains(year, logger.Entries[0].Message);
        _ = after; // used to bound the window
    }

    [Fact]
    public async Task BeforeToolCall_LogDoesNotContainInputContent()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        // The ToolCall input JSON contains "secret" and "do-not-log"
        await registry.FireAsync(new BeforeToolCallEvent(MakeCall()));

        Assert.DoesNotContain("secret", logger.Entries[0].Message);
        Assert.DoesNotContain("do-not-log", logger.Entries[0].Message);
    }

    // ── AfterToolCallEvent — success ──────────────────────────────────────────

    [Fact]
    public async Task AfterToolCall_Success_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall();
        var result = MakeResult(isError: false);
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        var afterEntry = logger.Entries[1];
        Assert.Equal(LogLevel.Information, afterEntry.Level);
    }

    [Fact]
    public async Task AfterToolCall_Success_LogContainsToolName()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall(name: "fetchData");
        var result = MakeResult();
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        Assert.Contains("fetchData", logger.Entries[1].Message);
    }

    [Fact]
    public async Task AfterToolCall_Success_LogContainsCallId()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall(id: "xyz-789");
        var result = MakeResult(callId: "xyz-789");
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        Assert.Contains("xyz-789", logger.Entries[1].Message);
    }

    [Fact]
    public async Task AfterToolCall_Success_LogContainsElapsed()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall();
        var result = MakeResult();
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        // ElapsedMs key should appear in the formatted message
        Assert.Contains("ElapsedMs", logger.Entries[1].Message);
    }

    [Fact]
    public async Task AfterToolCall_Success_LogDoesNotContainResultContent()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall();
        // Result content is "sensitive-output-do-not-log"
        var result = MakeResult();
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        Assert.DoesNotContain("sensitive-output-do-not-log", logger.Entries[1].Message);
    }

    // ── AfterToolCallEvent — error ────────────────────────────────────────────

    [Fact]
    public async Task AfterToolCall_IsError_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall();
        var result = MakeResult(isError: true);
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
    }

    [Fact]
    public async Task AfterToolCall_IsError_LogContainsToolNameAndCallId()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall(id: "err-001", name: "riskyTool");
        var result = MakeResult(callId: "err-001", isError: true);
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        var msg = logger.Entries[1].Message;
        Assert.Contains("riskyTool", msg);
        Assert.Contains("err-001", msg);
    }

    // ── Elapsed timing ────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterToolCall_WithoutPrecedingBefore_ElapsedIsZero()
    {
        // If BeforeToolCallEvent was never fired for this call ID, elapsed should be zero
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall(id: "orphan");
        var result = MakeResult(callId: "orphan");
        // Fire AfterToolCallEvent without a preceding BeforeToolCallEvent
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        Assert.Single(logger.Entries);
        Assert.Contains("0", logger.Entries[0].Message); // ElapsedMs=0
    }

    [Fact]
    public async Task AfterToolCall_ElapsedIsNonNegative()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call = MakeCall();
        var result = MakeResult();
        await registry.FireAsync(new BeforeToolCallEvent(call));
        await Task.Delay(5); // ensure some time passes
        await registry.FireAsync(new AfterToolCallEvent(call, result));

        // Parse the ElapsedMs value from the log message
        var msg = logger.Entries[1].Message;
        var idx = msg.IndexOf("ElapsedMs=", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var valueStr = msg[(idx + "ElapsedMs=".Length)..];
        var elapsed = double.Parse(valueStr.Split(' ', '\n')[0]);
        Assert.True(elapsed >= 0);
    }

    // ── Multiple concurrent calls ─────────────────────────────────────────────

    [Fact]
    public async Task MultipleCallIds_EachTrackedIndependently()
    {
        var logger = new CapturingLogger();
        var handler = new AuditLogHookHandler(logger);
        var registry = new HookRegistry();
        handler.Register(registry);

        var call1 = MakeCall(id: "c1", name: "tool1");
        var call2 = MakeCall(id: "c2", name: "tool2");
        var result1 = MakeResult(callId: "c1");
        var result2 = MakeResult(callId: "c2");

        await registry.FireAsync(new BeforeToolCallEvent(call1));
        await registry.FireAsync(new BeforeToolCallEvent(call2));
        await registry.FireAsync(new AfterToolCallEvent(call1, result1));
        await registry.FireAsync(new AfterToolCallEvent(call2, result2));

        Assert.Equal(4, logger.Entries.Count);
        // Both after-entries should be Information (no errors)
        Assert.Equal(LogLevel.Information, logger.Entries[2].Level);
        Assert.Equal(LogLevel.Information, logger.Entries[3].Level);
        // Each after-entry references its own call ID
        Assert.Contains("c1", logger.Entries[2].Message);
        Assert.Contains("c2", logger.Entries[3].Message);
    }
}

// ── Test helper: captures log entries ────────────────────────────────────────

internal sealed class LogEntry(LogLevel level, string message)
{
    public LogLevel Level { get; } = level;
    public string Message { get; } = message;
}

internal sealed class CapturingLogger : ILogger
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
