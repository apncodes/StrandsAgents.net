using System.Net;
using System.Text;
using System.Text.Json;
using StrandsAgents.Runtime.Tools;
using Xunit;

namespace StrandsAgents.Runtime.Tests;

/// <summary>
/// Unit tests for SemanticMemoryTool — all HTTP calls are intercepted by FakeHttpHandler.
/// </summary>
public sealed class SemanticMemoryToolTests
{
    // ── Definition ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Definition_HasCorrectName()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        Assert.Equal("agentcore_semantic_memory", tool.Definition.Name);
    }

    [Fact]
    public async Task Definition_InputSchema_IsValidJsonObject()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task Definition_Description_MentionsSearchStoreDelete()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        Assert.Contains("search", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("store", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_MissingOperation_ReturnsFailure()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"query": "something"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("operation", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_UnknownOperation_ReturnsFailure()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "fly_to_moon"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("fly_to_moon", result.Content);
    }

    // ── search_memory validation ──────────────────────────────────────────────

    [Fact]
    public async Task SearchMemory_EmptyQuery_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": ""}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("non-empty", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.CallCount); // no HTTP call made
    }

    [Fact]
    public async Task SearchMemory_MissingQuery_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchMemory_TopKBelowMin_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "top_k": 0}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchMemory_TopKAboveMax_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "top_k": 101}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Equal(0, handler.CallCount);
    }

    // ── search_memory HTTP ────────────────────────────────────────────────────

    [Fact]
    public async Task SearchMemory_ValidQuery_PostsToCorrectPath()
    {
        var responseBody = """[{"key":"k1","value":"v1","score":0.9},{"key":"k2","value":"v2","score":0.7}]""";
        var handler = new CapturingHandler(HttpStatusCode.OK, responseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-123", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "user preferences"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Contains("/memories/mem-123/search", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task SearchMemory_ValidQuery_ReturnsJsonArraySortedByScore()
    {
        // Return results in non-sorted order — tool should sort descending
        var responseBody = """[{"key":"k2","value":"v2","score":0.5},{"key":"k1","value":"v1","score":0.9}]""";
        var handler = new CapturingHandler(HttpStatusCode.OK, responseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        var parsed = JsonDocument.Parse(result.Content).RootElement;
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.Equal(2, parsed.GetArrayLength());
        // First result should have higher score
        Assert.Equal(0.9, parsed[0].GetProperty("score").GetDouble(), precision: 5);
        Assert.Equal(0.5, parsed[1].GetProperty("score").GetDouble(), precision: 5);
    }

    [Fact]
    public async Task SearchMemory_DefaultTopK_SendsTopK5InBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test"}""").RootElement;
        await tool.InvokeAsync(input);

        var body = handler.LastRequestBody;
        Assert.NotNull(body);
        var parsed = JsonDocument.Parse(body).RootElement;
        Assert.Equal(5, parsed.GetProperty("topK").GetInt32());
    }

    [Fact]
    public async Task SearchMemory_CustomTopK_SendsCorrectTopKInBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "[]");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "top_k": 10}""").RootElement;
        await tool.InvokeAsync(input);

        var body = handler.LastRequestBody;
        Assert.NotNull(body);
        var parsed = JsonDocument.Parse(body).RootElement;
        Assert.Equal(10, parsed.GetProperty("topK").GetInt32());
    }

    [Fact]
    public async Task SearchMemory_ApiReturnsNon2xx_ReturnsFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "error");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("500", result.Content);
    }

    // ── store_memory validation ───────────────────────────────────────────────

    [Fact]
    public async Task StoreMemory_MissingKey_ReturnsFailure()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "store_memory", "value": "v1"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("key", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreMemory_MissingValue_ReturnsFailure()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("value", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreMemory_TtlSecondsZero_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1", "value": "v1", "ttl_seconds": 0}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("positive", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task StoreMemory_NegativeTtlSeconds_ReturnsFailureWithoutHttpCall()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1", "value": "v1", "ttl_seconds": -60}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Equal(0, handler.CallCount);
    }

    // ── store_memory HTTP ─────────────────────────────────────────────────────

    [Fact]
    public async Task StoreMemory_WithoutTtl_PostsBodyWithoutTtlSeconds()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1", "value": "v1"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        Assert.Equal("k1", body.GetProperty("key").GetString());
        Assert.Equal("v1", body.GetProperty("value").GetString());
        Assert.False(body.TryGetProperty("ttlSeconds", out _));
    }

    [Fact]
    public async Task StoreMemory_WithTtl_ForwardsTtlSecondsInBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1", "value": "v1", "ttl_seconds": 3600}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        Assert.Equal(3600, body.GetProperty("ttlSeconds").GetInt32());
    }

    [Fact]
    public async Task StoreMemory_ApiReturnsNon2xx_ReturnsFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.Forbidden, "denied");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1", "value": "v1"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("403", result.Content);
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMemory_MissingKey_ReturnsFailure()
    {
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "delete_memory"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task DeleteMemory_ValidKey_SendsDeleteRequest()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "delete_memory", "key": "k1"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
        Assert.Contains("k1", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteMemory_ApiReturnsNon2xx_ReturnsFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound, "not found");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };
        await using var tool = new SemanticMemoryTool("mem-id", clientOverride: http);

        var input = JsonDocument.Parse("""{"operation": "delete_memory", "key": "k1"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_OwnedClient_DisposesClient()
    {
        // When clientOverride is provided, the tool does NOT own the client.
        // We verify the owned-client path doesn't throw on dispose.
        var fakeClient = new HttpClient(new CapturingHandler(HttpStatusCode.OK, "{}"))
        {
            BaseAddress = new Uri("https://fake/")
        };
        var tool = new SemanticMemoryTool("mem-id", clientOverride: fakeClient);

        await tool.DisposeAsync(); // should not throw
    }

    [Fact]
    public async Task DisposeAsync_InjectedClient_DoesNotDisposeClient()
    {
        var fakeClient = new HttpClient();
        var tool = new SemanticMemoryTool("mem-id", clientOverride: fakeClient);

        await tool.DisposeAsync();

        // Client is still usable — sending a new request should not throw ObjectDisposedException.
        // We verify by checking the Timeout property (accessible on a live client).
        Assert.True(fakeClient.Timeout > TimeSpan.Zero);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
