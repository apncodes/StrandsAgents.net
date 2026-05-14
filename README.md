# Strands Agents .NET

> **The Strands Agents framework — built for .NET.**
> Model-driven agentic AI for C# developers, built on the same principles as [AWS Strands Agents](https://strandsagents.com).

[![NuGet](https://img.shields.io/nuget/v/StrandsAgents.Core?label=NuGet&color=blue)](https://www.nuget.org/packages/StrandsAgents.Core)
[![CI](https://github.com/apncodes/StrandsAgents.net/actions/workflows/ci.yml/badge.svg)](https://github.com/apncodes/StrandsAgents.net/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-green)](LICENSE)

---

## Quickstart

```bash
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package StrandsAgents.Tools
dotnet add package StrandsAgents.SourceGenerator
```

Decorate a method with `[Tool]` on a `partial class` — the Roslyn source generator emits a compile-time `ITool` wrapper and an `IToolProvider` implementation automatically:

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;

// Declare the class partial — the source generator implements IToolProvider automatically
public partial class MyTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new MyTools()]   // pass the class directly — no generated type names needed
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);
```

> Prerequisites: .NET 10 SDK, AWS credentials with Bedrock access enabled.

> **Backward compatibility:** The old explicit-wrapper form still compiles and runs unchanged.
> ```csharp
> tools: [new MyTools_GetWeather_Tool(new MyTools())]
> ```
> Non-`partial` classes emit a `STRAND001` warning and continue to work via the wrapper form.

---

## Model providers

Four providers are included out of the box — swap in one line:

```csharp
// Amazon Bedrock (cross-region inference profile)
var model = new BedrockModel(region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Anthropic direct API
var model = new AnthropicModel(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-5");

// OpenAI / Azure OpenAI / Ollama / any OpenAI-compatible endpoint
var model = new OpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: "sk-...",
    modelId: "gpt-4o");

// Google Gemini
var model = new GeminiModel(apiKey: "...", modelId: "gemini-2.5-flash");
```

---

## Streaming

```csharp
await foreach (var evt in agent.StreamAsync("Explain async/await in C#"))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}
```

---

## Structured output

```csharp
record WeatherReport(string City, int TempC, string Condition);

var report = await agent.GetStructuredOutputAsync<WeatherReport>(
    "What is the weather in Paris right now?");

Console.WriteLine($"{report.City}: {report.TempC}°C, {report.Condition}");
```

---

## DI integration (ASP.NET Core / Worker Service)

```bash
dotnet add package StrandsAgents.Extensions.DI
```

```csharp
builder.Services
    .AddBedrockModel(region: "us-east-1")
    .AddHttpRequestTool()
    .AddStrandsToolProvider<MyTools>()     // register a partial [Tool] class as IToolProvider
    .AddStrandsInMemorySessionManager()
    .AddStrandsAgent();

// Resolve IAgent from the container
var agent = app.Services.GetRequiredService<IAgent>();
```

---

## Why Strands.NET

.NET is the dominant runtime in enterprise — AWS Lambda, Windows services, ASP.NET APIs, and beyond. Strands Agents .NET brings the model-driven agentic approach to every .NET developer: the same event loop, tool system, and multi-agent patterns, built ground-up in idiomatic C# 13. No language bridges, no sidecars. The API uses the patterns .NET developers already know: generics instead of string tags, `IAsyncEnumerable` instead of async generators, `Task.WhenAll` for parallel execution.

---

## Features

- **Model-driven event loop** — the LLM decides which tools to call; the SDK executes them and loops until `EndTurn`
- **Tool system** — decorate any `partial` class method with `[Tool]`; the Roslyn source generator emits a compile-time `ITool` wrapper with zero runtime reflection
- **`IToolProvider` pattern** — pass your tool class directly to `Agent` via `toolProviders:`; no generated wrapper type names in user code; `STRAND001` warning guides non-partial classes
- **Streaming** — `StreamAsync` returns `IAsyncEnumerable<StreamEvent>` end to end with `[EnumeratorCancellation]` on every boundary
- **Hook system** — type-safe `Register<TEvent>` callbacks for `BeforeToolCall`, `AfterToolCall`, `BeforeModelCall`, `AfterModelCall`
- **Human-in-the-loop** — set `e.Interrupt = true` in any `BeforeToolCallEvent` hook to pause before sensitive actions
- **Structured output** — `GetStructuredOutputAsync<T>()` extracts typed records with automatic JSON retry
- **Session management** — `InMemorySessionManager` or `FileSessionManager`; bring your own via `ISessionManager`
- **Context window trimming** — `SlidingWindowStrategy` or `SummarizingConversationManager` for long-running agents
- **OpenTelemetry** — `ActivitySource` named `"StrandsAgents.Agent"` emits traces and metrics with zero config
- **DI integration** — `AddBedrockModel()`, `AddAnthropicModel()`, `AddOpenAICompatibleModel()`, `AddGeminiModel()`, `AddStrandsAgent()`, `AddStrandsToolProvider<T>()` for native ASP.NET Core / Worker Service wiring
- **Multi-agent graph** — `GraphBuilder` with conditional routing; `PipelineOrchestrator`; `ParallelOrchestrator`
- **Agent as tool** — wrap any `IAgent` as an `ITool` with `agent.AsTool()` for hierarchical orchestration
- **MCP** — connect any Model Context Protocol server (stdio or SSE) via `McpToolProvider`
- **A2A protocol** — expose agents over HTTP with `MapA2AEndpoint`; call remote agents with `A2AAgent` (cross-framework, cross-language)
- **AgentCore Runtime** *(optional)* — `MapAgentCoreEndpoints()` deploys any agent to Amazon Bedrock AgentCore Runtime in one line; `UseAgentCorePort(8080)` binds the required port
- **AgentCore Memory** *(optional)* — `AgentCoreMemoryTool` / `AddAgentCoreMemory()` gives the agent explicit store/retrieve/delete access to Amazon Bedrock AgentCore Memory; `AddAgentCoreSessionManager()` persists conversation sessions to the same store
- **AgentCore Code Interpreter** *(optional)* — `AgentCoreCodeInterpreterTool` / `AddAgentCoreCodeInterpreter()` executes Python, JavaScript, or TypeScript in a managed, stateful sandbox; session is created lazily and reused across calls
- **AgentCore Browser** *(optional)* — `AgentCoreBrowserTool` / `AddAgentCoreBrowser()` manages a headless Chrome session; returns the CDP `automationStreamEndpoint` for Playwright or Nova Act automation
- **AgentCore Gateway** *(optional)* — `AgentCoreGatewayToolProvider` / `AddAgentCoreGatewayTools()` connects to an Amazon Bedrock AgentCore Gateway MCP endpoint and exposes its tools as `ITool` instances; supports IAM SigV4, JWT Bearer, and network-isolated (no-auth) modes

---

## Why .NET gets its own implementation

These aren't translations — they're the patterns .NET developers already know, applied to agentic AI.

| Capability | Strands Agents .NET |
|---|---|
| Type safety | Compile-time generics |
| Streaming | `IAsyncEnumerable<T>` |
| Hook registration | `Register<TEvent>` — compiler-checked |
| Tool schema | Roslyn source generator at compile time |
| Tool registration | `toolProviders: [new MyTools()]` — no generated type names |
| Parallel execution | `Task.WhenAll` |
| DI integration | `AddBedrockModel()` + `AddStrandsAgent()` + `AddStrandsToolProvider<T>()` |
| Enterprise hosting | `IHostedService` / AWS Lambda / any host |
| Model providers | Bedrock, Anthropic, OpenAI-compatible, Gemini |
| MCP | ✓ |
| A2A protocol | ✓ (interoperable across languages and frameworks) |
| Graph orchestration | ✓ with parallel-node support |

---

## Multi-agent patterns

### Sequential pipeline

```csharp
var pipeline = new PipelineOrchestrator([researchAgent, writerAgent, reviewerAgent]);
var result = await pipeline.InvokeAsync("Write a report on quantum computing");
```

### Parallel fan-out

```csharp
var results = await new ParallelOrchestrator([techAgent, marketAgent, riskAgent])
    .RunAsync("Analyse this topic from your specialist perspective");
// All three run concurrently via Task.WhenAll
```

### Graph with conditional routing

```csharp
var graph = new GraphBuilder()
    .AddNode("triage",    triageAgent)
    .AddNode("billing",   billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", r =>
        r.Message.Contains("billing") ? "billing" : "technical")
    .Build();
```

### Agent as tool

```csharp
var researchTool = researchAgent.AsTool("researcher", "Research a topic and return a summary");
var writerAgent  = new Agent(model, tools: [researchTool]);
```

---

## AgentCore Gateway (optional)

Connect your agent to tools hosted on an Amazon Bedrock AgentCore Gateway — a managed MCP endpoint that proxies external APIs, databases, and services with built-in auth and observability.

```bash
dotnet add package StrandsAgents.Runtime
```

```csharp
// Direct usage — connect and list tools
await using var gateway = await AgentCoreGatewayToolProvider.CreateAsync(
    gatewayUrl: new Uri("https://...gateway-url.../mcp"),
    auth: new AgentCoreGatewayAuth.Iam(region: "us-east-1"));

var tools = await gateway.ListToolsAsync();
var agent = new Agent(model, tools: tools);
```

Three auth modes match your gateway's inbound authorization setting:

```csharp
// IAM SigV4 — credentials resolved from the standard AWS chain
new AgentCoreGatewayAuth.Iam(region: "us-east-1")

// JWT Bearer — Cognito, Entra ID, Okta, Google, GitHub, etc.
new AgentCoreGatewayAuth.Bearer(accessToken: token)

// No auth — network-isolated (VPC / security groups)
new AgentCoreGatewayAuth.None()
```

With DI, `AddAgentCoreGatewayTools()` registers all gateway tools directly into the container — `AddStrandsAgent()` picks them up automatically:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Iam("us-east-1"))
    .AddStrandsAgent();
```

---

## AgentCore Runtime deployment (optional)

Deploy any Strands.NET agent to Amazon Bedrock AgentCore Runtime with one line. Your agent code is unchanged.

```bash
dotnet add package StrandsAgents.Runtime
```

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddStrandsAgent();

var app = builder.Build();
app.MapAgentCoreEndpoints();  // POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime expects port 8080
app.Run();
```

Optionally wire in managed AgentCore services before building the app:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSessionManager(memoryId)      // persist sessions to AgentCore Memory
    .AddAgentCoreMemory(memoryId)              // give the agent explicit memory store/retrieve/delete
    .AddAgentCoreCodeInterpreter()             // sandboxed Python / JS / TS execution
    .AddAgentCoreBrowser()                     // managed headless Chrome (CDP endpoint)
    .AddStrandsAgent();
```

---

## Packages

| Package | Description |
|---|---|
| `StrandsAgents.Core` | Agent, event loop, tool system, hooks, session management, Gemini/Anthropic/OpenAI models |
| `StrandsAgents.Models.Bedrock` | Amazon Bedrock model provider (Converse API) |
| `StrandsAgents.Tools` | Built-in tools: calculator, file read/write, HTTP request |
| `StrandsAgents.SourceGenerator` | Roslyn source generator — emits `ITool` wrappers and `IToolProvider` from `[Tool]` attributes |
| `StrandsAgents.Extensions.DI` | ASP.NET Core / Worker Service DI extensions |
| `StrandsAgents.MultiAgent` | Pipeline, parallel, and graph orchestration; A2A protocol |
| `StrandsAgents.Runtime` | Amazon Bedrock AgentCore Runtime hosting; managed Memory, Code Interpreter, Browser, and Gateway tools |

---

## Samples

| Sample | What it shows |
|---|---|
| [CliAgent](samples/CliAgent/) | Multi-turn streaming REPL — the minimal working agent |
| [AspNetAgent](samples/AspNetAgent/) | `/chat` endpoint with session continuity and SSE streaming |
| [DiAgent](samples/DiAgent/) | Full DI wiring with file tools and session management |
| [MultiAgentPipeline](samples/MultiAgentPipeline/) | Sequential pipeline + parallel fan-out with timestamps |
| [OrchestratedResearch](samples/OrchestratedResearch/) | All three orchestration patterns side by side |
| [SupportTriage](samples/SupportTriage/) | Graph routing, hooks, and structured output extraction |
| [CustomerServiceApi](samples/CustomerServiceApi/) | Production-shaped REST API with session persistence |
| [FinanceAssistant](samples/FinanceAssistant/) | 4-agent parallel swarm with typed report extraction |
| [PersistentAssistant](samples/PersistentAssistant/) | Cross-run memory with automatic summarization |
| [DistributedAgents](samples/DistributedAgents/) | A2A cross-process agent communication |
| [ChatUI](samples/ChatUI/) | Browser chat UI with SSE streaming and tool badges |
| [BlazorResearch](samples/BlazorResearch/) | Blazor Server portal with live parallel agent cards |
| [AgentCoreSample](samples/AgentCoreSample/) | Deploy any agent to AgentCore Runtime — `MapAgentCoreEndpoints()` in one line |
| [AgentCoreGatewaySample](samples/AgentCoreGatewaySample/) | Travel booking assistant using gateway-hosted flight and hotel search tools via `AddAgentCoreGatewayTools()` |

---

## About Strands Agents

Strands Agents is an open source SDK that takes a model-driven approach to building AI agents — the model drives its own behavior, decides which tools to call, and loops until the task is complete. This approach emerged from real-world production experience building agents at AWS.

Strands Agents .NET is a ground-up implementation of those design principles for the .NET ecosystem. It is not a port or wrapper — it is built natively in C# 13, using the patterns and idioms .NET developers already know. The core concepts (event loop, tool system, hooks, multi-agent orchestration) follow the Strands design, and the A2A protocol implementation is interoperable across languages and frameworks.

Learn more about the Strands Agents design principles at [strandsagents.com](https://strandsagents.com).

This project is not affiliated with or endorsed by AWS.

---

## Contributing

PRs, issues, and feedback are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. The biggest areas of need are additional model providers (Ollama), more built-in tools, and real-world samples.

---

## License

Apache 2.0. See [LICENSE](LICENSE).
