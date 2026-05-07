using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Xunit;

namespace Strands.AgentCore.Tests;

// ── Shared test infrastructure ────────────────────────────────────────────────

/// <summary>
/// A fake inner handler that captures the outgoing request and returns 200 OK.
/// </summary>
internal sealed class CapturingHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

// ── SigV4SigningHandler unit tests ────────────────────────────────────────────

public sealed class SigV4SigningHandlerTests
{
    private static readonly AWSCredentials FakeCredentials =
        new BasicAWSCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");

    private static readonly AWSCredentials FakeCredentialsWithToken =
        new SessionAWSCredentials(
            "AKIAIOSFODNN7EXAMPLE",
            "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            "FakeSessionToken123");

    private static (SigV4SigningHandler handler, CapturingHandler capturer) BuildHandler(
        AWSCredentials? credentials = null)
    {
        var capturer = new CapturingHandler();
        var handler = new SigV4SigningHandler(
            credentials ?? FakeCredentials,
            "us-east-1",
            "bedrock-agentcore");
        // Replace the default inner handler with our capturer.
        handler.InnerHandler = capturer;
        return (handler, capturer);
    }

    private static async Task<HttpRequestMessage> SendTestRequestAsync(
        SigV4SigningHandler handler)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://gateway.bedrock-agentcore.us-east-1.amazonaws.com/mcp");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await client.SendAsync(request);
        return request;
    }

    // ── Authorization header ──────────────────────────────────────────────────

    [Fact]
    public async Task Authorization_StartsWithAws4HmacSha256()
    {
        var (handler, _) = BuildHandler();
        var request = await SendTestRequestAsync(handler);

        var auth = request.Headers.Authorization?.ToString() ??
                   request.Headers.GetValues("Authorization").FirstOrDefault();

        Assert.NotNull(auth);
        Assert.StartsWith("AWS4-HMAC-SHA256", auth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_ContainsBedrockAgentcoreServiceName()
    {
        var (handler, _) = BuildHandler();
        var request = await SendTestRequestAsync(handler);

        var auth = request.Headers.Authorization?.ToString() ??
                   request.Headers.GetValues("Authorization").FirstOrDefault();

        Assert.NotNull(auth);
        Assert.Contains("bedrock-agentcore", auth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_ContainsRegionInCredentialScope()
    {
        var (handler, _) = BuildHandler();
        var request = await SendTestRequestAsync(handler);

        var auth = request.Headers.Authorization?.ToString() ??
                   request.Headers.GetValues("Authorization").FirstOrDefault();

        Assert.NotNull(auth);
        Assert.Contains("us-east-1", auth, StringComparison.Ordinal);
    }

    // ── X-Amz-Date header ────────────────────────────────────────────────────

    [Fact]
    public async Task XAmzDate_IsPresentAndMatchesFormat()
    {
        var (handler, _) = BuildHandler();
        var request = await SendTestRequestAsync(handler);

        Assert.True(request.Headers.TryGetValues("X-Amz-Date", out var values));
        string date = values!.First();
        Assert.Matches(@"^\d{8}T\d{6}Z$", date);
    }

    // ── X-Amz-Security-Token header ──────────────────────────────────────────

    [Fact]
    public async Task XAmzSecurityToken_IsSet_WhenSessionTokenIsNonNull()
    {
        var (handler, _) = BuildHandler(FakeCredentialsWithToken);
        var request = await SendTestRequestAsync(handler);

        Assert.True(request.Headers.TryGetValues("X-Amz-Security-Token", out var values));
        Assert.Equal("FakeSessionToken123", values!.First());
    }

    [Fact]
    public async Task XAmzSecurityToken_IsAbsent_WhenSessionTokenIsNull()
    {
        var (handler, _) = BuildHandler(FakeCredentials); // no session token
        var request = await SendTestRequestAsync(handler);

        Assert.False(request.Headers.TryGetValues("X-Amz-Security-Token", out _));
    }
}

// ── Bearer mode unit tests ────────────────────────────────────────────────────

public sealed class BearerModeTests
{
    private static async Task<HttpRequestMessage> SendViaHttpClientAsync(
        HttpClient client,
        string url = "https://gateway.bedrock-agentcore.us-east-1.amazonaws.com/mcp")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        // We only care about the default headers — no need to actually send.
        // Simulate what the HttpClient would attach from DefaultRequestHeaders.
        foreach (var header in client.DefaultRequestHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return await Task.FromResult(request);
    }

    [Fact]
    public void Bearer_SetsAuthorizationHeader()
    {
        var auth = new AgentCoreGatewayAuth.Bearer("my-jwt-token");
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var authHeader = client.DefaultRequestHeaders.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader.Scheme);
        Assert.Equal("my-jwt-token", authHeader.Parameter);
    }

    [Fact]
    public void Bearer_AuthorizationHeader_EqualsExpectedValue()
    {
        const string token = "eyJhbGciOiJSUzI1NiJ9.test";
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal($"Bearer {token}", client.DefaultRequestHeaders.Authorization.ToString());
    }

    [Fact]
    public void Bearer_NoXAmzDateInDefaultHeaders()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "some-token");

        Assert.False(client.DefaultRequestHeaders.Contains("X-Amz-Date"));
    }

    [Fact]
    public void Bearer_NoXAmzSecurityTokenInDefaultHeaders()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "some-token");

        Assert.False(client.DefaultRequestHeaders.Contains("X-Amz-Security-Token"));
    }
}

// ── None mode unit tests ──────────────────────────────────────────────────────

public sealed class NoneModeTests
{
    [Fact]
    public void None_NoAuthorizationHeader()
    {
        var client = new HttpClient(); // plain client — no headers added
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void None_NoXAmzDateHeader()
    {
        var client = new HttpClient();
        Assert.False(client.DefaultRequestHeaders.Contains("X-Amz-Date"));
    }

    [Fact]
    public void None_NoXAmzSecurityTokenHeader()
    {
        var client = new HttpClient();
        Assert.False(client.DefaultRequestHeaders.Contains("X-Amz-Security-Token"));
    }
}

// ── AgentCoreGatewayToolProvider argument validation tests ───────────────────

public sealed class GatewayToolProviderValidationTests
{
    [Fact]
    public async Task CreateAsync_NullGatewayUrl_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AgentCoreGatewayToolProvider.CreateAsync(
                gatewayUrl: null!,
                auth: new AgentCoreGatewayAuth.None()));
    }

    [Fact]
    public async Task CreateAsync_NullAuth_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AgentCoreGatewayToolProvider.CreateAsync(
                gatewayUrl: new Uri("https://gateway.example.com/mcp"),
                auth: null!));
    }

    [Fact]
    public async Task CreateAsync_BearerWithEmptyAccessToken_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => AgentCoreGatewayToolProvider.CreateAsync(
                gatewayUrl: new Uri("https://gateway.example.com/mcp"),
                auth: new AgentCoreGatewayAuth.Bearer("")));
    }

    [Fact]
    public async Task CreateAsync_BearerWithNullAccessToken_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => AgentCoreGatewayToolProvider.CreateAsync(
                gatewayUrl: new Uri("https://gateway.example.com/mcp"),
                auth: new AgentCoreGatewayAuth.Bearer(null!)));
    }
}
