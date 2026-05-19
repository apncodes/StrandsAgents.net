---
sidebar_position: 10
---

# FAQ & Troubleshooting

## Tools & Source Generator

### Why does my `[Tool]` class need to be `partial`?

The source generator emits a second partial declaration of your class that implements `IToolProvider`. C# merges the two partial declarations into one type at compile time. Without `partial`, the two declarations are treated as separate types and the build fails.

If you forget `partial`, the compiler emits a `STRAND001` warning:

```
warning STRAND001: Class 'WeatherTools' has [Tool] methods but is not declared partial.
```

Fix: add `partial` to the class declaration.

### Which version of `StrandsAgents.SourceGenerator` do I need for `toolProviders:`?

Version **0.1.9+** is required. If you're on an older version:

```bash
dotnet add package StrandsAgents.SourceGenerator --version 0.1.9
```

### Why isn't my tool being called by the model?

Common causes:

1. **Class not `partial`** — the `IToolProvider` implementation wasn't generated. Check for `STRAND001` warnings.

2. **Wrong constructor parameter** — using `tools:` instead of `toolProviders:`:
   ```csharp
   // Wrong — tools: expects ITool instances, not IToolProvider
   var agent = new Agent(model, tools: [new WeatherTools()]);
   
   // Correct
   var agent = new Agent(model, toolProviders: [new WeatherTools()]);
   ```

3. **Tool description too vague** — the model uses the `[Tool]` description to decide when to call it. Make the description specific about what the tool does and when to use it.

4. **System prompt conflicts** — if the system prompt says "don't use tools" or similar, the model will follow that instruction.

### How do I inspect the generated tool schema?

Add a hook to log the tool definitions before the first model call:

```csharp
agent.Hooks.Register<BeforeModelCallEvent>(async (e, ct) =>
{
    foreach (var tool in e.Tools)
        Console.WriteLine($"{tool.Name}: {tool.InputSchema}");
});
```

Or check the generated `.g.cs` files in your project's `obj/` directory — look for files ending in `_Tool.g.cs`.

---

## AWS Credentials

### How does the SDK resolve AWS credentials?

The SDK uses the standard AWS credential chain via the AWS SDK for .NET:

1. Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. AWS credentials file (`~/.aws/credentials`)
3. IAM instance profile (EC2, ECS, Lambda)
4. AWS SSO

Run `aws sts get-caller-identity` to verify your credentials are configured correctly.

### I'm getting `AccessDeniedException` from Bedrock

Two common causes:

1. **Model access not enabled** — go to the [Bedrock console](https://console.aws.amazon.com/bedrock/home#/modelaccess) and enable access for the model you're using.

2. **Wrong model ID** — use cross-region inference profile IDs (e.g., `us.anthropic.claude-haiku-4-5-20251001-v1:0`) rather than bare model IDs. Bare model IDs require on-demand throughput which isn't available by default.

---

## AOT & Lambda

### I'm getting AOT trimming warnings {#aot-trimming-warnings}

The SDK suppresses known-safe warnings in the `AotLambda` sample's `.csproj`:

```xml
<NoWarn>$(NoWarn);IL2026;IL3050;IL2104</NoWarn>
```

- `IL2026` / `IL3050` — come from the generated tool wrapper code and AWS Lambda serializer internals. Safe for the primitive types used in tool parameters.
- `IL2104` — comes from `AWSSDK.Core` which is not fully trim-annotated. Safe for the Bedrock Converse API usage pattern.

### My Lambda crashes with exit status 131 or 150

This means the binary was built on macOS and deployed to `provided.al2023`. NativeAOT binaries must be built on Linux. Use Docker:

```bash
docker run --rm -v $(pwd):/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && \
  dotnet publish -c Release -r linux-arm64 --output /src/publish -p:StripSymbols=true"
```

Use `linux-arm64` for Graviton2 (recommended) or `linux-x64` for x86_64.

### My Lambda returns `{}` instead of the expected JSON

This is an AOT serialization issue. Use `class` with `{ get; set; }` properties instead of `record` with `{ get; init; }` for types that are serialized as Lambda output:

```csharp
// Broken in AOT output serialization
public record MyResult { public string Value { get; init; } = ""; }

// Works in AOT
public class MyResult { public string Value { get; set; } = ""; }
```

Also ensure all types are registered in your `JsonSerializerContext`.

---

## Sessions & Memory

### How do I persist sessions across Lambda invocations?

Use `AgentCoreSessionManager` (requires `StrandsAgents.Runtime`):

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSessionManager(
        memoryId: Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID") ?? "")
    .AddStrandsAgent();
```

Or use `FileSessionManager` with an EFS mount for simpler setups.

### What's the difference between `tools:` and `toolProviders:`?

| Parameter | Accepts | Use when |
|---|---|---|
| `toolProviders:` | `IToolProvider` instances | Passing your `[Tool]`-decorated classes |
| `tools:` | `ITool` instances | Passing pre-built tools from `agent.AsTool()`, `McpToolProvider`, `AgentCoreGatewayToolProvider` |

Both can be used simultaneously on the same agent.

---

## DI & ASP.NET Core

### Can I use multiple tool providers on one agent?

Yes — call `AddStrandsToolProvider<T>()` multiple times:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddStrandsToolProvider<WeatherTools>()
    .AddStrandsToolProvider<SearchTools>()
    .AddStrandsToolProvider<CalculatorTools>()
    .AddStrandsAgent();
```

All registered providers are merged into the agent's tool registry.

### How do I cancel a long-running agent invocation?

Pass a `CancellationToken` to `InvokeAsync` or `StreamAsync`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var result = await agent.InvokeAsync("...", ct: cts.Token);
}
catch (OperationCanceledException)
{
    // Invocation was cancelled
}
```

### How do I use a model other than Bedrock?

```csharp
// Anthropic direct API
var model = new AnthropicModel(apiKey: "sk-ant-...", modelId: "claude-haiku-4-5");

// OpenAI / Azure OpenAI / Ollama
var model = new OpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: "sk-...",
    modelId: "gpt-4o-mini");

// Google Gemini
var model = new GeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");
```

### How do I enable OpenTelemetry tracing?

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("StrandsAgents.Agent")  // the SDK's ActivitySource
        .AddOtlpExporter());               // or AddConsoleExporter() for development
```

The SDK emits traces for every agent invocation, model call, and tool execution automatically.
