---
sidebar_position: 2
---

# Deploy to Lambda

Strands Agents .NET agents can be deployed to AWS Lambda as NativeAOT binaries on the `provided.al2023` runtime. The recommended configuration is `arm64` (Graviton2) — smaller binary, faster cold start, ~20% cheaper per GB-second.

## Quick reference

```bash
# 1. Add AOT settings to your .csproj
# <PublishAot>true</PublishAot>
# <InvariantGlobalization>true</InvariantGlobalization>

# 2. Build on Linux for arm64 (required for AOT cross-compilation)
docker run --rm -v $(pwd):/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && \
  dotnet publish -c Release -r linux-arm64 --output /src/publish -p:StripSymbols=true"

# 3. Package (binary must be named 'bootstrap')
cp ./publish/YourApp ./bootstrap
zip -j function.zip bootstrap

# 4. Deploy — arm64 Graviton2 at 1024 MB (sweet spot: 89.6ms avg cold start)
aws lambda create-function \
  --function-name my-agent \
  --runtime provided.al2023 \
  --handler bootstrap \
  --architectures arm64 \
  --role arn:aws:iam::ACCOUNT:role/ROLE \
  --zip-file fileb://function.zip \
  --memory-size 1024 \
  --timeout 30
```

## Why arm64 at 1024 MB?

The arm64 binary is 14 MB vs 25 MB for x86_64. Smaller binary = less to load from storage at cold start. Across 60 measured cold starts on Graviton2, **88% came in under 100ms with an overall average of 93.3ms** — consistent from 512 MB through 2048 MB. The binary uses only ~52 MB at runtime, so you're not paying for memory you don't need.

x86_64 at 1024 MB averaged 119.8ms and never broke 100ms across 20 runs.

For a complete walkthrough, see the **[Deploy to Lambda with AOT tutorial](../tutorials/aot-lambda)**.

For a multi-step durable pipeline, see the **[DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow)**.
