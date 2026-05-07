using ModelContextProtocol.Client;
using Strands.Core;
using System.Text.Json;

namespace Strands.Tools.Mcp;

/// <summary>
/// Connects to an MCP server, discovers its tools, and exposes them as
/// <see cref="ITool"/> instances for use with the Strands agent.
/// </summary>
/// <remarks>
/// Use the static factory methods <see cref="CreateForStdioAsync"/> and
/// <see cref="CreateForSseAsync"/> to construct instances. The provider
/// must be disposed when the agent session ends to cleanly shut down the
/// underlying MCP transport.
/// </remarks>
public sealed class McpToolProvider : IAsyncDisposable
{
    private readonly IMcpClient _client;
    private IReadOnlyList<ITool>? _cachedTools;

    private McpToolProvider(IMcpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Creates an <see cref="McpToolProvider"/> by launching a local subprocess
    /// and communicating over stdio.
    /// </summary>
    /// <param name="command">The executable to launch (e.g. "npx", "python").</param>
    /// <param name="args">Arguments passed to <paramref name="command"/>.</param>
    /// <param name="ct">Cancellation token for the connection handshake.</param>
    /// <returns>A connected and initialized <see cref="McpToolProvider"/>.</returns>
    public static async Task<McpToolProvider> CreateForStdioAsync(
        string command,
        string[] args,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = args
        });

        var client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct)
            .ConfigureAwait(false);

        return new McpToolProvider(client);
    }

    /// <summary>
    /// Creates an <see cref="McpToolProvider"/> by connecting to a remote server
    /// over Server-Sent Events.
    /// </summary>
    /// <param name="endpoint">The SSE endpoint URI of the MCP server.</param>
    /// <param name="ct">Cancellation token for the connection handshake.</param>
    /// <returns>A connected and initialized <see cref="McpToolProvider"/>.</returns>
    public static async Task<McpToolProvider> CreateForSseAsync(
        Uri endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var transport = new SseClientTransport(new SseClientTransportOptions
        {
            Endpoint = endpoint
        });

        var client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct)
            .ConfigureAwait(false);

        return new McpToolProvider(client);
    }

    /// <summary>
    /// Creates an <see cref="McpToolProvider"/> by connecting to a remote server
    /// over Streamable HTTP using a pre-configured <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// The <paramref name="httpClient"/> is owned by the returned provider and will be
    /// disposed when the provider is disposed. Use this overload when you need to supply
    /// a custom <see cref="HttpClient"/> — for example, one that carries authentication
    /// headers or a signing <see cref="System.Net.Http.DelegatingHandler"/>.
    /// </remarks>
    /// <param name="httpClient">
    /// A pre-configured <see cref="HttpClient"/>. Ownership is transferred to the provider.
    /// </param>
    /// <param name="endpoint">The Streamable HTTP endpoint URI of the MCP server.</param>
    /// <param name="ct">Cancellation token for the connection handshake.</param>
    /// <returns>A connected and initialized <see cref="McpToolProvider"/>.</returns>
    public static async Task<McpToolProvider> CreateForHttpClientAsync(
        HttpClient httpClient,
        Uri endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);

        var transport = new SseClientTransport(
            new SseClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: true);

        var client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct)
            .ConfigureAwait(false);

        return new McpToolProvider(client);
    }

    /// <summary>
    /// Lists all tools exposed by the connected MCP server, wrapping each as an <see cref="ITool"/>.
    /// Results are cached after the first call.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tools discovered on the MCP server.</returns>
    public async Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct = default)
    {
        if (_cachedTools is not null)
            return _cachedTools;

        var mcpTools = await _client.ListToolsAsync(
            serializerOptions: null,
            cancellationToken: ct).ConfigureAwait(false);

        _cachedTools = mcpTools
            .Select(t => (ITool)new McpToolWrapper(_client, t))
            .ToList()
            .AsReadOnly();

        return _cachedTools;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
