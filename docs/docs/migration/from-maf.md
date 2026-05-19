---
sidebar_position: 1
---

# Migrating from Other .NET Agent Frameworks

Coming from Semantic Kernel, Microsoft Agent Framework (MAF), or AutoGen? The core ideas map closely. This page gives you the concept-to-concept translation so you can get oriented quickly.

## Concept mapping

| Your framework | Strands Agents .NET equivalent | Notes |
|---|---|---|
| **Kernel** / **AgentRuntime** | `Agent` | The central object. Holds model, tools, system prompt. |
| **Plugin** / **Skill** | Tool class with `[Tool]`-decorated methods | Mark the class `partial`, add `[Tool]` to methods. The source generator handles the rest at compile time. |
| **KernelFunction** / **AgentTool** | `ITool` (generated) | You don't implement `ITool` directly — the source generator emits it from your `[Tool]` method. |
| **ChatHistory** / **ConversationHistory** | `ISessionManager` | In-memory by default. Swap in `FileSessionManager` or `AgentCoreSessionManager` for persistence. |
| **Planner** | Event loop | No separate planner. The model decides which tools to call and when to stop. |
| **Streaming** | `agent.StreamAsync(...)` → `IAsyncEnumerable<StreamEvent>` | Yields `TextDeltaEvent`, `ToolCallEvent`, `ToolResultEvent` as they arrive. |
| **Filters** / **Middleware** | `HookRegistry` | Register typed hooks for `BeforeToolCall`, `AfterToolCall`, `BeforeModelCall`, etc. |
| **Multi-agent** | `StrandsAgents.MultiAgent` | Pipeline, parallel fan-out, graph with conditional routing, agent-as-tool. |
| **Process Framework** | Graph orchestration | `GraphBuilder` with typed edge conditions. |

## Key differences

**No planner step.** Strands Agents .NET is model-driven: the LLM decides which tools to call and when to stop. You don't configure a planner or a plan format. This simplifies the code but means the model's reasoning quality directly affects behavior.

**Compile-time tool schema.** The `[Tool]` attribute triggers a Roslyn source generator. There is no runtime reflection, no `KernelPlugin.CreateFromType<T>()`, no dynamic discovery. The tool schema is baked into the binary at build time. This is what makes NativeAOT work.

**One loop, not a pipeline.** The event loop runs until `EndTurn`. You don't compose a chain of steps — you give the agent tools and let the model decide the sequence. For multi-step workflows with explicit ordering, use the Graph or Pipeline patterns in `StrandsAgents.MultiAgent`.

**Open protocols.** MCP (Model Context Protocol) and A2A (Agent-to-Agent) are first-class. If you're building agents that need to interop with Python or TypeScript Strands agents, A2A is the bridge.

## Getting started

The [Getting Started](../getting-started) guide and [Concepts: Agent & Event Loop](../concepts/agent-event-loop) are the fastest path to a working agent. Most developers coming from other frameworks are productive within an hour.

Questions? Start a thread in [GitHub Discussions](https://github.com/apncodes/StrandsAgents.net/discussions).
