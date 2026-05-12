# BrowserSample

Demonstrates `AgentCoreBrowserTool` — session lifecycle management for Amazon Bedrock AgentCore Browser. The tool creates and manages managed headless Chrome instances and surfaces the WebSocket endpoint for Playwright or Nova Act automation.

## How AgentCore Browser works

AgentCore Browser is not a simple request/response API. It follows a session model:

```
start_session
    └─▶ sessionId + automationStreamEndpoint (WebSocket URL)
              │
              ▼
    Connect via Playwright (connect_over_cdp) or Nova Act
              │
              ├─▶ navigate, click, screenshot, fill forms, ...
              │
              ▼
stop_session  (releases the managed Chrome instance)
```

The `AgentCoreBrowserTool` handles the session lifecycle. Actual browser automation (navigation, clicks, screenshots) happens through the `automationStreamEndpoint` using Playwright or Nova Act — not through this tool directly.

## SDK concepts demonstrated

**`AgentCoreBrowserTool`** — backed by the official `AWSSDK.BedrockAgentCore` SDK client. Exposes three operations:
- `start_session` → `StartBrowserSessionAsync` — creates a managed Chrome instance. Returns `sessionId` and `automationStreamEndpoint`.
- `get_session` → `GetBrowserSessionAsync` — returns session status and stream endpoint.
- `stop_session` → `StopBrowserSessionAsync` — releases the browser instance.

**`automationStreamEndpoint`** — the WebSocket URL returned by `start_session`. Connect to it via:
```python
# Playwright (Python)
browser = await playwright.chromium.connect_over_cdp(automation_stream_endpoint)

# Nova Act
from nova_act import NovaAct
act = NovaAct(starting_page="https://example.com", cdp_endpoint=automation_stream_endpoint)
```

**`session_timeout_seconds`** — configurable per session (default 3600). The managed Chrome instance is automatically released after the timeout.

## Prerequisites

- .NET 10 SDK
- AWS credentials configured for Bedrock and AgentCore
- An AgentCore Browser resource in your AWS account (optional — uses `"default"` if not set)

## How to run

```bash
# Optional: set your browser resource ID
export AGENTCORE_BROWSER_ID=browser-abc123

dotnet run --project samples/BrowserSample
```

## Example session

```
You: Start a browser session
  [agentcore_browser]...
  {"sessionId":"abc123","automationStreamEndpoint":"wss://...","status":"STARTED",...}
Assistant: Browser session started. Here are the details:
           - Session ID: abc123
           - Automation Stream Endpoint: wss://...
           Connect to the endpoint via Playwright or Nova Act to begin automation.

You: What is the status of session abc123?
  [agentcore_browser]...
  {"sessionId":"abc123","status":"ACTIVE","automationStreamEndpoint":"wss://..."}
Assistant: Session abc123 is ACTIVE and ready for automation.

You: Stop session abc123
  [agentcore_browser]...
Assistant: Session abc123 has been stopped and resources released.
```

## Connecting via Playwright (.NET)

Once you have the `automationStreamEndpoint`, connect from a separate process:

```csharp
using Microsoft.Playwright;

var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.ConnectOverCDPAsync(automationStreamEndpoint);
var page = await browser.NewPageAsync();
await page.GotoAsync("https://example.com");
var title = await page.TitleAsync();
Console.WriteLine(title);
await browser.CloseAsync();
```

## Where you'd use these patterns

- **Web scraping agents** — start a session, hand the endpoint to a Playwright script, stop when done.
- **Form automation** — agents that fill and submit web forms as part of a workflow.
- **Visual regression testing** — agents that navigate and screenshot pages for comparison.
- **Research assistants** — agents that browse the web and extract structured data.
