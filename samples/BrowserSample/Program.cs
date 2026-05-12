using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Runtime.Tools;

// BrowserSample — demonstrates AgentCoreBrowserTool.
//
// Architecture:
//   AgentCoreBrowserTool — manages Amazon Bedrock AgentCore Browser sessions.
//                          The tool exposes session lifecycle operations and surfaces
//                          the automationStreamEndpoint URL for Playwright/Nova Act.
//
// How AgentCore Browser works:
//   1. start_session  → creates a managed headless Chrome instance in AWS
//                       returns sessionId + automationStreamEndpoint (WebSocket URL)
//   2. Connect to the automationStreamEndpoint via Playwright (connect_over_cdp)
//      or Nova Act to perform actual browser automation (navigate, click, screenshot)
//   3. stop_session   → releases the browser instance
//
// SDK features shown:
//   • AgentCoreBrowserTool — session lifecycle via AWSSDK.BedrockAgentCore SDK client
//   • start_session / get_session / stop_session operations
//   • automationStreamEndpoint — the WebSocket URL for Playwright/Nova Act connection
//   • ToolCallStartEvent / ToolCallResultEvent — shows tool execution in the REPL
//
// Prerequisites:
//   • AWS credentials configured (env vars, ~/.aws/credentials, or IAM role)
//   • An AgentCore Browser resource in your AWS account
//   • Set AGENTCORE_BROWSER_ID environment variable (or uses "default")
//
// Usage:
//   dotnet run --project samples/BrowserSample

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

var browserId = Environment.GetEnvironmentVariable("AGENTCORE_BROWSER_ID");

// ── tool + agent ───────────────────────────────────────────────────────────────

using var browserTool = new AgentCoreBrowserTool(
    browserIdentifier: browserId,
    region: Region);

var model = new BedrockModel(region: Region, modelId: ModelId);

var agent = new Agent(
    model,
    systemPrompt: """
        You are a web research assistant with access to a managed browser session.

        The browser tool manages session lifecycle:
        - Use start_session to create a browser session. The response includes a
          sessionId and an automationStreamEndpoint WebSocket URL.
        - The automationStreamEndpoint is used by Playwright (connect_over_cdp) or
          Nova Act to perform actual browser automation (navigate, click, screenshot).
        - Use get_session to check the status of an existing session.
        - Use stop_session to release a session when done.

        When the user asks you to browse a website or perform web research:
        1. Start a browser session and report the sessionId and automationStreamEndpoint.
        2. Explain that the caller should connect to the endpoint via Playwright or Nova Act
           to perform the actual navigation and interaction.
        3. Offer to stop the session when the user is done.

        Be clear about what the tool does (session management) vs what requires
        Playwright/Nova Act (actual browser automation).
        """,
    tools: [browserTool]);

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  AgentCore Browser Sample");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Managed headless Chrome via Amazon Bedrock AgentCore.");
Console.WriteLine("  This tool manages sessions and surfaces the WebSocket");
Console.WriteLine("  endpoint for Playwright (connect_over_cdp) or Nova Act.");
Console.WriteLine();
Console.WriteLine("  Try asking:");
Console.WriteLine("    'Start a browser session'");
Console.WriteLine("    'What is the status of session <id>?'");
Console.WriteLine("    'Stop session <id>'");
Console.ResetColor();
Console.WriteLine(new string('─', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Region: {Region}");
Console.WriteLine($"  Browser ID: {browserId ?? "default"}");
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
                    Console.Write($"  [{toolStart.ToolName}]...");
                    Console.ResetColor();
                    break;

                case ToolCallResultEvent toolResult:
                    Console.ForegroundColor = toolResult.Result.IsError
                        ? ConsoleColor.Red
                        : ConsoleColor.DarkGray;
                    var preview = toolResult.Result.Content.Length > 200
                        ? toolResult.Result.Content[..200] + "..."
                        : toolResult.Result.Content;
                    Console.WriteLine($"\n  {preview}");
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
