---
sidebar_position: 1
---

# Strands Agents .NET

**Model-driven agentic AI for C# developers.**

Strands Agents .NET brings the [Strands Agents](https://strandsagents.com) architecture to the .NET ecosystem — the same event loop, tool system, and multi-agent patterns, built ground-up in idiomatic C# 13.

Give an agent a model, tools, and a prompt. The event loop calls the model, executes whatever tools it requests, feeds results back, and repeats until the model signals it's done. You never write the orchestration loop.

## Why Strands Agents .NET

**The goal is that any .NET developer — from line-of-business engineer to senior architect — can read the quickstart and start building agents the same afternoon.** No prior agent experience required, no agent-framework vocabulary to learn, no orchestration loop to write.

Built around four principles: don't over-engineer, keep things clean, embrace open standards, be pragmatic about what to ship. The vocabulary matches what the broader agentic ecosystem has converged on. The protocols are open. The cloud integration is native where it makes sense, abstracted where it doesn't.

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
    toolProviders: [new WeatherTools()]);   // pass your tool classes here

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

// Mark the class partial — the source generator fills in the rest at compile time
public partial class WeatherTools
{
    // Any public method with [Tool] becomes an agent tool automatically
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}
```

The `[Tool]` attribute and `partial class` tell the Roslyn source generator — built into modern .NET — to generate the tool wiring at build time. You write the method; the framework handles the schema, dispatch, and result formatting.

## Key capabilities

- **Easy to learn, idiomatic to write** — any .NET developer can pick this up and ship a working agent in an afternoon. If you can write a C# method, you can write a tool. The advanced .NET features are present where they help and hidden where they don't.
- **Industry-standard vocabulary** — agent, tool, system prompt, session, hook. Reads natively to anyone coming from Strands Python, OpenAI, Anthropic, or LangChain. No proprietary terminology to translate.
- **Zero runtime reflection** — compile-time tool dispatch via Roslyn source generators. The `STRAND001` diagnostic catches tool misconfiguration at build time, not at first invocation.
- **NativeAOT-ready** — designed for AOT-published deployment. Measured **89.6ms average** cold-start init on Graviton2 Lambda (arm64, 1024 MB, 20 cold starts — 19/20 under 100ms). See the [AotLambda sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda).
- **Cloud-neutral core, deep integrations available** — four model providers (Bedrock, Anthropic, OpenAI-compatible, Gemini), open protocols (MCP, A2A), first-class AWS Bedrock and AgentCore support. Runs anywhere .NET runs.
- **Multi-agent in one package** — pipeline, parallel, graph orchestration, agent-as-tool, A2A protocol for cross-language interop.

## Where to go next

- **[Getting Started](./getting-started)** — install, configure, run your first agent
- **[Concepts: Agent & Event Loop](./concepts/agent-event-loop)** — understand the mental model
- **[Concepts: Model Providers](./concepts/model-providers)** — Bedrock, Anthropic, OpenAI, Gemini
- **[Concepts: AgentCore](./concepts/agentcore)** — Runtime, Memory, Code Interpreter, Browser, Gateway
- **[Tutorials](./tutorials/first-agent)** — step-by-step walkthroughs
- **[FAQ](./faq)** — common questions and troubleshooting

## About this project

Strands Agents .NET is a ground-up C# implementation of the Strands Agents design. The core concepts — model-driven event loop, tool system, hooks, multi-agent orchestration — follow the Strands architecture, and the A2A protocol implementation is interoperable across the Strands Python and TypeScript SDKs.

This project is currently maintained independently and is not officially affiliated with the upstream Strands Agents project. Licensed under Apache 2.0.
