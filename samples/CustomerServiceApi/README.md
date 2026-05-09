# CustomerServiceApi

A production-shaped customer service API. Each session gets its own isolated `Agent` with conversation history persisted to disk. Hooks log every tool call and model response. Built with ASP.NET Core minimal APIs and the `StrandsAgents.Extensions.DI` package.

## SDK concepts demonstrated

**`AddBedrockModel` / `AddStrandsAgent`** — the DI extension methods register `IModel` and `IAgent` into the service container. Tools are registered as factory lambdas (`AddTransient<ITool>`) because the source-generated wrapper classes require a wrapping type instance.

**`FileSessionManager`** — sessions are persisted to disk so conversation history survives process restarts. Each session is stored as `{sessionId}.json`. In-memory agent instances are kept in a `ChatSessionStore` (`ConcurrentDictionary`) for the lifetime of the process.

**Per-session `HookRegistry`** — each `Agent` is created with its own `HookRegistry`. `BeforeToolCallEvent` and `AfterToolCallEvent` hooks log every CRM and knowledge base lookup to the console, attributed to the session ID.

**SSE streaming** — `StreamAsync` drives the response. The endpoint iterates `TextDeltaEvent` and writes each delta as `data: <text>\n\n`. The stream ends with `data: [DONE]\n\n`.

## Scenario

Create a session, then send messages about orders or product questions. The agent has two tools: `OrderStatusTool` (looks up order status by order ID) and `KnowledgeBaseTool` (searches a product FAQ). Conversation history accumulates across turns within the same session.

## How to run

```bash
dotnet run --project samples/CustomerServiceApi
```

```bash
# Create a session
curl -sX POST http://localhost:5000/sessions
# → {"sessionId":"abc123..."}

# Chat (SSE stream)
curl -N -X POST http://localhost:5000/sessions/abc123.../chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Check order ORD-4521"}'

# Delete session
curl -X DELETE http://localhost:5000/sessions/abc123...
```

Test order IDs: `ORD-4521` (shipped) · `ORD-4890` (processing) · `ORD-3317` (delivered) · `ORD-5001` (cancelled)

## Where you'd use these patterns

- **SaaS chat features** — the session-per-user model with disk persistence maps directly to a multi-tenant support or assistant feature.
- **API-first AI backends** — expose any agent capability over HTTP with SSE streaming to any frontend: React, Vue, mobile, or another backend service.
- **Auditable agent deployments** — the hook-based logging pattern gives you a complete per-session audit trail of every tool call and model response without modifying agent logic.
