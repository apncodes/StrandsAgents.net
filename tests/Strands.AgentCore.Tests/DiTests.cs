using Microsoft.Extensions.DependencyInjection;
using Strands.AgentCore.Extensions;
using Strands.AgentCore.Session;
using Strands.AgentCore.Tools;
using Strands.Core;
using Xunit;

namespace Strands.AgentCore.Tests;

public sealed class DiTests
{
    // ── AddAgentCoreGatewayTools ─────────────────────────────────────────────
    // Note: AddAgentCoreGatewayTools connects to the gateway eagerly at registration time
    // (MCP handshake + tools/list) so that each tool can be registered as ITool.
    // Tests that require a real gateway are marked Skip and run as integration tests.

    [Fact]
    public void AddAgentCoreGatewayTools_NullGatewayUrl_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddAgentCoreGatewayTools(null!, new AgentCoreGatewayAuth.None()));
    }

    [Fact]
    public void AddAgentCoreGatewayTools_NullAuth_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddAgentCoreGatewayTools(
                new Uri("https://gateway.example.com/mcp"), null!));
    }

    [Fact(Skip = "Requires a real AgentCore Gateway — run as integration test with AGENTCORE_GATEWAY_URL set")]
    public void AddAgentCoreGatewayTools_NoneAuth_RegistersToolsAndProvider()
    {
        var gatewayUrl = new Uri(
            Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")
            ?? "https://gateway.bedrock-agentcore.us-east-1.amazonaws.com/mcp");

        var services = new ServiceCollection();
        services.AddAgentCoreGatewayTools(gatewayUrl, new AgentCoreGatewayAuth.None());

        // Provider registered as singleton
        var providerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(AgentCoreGatewayToolProvider));
        Assert.NotNull(providerDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, providerDescriptor.Lifetime);

        // At least one ITool registered (gateway has tools)
        var toolDescriptors = services.Where(d => d.ServiceType == typeof(ITool)).ToList();
        Assert.NotEmpty(toolDescriptors);

        // Returns IServiceCollection for chaining
        var result = services.AddAgentCoreGatewayTools(gatewayUrl, new AgentCoreGatewayAuth.None());
        Assert.Same(services, result);
    }


    [Fact]
    public void AddAgentCoreSessionManager_RegistersISessionManager()
    {
        var services = new ServiceCollection();
        services.AddAgentCoreSessionManager("mem-id-123");

        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<ISessionManager>();

        Assert.IsType<AgentCoreSessionManager>(manager);
    }

    [Fact]
    public void AddAgentCoreMemory_RegistersITool()
    {
        var services = new ServiceCollection();
        services.AddAgentCoreMemory("mem-id-456");

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<AgentCoreMemoryTool>(tools[0]);
    }

    [Fact]
    public void AddAgentCoreBrowser_RegistersITool()
    {
        var services = new ServiceCollection();
        services.AddAgentCoreBrowser();

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<AgentCoreBrowserTool>(tools[0]);
    }

    [Fact]
    public void AddAgentCoreCodeInterpreter_RegistersITool()
    {
        var services = new ServiceCollection();
        services.AddAgentCoreCodeInterpreter();

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<AgentCoreCodeInterpreterTool>(tools[0]);
    }

    [Fact]
    public void AddMultipleAgentCoreTools_AllRegisteredAsITool()
    {
        var services = new ServiceCollection();
        services
            .AddAgentCoreMemory("mem-id")
            .AddAgentCoreBrowser()
            .AddAgentCoreCodeInterpreter();

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t is AgentCoreMemoryTool);
        Assert.Contains(tools, t => t is AgentCoreBrowserTool);
        Assert.Contains(tools, t => t is AgentCoreCodeInterpreterTool);
    }
}
