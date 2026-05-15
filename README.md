# Strands Agents .NET

> **The Strands Agents framework — built for .NET.** Model-driven agentic AI for C# developers, built on the same principles as [AWS Strands Agents](https://strandsagents.com).

[![NuGet](https://img.shields.io/nuget/v/StrandsAgents.Core?label=NuGet&color=blue)](https://www.nuget.org/packages/StrandsAgents.Core) [![CI](https://github.com/apncodes/StrandsAgents.net/actions/workflows/ci.yml/badge.svg)](https://github.com/apncodes/StrandsAgents.net/actions/workflows/ci.yml) [![License](https://img.shields.io/badge/license-Apache--2.0-green)](https://github.com/apncodes/StrandsAgents.net/blob/main/LICENSE)

**Jump to:** [Quickstart](#quickstart) · [Why StrandsAgents.NET](#why-strandsagentsnet) · [Production essentials](#production-essentials) · [AWS-native deployment](#aws-native-deployment) · [Multi-agent](#multi-agent-patterns) · [Samples](#samples)

## At a glance

- **Zero runtime reflection** — compile-time tool dispatch via Roslyn source generators
- **NativeAOT-ready** — reflection-free hot path, suitable for AOT-published Lambda binaries
- **Idiomatic .NET** — `IAsyncEnumerable<T>`, generics, DI, OpenTelemetry, `[LoggerMessage]`
- **AWS-native** — Bedrock + AgentCore Runtime, Memory, Code Interpreter, Browser, Gateway
- **Multi-agent in one package** — pipeline, parallel, graph, A2A protocol

---

## Quickstart

Decorate a method with `[Tool]` on a `partial class` — the Roslyn source generator emits a compile-time `ITool` wrapper and an `IToolProvider` implementation automatically.

```bash
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package StrandsAgents.Tools
dotnet add package StrandsAgents.SourceGenerator
```

> **SourceGenerator 0.1.8.1+** is required for the `toolProviders:` pattern. If you're on 0.1.8, upgrade: `dotnet add package StrandsAgents.SourceGenerator --version 0.1.8.1`

**Single-file option** — put the class declaration after the top-level statements:

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using MyApp;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

// Type declarations must come after top-level statements in the same file.
// Use a block-body namespace (not file-scoped) when mixing with top-level statements.
namespace MyApp
{
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city")]
        public string GetWeather(string city) => $"Sunny, 22°C in {city}";
    }
}
```

**Two-file option** — cleaner for larger projects:

**WeatherTools.cs**

```csharp
using StrandsAgents.Core;

namespace MyApp;

public partial class WeatherTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}
```

**Program.cs**

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using MyApp;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);
```

> **`toolProviders:` vs `tools:`** — use `toolProviders:` when passing your `[Tool]`-decorated classes (the common case). Use `tools:` when you have pre-built `ITool` instances, such as from `agent.AsTool()` or `AgentCoreGatewayToolProvider`.

> The namespace on the `partial class` is required. The source generator emits its `IToolProvider` implementation in the same namespace, and C# merges the two partial declarations into one type. Without a matching namespace they are treated as separate types and the build fails.

> **Prerequisites:** .NET 10 SDK, AWS credentials with Bedrock access enabled.

---

## Why StrandsAgents.NET

.NET is the dominant runtime in enterprise — AWS Lambda, Windows services, ASP.NET APIs, and beyond. StrandsAgents.NET brings the model-driven agentic approach to every .NET developer: the same event loop, tool system, and multi-agent patterns, built ground-up in idiomatic C# 13. No language bridges, no sidecars.

| Capability | StrandsAgents.NET |
| --- | --- |
| Type safety | Compile-time generics |
| Streaming | `IAsyncEnumerable<T>` |
| Hook registration | `Register<TEvent>` — compiler-checked |
| Tool schema | Roslyn source generator at compile time |
| Tool registration | `toolProviders: [new WeatherTools()]` — no generated type names |
| Parallel execution | `Task.WhenAll` |
| DI integration | `AddBedrockModel()` + `AddStrandsAgent()` + `AddStrandsToolProvider<T>()` |
| Enterprise hosting | `IHostedService` / AWS Lambda / any host |
| Model providers | Bedrock, Anthropic, OpenAI-compatible, Gemini |
| MCP | ✓ |
| A2A protocol | ✓ (interoperable across languages and frameworks) |
| Graph orchestration | ✓ with parallel-node support |

---

## Production essentials

### Streaming

```csharp
await foreach (var evt in agent.StreamAsync("Explain async/await in C#"))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}
```

### Structured output

```csharp
record WeatherReport(string City, int TempC, string Condition);

var report = await agent.GetStructuredOutputAsync<WeatherReport>(
    "What is the weather in Paris right now?");

Console.WriteLine($"{report.City}: {report.TempC}°C, {report.Condition}");
```

### DI integration (ASP.NET Core / Worker Service)

```bash
dotnet add package StrandsAgents.Extensions.DI
```

```csharp
builder.Services
    .AddBedrockModel(region: "us-east-1")
    .AddHttpRequestTool()
    .AddStrandsToolProvider<WeatherTools>()     // register a partial [Tool] class as IToolProvider
    .AddStrandsInMemorySessionManager()
    .AddStrandsAgent();

// Resolve IAgent from the container
var agent = app.Services.GetRequiredService<IAgent>();
```

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

## AWS-native deployment

### AgentCore Runtime

Deploy any Strands Agents .NET agent to Amazon Bedrock AgentCore Runtime with one line. Your agent code is unchanged.

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

### AgentCore Gateway

Connect your agent to tools hosted on an Amazon Bedrock AgentCore Gateway — a managed MCP endpoint that proxies external APIs, databases, and services with built-in auth and observability.

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

## Features

### Core

- **Model-driven event loop** — the LLM decides which tools to call; the SDK executes them and loops until `EndTurn`
- **Tool system** — decorate any `partial` class method with `[Tool]`; Roslyn source generator emits a compile-time `ITool` wrapper with zero runtime reflection
- **`IToolProvider` pattern** — pass your tool class directly to `Agent` via `toolProviders:`; no generated wrapper type names in user code; `STRAND001` warning guides non-partial classes
- **Hook system** — type-safe `Register<TEvent>` callbacks for `BeforeToolCall`, `AfterToolCall`, `BeforeModelCall`, `AfterModelCall`
- **Human-in-the-loop** — set `e.Interrupt = true` in any `BeforeToolCallEvent` hook to pause before sensitive actions
- **Streaming** — `StreamAsync` returns `IAsyncEnumerable<StreamEvent>` end to end with `[EnumeratorCancellation]` on every boundary
- **Structured output** — `GetStructuredOutputAsync<T>()` extracts typed records with automatic JSON retry
- **Session management** — `InMemorySessionManager` or `FileSessionManager`; bring your own via `ISessionManager`
- **Context window trimming** — `SlidingWindowStrategy` or `SummarizingConversationManager` for long-running agents

### Production

- **DI integration** — `AddBedrockModel()`, `AddAnthropicModel()`, `AddOpenAICompatibleModel()`, `AddGeminiModel()`, `AddStrandsAgent()`, `AddStrandsToolProvider<T>()` for native ASP.NET Core / Worker Service wiring
- **OpenTelemetry** — `ActivitySource` named `"StrandsAgents.Agent"` emits traces and metrics with zero config

### Multi-agent

- **Multi-agent graph** — `GraphBuilder` with conditional routing; `PipelineOrchestrator`; `ParallelOrchestrator`
- **Agent as tool** — wrap any `IAgent` as an `ITool` with `agent.AsTool()` for hierarchical orchestration
- **MCP** — connect any Model Context Protocol server (stdio or SSE) via `McpToolProvider`
- **A2A protocol** — expose agents over HTTP with `MapA2AEndpoint`; call remote agents with `A2AAgent` (cross-framework, cross-language)

### AWS-native (optional)

- **AgentCore Runtime** — `MapAgentCoreEndpoints()` deploys any agent to Amazon Bedrock AgentCore Runtime in one line
- **AgentCore Memory** — `AgentCoreMemoryTool` / `AddAgentCoreMemory()` gives the agent explicit store/retrieve/delete access; `AddAgentCoreSessionManager()` persists conversation sessions to the same store
- **AgentCore Code Interpreter** — `AgentCoreCodeInterpreterTool` / `AddAgentCoreCodeInterpreter()` executes Python, JavaScript, or TypeScript in a managed, stateful sandbox
- **AgentCore Browser** — `AgentCoreBrowserTool` / `AddAgentCoreBrowser()` manages a headless Chrome session; returns the CDP `automationStreamEndpoint` for Playwright or Nova Act automation
- **AgentCore Gateway** — `AgentCoreGatewayToolProvider` / `AddAgentCoreGatewayTools()` connects to an Amazon Bedrock AgentCore Gateway MCP endpoint; supports IAM SigV4, JWT Bearer, and no-auth modes

---

## Roadmap

**Stable in v0.1** — core agent loop, tool system, `IToolProvider` pattern, Bedrock / Anthropic / OpenAI-compatible / Gemini model providers, DI integration, OpenTelemetry, session management, A2A protocol.

**Evolving** — multi-agent orchestration API may see refinements before v1.0.

**Coming next** — Ollama model provider, additional built-in tools, expanded multi-agent patterns, Step Functions durability sample.

---

## Packages

| Package | Description |
| --- | --- |
| `StrandsAgents.Core` | Agent, event loop, tool system, hooks, session management; Gemini / Anthropic / OpenAI model providers |
| `StrandsAgents.Models.Bedrock` | Amazon Bedrock model provider (Converse API) |
| `StrandsAgents.Tools` | Built-in tools: calculator, file read/write, HTTP request |
| `StrandsAgents.SourceGenerator` | Roslyn source generator — emits `ITool` wrappers and `IToolProvider` implementations from `[Tool]` attributes |
| `StrandsAgents.Extensions.DI` | ASP.NET Core / Worker Service DI extensions |
| `StrandsAgents.MultiAgent` | Pipeline, parallel, and graph orchestration; A2A protocol |
| `StrandsAgents.Runtime` | Amazon Bedrock AgentCore Runtime hosting; managed Memory, Code Interpreter, Browser, and Gateway tools |

---

## Samples

| Sample | What it shows |
| --- | --- |
| [CliAgent](samples/CliAgent) | Multi-turn streaming REPL — the minimal working agent |
| [AspNetAgent](samples/AspNetAgent) | `/chat` endpoint with session continuity and SSE streaming |
| [DiAgent](samples/DiAgent) | Full DI wiring with file tools and session management |
| [FileAgent](samples/FileAgent) | `FileReadTool` / `FileWriteTool` + `SlidingWindowStrategy` context trimming |
| [AutoTrimAssistant](samples/AutoTrimAssistant) | Zero-boilerplate auto-trim via `IAutoTrimConversationManager`; file session with TTL |
| [MultiAgentPipeline](samples/MultiAgentPipeline) | Sequential pipeline + parallel fan-out with timestamps |
| [OrchestratedResearch](samples/OrchestratedResearch) | All three orchestration patterns side by side |
| [SupportTriage](samples/SupportTriage) | Graph routing, hooks, and structured output extraction |
| [CustomerServiceApi](samples/CustomerServiceApi) | Production-shaped REST API with session persistence |
| [FinanceAssistant](samples/FinanceAssistant) | 4-agent parallel swarm with typed report extraction |
| [PersistentAssistant](samples/PersistentAssistant) | Cross-run memory with automatic summarization |
| [DistributedAgents](samples/DistributedAgents) | A2A cross-process agent communication |
| [ChatUI](samples/ChatUI) | Browser chat UI with SSE streaming and tool badges |
| [BlazorResearch](samples/BlazorResearch) | Blazor Server portal with live parallel agent cards |
| [ResponsibleAiSample](samples/ResponsibleAiSample) | Bedrock Guardrails, `[ToolParameterValidation]`, audit logging, least-privilege tool design |
| [CodeInterpreterSample](samples/CodeInterpreterSample) | `AgentCoreCodeInterpreterTool` — stateful Python / JS / TS sandbox via AgentCore |
| [BrowserSample](samples/BrowserSample) | `AgentCoreBrowserTool` — managed headless Chrome session; CDP endpoint for Playwright / Nova Act |
| [SemanticMemorySample](samples/SemanticMemorySample) | `SemanticMemoryTool` — vector / semantic search over AgentCore Memory |
| [AgentCoreSample](samples/AgentCoreSample) | Deploy any agent to AgentCore Runtime — `MapAgentCoreEndpoints()` in one line |
| [AgentCoreGatewaySample](samples/AgentCoreGatewaySample) | Travel booking assistant using gateway-hosted tools via `AddAgentCoreGatewayTools()` |
| [AotLambda](samples/AotLambda) | NativeAOT Lambda — sub-100ms cold start, zero runtime reflection, `provided.al2023` runtime |

---

## About Strands Agents

Strands Agents is an open source SDK that takes a model-driven approach to building AI agents — the model drives its own behavior, decides which tools to call, and loops until the task is complete. This approach emerged from real-world production experience building agents at AWS.

Strands Agents .NET is a ground-up implementation of those design principles for the .NET ecosystem. It is not a port or wrapper — Strands Agents .NET is built natively in C# 13, using the patterns and idioms .NET developers already know. The core concepts (event loop, tool system, hooks, multi-agent orchestration) follow the Strands design, and the A2A protocol implementation is interoperable across languages and frameworks.

Learn more about the Strands Agents design principles at [strandsagents.com](https://strandsagents.com).

This project is not affiliated with or endorsed by AWS.

---

## Contributing

PRs, issues, and feedback are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. The biggest areas of need are additional model providers (Ollama), more built-in tools, and real-world samples.

---

## Community

Questions and ideas welcome in [GitHub Discussions](https://github.com/apncodes/StrandsAgents.net/discussions).

---

## License

Apache 2.0. See [LICENSE](LICENSE).