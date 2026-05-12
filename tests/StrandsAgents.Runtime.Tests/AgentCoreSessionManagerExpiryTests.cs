using System.Net;
using System.Text;
using System.Text.Json;
using StrandsAgents.Runtime.Session;
using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Runtime.Tests;

/// <summary>
/// Tests for AgentCoreSessionManager expiry enforcement and DeleteAsync.
/// All HTTP calls are intercepted by FakeHttpHandler.
/// </summary>
public sealed class AgentCoreSessionManagerExpiryTests
{
    private static string BuildSessionRecord(
        string sessionId,
        DateTimeOffset lastUpdated,
        DateTimeOffset? expiresAt = null)
    {
        var messages = new List<Message> { Message.User("hello") };
        var state = new Dictionary<string, object?>();

        var record = new
        {
            sessionId,
            messagesJson = JsonSerializer.Serialize(messages, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            lastUpdated,
            expiresAt,
        };

        return JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    // ── LoadAsync expiry ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NullExpiresAt_ReturnsSession()
    {
        var body = BuildSessionRecord("s1", DateTimeOffset.UtcNow, expiresAt: null);
        var handler = new TrackingHandler(HttpStatusCode.OK, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded.SessionId);
    }

    [Fact]
    public async Task LoadAsync_FutureExpiresAt_ReturnsSession()
    {
        var now = DateTimeOffset.UtcNow;
        var body = BuildSessionRecord("s1", now, expiresAt: now.AddHours(1));
        var handler = new TrackingHandler(HttpStatusCode.OK, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task LoadAsync_ExpiredSession_CallsDeleteAndReturnsNull()
    {
        var pastTime = DateTimeOffset.UtcNow.AddHours(-2);
        var body = BuildSessionRecord("s1", pastTime, expiresAt: pastTime.AddSeconds(1));

        // First call (GET) returns the expired session; second call (DELETE) returns 200
        var handler = new MultiResponseHandler(
            (HttpStatusCode.OK, body),
            (HttpStatusCode.OK, "{}"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        var loaded = await manager.LoadAsync("s1");

        Assert.Null(loaded);
        Assert.Equal(2, handler.CallCount); // GET + DELETE
        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var handler = new TrackingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        await manager.DeleteAsync("session-99");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
        Assert.Contains("session-99", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteAsync_Returns404_DoesNotThrow()
    {
        var handler = new TrackingHandler(HttpStatusCode.NotFound, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        // 404 is treated as idempotent — should not throw
        await manager.DeleteAsync("nonexistent");
    }

    // ── SaveAsync with ExpiresAt ──────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_WithExpiresAt_IncludesExpiresAtInBody()
    {
        var handler = new TrackingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(1);
        var session = new AgentSession("s1", [], new Dictionary<string, object?>(), now, expiresAt);

        await manager.SaveAsync("s1", session);

        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        Assert.True(body.TryGetProperty("expiresAt", out var expiresAtEl));
        Assert.NotEqual(JsonValueKind.Null, expiresAtEl.ValueKind);
    }

    [Fact]
    public async Task SaveAsync_WithoutExpiresAt_ExpiresAtIsNullInBody()
    {
        var handler = new TrackingHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake/") };

        await using var manager = new AgentCoreSessionManager("mem-id", clientOverride: http);

        var session = new AgentSession("s1", [], new Dictionary<string, object?>(), DateTimeOffset.UtcNow);

        await manager.SaveAsync("s1", session);

        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        // expiresAt should be present but null
        if (body.TryGetProperty("expiresAt", out var expiresAtEl))
            Assert.Equal(JsonValueKind.Null, expiresAtEl.ValueKind);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class TrackingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MultiResponseHandler(
        params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _index;
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var (status, body) = _index < responses.Length
                ? responses[_index++]
                : responses[^1]; // repeat last response if exhausted

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
