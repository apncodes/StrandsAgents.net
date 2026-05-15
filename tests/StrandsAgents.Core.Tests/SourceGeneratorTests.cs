using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StrandsAgents.SourceGenerator;
using System.Reflection;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class SourceGeneratorTests
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
    public void Generator_SimpleMethod_EmitsIToolWrapper()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Calculator
            {
                [Tool("Adds two numbers")]
                public double Add(double a, double b) => a + b;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("class Calculator_Add_Tool", generated);
        Assert.Contains(": ITool", generated);
        Assert.Contains("\"Add\"", generated);
        Assert.Contains("\"Adds two numbers\"", generated);
    }

    [Fact]
    public void Generator_MethodWithParameters_EmitsJsonSchema()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class MathTools
            {
                [Tool("Multiplies two numbers")]
                public double Multiply(double x, double y) => x * y;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("\"x\"", generated);
        Assert.Contains("\"y\"", generated);
        Assert.Contains(@"\""number\""", generated);
    }

    [Fact]
    public void Generator_StringParameter_EmitsStringType()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class StringTools
            {
                [Tool("Greets someone")]
                public string Greet(string name) => $"Hello, {name}!";
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(@"\""string\""", generated);
        Assert.Contains("\"name\"", generated);
    }

    [Fact]
    public void Generator_BoolParameter_EmitsBooleanType()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class BoolTools
            {
                [Tool("Toggles a flag")]
                public bool Toggle(bool value) => !value;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(@"\""boolean\""", generated);
    }

    [Fact]
    public void Generator_NameOverride_UsesCustomName()
    {
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class Tools
            {
                [Tool("Does something", Name = "custom_name")]
                public string DoSomething() => "done";
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("\"custom_name\"", generated);
    }

    [Fact]
    public void Generator_NoToolAttribute_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class Plain
            {
                public void NoAttribute() { }
            }
            """;

        var (_, _, generated) = RunGenerator(source);

        Assert.Empty(generated);
    }

    [Fact]
    public void Generator_StringParameter_EmitsGetStringAccessor()
    {
        // Verifies AOT-safe deserialization: GetString() instead of Deserialize<string>()
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class WeatherTools
            {
                [Tool("Gets weather")]
                public string GetWeather(string city) => city;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetString()", generated);
        Assert.DoesNotContain("Deserialize<", generated);
    }

    [Fact]
    public void Generator_DoubleParameter_EmitsGetDoubleAccessor()
    {
        // Verifies AOT-safe deserialization: GetDouble() instead of Deserialize<double>()
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class MathTools
            {
                [Tool("Adds numbers")]
                public double Add(double a, double b) => a + b;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetDouble()", generated);
        Assert.DoesNotContain("Deserialize<", generated);
    }

    [Fact]
    public void Generator_IntParameter_EmitsGetInt32Accessor()
    {
        // Verifies AOT-safe deserialization: GetInt32() instead of Deserialize<int>()
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class ForecastTools
            {
                [Tool("Gets forecast")]
                public string GetForecast(string city, int days) => $"{days}-day forecast for {city}";
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetInt32()", generated);
        Assert.Contains("GetString()", generated);
        Assert.DoesNotContain("Deserialize<", generated);
    }

    [Fact]
    public void Generator_BoolParameter_EmitsGetBooleanAccessor()
    {
        // Verifies AOT-safe deserialization: GetBoolean() instead of Deserialize<bool>()
        var source = """
            using StrandsAgents.Core;
            namespace MyApp;
            public class BoolTools
            {
                [Tool("Toggles a flag")]
                public bool Toggle(bool value) => !value;
            }
            """;

        var (_, diagnostics, generated) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetBoolean()", generated);
        Assert.DoesNotContain("Deserialize<", generated);
    }
}
