using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Runtime.Tools;

// CodeInterpreterSample — demonstrates AgentCoreCodeInterpreterTool.
//
// Architecture:
//   AgentCoreCodeInterpreterTool — managed code execution sandbox backed by
//                                  Amazon Bedrock AgentCore Code Interpreter.
//                                  A session is created on first use and reused
//                                  across turns, so variables persist between calls.
//
// SDK features shown:
//   • AgentCoreCodeInterpreterTool — session lifecycle (start on first use, stop on dispose)
//   • Stateful session — variables and imports persist across calls within the same session
//   • clear_context parameter — resets the session state when needed
//   • Supported languages: python, javascript, typescript
//   • ToolCallStartEvent / ToolCallResultEvent — shows tool execution in the REPL
//
// Prerequisites:
//   • AWS credentials configured (env vars, ~/.aws/credentials, or IAM role)
//   • An AgentCore Code Interpreter resource in your AWS account
//   • Set AGENTCORE_CODE_INTERPRETER_ID environment variable (or uses "default")
//
// Usage:
//   dotnet run --project samples/CodeInterpreterSample

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

var codeInterpreterId = Environment.GetEnvironmentVariable("AGENTCORE_CODE_INTERPRETER_ID");

// ── tool + agent ───────────────────────────────────────────────────────────────

// The tool manages its own session — no setup needed.
// Session is created lazily on first InvokeAsync call and reused across turns.
await using var codeInterpreter = new AgentCoreCodeInterpreterTool(
    codeInterpreterIdentifier: codeInterpreterId,
    region: Region);

var model = new BedrockModel(region: Region, modelId: ModelId);

var agent = new Agent(
    model,
    systemPrompt: """
        You are a helpful data analysis and programming assistant with access to a
        managed code execution sandbox.

        When asked to perform calculations, data analysis, or generate visualizations,
        write and execute code rather than computing manually. The sandbox is stateful —
        variables and imports persist across calls, so you can build on previous results.

        Supported languages: python, javascript, typescript.

        Always show the code you are running and explain the output.
        If execution fails, diagnose the error and retry with corrected code.
        """,
    tools: [codeInterpreter]);

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  AgentCore Code Interpreter Sample");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Managed Python/JavaScript/TypeScript execution sandbox.");
Console.WriteLine("  Session is stateful — variables persist across turns.");
Console.WriteLine();
Console.WriteLine("  Try asking:");
Console.WriteLine("    'Calculate the first 20 Fibonacci numbers in Python'");
Console.WriteLine("    'Now plot them as a bar chart'");
Console.WriteLine("    'What is the sum of all even Fibonacci numbers below 1000?'");
Console.WriteLine("    'Rewrite the Fibonacci function in JavaScript'");
Console.ResetColor();
Console.WriteLine(new string('─', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Region: {Region}");
Console.WriteLine($"  Code Interpreter ID: {codeInterpreterId ?? "default"}");
Console.WriteLine("  Type 'quit' or press Ctrl+C to exit.");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
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
                    Console.Write($"  [executing code]...");
                    Console.ResetColor();
                    break;

                case ToolCallResultEvent toolResult:
                    Console.ForegroundColor = toolResult.Result.IsError
                        ? ConsoleColor.Red
                        : ConsoleColor.DarkGray;
                    // Show first 120 chars of the result inline
                    var preview = toolResult.Result.Content.Length > 120
                        ? toolResult.Result.Content[..120].Replace('\n', ' ') + "..."
                        : toolResult.Result.Content.Replace('\n', ' ');
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
Console.WriteLine("Goodbye. Code interpreter session will be stopped.");
Console.ResetColor();
// AgentCoreCodeInterpreterTool.DisposeAsync() stops the session automatically.
