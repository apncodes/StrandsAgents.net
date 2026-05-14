using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StrandsAgents.SourceGenerator;
using System.Reflection;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Tests that the source generator correctly emits the IToolProvider partial class
/// and the STRAND001 diagnostic for non-partial classes.
/// </summary>
public class SourceGeneratorToolProviderTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Compilation CreateCompilation(string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonElement).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StrandsAgents.Core.ToolAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Memory").Location),
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Runs the generator and returns all generated .g.cs files keyed by filename.
    /// </summary>
    private static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics, Dictionary<string, string> GeneratedFiles) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new ToolGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedFiles = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs"))
            .ToDictionary(
                t => System.IO.Path.GetFileName(t.FilePath),
                t => t.ToString());

        return (outputCompilation, diagnostics, generatedFiles);
    }

    // ── 5.1 IToolProvider partial emitted for partial class ──────────────────

    [Fact]
    public void Generator_PartialClass_EmitsIToolProviderPartial()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public partial class WeatherTools
            {
                [Tool("Returns weather")]
                public string GetWeather(string city) => city;
            }
            """;

        var (_, diagnostics, files) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // The _ToolProvider.g.cs file must be emitted
        Assert.True(files.ContainsKey("WeatherTools_ToolProvider.g.cs"),
            $"Expected WeatherTools_ToolProvider.g.cs. Found: {string.Join(", ", files.Keys)}");

        var providerSource = files["WeatherTools_ToolProvider.g.cs"];
        Assert.Contains("IToolProvider", providerSource);
        Assert.Contains("GetTools()", providerSource);
        Assert.Contains("yield return new WeatherTools_GetWeather_Tool(this)", providerSource);
    }

    // ── 5.2 STRAND001 diagnostic on non-partial class ────────────────────────

    [Fact]
    public void Generator_NonPartialClass_EmitsDiagnosticSTRAND001()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class WeatherTools
            {
                [Tool("Returns weather")]
                public string GetWeather(string city) => city;
            }
            """;

        var (_, diagnostics, _) = RunGenerator(source);

        var strand001 = diagnostics.FirstOrDefault(d => d.Id == "STRAND001");
        Assert.NotNull(strand001);
        Assert.Equal(DiagnosticSeverity.Warning, strand001!.Severity);
        Assert.Contains("WeatherTools", strand001.GetMessage());
    }

    // ── 5.3 Per-method wrapper still emitted when STRAND001 fires ────────────

    [Fact]
    public void Generator_NonPartialClass_StillEmitsWrapperClass()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class WeatherTools
            {
                [Tool("Returns weather")]
                public string GetWeather(string city) => city;
            }
            """;

        var (_, diagnostics, files) = RunGenerator(source);

        // STRAND001 fires
        Assert.Contains(diagnostics, d => d.Id == "STRAND001");

        // But the per-method wrapper is still emitted
        Assert.True(files.ContainsKey("WeatherTools_GetWeather_Tool.g.cs"),
            $"Expected WeatherTools_GetWeather_Tool.g.cs. Found: {string.Join(", ", files.Keys)}");

        // No IToolProvider partial for non-partial class
        Assert.False(files.ContainsKey("WeatherTools_ToolProvider.g.cs"),
            "IToolProvider partial should NOT be emitted for a non-partial class");
    }

    // ── 5.4 Multiple [Tool] methods → correct number of yield returns ─────────

    [Fact]
    public void Generator_MultipleToolMethods_AllYieldedInGetTools()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public partial class WeatherTools
            {
                [Tool("Returns weather")]
                public string GetWeather(string city) => city;

                [Tool("Returns forecast")]
                public string GetForecast(string city) => city;

                [Tool("Returns humidity")]
                public string GetHumidity(string city) => city;
            }
            """;

        var (_, diagnostics, files) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(files.ContainsKey("WeatherTools_ToolProvider.g.cs"));

        var providerSource = files["WeatherTools_ToolProvider.g.cs"];

        // Exactly 3 yield return statements
        var yieldCount = CountOccurrences(providerSource, "yield return new");
        Assert.Equal(3, yieldCount);

        Assert.Contains("yield return new WeatherTools_GetWeather_Tool(this)", providerSource);
        Assert.Contains("yield return new WeatherTools_GetForecast_Tool(this)", providerSource);
        Assert.Contains("yield return new WeatherTools_GetHumidity_Tool(this)", providerSource);
    }

    // ── 5.5 Generated code compiles without errors ────────────────────────────

    [Fact]
    public void Generator_PartialClass_GeneratedCodeHasNoErrors()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public partial class WeatherTools
            {
                [Tool("Returns weather for a city")]
                public string GetWeather(string city) => $"Sunny in {city}";

                [Tool("Returns forecast")]
                public string GetForecast(string city, int days) => $"{days}-day forecast for {city}";
            }
            """;

        var (outputCompilation, diagnostics, files) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(files.ContainsKey("WeatherTools_ToolProvider.g.cs"));

        // The full output compilation (user source + all generated files) must have no errors
        var outputDiagnostics = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(outputDiagnostics);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
