// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// In-memory implementation of session storage.
/// Suitable for single-instance deployments and development.
/// </summary>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task SaveSessionAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;
        _sessions.AddOrUpdate(session.SessionId, session, (_, _) => session);
        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
