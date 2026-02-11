// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Interface for persisting and retrieving chat sessions.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Retrieves a session by its ID.
    /// </summary>
    /// <param name="sessionId">The session ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session if found, or null if not found.</returns>
    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a session.
    /// </summary>
    /// <param name="session">The session to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSessionAsync(ChatSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session by its ID.
    /// </summary>
    /// <param name="sessionId">The session ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
