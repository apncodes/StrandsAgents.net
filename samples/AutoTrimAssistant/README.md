# AutoTrimAssistant

A multi-turn assistant that automatically compacts its conversation history when it grows too long — with zero boilerplate. Unlike `PersistentAssistant`, there is no manual `TrimAsync` call anywhere in the code. The `Agent` detects `IAutoTrimConversationManager` and calls `TrimAfterTurnAsync` automatically after every turn.

Sessions are saved with a 7-day TTL. Expired sessions are cleaned up automatically on the next load — no background job needed.

## SDK concepts demonstrated

**`IAutoTrimConversationManager`** — the new opt-in interface. Any `IConversationManager` that implements it will have `TrimAfterTurnAsync` called by the `Agent` after every `InvokeAsync` and `StreamAsync` turn. No manual wiring required.

**`SummarizingConversationManager` as `IAutoTrimConversationManager`** — `SummarizingConversationManager` now implements `IAutoTrimConversationManager` directly. Passing it to `Agent` is all that's needed. When the message count exceeds the threshold, the oldest messages are replaced with a model-generated summary automatically.

**`AutoTrimConversationManagerDecorator`** — if you already hold a `SummarizingConversationManager` reference and want to add auto-trim without changing its declared type, wrap it: `new AutoTrimConversationManagerDecorator(existingManager)`.

**`AgentSession.ExpiresAt`** — the new optional TTL field on `AgentSession`. When set, all three session managers (`InMemorySessionManager`, `FileSessionManager`, `AgentCoreSessionManager`) automatically treat the session as deleted once `UtcNow >= ExpiresAt`. No cleanup job needed.

**`ISessionManager.DeleteAsync`** — the new explicit delete method. Used here for the `--reset` flag instead of manually deleting the JSON file.

## Contrast with PersistentAssistant

| | PersistentAssistant | AutoTrimAssistant |
|---|---|---|
| Trim trigger | Manual `await conversation.TrimAsync(...)` after every turn | Automatic — `Agent` calls `TrimAfterTurnAsync` |
| Session reset | `File.Delete(path)` | `await sessionManager.DeleteAsync(SessionId)` |
| Session TTL | None — persists forever | 7-day `ExpiresAt` on `AgentSession` |

## How to run

```bash
dotnet run --project samples/AutoTrimAssistant            # start or resume
dotnet run --project samples/AutoTrimAssistant -- --reset # wipe and restart
```

Session is saved at `~/.strands/auto-trim-assistant/auto-trim-session.json`.

## Where you'd use these patterns

- **Any long-lived assistant** — drop `SummarizingConversationManager` in and get automatic context management with no extra code.
- **Multi-tenant APIs** — set `ExpiresAt` per session to enforce data retention policies without a background cleanup job.
- **Compliance-sensitive applications** — TTL on sessions ensures conversation data is not retained beyond the required window.
