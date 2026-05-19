---
sidebar_position: 3
---

# Deploy to Lambda with AOT

**Time:** ~30 minutes  
**What you'll build:** A Strands Agents .NET agent published as a NativeAOT AWS Lambda function with sub-100ms cold start on Graviton2.

## Why AOT on Lambda?

Most agent frameworks carry a JIT tax on Lambda. The runtime loads, assemblies resolve, the hot path compiles — all before your first request. For a typical managed .NET agent, that's 200–500ms of init duration on every cold start.

Strands Agents .NET eliminates that tax. The `[Tool]` attribute triggers a Roslyn source generator that emits all tool schema and dispatch code at compile time. Zero runtime reflection means the binary loads and starts in under 100ms — on Graviton2, consistently, across all memory tiers.

Across 60 measured cold starts on arm64 Graviton2 (512 MB through 2048 MB), **88% came in under 100ms with an overall average of 93.3ms**. The binary uses only ~52 MB of memory at runtime — meaning you get near-sub-100ms cold starts on the smallest practical Lambda configuration, not just on oversized instances.

**Recommended configuration: `arm64` (Graviton2) at 1024 MB** — 89.6ms average, 19/20 runs under 100ms, ~20% cheaper per GB-second than x86_64.

See the full benchmark data in the [AotLambda sample README](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda).

## Prerequisites

- .NET 10 SDK
- AWS CLI configured with credentials
- Amazon Bedrock access enabled
- **Linux build environment** — NativeAOT cross-compilation from macOS to `linux-x64` requires a Linux linker. Use one of:
  - A Linux machine or WSL2
  - Docker: `docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet publish ...`
  - EC2 instance (see the [AotLambda sample README](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda))

## Step 1: Create the project

```bash
dotnet new console -n AotWeatherAgent
cd AotWeatherAgent
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package Amazon.Lambda.Core
dotnet add package Amazon.Lambda.RuntimeSupport
dotnet add package Amazon.Lambda.Serialization.SystemTextJson
dotnet add package StrandsAgents.SourceGenerator
```

## Step 2: Configure for AOT

Edit `AotWeatherAgent.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>false</StripSymbols>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);IL2026;IL3050;IL2104</NoWarn>
  </PropertyGroup>
</Project>
```

## Step 3: Write the Lambda handler

Replace `Program.cs`:

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using System.Text.Json.Serialization;
using AotWeatherAgent;

var handler = async (string input, ILambdaContext context) =>
{
    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0"),
        systemPrompt: "You are a helpful weather assistant.",
        toolProviders: [new WeatherTools()]);

    var result = await agent.InvokeAsync(input);
    return result.Message;
};

await LambdaBootstrapBuilder
    .Create(handler, new SourceGeneratorLambdaJsonSerializer<LambdaJsonContext>())
    .Build()
    .RunAsync();

[JsonSerializable(typeof(string))]
public partial class LambdaJsonContext : JsonSerializerContext { }

namespace AotWeatherAgent
{
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city")]
        public string GetWeather(string city) => $"Sunny, 22°C in {city}";
    }
}
```

:::important AOT serialization
Use `SourceGeneratorLambdaJsonSerializer<T>` instead of `DefaultLambdaJsonSerializer`. The default serializer uses reflection which is disabled in AOT.
:::

## Step 4: Build on Linux (arm64 recommended)

On a Linux machine (or in Docker):

```bash
# arm64 — recommended (Graviton2, ~20% cheaper, faster cold start)
docker run --rm -v $(pwd):/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && \
  dotnet publish -c Release -r linux-arm64 --output /src/publish -p:StripSymbols=true"

cp ./publish/AotWeatherAgent ./bootstrap
zip -j function.zip bootstrap

# x86_64 — alternative
# dotnet publish -c Release -r linux-x64 --output ./publish
```

## Step 5: Deploy to Lambda (arm64)

```bash
# Create the function — arm64 Graviton2 at 1024 MB (sweet spot)
aws lambda create-function \
  --function-name aot-weather-agent \
  --runtime provided.al2023 \
  --handler bootstrap \
  --architectures arm64 \
  --role arn:aws:iam::YOUR_ACCOUNT:role/YOUR_LAMBDA_ROLE \
  --zip-file fileb://function.zip \
  --memory-size 1024 \
  --timeout 30 \
  --region us-east-1

# Wait for it to be active
aws lambda wait function-active --function-name aot-weather-agent --region us-east-1
```

## Step 6: Test it

```bash
aws lambda invoke \
  --function-name aot-weather-agent \
  --payload '"What is the weather in London?"' \
  --cli-binary-format raw-in-base64-out \
  --log-type Tail \
  --region us-east-1 \
  --query 'LogResult' --output text \
  response.json | base64 --decode | grep "Init Duration"

cat response.json
```

You should see `Init Duration: ~90ms` in the logs and the agent's response in `response.json`.

## Benchmark results

Measured across 60 cold starts on arm64 Graviton2 (`us-east-1`, `provided.al2023`):

| Configuration | Avg init | Min | Under 100ms |
|---|---|---|---|
| **arm64 Graviton2, 1024 MB** | **89.6 ms** | 77.7 ms | **19/20** |
| arm64 Graviton2, 512 MB | 95.3 ms | 77.0 ms | 17/20 |
| arm64 Graviton2, 2048 MB | 95.1 ms | 81.3 ms | 10/10 |
| x86_64, 1024 MB | 119.8 ms | 101.5 ms | 0/20 |

The binary uses only ~52 MB of memory at runtime — you get near-sub-100ms cold starts on the smallest practical Lambda configuration, not just on oversized instances.

## Next steps

- **[AotLambda sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda)** — the full sample with detailed benchmark methodology
- **[DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow)** — multi-step AOT agents with Step Functions durability
- **[FAQ: AOT trimming warnings](../faq#aot-trimming-warnings)** — common issues and fixes
