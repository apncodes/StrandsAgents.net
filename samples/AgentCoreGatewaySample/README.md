# AgentCoreGatewaySample

A conversational travel booking assistant that demonstrates connecting a Strands.NET agent to an [Amazon Bedrock AgentCore Gateway](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/gateway-building.html). The agent discovers and calls flight and hotel search tools exposed by the gateway over MCP — no tool code required in the agent itself.

## What this sample shows

AgentCore Gateway is an **MCP server**, not a model proxy. The agent still calls a Bedrock model directly. The gateway provides the *tools* — Lambda functions, REST APIs, or other AgentCore Runtime agents — over the MCP Streamable HTTP protocol. The agent discovers and calls those tools automatically.

```
Agent → BedrockModel (unchanged)
      → AgentCoreGateway (MCP server) → Lambda / REST API / AgentCore Runtime agent
```

## SDK concepts demonstrated

- **`AgentCoreGatewayAuth`** — three inbound auth modes as a discriminated union:
  - `Iam(region)` — SigV4 via the standard AWS credential chain (same as `BedrockModel`)
  - `Bearer(accessToken)` — JWT/OIDC from Cognito, Entra ID, Okta, Google, GitHub, etc.
  - `None()` — no application-level auth; access controlled by security groups / VPC routing
- **`AddAgentCoreGatewayTools(gatewayUrl, auth)`** — connects to the gateway, runs the MCP handshake, discovers all tools, and registers each as `ITool` with the agent
- **`AgentCoreGatewayToolProvider`** — the underlying provider for non-DI scenarios
- **Streaming chat UI** — plain HTML/CSS/JS frontend over SSE; no Blazor, no SignalR
- **Per-session conversation memory** — `InMemoryConversationManager` keyed by browser session ID so the agent remembers context across turns
- **Intelligent empty-result handling** — agent automatically searches ±3 days when a date returns no results

## Deploy a real AgentCore Gateway

To test with a real gateway that has flight and hotel search tools, deploy the sample CDK stack from:

**[github.com/apncodes/bedrockagentcore-dotnet-samples](https://github.com/apncodes/bedrockagentcore-dotnet-samples)**

That repo contains a CDK stack that deploys:
- A `FlightApi` Lambda with dynamically generated flight data across multiple routes and dates
- A `HotelApi` Lambda with hotel data for major cities
- An AgentCore Gateway with IAM inbound authorization wiring both Lambdas as MCP targets

After deploying, note the `GatewayEndpoint` output and use it as `AGENTCORE_GATEWAY_URL` below.

## Prerequisites

- .NET 10 SDK
- AWS credentials configured (`aws configure` or environment variables)
- IAM permissions: `bedrock:InvokeModel` + `bedrock-agentcore:InvokeGateway` on the gateway ARN
- An AgentCore Gateway endpoint URL

## Run locally

```bash
# IAM auth (default — uses your local AWS credentials automatically)
export AGENTCORE_GATEWAY_URL="https://your-gateway-id.gateway.bedrock-agentcore.us-east-1.amazonaws.com/mcp"

dotnet run --project samples/AgentCoreGatewaySample/AgentCoreGatewaySample.csproj
```

Open **http://localhost:5050** in your browser.

```bash
# Bearer / JWT mode — set the token and switch auth in Program.cs
export AGENTCORE_GATEWAY_URL="https://..."
export AGENTCORE_ACCESS_TOKEN="eyJhbGci..."
```

```bash
# Network-isolated mode (no credentials) — switch auth in Program.cs
export AGENTCORE_GATEWAY_URL="https://internal-gateway.vpc.local/mcp"
```

## Switching auth modes

Edit the `AddAgentCoreGatewayTools` call in `Program.cs`:

```csharp
// IAM — credentials from env / ~/.aws / instance metadata (default)
.AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Iam("us-east-1"))

// Bearer / JWT
.AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Bearer(accessToken))

// No auth — network-level security only
.AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.None())
```

| Mode | When to use |
|------|-------------|
| `Iam` | Gateway configured with IAM inbound auth; agent runs on AWS with an IAM role or local credentials |
| `Bearer` | Gateway configured with JWT/OIDC inbound auth (Cognito, Entra, Okta, etc.) |
| `None` | Gateway has no inbound auth; access controlled by security groups / VPC routing |

## Non-DI usage

```csharp
await using var gateway = await AgentCoreGatewayToolProvider.CreateAsync(
    gatewayUrl: new Uri("https://..."),
    auth: new AgentCoreGatewayAuth.Iam("us-east-1"));

var tools = await gateway.ListToolsAsync();
var agent = new Agent(model: new BedrockModel(), tools: tools);
var result = await agent.InvokeAsync("Find flights from New York to Los Angeles next Friday");
```

## Chat API

The sample exposes a single endpoint consumed by the browser UI:

```
POST /chat
Content-Type: application/json

{ "message": "Find flights from JFK to LAX next Friday", "sessionId": "<uuid>" }
```

Responses stream as Server-Sent Events:

| Event | Data |
|-------|------|
| `delta` | Text token from the agent |
| `tool_start` | Tool name being called |
| `tool_done` | Tool call completed |
| `done` | Agent turn complete |
