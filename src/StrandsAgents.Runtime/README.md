# StrandsAgents.Runtime

Deploy any [Strands Agents .NET](https://github.com/apncodes/StrandsAgents.net) agent to [Amazon Bedrock AgentCore Runtime](https://aws.amazon.com/bedrock/agentcore/) with one line. Optionally use AgentCore managed services — Memory, Browser, and Code Interpreter — as tools your agent can invoke.

```bash
dotnet add package StrandsAgents.Runtime
```

## Your agent code is unchanged

```csharp
using StrandsAgents.Runtime.Hosting;
using StrandsAgents.Runtime.Extensions;
using StrandsAgents.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreBrowser()
    .AddAgentCoreCodeInterpreter()
    .AddAgentCoreSessionManager(
        Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID") ?? "")
    .AddStrandsAgent("You are a helpful assistant.");

var app = builder.Build();
app.MapAgentCoreEndpoints();  // POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime expects port 8080
app.Run();
```

## AgentCore Gateway

Connect your agent to tools hosted on an Amazon Bedrock AgentCore Gateway:

```csharp
await using var gateway = await AgentCoreGatewayToolProvider.CreateAsync(
    gatewayUrl: new Uri("https://...gateway-url.../mcp"),
    auth: new AgentCoreGatewayAuth.Iam(region: "us-east-1"));

var tools = await gateway.ListToolsAsync();
var agent = new Agent(model, tools: tools);
```

Three auth modes: `AgentCoreGatewayAuth.Iam`, `AgentCoreGatewayAuth.Bearer`, `AgentCoreGatewayAuth.None`.

With DI:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Iam("us-east-1"))
    .AddStrandsAgent();
```
