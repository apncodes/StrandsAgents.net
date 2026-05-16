// ═══════════════════════════════════════════════════════════════════════════════
// PATTERN: Decomposed Sequential Pipeline with Platform-Managed Durability
// ═══════════════════════════════════════════════════════════════════════════════
//
// This is Stage 1 of 3 in a durable research pipeline.
//
// WHY DECOMPOSE?
// A single Lambda has a 15-minute hard timeout. A research pipeline that calls
// an LLM once per focus area (5 areas × 2 LLM calls each = 10 Bedrock calls)
// can easily take 10-20 minutes. Even within the timeout, if the pipeline fails
// at minute 12, you've lost all prior work and must restart from scratch.
//
// THE PATTERN:
// Each pipeline stage is a separate, stateless Lambda. Step Functions is the
// checkpoint manager — it stores each stage's output and passes it as input to
// the next stage. If Stage 2 fails after 8 minutes of work, Step Functions
// retries Stage 2 only. Stage 1's output is preserved in the execution state.
//
// AGENT ROLE — PlanAgent (Stage 1):
// Receives the user's topic. Uses an LLM to decompose it into a structured
// research plan: 3-5 focus areas with specific research questions for each.
// Returns a ResearchPlan that Stage 2 (ExecuteAgent) will work through.
//
// AGENT KNOWLEDGE:
// This agent has NO knowledge of Stage 2 or Stage 3. It does not call them.
// It simply receives input, does its job, and returns output.
// Step Functions decides what runs next.
// ═══════════════════════════════════════════════════════════════════════════════

using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlanAgent;

var handler = async (WorkflowInput input, ILambdaContext context) =>
{
    context.Logger.LogInformation(
        $"[Stage 1/3 — PlanAgent] Decomposing topic: '{input.Topic}'");

    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            // Claude Sonnet 4.6 — chosen for structured reasoning and reliable JSON output.
            // Planning requires careful decomposition into well-formed focus areas.
            // Each stage in this pipeline can use a different model independently.
            modelId: "us.anthropic.claude-sonnet-4-6"),
        systemPrompt: """
            You are a research planning specialist. Your job is to decompose a broad topic
            into a structured research plan with 3 specific focus areas.

            For each focus area, provide:
            - A clear, specific research question (not just a topic label)
            - Why this question matters for understanding the overall topic

            Respond with ONLY a JSON object in this exact format:
            {
              "topic": "the original topic",
              "objective": "one sentence describing the overall research goal",
              "focusAreas": [
                {
                  "name": "Focus Area Name",
                  "question": "The specific research question to answer",
                  "rationale": "Why this question matters"
                }
              ]
            }
            """);

    var result = await agent.InvokeAsync(
        $"Create a research plan for: {input.Topic}");

    var json = ExtractJson(result.Message);
    context.Logger.LogInformation(
        $"[Stage 1/3 — PlanAgent] Extracted JSON ({json.Length} chars): {json[..Math.Min(500, json.Length)]}");
    context.Logger.LogInformation(
        $"[Stage 1/3 — PlanAgent] Plan produced. Passing to Stage 2 (ExecuteAgent) via Step Functions.");

    var plan = JsonSerializer.Deserialize<ResearchPlan>(json, WorkflowJsonContext.Default.ResearchPlan)
        ?? new ResearchPlan
        {
            Topic = input.Topic,
            Objective = $"Research {input.Topic}",
            FocusAreas =
            [
                new FocusArea { Name = "Overview", Question = $"What is {input.Topic}?", Rationale = "Foundation" }
            ]
        };

    // This return value becomes the input to ExecuteAgent.
    // Step Functions stores it in the execution state — it survives Lambda termination.
    return plan;
};

await LambdaBootstrapBuilder
    .Create(handler, new SourceGeneratorLambdaJsonSerializer<WorkflowJsonContext>())
    .Build()
    .RunAsync();

static string ExtractJson(string text)
{
    // Strip markdown code blocks if present (model may wrap JSON in ```json ... ```)
    var stripped = text;
    var codeBlockStart = text.IndexOf("```");
    if (codeBlockStart >= 0)
    {
        var contentStart = text.IndexOf('\n', codeBlockStart);
        var codeBlockEnd = text.LastIndexOf("```");
        if (contentStart >= 0 && codeBlockEnd > contentStart)
            stripped = text[(contentStart + 1)..codeBlockEnd].Trim();
    }
    var start = stripped.IndexOf('{');
    var end = stripped.LastIndexOf('}');
    return start >= 0 && end > start ? stripped[start..(end + 1)] : "{}";
}

// ── Data contracts ────────────────────────────────────────────────────────────
// These records define the Step Functions execution state.
// Each Lambda receives the previous stage's output as its typed input.
// The contracts are duplicated across Lambda projects intentionally —
// each Lambda is a separate deployment unit with no shared code dependency.

namespace PlanAgent
{
    // Input to Stage 1
    public class WorkflowInput
    {
        [JsonPropertyName("topic")]
        public string Topic { get; set; } = "";
    }

    // Output of Stage 1 / Input to Stage 2
    public class ResearchPlan
    {
        [JsonPropertyName("topic")]
        public string Topic { get; set; } = "";
        [JsonPropertyName("objective")]
        public string Objective { get; set; } = "";
        [JsonPropertyName("focusAreas")]
        public FocusArea[] FocusAreas { get; set; } = [];
    }

    public class FocusArea
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("question")]
        public string Question { get; set; } = "";
        [JsonPropertyName("rationale")]
        public string Rationale { get; set; } = "";
    }

    [JsonSerializable(typeof(WorkflowInput))]
    [JsonSerializable(typeof(ResearchPlan))]
    [JsonSerializable(typeof(FocusArea))]
    [JsonSerializable(typeof(FocusArea[]))]
    public partial class WorkflowJsonContext : JsonSerializerContext { }
}
