---
sidebar_position: 1
---

# Strands Agents .NET

**Model-driven agentic AI for C# developers.**

Strands Agents .NET brings the [Strands Agents](https://strandsagents.com) architecture to the .NET ecosystem — the same event loop, tool system, and multi-agent patterns, built ground-up in idiomatic C# 13.

Give an agent a model, tools, and a prompt. The event loop calls the model, executes whatever tools it requests, feeds results back, and repeats until the model signals it's done. You never write the orchestration loop.

## Quick install

```bash
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package StrandsAgents.SourceGenerator
```

## Minimal example

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

public partial class WeatherTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}
```

## Key capabilities

- **Zero runtime reflection** — compile-time tool dispatch via Roslyn source generators
- **NativeAOT-ready** — suitable for AOT-published Lambda with sub-100ms cold start
- **Idiomatic .NET** — `IAsyncEnumerable<T>`, generics, DI, OpenTelemetry
- **AWS-native** — Bedrock + AgentCore Runtime, Memory, Code Interpreter, Browser, Gateway
- **Multi-model** — Bedrock, Anthropic direct API, OpenAI-compatible (OpenAI, Azure, Ollama), Google Gemini
- **Multi-agent** — pipeline, parallel, graph, A2A protocol

## Where to go next

- **[Getting Started](./getting-started)** — install, configure, run your first agent
- **[Concepts: Agent & Event Loop](./concepts/agent-event-loop)** — understand the mental model
- **[Concepts: Model Providers](./concepts/model-providers)** — Bedrock, Anthropic, OpenAI, Gemini
- **[Concepts: AgentCore](./concepts/agentcore)** — Runtime, Memory, Code Interpreter, Browser, Gateway
- **[Tutorials](./tutorials/first-agent)** — step-by-step walkthroughs
- **[FAQ](./faq)** — common questions and troubleshooting
