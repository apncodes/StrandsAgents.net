using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;

// AutoTrimAssistant — demonstrates zero-boilerplate automatic conversation summarization.
//
// Architecture:
//   SummarizingConversationManager — implements IAutoTrimConversationManager, so the
//                                    Agent calls TrimAfterTurnAsync automatically after
//                                    every turn. No manual TrimAsync call needed.
//   FileSessionManager             — persists the (already-trimmed) conversation to disk
//                                    so the session survives process restarts.
//
// SDK features shown:
//   • IAutoTrimConversationManager — opt-in interface that triggers automatic trimming
//   • SummarizingConversationManager — LLM-driven context compaction (now auto-wired)
//   • AutoTrimConversationManagerDecorator — wraps an existing SummarizingConversationManager
//                                            to add auto-trim without changing its type
//   • FileSessionManager + ExpiresAt — session with a 7-day TTL; expired sessions are
//                                      cleaned up automatically on next load
//
// Contrast with PersistentAssistant:
//   PersistentAssistant calls `await conversation.TrimAsync(...)` manually after every turn.
//   This sample passes SummarizingConversationManager directly to Agent — the Agent detects
//   IAutoTrimConversationManager and calls TrimAfterTurnAsync automatically. Zero boilerplate.
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run                 (start or resume your session)
//   dotnet run -- --reset      (delete saved session and start fresh)

const string Region    = "us-east-1";
const string ModelId   = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
const string SessionId = "auto-trim-session";

// Summarization threshold: low for demo purposes. In production use 40+.
const int SummarizationThreshold = 6;
const int KeepRecentMessages     = 3;

// Session TTL: 7 days. Expired sessions are cleaned up automatically on next load.
var sessionTtl = TimeSpan.FromDays(7);

// ── storage ────────────────────────────────────────────────────────────────────

var sessionsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".strands", "auto-trim-assistant");

var sessionManager = new FileSessionManager(sessionsDir);

// ── handle --reset flag ────────────────────────────────────────────────────────

if (args.Length > 0 && args[0] == "--reset")
{
    await sessionManager.DeleteAsync(SessionId);
    Console.WriteLine("Session reset. Starting fresh.");
    Console.WriteLine();
}

// ── load prior session ─────────────────────────────────────────────────────────

var existingSession = await sessionManager.LoadAsync(SessionId);
var isNewSession    = existingSession is null;

// ── model + conversation manager ──────────────────────────────────────────────

var model = new BedrockModel(region: Region, modelId: ModelId);

// SummarizingConversationManager implements IAutoTrimConversationManager directly.
// Passing it to Agent is all that's needed — no manual TrimAsync calls required.
var conversation = new SummarizingConversationManager(
    model,
    threshold:       SummarizationThreshold,
    keepRecentCount: KeepRecentMessages);

// Restore messages from the saved session.
if (existingSession is not null)
{
    foreach (var msg in existingSession.Messages)
        conversation.Append(msg);
}

// ── agent ──────────────────────────────────────────────────────────────────────

// The Agent detects that `conversation` implements IAutoTrimConversationManager
// and calls TrimAfterTurnAsync automatically after each InvokeAsync / StreamAsync.
// No manual trim wiring needed anywhere in this file.
var agent = new Agent(
    model,
    systemPrompt: """
        You are a helpful assistant with a good memory.
        You remember details the user shares because their conversation history is preserved.
        When the conversation grows long, older messages are automatically summarized in the
        background — you will see a summary message at the start of the history.
        Be warm, concise, and reference prior context naturally.
        """,
    conversationManager: conversation);

// Restore agent state (e.g. user name).
if (existingSession is not null)
{
    foreach (var (key, value) in existingSession.State)
    {
        if (value is string s)
            agent.State.Set(key, s);
        else if (value?.ToString() is string sv)
            agent.State.Set(key, sv);
    }
}

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  AutoTrim Assistant");
Console.ResetColor();
Console.WriteLine(new string('═', 60));

if (isNewSession)
{
    Console.WriteLine("  New session — no prior history found.");
}
else
{
    var userName = agent.State.Get<string>("user.name");
    var greeting = userName is not null ? $"  Welcome back, {userName}!" : "  Resuming your session.";
    Console.WriteLine(greeting);
    Console.WriteLine($"  Restored {existingSession!.Messages.Count} messages.");
    Console.WriteLine($"  Last active: {existingSession.LastUpdated:yyyy-MM-dd HH:mm} UTC");
}

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Auto-trim threshold: {SummarizationThreshold} messages (keeping {KeepRecentMessages} recent)");
Console.WriteLine($"  Session TTL: {sessionTtl.Days} days");
Console.WriteLine("  Type 'quit' or press Ctrl+C to exit. Run with --reset to clear history.");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.WriteLine();

// ── REPL ───────────────────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var turnCount = 0;

while (!cts.Token.IsCancellationRequested)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (input is null || input.Trim().ToLowerInvariant() is "quit" or "exit" or "q")
        break;

    if (string.IsNullOrWhiteSpace(input))
        continue;

    // Capture user name from natural language.
    if (agent.State.Get<string>("user.name") is null)
    {
        var lower = input.ToLowerInvariant();
        var nameIdx = lower.IndexOf("my name is ", StringComparison.Ordinal);
        if (nameIdx >= 0)
        {
            var name = input[(nameIdx + 11)..].Split(' ')[0].Trim(',', '.', '!');
            if (name.Length > 0)
                agent.State.Set("user.name", name);
        }
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Assistant: ");
    Console.ResetColor();

    var messagesBefore = conversation.GetMessages().Count;

    try
    {
        // StreamAsync — auto-trim fires automatically after AgentCompleteEvent.
        // No TrimAsync call needed here.
        await foreach (var evt in agent.StreamAsync(input, cts.Token).ConfigureAwait(false))
        {
            if (evt is TextDeltaEvent delta)
                Console.Write(delta.Delta);
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Console.WriteLine();
    Console.WriteLine();
    turnCount++;

    // Show trim indicator if the conversation was compacted.
    var messagesAfter = conversation.GetMessages().Count;
    if (messagesAfter < messagesBefore)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [auto-trim] Conversation compacted: " +
                          $"{messagesBefore} → {messagesAfter} messages");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ── save session with TTL ──────────────────────────────────────────────────

    var now = DateTimeOffset.UtcNow;
    var session = new AgentSession(
        SessionId:   SessionId,
        Messages:    conversation.GetMessages(),
        State:       agent.State.ToSnapshot(),
        LastUpdated: now,
        ExpiresAt:   now + sessionTtl);   // session expires in 7 days

    await sessionManager.SaveAsync(SessionId, session, cts.Token).ConfigureAwait(false);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  [session] Saved — {conversation.GetMessages().Count} messages " +
                      $"| turn {turnCount} | expires {session.ExpiresAt:yyyy-MM-dd}");
    Console.ResetColor();
    Console.WriteLine();
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"Session saved. {turnCount} turn(s) this session.");
Console.ResetColor();
