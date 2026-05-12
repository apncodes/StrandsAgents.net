using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StrandsAgents.SourceGenerator;

internal record ToolParameterConstraintsInfo(
    bool Required,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    string[]? AllowedValues);

[Generator]
public sealed class ToolGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var toolMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "StrandsAgents.Core.ToolAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => GetToolMethod(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(toolMethods, static (spc, method) =>
        {
            var source = GenerateToolClass(method);
            spc.AddSource($"{method.GeneratedClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static ToolMethodInfo? GetToolMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method)
            return null;

        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null) return null;

        var description = attr.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;
        var nameOverride = attr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value?.ToString();
        var toolName = nameOverride ?? method.Name;

        var containingType = method.ContainingType;
        var ns = containingType.ContainingNamespace.IsGlobalNamespace
            ? null
            : containingType.ContainingNamespace.ToDisplayString();

        // Detect CancellationToken parameter — it is forwarded from InvokeAsync's ct,
        // not deserialized from the JSON input, so it must be excluded from the schema.
        var hasCancellationToken = method.Parameters.Any(
            p => p.Type.Name == "CancellationToken");

        var parameters = method.Parameters
            .Where(p => p.Type.Name != "CancellationToken")
            .Select(p =>
            {
                var validationAttr = p.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "StrandsAgents.Core.ToolParameterValidationAttribute");

                ToolParameterConstraintsInfo? constraints = null;
                if (validationAttr is not null)
                {
                    var required = validationAttr.NamedArguments.FirstOrDefault(a => a.Key == "Required").Value.Value as bool? ?? false;

                    // MinLength and MaxLength are plain int with -1 as the "not set" sentinel
                    // (int? is not a valid named attribute argument type in C#)
                    var minLengthRaw = validationAttr.NamedArguments.FirstOrDefault(a => a.Key == "MinLength").Value.Value;
                    var minLength = minLengthRaw is int ml && ml >= 0 ? (int?)ml : null;

                    var maxLengthRaw = validationAttr.NamedArguments.FirstOrDefault(a => a.Key == "MaxLength").Value.Value;
                    var maxLength = maxLengthRaw is int mx && mx >= 0 ? (int?)mx : null;

                    var pattern = validationAttr.NamedArguments.FirstOrDefault(a => a.Key == "Pattern").Value.Value as string;

                    // AllowedValues is a string[] — extract from TypedConstant array
                    string[]? allowedValues = null;
                    var allowedValuesArg = validationAttr.NamedArguments.FirstOrDefault(a => a.Key == "AllowedValues");
                    if (!allowedValuesArg.Equals(default(KeyValuePair<string, TypedConstant>))
                        && allowedValuesArg.Value.Kind == TypedConstantKind.Array)
                    {
                        allowedValues = allowedValuesArg.Value.Values
                            .Select(v => v.Value?.ToString() ?? string.Empty)
                            .ToArray();
                    }

                    constraints = new ToolParameterConstraintsInfo(required, minLength, maxLength, pattern, allowedValues);
                }

                return new ToolParameterInfo(
                    p.Name,
                    GetJsonType(p.Type),
                    !p.Type.NullableAnnotation.Equals(NullableAnnotation.Annotated) && !p.HasExplicitDefaultValue,
                    GetCSharpType(p.Type),
                    constraints);
            }).ToList();

        bool isAsync = method.ReturnType.Name is "Task" or "ValueTask";
        bool returnsVoid = method.ReturnsVoid
            || (method.ReturnType.Name == "Task" && method.ReturnType is INamedTypeSymbol { TypeArguments: { Length: 0 } });

        return new ToolMethodInfo(
            ns,
            containingType.Name,
            method.Name,
            toolName,
            description,
            parameters,
            isAsync,
            returnsVoid,
            hasCancellationToken
        );
    }

    private static string GetJsonType(ITypeSymbol type)
    {
        var name = type.Name;
        if (type is INamedTypeSymbol named && named.IsGenericType && named.Name == "Nullable")
            name = named.TypeArguments[0].Name;

        return name switch
        {
            "String" => "string",
            "Int32" or "Int64" or "Int16" => "integer",
            "Double" or "Single" or "Decimal" => "number",
            "Boolean" => "boolean",
            _ => "string"
        };
    }

    private static string GetCSharpType(ITypeSymbol type) => type.ToDisplayString();

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string GenerateToolClass(ToolMethodInfo m)
    {
        var sb = new StringBuilder();
        var className = m.GeneratedClassName;

        // Build JSON schema properties (CancellationToken is excluded — not a JSON param)
        var schemaProps = string.Join(", ", m.Parameters.Select(p =>
            $"\\\"{p.Name}\\\": {{\\\"type\\\": \\\"{p.JsonType}\\\"}}"));

        var requiredItems = m.Parameters.Where(p => p.IsRequired).Select(p => $"\\\"{p.Name}\\\"").ToList();
        var requiredJson = requiredItems.Count > 0
            ? $", \\\"required\\\": [{string.Join(", ", requiredItems)}]"
            : "";

        // Use verbatim string for the actual JSON (embedded in generated code as a string literal)
        var schemaJson = $"{{\\\"type\\\": \\\"object\\\", \\\"properties\\\": {{{schemaProps}}}{requiredJson}}}";

        // Build parameter deserialization (CancellationToken excluded — passed as ct directly)
        var paramDeserialize = string.Join("\n            ", m.Parameters.Select(p =>
            $"var {p.Name} = input.GetProperty(\"{p.Name}\").Deserialize<{p.CSharpType}>();"));

        // Append ct as the last argument when the target method accepts CancellationToken
        var paramArgs = string.Join(", ", m.Parameters.Select(p => p.Name));
        var paramCall = m.HasCancellationToken
            ? (paramArgs.Length > 0 ? $"{paramArgs}, ct" : "ct")
            : paramArgs;

        string invokeBody;
        // Cast through (object?) so the null-conditional works for both value types
        // (which are boxed to object?) and reference types.
        if (!m.ReturnsVoid && m.IsAsync)
            invokeBody = $"var result = await _instance.{m.MethodName}({paramCall});\n            return ToolResult.Success(\"{m.ToolName}\", Convert.ToString(result) ?? string.Empty);";
        else if (!m.ReturnsVoid && !m.IsAsync)
            invokeBody = $"var result = _instance.{m.MethodName}({paramCall});\n            return ToolResult.Success(\"{m.ToolName}\", Convert.ToString(result) ?? string.Empty);";
        else if (m.IsAsync)
            invokeBody = $"await _instance.{m.MethodName}({paramCall});\n            return ToolResult.Success(\"{m.ToolName}\", string.Empty);";
        else
            invokeBody = $"_instance.{m.MethodName}({paramCall});\n            return ToolResult.Success(\"{m.ToolName}\", string.Empty);";

        var escapedDescription = EscapeString(m.Description);
        var targetNamespace = m.Namespace ?? "StrandsAgents.Generated";

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using StrandsAgents.Core;");
        sb.AppendLine();
        sb.AppendLine($"namespace {targetNamespace};");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>Auto-generated ITool wrapper for {m.ContainingTypeName}.{m.MethodName}.</summary>");
        sb.AppendLine($"public sealed class {className} : ITool");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {m.ContainingTypeName} _instance;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}({m.ContainingTypeName} instance) => _instance = instance;");
        sb.AppendLine();

        // Build ParameterConstraints dictionary if any parameters have [ToolParameterValidation]
        var constrainedParams = m.Parameters.Where(p => p.Constraints is not null).ToList();

        if (constrainedParams.Count > 0)
        {
            var constraintEntries = string.Join(",\n            ", constrainedParams.Select(p =>
            {
                var c = p.Constraints!;
                var parts = new List<string>();
                if (c.Required) parts.Add("Required: true");
                if (c.MinLength.HasValue) parts.Add($"MinLength: {c.MinLength.Value}");
                if (c.MaxLength.HasValue) parts.Add($"MaxLength: {c.MaxLength.Value}");
                if (c.Pattern is not null) parts.Add($"Pattern: \"{EscapeString(c.Pattern)}\"");
                if (c.AllowedValues is { Length: > 0 })
                {
                    var vals = string.Join(", ", c.AllowedValues.Select(v => $"\"{EscapeString(v)}\""));
                    parts.Add($"AllowedValues: new[] {{ {vals} }}");
                }
                var ctorArgs = string.Join(", ", parts);
                return $"[\"{p.Name}\"] = new ToolParameterConstraints({ctorArgs})";
            }));

            sb.AppendLine($"    public ToolDefinition Definition {{ get; }} = new(");
            sb.AppendLine($"        \"{m.ToolName}\",");
            sb.AppendLine($"        \"{escapedDescription}\",");
            sb.AppendLine($"        JsonDocument.Parse(\"{schemaJson}\").RootElement.Clone(),");
            sb.AppendLine($"        ParameterConstraints: new Dictionary<string, ToolParameterConstraints>");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            {constraintEntries}");
            sb.AppendLine($"        }});");
        }
        else
        {
            sb.AppendLine($"    public ToolDefinition Definition {{ get; }} = new(");
            sb.AppendLine($"        \"{m.ToolName}\",");
            sb.AppendLine($"        \"{escapedDescription}\",");
            sb.AppendLine($"        JsonDocument.Parse(\"{schemaJson}\").RootElement.Clone());");
        }

        sb.AppendLine();
        sb.AppendLine("    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        if (!string.IsNullOrEmpty(paramDeserialize))
        {
            sb.AppendLine($"            {paramDeserialize}");
        }
        sb.AppendLine($"            {invokeBody}");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return ToolResult.Failure(\"{m.ToolName}\", ex.Message);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

internal record ToolMethodInfo(
    string? Namespace,
    string ContainingTypeName,
    string MethodName,
    string ToolName,
    string Description,
    List<ToolParameterInfo> Parameters,
    bool IsAsync,
    bool ReturnsVoid,
    bool HasCancellationToken)
{
    public string GeneratedClassName => $"{ContainingTypeName}_{MethodName}_Tool";
}

internal record ToolParameterInfo(
    string Name,
    string JsonType,
    bool IsRequired,
    string CSharpType,
    ToolParameterConstraintsInfo? Constraints = null);
