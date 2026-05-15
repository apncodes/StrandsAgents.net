# StrandsAgents.MultiAgent

Multi-agent orchestration for [Strands Agents .NET](https://github.com/apncodes/StrandsAgents.net).

```bash
dotnet add package StrandsAgents.MultiAgent
```

```csharp
using StrandsAgents.MultiAgent;

// Sequential pipeline — each stage receives the previous output as its prompt
var pipeline = new PipelineOrchestrator([researchAgent, writerAgent, reviewerAgent]);
var result = await pipeline.InvokeAsync("Write a report on quantum computing");

// Parallel fan-out — Task.WhenAll over all agents
var results = await new ParallelOrchestrator([agent1, agent2, agent3])
    .RunAsync("Analyse this from your specialist perspective");

// Graph routing — LLM decides the next node
var graph = new GraphBuilder()
    .AddNode("triage", triageAgent)
    .AddNode("billing", billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", r => r.Message.Contains("billing") ? "billing" : "technical")
    .Build();

// A2A — call a remote agent over HTTP (cross-framework, cross-language)
using var remote = new A2AAgent(new Uri("http://python-service/agent"));
var result = await remote.InvokeAsync("Research this topic");
```
