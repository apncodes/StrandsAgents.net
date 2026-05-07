using Microsoft.Extensions.DependencyInjection;
using Strands.AgentCore.Session;
using Strands.AgentCore.Tools;
using Strands.Core;

namespace Strands.AgentCore.Extensions;

/// <summary>
/// Extension methods for registering Strands AgentCore components with
/// <see cref="IServiceCollection"/>.
///
/// <para>
/// These methods are additive — they extend the standard Strands.NET DI setup
/// without modifying any existing registrations.
/// </para>
///
/// <para>Three customer journeys:</para>
/// <code>
/// // Journey 1 — pure Strands.NET, no AgentCore package installed
/// builder.Services.AddBedrockModel().AddStrandsAgent();
///
/// // Journey 2 — AgentCore managed services, any hosting
/// builder.Services
///     .AddBedrockModel()
///     .AddAgentCoreSessionManager(memoryId)
///     .AddAgentCoreBrowser()
///     .AddAgentCoreCodeInterpreter()
///     .AddStrandsAgent();
///
/// // Journey 3 — full AgentCore: managed services + Runtime hosting
/// var app = builder.Build();
/// app.MapAgentCoreEndpoints();
/// app.UseAgentCorePort(8080);
/// app.Run();
/// </code>
/// </summary>
public static class AgentCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AgentCoreSessionManager"/> as the <see cref="ISessionManager"/> singleton.
    /// Replaces any previously registered <see cref="ISessionManager"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="memoryId">The AgentCore memory resource ID for session storage.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAgentCoreSessionManager(
        this IServiceCollection services,
        string memoryId,
        string region = "us-east-1")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        services.AddSingleton<ISessionManager>(_ => new AgentCoreSessionManager(memoryId, region));
        return services;
    }

    /// <summary>
    /// Registers <see cref="AgentCoreMemoryTool"/> as an <see cref="ITool"/>.
    /// Enables the agent to explicitly store, retrieve, and delete long-term memories.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="memoryId">The AgentCore memory resource ID.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAgentCoreMemory(
        this IServiceCollection services,
        string memoryId,
        string region = "us-east-1")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        services.AddSingleton<ITool>(_ => new AgentCoreMemoryTool(memoryId, region));
        return services;
    }

    /// <summary>
    /// Registers <see cref="AgentCoreBrowserTool"/> as an <see cref="ITool"/>.
    /// Enables the agent to operate a managed browser sandbox for JS-rendered pages.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAgentCoreBrowser(
        this IServiceCollection services,
        string region = "us-east-1")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITool>(_ => new AgentCoreBrowserTool(region));
        return services;
    }

    /// <summary>
    /// Registers <see cref="AgentCoreCodeInterpreterTool"/> as an <see cref="ITool"/>.
    /// Enables the agent to execute code in a managed, isolated sandbox.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAgentCoreCodeInterpreter(
        this IServiceCollection services,
        string region = "us-east-1")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITool>(_ => new AgentCoreCodeInterpreterTool(region));
        return services;
    }

    /// <summary>
    /// Registers the tools exposed by an Amazon Bedrock AgentCore Gateway as <see cref="ITool"/>
    /// singletons in the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="gatewayUrl">The AgentCore Gateway MCP endpoint URL.</param>
    /// <param name="auth">
    /// The inbound authorization mode. Use <see cref="AgentCoreGatewayAuth.Bearer"/> for JWT/OIDC,
    /// <see cref="AgentCoreGatewayAuth.Iam"/> for SigV4, or <see cref="AgentCoreGatewayAuth.None"/>
    /// for network-isolated environments.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAgentCoreGatewayTools(
        this IServiceCollection services,
        Uri gatewayUrl,
        AgentCoreGatewayAuth auth)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gatewayUrl);
        ArgumentNullException.ThrowIfNull(auth);

        // Connect to the gateway and discover tools eagerly at registration time.
        // This performs the MCP handshake (tools/list) once so each tool can be
        // registered individually as ITool — the pattern AddStrandsAgent expects.
        var provider = AgentCoreGatewayToolProvider.CreateAsync(gatewayUrl, auth)
            .GetAwaiter().GetResult();

        var tools = provider.ListToolsAsync().GetAwaiter().GetResult();

        // Register the provider as a singleton for direct resolution if needed.
        services.AddSingleton(provider);

        // Register each discovered tool as ITool so AddStrandsAgent picks them all up
        // via IEnumerable<ITool> — same pattern as AddAgentCoreBrowser, AddStrandsTool, etc.
        foreach (var tool in tools)
        {
            var captured = tool;
            services.AddSingleton<ITool>(_ => captured);
        }

        return services;
    }
}
