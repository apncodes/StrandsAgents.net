# SemanticMemorySample

An agent that retrieves memories by meaning rather than exact key. Ask "What coffee do I like?" and the agent calls `search_memory("coffee preference")` — it finds the right memory without knowing the exact key it was stored under.

## SDK concepts demonstrated

**`SemanticMemoryTool`** — the new tool backed by Amazon Bedrock AgentCore Memory's vector search API. Exposes three operations to the LLM:
- `search_memory` — finds memories semantically similar to a natural-language query. Returns a ranked list of `{ key, value, score }` objects sorted by cosine similarity.
- `store_memory` — saves a key/value pair. Accepts an optional `ttl_seconds` for automatic expiry.
- `delete_memory` — removes a memory entry by key.

**SigV4 authentication** — `SemanticMemoryTool` signs every HTTP request to `bedrock-agentcore.{region}.amazonaws.com` automatically using credentials from the standard AWS credential chain (env vars, `~/.aws/credentials`, instance metadata). No manual auth configuration needed.

**`ttl_seconds` on `store_memory`** — sensitive or temporary facts can be stored with an expiry. The AgentCore Memory API deletes them automatically after the TTL elapses. The agent uses this for one-time codes or temporary preferences.

**Tool call visibility** — the REPL prints `ToolCallStartEvent` and `ToolCallResultEvent` so you can see exactly which memory operations the agent performs on each turn.

## Contrast with AgentCoreMemoryTool

| | `AgentCoreMemoryTool` | `SemanticMemoryTool` |
|---|---|---|
| Retrieval | Exact key required | Natural-language query |
| Search API | `GET /records/{key}` | `POST /search` (vector similarity) |
| Results | Single value | Ranked list of `{ key, value, score }` |
| TTL | Not supported | Optional `ttl_seconds` on `store_memory` |
| Auth | SigV4 (new) | SigV4 |

## Prerequisites

- .NET 10, AWS credentials configured for Bedrock
- An AgentCore Memory resource created in your AWS account
- `AGENTCORE_MEMORY_ID` environment variable set to your memory resource ID

## How to run

```bash
export AGENTCORE_MEMORY_ID=mem-abc123
dotnet run --project samples/SemanticMemorySample
```

On startup the agent seeds five sample memories (name, coffee preference, language preference, current project, timezone). Then enter the REPL and ask questions like:

- "What do you know about me?"
- "What coffee do I like?"
- "What project am I working on?"
- "What timezone am I in?"

## Where you'd use these patterns

- **Personalization engines** — store user preferences at onboarding; retrieve relevant ones per request without managing a key taxonomy.
- **Knowledge bases** — store domain facts; let the agent find relevant context by describing what it needs.
- **Long-running assistants** — combine with `SummarizingConversationManager` for short-term context and `SemanticMemoryTool` for long-term facts that survive conversation resets.
