using Strands.Core;
using Strands.Models.Bedrock;
using Strands.Tools;

// ── Model selection ──────────────────────────────────────────────────────────
// Uncomment the model you want to use and comment out the others.

// Amazon Bedrock
var model = new BedrockModel(region: "us-east-1", modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Anthropic direct API
// var model = new AnthropicModel(apiKey: "sk-ant-...", modelId: "claude-haiku-4-5");

// OpenAI / any OpenAI-compatible endpoint
// var model = new OpenAICompatibleModel(baseUrl: "https://api.openai.com/v1", apiKey: "sk-...", modelId: "gpt-4o-mini");

// Google Gemini
//var model = new GeminiModel(apiKey: apiKey, modelId: "gemini-2.5-flash");

var agent = new Agent(
    model,
    systemPrompt: "You are a helpful assistant. Use the calculator tool when arithmetic is needed.",
    tools: [new CalculatorTool_Calculate_Tool(new CalculatorTool())]);

Console.WriteLine("Strands CLI Agent — type your message, 'exit' to quit");
Console.WriteLine();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.Token.IsCancellationRequested)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (input is null || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Goodbye.");
        break;
    }

    if (string.IsNullOrWhiteSpace(input)) continue;

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Agent: ");
    Console.ResetColor();

    try
    {
        await foreach (var evt in agent.StreamAsync(input, cts.Token))
        {
            if (evt is TextDeltaEvent delta)
                Console.Write(delta.Delta);
        }
    }
    catch (OperationCanceledException) { break; }

    Console.WriteLine();
    Console.WriteLine();
}
