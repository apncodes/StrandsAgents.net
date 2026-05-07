using System.Collections.Concurrent;
using System.Text.Json;
using Strands.AgentCore;
using Strands.AgentCore.Extensions;
using Strands.Core;
using Strands.Extensions.DI;

var gatewayUrl = new Uri(
    Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")
    ?? throw new InvalidOperationException(
        "Set the AGENTCORE_GATEWAY_URL environment variable to your AgentCore Gateway MCP endpoint. " +
        "See README.md for setup instructions."));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");

builder.Services
    .AddBedrockModel(region: "us-east-1", modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0")
    .AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Iam("us-east-1"));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// In-process session store: sessionId → conversation manager
// Each browser tab gets its own persistent conversation history.
var sessions = new ConcurrentDictionary<string, InMemoryConversationManager>();

// POST /chat — streams agent response as SSE
app.MapPost("/chat", async (HttpContext ctx, IModel model, IEnumerable<ITool> tools) =>
{
    var body = await JsonDocument.ParseAsync(ctx.Request.Body);
    var message   = body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    var sessionId = body.RootElement.TryGetProperty("sessionId", out var sid)
        ? sid.GetString() ?? Guid.NewGuid().ToString()
        : Guid.NewGuid().ToString();

    // Reuse or create the conversation manager for this session
    var conversation = sessions.GetOrAdd(sessionId, _ => new InMemoryConversationManager());

    var today = DateTime.UtcNow.ToString("dddd, MMMM d, yyyy");
    var systemPrompt = $"""
        You are a helpful travel booking assistant. Today's date is {today}.
        You can search for flights and hotels using the available tools.

        Date handling rules — follow these strictly:
        - Accept dates in ANY format the user provides: natural language ("next Friday",
          "this weekend", "in two weeks"), partial dates ("June 15", "the 20th"),
          or any written format ("May 15th", "15/06", "June 15 2026").
        - NEVER ask the user to reformat a date or provide it in YYYY-MM-DD format.
        - Convert whatever the user says into the correct date yourself using today's date
          as the reference point, then call the tool immediately.
        - If a year is not specified, assume the nearest future occurrence.

        Empty results strategy — follow this automatically, without asking the user:
        - If a flight search returns no results (count: 0), immediately search the 3 days
          before AND 3 days after the requested date (up to 6 additional searches).
          Present all results together, clearly labelled by date.
        - If all nearby dates also return no results, the route likely doesn't exist in
          the system. Tell the user clearly: "No flights found for [route] in the system.
          The available routes may be limited." Do NOT keep retrying the same route.
        - If a hotel search returns no results, try the same city with ±2 days on the
          check-in date before reporting no availability.
        - Never treat an empty result as a system error — it just means no inventory
          for that specific date or route.

        General rules:
        - Call search tools proactively as soon as you have enough information.
        - Only ask a follow-up question if genuinely critical information is missing
          (e.g. the origin city for a flight).
        - When a tool returns an error, report the raw error — do not invent a message.
        - Present results in a clear, friendly format with prices and schedules.
        """;

    // Reuse the same agent instance shape but with the persistent conversation manager
    var agent = new Agent(model, systemPrompt: systemPrompt, tools: tools,
        conversationManager: conversation);

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var ct = ctx.RequestAborted;

    static async Task WriteEvent(HttpResponse r, string type, string data, CancellationToken ct)
    {
        var line = $"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n";
        await r.WriteAsync(line, ct);
        await r.Body.FlushAsync(ct);
    }

    try
    {
        await foreach (var evt in agent.StreamAsync(message, ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case TextDeltaEvent delta:
                    await WriteEvent(ctx.Response, "delta", delta.Delta, ct);
                    break;
                case ToolCallStartEvent tool:
                    await WriteEvent(ctx.Response, "tool_start", tool.ToolName, ct);
                    break;
                case ToolCallResultEvent:
                    await WriteEvent(ctx.Response, "tool_done", "", ct);
                    break;
                case AgentCompleteEvent:
                    await WriteEvent(ctx.Response, "done", "", ct);
                    break;
            }
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

Console.WriteLine("Travel Booking Assistant → http://localhost:5050");
app.Run();
