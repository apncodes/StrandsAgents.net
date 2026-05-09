# DiAgent

A two-turn console assistant wired entirely through `Microsoft.Extensions.DependencyInjection`. Shows how to register models, tools, sessions, and the agent itself using the `StrandsAgents.Extensions.DI` extension methods, and how to configure context-window trimming through `AgentConfig`.

## SDK concepts demonstrated

**DI extension methods** — `AddBedrockModel`, `AddFileReadTool`, `AddFileWriteTool`, `AddStrandsInMemorySessionManager`, and `AddStrandsAgent` register all SDK components into a standard `ServiceCollection`. The agent is resolved with `provider.GetRequiredService<IAgent>()` — no manual construction.

**`AgentConfig` with `SlidingWindowStrategy`** — passed to `AddStrandsAgent` to configure the context window. When accumulated messages exceed `MaxContextTokens`, the strategy trims the middle of the conversation, always preserving the system prompt and the most recent message. This is the mechanism for keeping long-running agents within token limits.

**Multi-turn conversation** — the resolved `IAgent` instance holds its own `InMemoryConversationManager`. Calling `InvokeAsync` twice on the same instance produces a two-turn conversation; the second turn can reference what happened in the first because the full message history is in memory.

## Scenario

The agent is given access to a temporary workspace directory with a `readme.md` file. Turn 1: read the file, summarise it, and append a status line. Turn 2: a follow-up question that relies on the conversation history from turn 1. The workspace is deleted on exit.

## How to run

```bash
dotnet run --project samples/DiAgent
```

## Where you'd use these patterns

- **ASP.NET Core applications** — the DI pattern here is identical to how you'd wire Strands into a controller-based or minimal API app; just call the same extension methods in `Program.cs`.
- **Configurable agent deployments** — `AgentConfig` is the knob for tuning context window strategy, max iterations, and parallel tool execution without touching agent logic.
- **Worker services** — resolve one `IAgent` per background job scope for isolated conversation history per job.
