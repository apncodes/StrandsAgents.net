using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace StrandsAgents.Core;

/// <summary>
/// File-backed implementation of <see cref="ISessionManager"/>.
/// Each session is stored as a JSON file named <c>{sessionId}.json</c> in the specified directory.
/// </summary>
public sealed class FileSessionManager : ISessionManager
{
    private readonly string _directory;

    // Use reflection-based resolver so polymorphic ContentBlock and object? state round-trip correctly.
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = false,
    };

    /// <summary>
    /// Initialises a new <see cref="FileSessionManager"/> that stores sessions in <paramref name="directory"/>.
    /// The directory is created if it does not already exist.
    /// </summary>
    public FileSessionManager(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(session);

        var path = FilePath(sessionId);
        var json = JsonSerializer.Serialize(session, _options);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Returns <c>null</c> and deletes the file when the session has expired.</remarks>
    public async Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var path = FilePath(sessionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var session = JsonSerializer.Deserialize<AgentSession>(json, _options);

        if (session is null)
            return null;

        if (session.ExpiresAt.HasValue && session.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            File.Delete(path);
            return null;
        }

        return session;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var path = FilePath(sessionId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string FilePath(string sessionId) =>
        Path.Combine(_directory, $"{sessionId}.json");
}
