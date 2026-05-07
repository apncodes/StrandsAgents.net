using System.Net.Http.Headers;
using Amazon.Runtime;
using Strands.Core;
using Strands.Tools.Mcp;

namespace Strands.AgentCore;

/// <summary>
/// Connects to an Amazon Bedrock AgentCore Gateway endpoint, performs the MCP handshake,
/// and exposes the gateway's tools as <see cref="ITool"/> instances for use with the
/// Strands agent.
/// </summary>
/// <remarks>
/// Use the static factory method <see cref="CreateAsync"/> to construct an instance.
/// The provider must be disposed when the agent session ends to cleanly shut down the
/// underlying HTTP and MCP resources.
/// </remarks>
public sealed class AgentCoreGatewayToolProvider : IAsyncDisposable
{
    private readonly McpToolProvider _inner;
    private int _disposed;

    private AgentCoreGatewayToolProvider(McpToolProvider inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Creates an <see cref="AgentCoreGatewayToolProvider"/> connected to an Amazon Bedrock
    /// AgentCore Gateway endpoint using the specified inbound authorization mode.
    /// </summary>
    /// <param name="gatewayUrl">The AgentCore Gateway MCP endpoint URL.</param>
    /// <param name="auth">
    /// The inbound authorization mode. Use <see cref="AgentCoreGatewayAuth.Bearer"/> for
    /// JWT/OIDC, <see cref="AgentCoreGatewayAuth.Iam"/> for SigV4, or
    /// <see cref="AgentCoreGatewayAuth.None"/> for network-isolated environments.
    /// </param>
    /// <param name="ct">Cancellation token for the connection handshake.</param>
    /// <returns>A connected and initialized <see cref="AgentCoreGatewayToolProvider"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gatewayUrl"/> or <paramref name="auth"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="auth"/> is <see cref="AgentCoreGatewayAuth.Bearer"/> and
    /// the <c>AccessToken</c> is null or empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="auth"/> is <see cref="AgentCoreGatewayAuth.Iam"/> and
    /// the AWS credential chain returns no credentials.
    /// </exception>
    public static async Task<AgentCoreGatewayToolProvider> CreateAsync(
        Uri gatewayUrl,
        AgentCoreGatewayAuth auth,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayUrl);
        ArgumentNullException.ThrowIfNull(auth);

        HttpClient httpClient = auth switch
        {
            AgentCoreGatewayAuth.Bearer bearer => CreateBearerHttpClient(bearer),
            AgentCoreGatewayAuth.Iam iam => CreateIamHttpClient(iam),
            AgentCoreGatewayAuth.None => new HttpClient(),
            _ => throw new InvalidOperationException($"Unknown auth type: {auth.GetType().Name}")
        };

        var inner = await McpToolProvider.CreateForHttpClientAsync(httpClient, gatewayUrl, ct)
            .ConfigureAwait(false);

        return new AgentCoreGatewayToolProvider(inner);
    }

    /// <summary>
    /// Lists all tools exposed by the connected AgentCore Gateway, wrapping each as an
    /// <see cref="ITool"/>. Results are cached after the first call.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tools discovered on the gateway.</returns>
    public Task<IReadOnlyList<ITool>> ListToolsAsync(CancellationToken ct = default)
        => _inner.ListToolsAsync(ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static HttpClient CreateBearerHttpClient(AgentCoreGatewayAuth.Bearer bearer)
    {
        if (string.IsNullOrEmpty(bearer.AccessToken))
            throw new ArgumentException(
                "Bearer AccessToken must be non-null and non-empty.",
                nameof(bearer));

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearer.AccessToken);
        return client;
    }

    private static HttpClient CreateIamHttpClient(AgentCoreGatewayAuth.Iam iam)
    {
        AWSCredentials credentials;
        try
        {
            credentials = FallbackCredentialsFactory.GetCredentials();
        }
        catch (AmazonClientException ex)
        {
            throw new InvalidOperationException(
                "Failed to resolve AWS credentials from the credential chain. " +
                "Ensure that valid credentials are configured (environment variables, " +
                "~/.aws/credentials, or instance metadata).",
                ex);
        }

        var signingHandler = new SigV4SigningHandler(credentials, iam.Region, "bedrock-agentcore");
        return new HttpClient(signingHandler);
    }
}
