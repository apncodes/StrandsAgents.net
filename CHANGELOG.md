# Changelog

All notable changes to Strands Agents .NET are documented here.

---

## [Unreleased]

### Added

- **`IToolProvider` pattern** — classes with `[Tool]`-decorated methods can now be passed directly to the `Agent` constructor without referencing source-generated wrapper type names. Declare the class `partial` and pass it via `toolProviders:`:

  ```csharp
  public partial class MyTools
  {
      [Tool("Returns weather")]
      public string GetWeather(string city) => $"Sunny in {city}";
  }

  var agent = new Agent(model, toolProviders: [new MyTools()]);
  ```

- **`IToolProvider` interface** (`StrandsAgents.Core`) — single contract implemented automatically by the source generator. Users never write `GetTools()` by hand.

- **`STRAND001` diagnostic** — the source generator emits a `Warning` when a class with `[Tool]` methods is not declared `partial`. The per-method wrapper classes are still emitted so existing code keeps compiling.

- **`Agent` constructor `toolProviders` parameter** — new optional `IEnumerable<IToolProvider>? toolProviders` parameter (positioned after `tools`). Both `tools` and `toolProviders` can be supplied simultaneously; all tools are merged into the same registry. All existing call sites compile unchanged.

- **`AddStrandsToolProvider<TProvider>()` DI extension** (`StrandsAgents.Extensions.DI`) — registers a tool-provider type as a transient `IToolProvider`. Multiple calls accumulate providers, all resolved by `AddStrandsAgent()`.

- **`CalculatorTool` declared `partial`** — enables `toolProviders: [new CalculatorTool()]` in user code and in `samples/CliAgent`.

### Changed

- `samples/CliAgent/Program.cs` updated to use `toolProviders: [new CalculatorTool()]` instead of the explicit wrapper form.
- README quickstart updated to show the `partial class` + `toolProviders:` pattern as the primary example, with the old wrapper form retained as a backward-compatibility note.

---

## [0.1.5] — 2025-05-09

### Fixed

- Package README files corrected — all `Strands.*` references updated to `StrandsAgents.*`
- `PackageProjectUrl` and `RepositoryUrl` updated to `https://github.com/apncodes/StrandsAgents.net`
- CI badge in root README updated to new repo URL
- `CONTRIBUTING.md` and `docs/ARCHITECTURE.md` updated to `StrandsAgents.*` names

---

## [0.1.4] — 2025-05-09

This release renames all packages from `Strands.*` to `StrandsAgents.*` to align with the project's brand at [strandsagents.com](https://strandsagents.com). The project was made public only days before this release, so the rename happens early — before any production adoption — to avoid a more disruptive migration later.

### New

- **`GeminiModel`** — Google Gemini support via the Gemini Developer REST API. No third-party SDK required; uses `HttpClient` + `System.Text.Json` consistent with `AnthropicModel` and `OpenAICompatibleModel`. Available in `StrandsAgents.Core`.

```csharp
var model = new GeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");

// DI
services.AddGeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");
```

### Changed

**Package IDs renamed** (pre-1.0 housekeeping — done early to avoid a larger migration later):

| Old | New |
|---|---|
| `Strands.Core` | `StrandsAgents.Core` |
| `Strands.Models.Bedrock` | `StrandsAgents.Models.Bedrock` |
| `Strands.Tools` | `StrandsAgents.Tools` |
| `Strands.SourceGenerator` | `StrandsAgents.SourceGenerator` |
| `Strands.Extensions.DI` | `StrandsAgents.Extensions.DI` |
| `Strands.MultiAgent` | `StrandsAgents.MultiAgent` |
| `Strands.AgentCore` | `StrandsAgents.Runtime` |

The old `Strands.*` packages remain on NuGet as deprecated compatibility shims that forward to the new names. They will stop receiving updates after **November 2025**.

**Namespaces renamed** to match package IDs:

```csharp
// Before
using Strands.Core;
using Strands.Models.Bedrock;
using Strands.MultiAgent;

// After
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.MultiAgent;
```

**OpenTelemetry source and meter names renamed:**

```csharp
// Before
.AddSource("Strands.Agent")
.AddMeter("Strands.Agent")

// After
.AddSource("StrandsAgents.Agent")
.AddMeter("StrandsAgents.Agent")
```

Metric names: `strands.tokens.input` → `strandsagents.tokens.input`, `strands.tokens.output` → `strandsagents.tokens.output`, `strands.tool.calls` → `strandsagents.tool.calls`, `strands.agent.latency` → `strandsagents.agent.latency`.

---

## [0.1.3] — 2025-05-08

- Initial public release.
- Core agent, event loop, tool system, hooks, session management.
- Model providers: `BedrockModel`, `AnthropicModel`, `OpenAICompatibleModel`.
- Multi-agent patterns: pipeline, parallel, graph, A2A.
- AgentCore Runtime hosting and Gateway support.
- 14 sample projects.

---

## [0.1.0] – [0.1.2]

Pre-public development iterations.
