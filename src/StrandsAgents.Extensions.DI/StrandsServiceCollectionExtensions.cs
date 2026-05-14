using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Tools;

namespace StrandsAgents.Extensions.DI;

/// <summary>
/// Extension methods for registering Strands SDK components with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class StrandsServiceCollectionExtensions
{
    // ── Agent ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="IAgent"/> as a transient service backed by <see cref="Agent"/>.
    /// Requires <see cref="IModel"/> to be registered separately via one of the model
    /// registration methods on this class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">
    /// Optional <see cref="AgentConfig"/> to control context window trimming and other
    /// event-loop settings. Uses defaults when <see langword="null"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStrandsAgent(
        this IServiceCollection services,
        AgentConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(config ?? new AgentConfig());

        services.AddTransient<IAgent>(sp =>
        {
            var model = sp.GetRequiredService<IModel>();
            var tools = sp.GetService<IEnumerable<ITool>>();
            var toolProviders = sp.GetService<IEnumerable<IToolProvider>>();
            var conversationManager = sp.GetService<IConversationManager>();
            var sessionManager = sp.GetService<ISessionManager>();
            var hooks = sp.GetService<HookRegistry>();
            var agentConfig = sp.GetRequiredService<AgentConfig>();

            return new Agent(
                model,
                systemPrompt: null,
                tools: tools,
                toolProviders: toolProviders,
                conversationManager: conversationManager,
                sessionManager: sessionManager,
                hooks: hooks,
                config: agentConfig);
        });

        return services;
    }

    // ── Models ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="BedrockModel"/> as the <see cref="IModel"/> singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="region">AWS region. Default: "us-east-1".</param>
    /// <param name="modelId">Bedrock cross-region inference profile ID.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddBedrockModel(
        this IServiceCollection services,
        string region = "us-east-1",
        string modelId = "us.anthropic.claude-sonnet-4-20250514-v1:0")
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IModel>(_ => new BedrockModel(region, modelId));
        return services;
    }

    /// <summary>
    /// Registers <see cref="OpenAICompatibleModel"/> as the <see cref="IModel"/> singleton,
    /// using a named <see cref="HttpClient"/> managed by <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">Base URL of the OpenAI-compatible endpoint.</param>
    /// <param name="apiKey">API key sent as a Bearer token.</param>
    /// <param name="modelId">Model identifier. Default: "gpt-4o".</param>
    /// <param name="httpClientName">
    /// Named client key. Defaults to <see cref="OpenAICompatibleModel.HttpClientName"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOpenAICompatibleModel(
        this IServiceCollection services,
        string baseUrl,
        string apiKey,
        string modelId = "gpt-4o",
        string? httpClientName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseUrl);

        var clientName = httpClientName ?? OpenAICompatibleModel.HttpClientName;
        services.AddHttpClient(clientName);

        services.AddSingleton<IModel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new OpenAICompatibleModel(baseUrl, apiKey, modelId, httpClientFactory: factory);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="AnthropicModel"/> as the <see cref="IModel"/> singleton,
    /// using a named <see cref="HttpClient"/> managed by <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="modelId">Model identifier. Default: "claude-sonnet-4-5".</param>
    /// <param name="httpClientName">
    /// Named client key. Defaults to <see cref="AnthropicModel.HttpClientName"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAnthropicModel(
        this IServiceCollection services,
        string apiKey,
        string modelId = "claude-sonnet-4-5",
        string? httpClientName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiKey);

        var clientName = httpClientName ?? AnthropicModel.HttpClientName;
        services.AddHttpClient(clientName);

        services.AddSingleton<IModel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new AnthropicModel(apiKey, modelId, httpClientFactory: factory);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="GeminiModel"/> as the <see cref="IModel"/> singleton,
    /// using a named <see cref="HttpClient"/> managed by <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">Google AI Studio API key.</param>
    /// <param name="modelId">Model identifier. Default: "gemini-2.0-flash".</param>
    /// <param name="httpClientName">
    /// Named client key. Defaults to <see cref="GeminiModel.HttpClientName"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddGeminiModel(
        this IServiceCollection services,
        string apiKey,
        string modelId = "gemini-2.0-flash",
        string? httpClientName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiKey);

        var clientName = httpClientName ?? GeminiModel.HttpClientName;
        services.AddHttpClient(clientName);

        services.AddSingleton<IModel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new GeminiModel(apiKey, modelId, httpClientFactory: factory);
        });

        return services;
    }

    // ── Tools ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a tool type as an <see cref="ITool"/> in the DI container.
    /// Multiple calls to this method accumulate tools — all registered tools are
    /// resolved by <see cref="AddStrandsAgent"/> via <c>IEnumerable&lt;ITool&gt;</c>.
    /// </summary>
    /// <typeparam name="TTool">The tool implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStrandsTool<TTool>(this IServiceCollection services)
        where TTool : class, ITool
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ITool, TTool>();
        return services;
    }

    /// <summary>
    /// Registers a tool-provider type as an <see cref="IToolProvider"/> in the DI container.
    /// Multiple calls accumulate providers — all are resolved by <see cref="AddStrandsAgent"/>
    /// via <c>IEnumerable&lt;IToolProvider&gt;</c>.
    /// </summary>
    /// <typeparam name="TProvider">
    /// The tool-provider type. Must be a <c>partial class</c> decorated with <c>[Tool]</c>
    /// methods so the source generator can emit the <see cref="IToolProvider"/> implementation.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStrandsToolProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, IToolProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<IToolProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="HttpRequestTool"/> as an <see cref="ITool"/>, creating the
    /// named <see cref="HttpClient"/> it requires via <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddHttpRequestTool(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient(HttpRequestTool.HttpClientName);
        services.AddTransient<ITool>(sp =>
            new HttpRequestTool(sp.GetRequiredService<IHttpClientFactory>()));
        return services;
    }

    /// <summary>
    /// Registers <see cref="FileReadTool"/> as an <see cref="ITool"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="allowedBasePath">Directory within which reads are permitted.</param>
    /// <param name="maxSizeBytes">Maximum file size. Default: 1 MiB.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFileReadTool(
        this IServiceCollection services,
        string allowedBasePath,
        int maxSizeBytes = FileReadTool.DefaultMaxSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ITool>(_ => new FileReadTool(allowedBasePath, maxSizeBytes));
        return services;
    }

    /// <summary>
    /// Registers <see cref="FileWriteTool"/> as an <see cref="ITool"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="allowedBasePath">Directory within which writes are permitted.</param>
    /// <param name="maxContentBytes">Maximum content size. Default: 1 MiB.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFileWriteTool(
        this IServiceCollection services,
        string allowedBasePath,
        int maxContentBytes = FileWriteTool.DefaultMaxContentBytes)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ITool>(_ => new FileWriteTool(allowedBasePath, maxContentBytes));
        return services;
    }

    // ── Workers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <typeparamref name="T"/> as an <see cref="IHostedService"/> (background worker).
    /// </summary>
    /// <typeparam name="T">The worker type, must implement <see cref="IHostedService"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStrandsWorker<T>(this IServiceCollection services)
        where T : class, IHostedService
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<T>();
        return services;
    }

    // ── Session manager ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers the in-memory <see cref="ISessionManager"/> singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStrandsInMemorySessionManager(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISessionManager, InMemorySessionManager>();
        return services;
    }
}
