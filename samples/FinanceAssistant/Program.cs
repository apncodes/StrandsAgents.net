using Strands.Core;
using Strands.Models.Bedrock;
using Strands.MultiAgent;
using FinanceAssistant;

// FinanceAssistant — equity research swarm demonstrating parallel multi-agent orchestration.
//
// Architecture:
//   4 specialist analyst agents run concurrently (same prompt, distinct tools + system prompts):
//     PriceAnalyst        → FinanceDataTool_GetQuote_Tool
//     FundamentalsAnalyst → FinanceDataTool_GetFinancials_Tool
//     NewsAnalyst         → FinanceDataTool_GetHeadlines_Tool
//     RiskAnalyst         → FinanceDataTool_GetRiskMetrics_Tool
//   Their findings are concatenated and fed to a Synthesis agent.
//   GetStructuredOutputAsync<EquityReport> extracts a typed investment report.
//
// SDK features shown:
//   • ParallelOrchestrator        — 4 agents invoked with Task.WhenAll
//   • [Tool] source generator     — compile-time ITool wrappers, zero reflection
//   • GetStructuredOutputAsync<T> — schema-constrained JSON extraction with 3-attempt retry
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run           (defaults to NVDA)
//   dotnet run -- AAPL



var ticker = args.Length > 0 && args[0] != "." && !args[0].StartsWith('-')
    ? args[0].ToUpperInvariant()
    : "NVDA";

Console.WriteLine(new string('═', 70));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"  Equity Research Swarm — {ticker}");
Console.ResetColor();
Console.WriteLine(new string('═', 70));
Console.WriteLine();

// Bedrock Model
const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
var model = new BedrockModel(region: Region, modelId: ModelId);

// Gemini
// var apikey="";
//var model = new GeminiModel(apiKey: apikey, modelId: "gemini-2.5-flash");


// Single shared tool instance — all generated wrappers reference it.
var financeTools = new FinanceDataTool();

// ── specialist analyst agents ──────────────────────────────────────────────────
//
// Each agent has a single focused tool matching its domain.
// ParallelOrchestrator sends the same prompt to all four; each agent
// uses its own tool and system prompt to produce a specialist view.

var priceAnalyst = new Agent(model,
    systemPrompt: """
        You are a quantitative price analyst covering equity markets.
        The user will tell you the ticker symbol. Call GetQuote with that exact ticker symbol.
        Analyse the price level, daily momentum, volume, and market capitalisation.
        Write a concise 3-4 sentence technical assessment.
        End your response with: Price Trend: Bullish | Neutral | Bearish
        """,
    tools: [new FinanceDataTool_GetQuote_Tool(financeTools)]);

var fundamentalsAnalyst = new Agent(model,
    systemPrompt: """
        You are a fundamental equity analyst.
        The user will tell you the ticker symbol. Call GetFinancials with that exact ticker symbol.
        Evaluate the valuation multiple (P/E), revenue scale and growth trajectory, and profitability.
        Write a concise 3-4 sentence fundamental assessment.
        End your response with: Valuation: Attractive | Fair | Stretched
        """,
    tools: [new FinanceDataTool_GetFinancials_Tool(financeTools)]);

var newsAnalyst = new Agent(model,
    systemPrompt: """
        You are a news and sentiment analyst.
        The user will tell you the ticker symbol. Call GetHeadlines with that exact ticker symbol.
        Assess each headline for its near-term impact — catalyst or headwind — and overall sentiment.
        Write a concise 3-4 sentence sentiment assessment.
        End your response with: Sentiment: Positive | Mixed | Negative
        """,
    tools: [new FinanceDataTool_GetHeadlines_Tool(financeTools)]);

var riskAnalyst = new Agent(model,
    systemPrompt: """
        You are a portfolio risk analyst.
        The user will tell you the ticker symbol. Call GetRiskMetrics with that exact ticker symbol.
        Evaluate market correlation (beta), price volatility, and the 52-week range context.
        Write a concise 3-4 sentence risk assessment.
        End your response with: Risk Rating: Low | Medium | High
        """,
    tools: [new FinanceDataTool_GetRiskMetrics_Tool(financeTools)]);

