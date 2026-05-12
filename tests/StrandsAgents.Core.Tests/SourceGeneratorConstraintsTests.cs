using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StrandsAgents.SourceGenerator;
using System.Reflection;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Tests that the source generator correctly emits ParameterConstraints in the generated
/// ToolDefinition when [ToolParameterValidation] attributes are present on parameters.
/// </summary>
public class SourceGeneratorConstraintsTests
{
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

    private static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics, string GeneratedSource) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new ToolGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs"))
            .ToList();

        var generatedSource = generatedTrees.Count > 0
            ? generatedTrees[0].ToString()
            : string.Empty;

        return (outputCompilation, diagnostics, generatedSource);
    }

    [Fact]
    public void Generator_NoValidationAttribute_EmitsNullParameterConstraints()
    {
        // When no [ToolParameterValidation] is present, ParameterConstraints should be absent
        // (the ToolDefinition constructor call uses the default null value).
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class SearchTools
            {
                [Tool("Searches for something")]
                public string Search(string query) => query;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // No ParameterConstraints dictionary should be emitted
        Assert.DoesNotContain("ParameterConstraints", generated);
        Assert.DoesNotContain("ToolParameterConstraints", generated);
        // The ToolDefinition call should close with the schema argument directly
        Assert.Contains("RootElement.Clone());", generated);
    }

    [Fact]
    public void Generator_RequiredAttribute_EmitsParameterConstraintsWithRequired()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class SearchTools
            {
                [Tool("Searches for something")]
                public string Search([ToolParameterValidation(Required = true)] string query) => query;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("ToolParameterConstraints", generated);
        Assert.Contains("[\"query\"]", generated);
        Assert.Contains("Required: true", generated);
    }

    [Fact]
    public void Generator_MinLengthAttribute_EmitsMinLengthConstraint()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something")]
                public string DoIt([ToolParameterValidation(MinLength = 3)] string input) => input;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"input\"]", generated);
        Assert.Contains("MinLength: 3", generated);
    }

    [Fact]
    public void Generator_MaxLengthAttribute_EmitsMaxLengthConstraint()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something")]
                public string DoIt([ToolParameterValidation(MaxLength = 200)] string url) => url;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"url\"]", generated);
        Assert.Contains("MaxLength: 200", generated);
    }

    [Fact]
    public void Generator_PatternAttribute_EmitsPatternConstraint()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Fetches a URL")]
                public string Fetch([ToolParameterValidation(Pattern = "^https://")] string url) => url;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"url\"]", generated);
        Assert.Contains("Pattern:", generated);
        Assert.Contains("^https://", generated);
    }

    [Fact]
    public void Generator_AllowedValuesAttribute_EmitsAllowedValuesConstraint()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Sets mode")]
                public string SetMode([ToolParameterValidation(AllowedValues = new[] { "read", "write", "admin" })] string mode) => mode;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"mode\"]", generated);
        Assert.Contains("AllowedValues:", generated);
        Assert.Contains("\"read\"", generated);
        Assert.Contains("\"write\"", generated);
        Assert.Contains("\"admin\"", generated);
    }

    [Fact]
    public void Generator_MultipleConstraints_EmitsAllConstraints()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Fetches content")]
                public string Fetch(
                    [ToolParameterValidation(Required = true, MaxLength = 200, Pattern = "^https://")] string url) => url;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"url\"]", generated);
        Assert.Contains("Required: true", generated);
        Assert.Contains("MaxLength: 200", generated);
        Assert.Contains("Pattern:", generated);
        Assert.Contains("^https://", generated);
    }

    [Fact]
    public void Generator_MultipleParameters_OnlyConstrainedParamsInDictionary()
    {
        // Only the parameter with [ToolParameterValidation] should appear in the dictionary
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something")]
                public string DoIt(
                    [ToolParameterValidation(Required = true)] string constrained,
                    string unconstrained) => constrained + unconstrained;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"constrained\"]", generated);
        // The unconstrained parameter should NOT appear in the constraints dictionary
        Assert.DoesNotContain("[\"unconstrained\"]", generated);
    }

    [Fact]
    public void Generator_MultipleConstrainedParameters_EmitsBothInDictionary()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something")]
                public string DoIt(
                    [ToolParameterValidation(Required = true)] string first,
                    [ToolParameterValidation(MaxLength = 50)] string second) => first + second;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ParameterConstraints", generated);
        Assert.Contains("[\"first\"]", generated);
        Assert.Contains("[\"second\"]", generated);
        Assert.Contains("Required: true", generated);
        Assert.Contains("MaxLength: 50", generated);
    }

    [Fact]
    public void Generator_WithValidationAttribute_ToolDefinitionClosesWithDictionary()
    {
        // Verify the generated ToolDefinition uses the ParameterConstraints named argument form
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something")]
                public string DoIt([ToolParameterValidation(Required = true)] string input) => input;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // The ToolDefinition should use the ParameterConstraints named argument
        Assert.Contains("ParameterConstraints: new Dictionary<string, ToolParameterConstraints>", generated);
        // The schema line should end with a comma (not a closing paren) when constraints follow
        Assert.Contains("RootElement.Clone(),", generated);
    }

    [Fact]
    public void Generator_WithValidationAttribute_GeneratedCodeHasNoErrors()
    {
        // The generated code itself should compile without errors when fed back into Roslyn
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class ContentFetch
            {
                [Tool("Fetches content from a URL")]
                public string Fetch(
                    [ToolParameterValidation(Required = true, MaxLength = 200, Pattern = "^https://")] string url,
                    string? userAgent = null) => url;
            }
            """;

        var (outputCompilation, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(generated);

        // Verify the output compilation itself has no errors
        var outputDiagnostics = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(outputDiagnostics);
    }
}
