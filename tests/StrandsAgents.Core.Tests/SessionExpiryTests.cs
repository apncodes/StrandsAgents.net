using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Tests for AgentSession.ExpiresAt validation, ISessionManager.DeleteAsync,
/// and expiry enforcement in InMemorySessionManager and FileSessionManager.
/// </summary>
public sealed class AgentSessionExpiryTests
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    // ── AgentSession validation ───────────────────────────────────────────────

    [Fact]
    public void AgentSession_NullExpiresAt_IsValid()
    {
        var session = new AgentSession("s1", [], new Dictionary<string, object?>(), _now, null);
        Assert.Null(session.ExpiresAt);
    }

    [Fact]
    public void AgentSession_ExpiresAtAfterLastUpdated_IsValid()
    {
        var session = new AgentSession("s1", [], new Dictionary<string, object?>(), _now,
            ExpiresAt: _now.AddHours(1));
        Assert.NotNull(session.ExpiresAt);
    }

    [Fact]
    public void AgentSession_ExpiresAtEqualToLastUpdated_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSession("s1", [], new Dictionary<string, object?>(), _now, ExpiresAt: _now));
    }

    [Fact]
    public void AgentSession_ExpiresAtBeforeLastUpdated_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSession("s1", [], new Dictionary<string, object?>(), _now,
                ExpiresAt: _now.AddSeconds(-1)));
    }
}

public sealed class InMemorySessionManagerExpiryTests
{
    private static AgentSession MakeSession(string id, DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentSession(id, [Message.User("hi")], new Dictionary<string, object?>(), now, expiresAt);
    }

    // ── LoadAsync expiry ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NullExpiresAt_ReturnsSession()
    {
        var manager = new InMemorySessionManager();
        var session = MakeSession("s1", expiresAt: null);
        await manager.SaveAsync("s1", session);

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded.SessionId);
    }

    [Fact]
    public async Task LoadAsync_FutureExpiresAt_ReturnsSession()
    {
        var manager = new InMemorySessionManager();
        var session = MakeSession("s1", expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await manager.SaveAsync("s1", session);

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task LoadAsync_ExpiredSession_ReturnsNullAndRemovesEntry()
    {
        var manager = new InMemorySessionManager();
        // Manually construct a session that is already expired
        var pastTime = DateTimeOffset.UtcNow.AddHours(-2);
        var expiredSession = new AgentSession("s1", [], new Dictionary<string, object?>(),
            LastUpdated: pastTime,
            ExpiresAt: pastTime.AddSeconds(1)); // expired 2h ago

        await manager.SaveAsync("s1", expiredSession);

        var loaded = await manager.LoadAsync("s1");

        Assert.Null(loaded);

        // Entry should be removed — second load also returns null
        var secondLoad = await manager.LoadAsync("s1");
        Assert.Null(secondLoad);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingSession_RemovesIt()
    {
        var manager = new InMemorySessionManager();
        await manager.SaveAsync("s1", MakeSession("s1"));

        await manager.DeleteAsync("s1");

        var loaded = await manager.LoadAsync("s1");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentSession_DoesNotThrow()
    {
        var manager = new InMemorySessionManager();

        // Should complete without exception (idempotent)
        await manager.DeleteAsync("does-not-exist");
    }

    [Fact]
    public async Task DeleteAsync_CalledTwice_IsIdempotent()
    {
        var manager = new InMemorySessionManager();
        await manager.SaveAsync("s1", MakeSession("s1"));

        await manager.DeleteAsync("s1");
        await manager.DeleteAsync("s1"); // second call — no exception

        Assert.Null(await manager.LoadAsync("s1"));
    }
}

public sealed class FileSessionManagerExpiryTests : IDisposable
{
    private readonly string _dir;

    public FileSessionManagerExpiryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static AgentSession MakeSession(string id, DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentSession(id, [Message.User("hi")], new Dictionary<string, object?>(), now, expiresAt);
    }

    // ── LoadAsync expiry ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NullExpiresAt_ReturnsSession()
    {
        var manager = new FileSessionManager(_dir);
        await manager.SaveAsync("s1", MakeSession("s1", expiresAt: null));

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task LoadAsync_FutureExpiresAt_ReturnsSession()
    {
        var manager = new FileSessionManager(_dir);
        await manager.SaveAsync("s1", MakeSession("s1", expiresAt: DateTimeOffset.UtcNow.AddHours(1)));

        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task LoadAsync_ExpiredSession_ReturnsNullAndDeletesFile()
    {
        var manager = new FileSessionManager(_dir);
        var pastTime = DateTimeOffset.UtcNow.AddHours(-2);
        var expiredSession = new AgentSession("s1", [], new Dictionary<string, object?>(),
            LastUpdated: pastTime,
            ExpiresAt: pastTime.AddSeconds(1));

        await manager.SaveAsync("s1", expiredSession);
        Assert.True(File.Exists(Path.Combine(_dir, "s1.json")));

        var loaded = await manager.LoadAsync("s1");

        Assert.Null(loaded);
        Assert.False(File.Exists(Path.Combine(_dir, "s1.json")));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingSession_DeletesFile()
    {
        var manager = new FileSessionManager(_dir);
        await manager.SaveAsync("s1", MakeSession("s1"));
        Assert.True(File.Exists(Path.Combine(_dir, "s1.json")));

        await manager.DeleteAsync("s1");

        Assert.False(File.Exists(Path.Combine(_dir, "s1.json")));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentSession_DoesNotThrow()
    {
        var manager = new FileSessionManager(_dir);

        await manager.DeleteAsync("does-not-exist");
    }

    [Fact]
    public async Task DeleteAsync_CalledTwice_IsIdempotent()
    {
        var manager = new FileSessionManager(_dir);
        await manager.SaveAsync("s1", MakeSession("s1"));

        await manager.DeleteAsync("s1");
        await manager.DeleteAsync("s1"); // no exception

        Assert.Null(await manager.LoadAsync("s1"));
    }

    [Fact]
    public async Task RoundTrip_ExpiresAt_PreservedInFile()
    {
        var manager = new FileSessionManager(_dir);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var session = MakeSession("s1", expiresAt: expiresAt);

        await manager.SaveAsync("s1", session);
        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.ExpiresAt);
        // Allow 1 second tolerance for serialization rounding
        Assert.True(Math.Abs((loaded.ExpiresAt!.Value - expiresAt).TotalSeconds) < 1);
    }
}