// ── parallel research phase ─────────────────────────────────────────────────────

Console.WriteLine("Running 4 analyst agents in parallel...");
Console.WriteLine();

var parallel = new ParallelOrchestrator(
    [priceAnalyst, fundamentalsAnalyst, newsAnalyst, riskAnalyst]);

var analyses = await parallel.RunAsync(
    $"The ticker symbol is {ticker}. " +
    $"Retrieve the relevant data for {ticker} using your available tool, " +
    $"then provide your specialist assessment.");

string[] analystLabels = ["Price", "Fundamentals", "News & Sentiment", "Risk"];

for (var i = 0; i < analyses.Count; i++)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"── {analystLabels[i]} Analyst " + new string('─', 50 - analystLabels[i].Length));
    Console.ResetColor();
    Console.WriteLine(analyses[i].Message);
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  tokens: {analyses[i].Usage.Total}");
    Console.ResetColor();
    Console.WriteLine();
}

// ── synthesis phase ─────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(new string('─', 70));
Console.WriteLine("  Synthesis — integrating analyst views");
Console.WriteLine(new string('─', 70));
Console.ResetColor();
Console.WriteLine();

var combinedFindings = $"""
    ANALYST FINDINGS FOR {ticker}

    ## Price Analysis
    {analyses[0].Message}

    ## Fundamentals Analysis
    {analyses[1].Message}

    ## News & Sentiment Analysis
    {analyses[2].Message}

    ## Risk Analysis
    {analyses[3].Message}
    """;

var synthesisAgent = new Agent(model,
    systemPrompt: """
        You are a senior equity research analyst writing the conclusion of an institutional research report.
        You will receive assessments from four specialist analysts covering price, fundamentals, news, and risk.
        Synthesise their views into a coherent 4-5 sentence investment thesis.
        Be direct: state a clear Buy / Hold / Sell recommendation and a 12-month price target with brief rationale.
        Acknowledge the key bull and bear cases in a balanced way.
        """);

var synthesis = await synthesisAgent.InvokeAsync(combinedFindings);

Console.WriteLine(synthesis.Message);
Console.WriteLine();

// ── structured extraction ───────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine(new string('─', 70));
Console.WriteLine("  Structured Report");
Console.WriteLine(new string('─', 70));
Console.ResetColor();
Console.WriteLine();

var extractor = new Agent(model);
var report = await extractor.GetStructuredOutputAsync<EquityReport>(
    $"Extract a structured equity report from this investment thesis for {ticker}:\n\n{synthesis.Message}");

Console.WriteLine($"  Ticker:          {report.Ticker}");
Console.ForegroundColor = report.Recommendation.ToUpperInvariant() switch
{
    "BUY"  => ConsoleColor.Green,
    "SELL" => ConsoleColor.Red,
    _      => ConsoleColor.Yellow,
};
Console.WriteLine($"  Recommendation:  {report.Recommendation}");
Console.ResetColor();
Console.WriteLine($"  Price Target:    {report.PriceTarget}");
Console.WriteLine($"  Thesis:          {report.InvestmentThesis}");
Console.WriteLine($"  Key Risks:       {report.KeyRisks}");
Console.WriteLine($"  Confidence:      {report.ConfidenceScore}/10");
Console.WriteLine();
Console.WriteLine(new string('═', 70));

// ── typed extraction record ─────────────────────────────────────────────────────

/// <summary>Structured equity research report extracted from the synthesis agent's response.</summary>
internal record EquityReport(
    /// <summary>Stock ticker symbol, e.g. NVDA.</summary>
    string Ticker,
    /// <summary>Investment recommendation: Buy, Hold, or Sell.</summary>
    string Recommendation,
    /// <summary>12-month price target, e.g. "$950".</summary>
    string PriceTarget,
    /// <summary>1-2 sentence investment thesis summarising the bull case.</summary>
    string InvestmentThesis,
    /// <summary>Primary risk factors in 1-2 sentences.</summary>
    string KeyRisks,
    /// <summary>Overall analyst confidence from 1 (very low) to 10 (very high).</summary>
    int ConfidenceScore);
