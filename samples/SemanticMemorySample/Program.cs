using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Runtime.Tools;

// SemanticMemorySample — demonstrates semantic (vector) memory retrieval via SemanticMemoryTool.
//
// Architecture:
//   SemanticMemoryTool  — exposes search_memory / store_memory / delete_memory to the LLM.
//                         search_memory retrieves memories by meaning, not exact key.
//   AgentCoreMemoryTool — key-value memory for comparison (exact-key retrieval).
//
// SDK features shown:
//   • SemanticMemoryTool — new tool backed by AgentCore Memory semantic search API.
//                          The LLM can call search_memory("user preferences") and get
//                          back the closest matches ranked by cosine similarity score.
//   • ttl_seconds        — store_memory accepts an optional TTL so sensitive facts
//                          expire automatically.
//   • SigV4 auth         — SemanticMemoryTool signs every HTTP request automatically
//                          using credentials from the standard AWS credential chain.
//
// Prerequisites:
//   • AWS credentials configured (env vars, ~/.aws/credentials, or IAM role)
//   • An AgentCore Memory resource created in your AWS account
//   • Set AGENTCORE_MEMORY_ID environment variable to your memory resource ID
//
// Usage:
//   AGENTCORE_MEMORY_ID=mem-abc123 dotnet run
//
// The agent will:
//   1. Store a few sample memories on startup (user preferences, facts)
//   2. Enter a REPL where you can ask questions — the agent uses search_memory
//      to find relevant context before answering

var memoryId = Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID")
    ?? throw new InvalidOperationException(
        "Set the AGENTCORE_MEMORY_ID environment variable to your AgentCore Memory resource ID.");

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

// ── tools ──────────────────────────────────────────────────────────────────────

// SemanticMemoryTool — SigV4-signed automatically; no clientOverride needed in production.
await using var semanticMemory = new SemanticMemoryTool(memoryId, region: Region);

// ── agent ──────────────────────────────────────────────────────────────────────

var model = new BedrockModel(region: Region, modelId: ModelId);

var agent = new Agent(
    model,
    systemPrompt: """
        You are a helpful personal assistant with access to a semantic memory store.

        Before answering questions about the user, always call search_memory with a
        natural-language description of what you are looking for. The tool returns
        memories ranked by relevance — use the top results to inform your answer.

        When the user shares new facts about themselves, store them with store_memory.
        For sensitive or temporary facts (e.g. one-time codes, temporary preferences),
        use ttl_seconds to set an appropriate expiry.

        Be warm, concise, and reference stored memories naturally in your responses.
        """,
    tools: [semanticMemory]);

// ── seed some memories ─────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  Semantic Memory Sample");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Seeding sample memories...");
Console.ResetColor();

// Seed a few memories so the demo has something to search.
// In a real app the agent would store these itself via store_memory.
var seedPrompt = """
    Please store the following facts about the user using store_memory:
    1. key="user.name", value="Alex"
    2. key="user.preference.coffee", value="Prefers oat milk flat white, no sugar"
    3. key="user.preference.language", value="Prefers concise answers, no bullet points"
    4. key="user.project", value="Working on a distributed agent system using Strands Agents .NET"
    5. key="user.timezone", value="Europe/London"

    Store each one separately. Confirm when done.
    """;

var seedResult = await agent.InvokeAsync(seedPrompt);
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Seed: {seedResult.Message[..Math.Min(80, seedResult.Message.Length)]}...");
Console.ResetColor();
Console.WriteLine();

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('─', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Memory ID: " + memoryId);
Console.WriteLine("  Try asking: 'What do you know about me?'");
Console.WriteLine("              'What coffee do I like?'");
Console.WriteLine("              'What project am I working on?'");
Console.WriteLine("  Type 'quit' or press Ctrl+C to exit.");
Console.ResetColor();
Console.WriteLine(new string('─', 60));
Console.WriteLine();

// ── REPL ───────────────────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

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

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Assistant: ");
    Console.ResetColor();

    try
    {
        await foreach (var evt in agent.StreamAsync(input, cts.Token).ConfigureAwait(false))
        {
            switch (evt)
            {
                case TextDeltaEvent delta:
                    Console.Write(delta.Delta);
                    break;

                case ToolCallStartEvent toolStart:
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"  [tool] {toolStart.ToolName}...");
                    Console.ResetColor();
                    break;

                case ToolCallResultEvent toolResult:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    var preview = toolResult.Result.Content.Length > 60
                        ? toolResult.Result.Content[..60] + "..."
                        : toolResult.Result.Content;
                    Console.WriteLine($" → {preview}");
                    Console.ResetColor();
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("Goodbye.");
Console.ResetColor();
