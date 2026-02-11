// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Tracks user context and authentication state across the session.
/// </summary>
public class UserSessionContext
{
    /// <summary>Unique identifier for this session.</summary>
    public required string SessionId { get; set; }

    /// <summary>Current authentication state.</summary>
    public AuthenticationState AuthState { get; set; } = AuthenticationState.Anonymous;

    /// <summary>Customer ID if authenticated.</summary>
    public string? CustomerId { get; set; }

    /// <summary>Customer name if authenticated.</summary>
    public string? CustomerName { get; set; }

    /// <summary>When the authenticated session expires.</summary>
    public DateTimeOffset? SessionExpiry { get; set; }

    /// <summary>When the user last interacted with the chatbot.</summary>
    public DateTimeOffset LastInteraction { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns true if the user is currently authenticated and the session hasn't expired.
    /// </summary>
    public bool IsAuthenticated =>
        AuthState == AuthenticationState.Authenticated &&
        SessionExpiry.HasValue &&
        SessionExpiry.Value > DateTimeOffset.UtcNow;
}
