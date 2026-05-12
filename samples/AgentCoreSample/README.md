# AgentCoreSample

Demonstrates deploying a Strands.NET agent to [Amazon Bedrock AgentCore Runtime](https://aws.amazon.com/bedrock/agentcore/) with a single line of hosting code. The agent itself is completely unchanged.

## SDK concepts demonstrated

- **`MapAgentCoreEndpoints()`** — registers `POST /invocations` and `GET /health` on the ASP.NET Core app; this is the .NET equivalent of Python's `@app.entrypoint`
- **`UseAgentCorePort(8080)`** — binds to the port AgentCore Runtime expects
- **`AgentCoreBrowserTool`** — managed browser sandbox registered as a standard `ITool`; the agent can navigate, extract text, and interact with JS-rendered pages
- **`AgentCoreCodeInterpreterTool`** — managed code execution sandbox; the agent can run Python, JavaScript, or TypeScript snippets
- **`AgentCoreSessionManager`** — conversation history persisted to AgentCore Memory; works identically to `FileSessionManager` from the agent's perspective

## The key point

`Program.cs` is split into two clearly labelled sections:

1. **Agent configuration** — identical to any other Strands.NET agent. Comment out `MapAgentCoreEndpoints()` and this is a standard agent.
2. **AgentCore hosting** — two lines. Remove them and the agent still builds and runs locally.

## Prerequisites

- .NET 10 SDK
- AWS credentials with Bedrock access
- (For AgentCore managed services) An AgentCore memory resource ID in `AGENTCORE_MEMORY_ID`

## Run locally

```bash
cd samples/AgentCoreSample
dotnet run
```

```bash
# Non-streaming
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is 42 multiplied by 1764?"}'

# Streaming
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{"prompt": "Explain quantum computing in 3 sentences"}'

# Health check (required by AgentCore Runtime before routing traffic)
curl http://localhost:8080/health
```

## Deploy to AgentCore Runtime

```bash
# Build ARM64 image (required by AgentCore Runtime)
docker buildx build --platform linux/arm64 -t agentcore-sample:latest .

# Push to ECR and deploy via AWS Console or CLI
```

## Real-world applicability

Any Strands.NET agent can be deployed to AgentCore Runtime by adding `StrandsAgents.Runtime` and two lines of hosting code. AgentCore provides infrastructure-level session isolation, scaling, and observability — your agent code does not change.
