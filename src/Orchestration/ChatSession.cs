// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Represents a complete chat session including user context, conversation history,
/// and any in-progress authentication flow.
/// </summary>
public class ChatSession
{
    /// <summary>Unique identifier for this session.</summary>
    public required string SessionId { get; set; }

    /// <summary>User context and authentication state.</summary>
    public required UserSessionContext UserContext { get; set; }

    /// <summary>History of messages in this conversation.</summary>
    public List<ConversationMessage> ConversationHistory { get; set; } = [];

    /// <summary>Active authentication session, if auth flow is in progress.</summary>
    public AuthSession? AuthSession { get; set; }

    /// <summary>Active utility data session for account queries.</summary>
    public UtilityDataSession? UtilityDataSession { get; set; }

    /// <summary>
    /// Query that was pending before authentication started.
    /// Will be answered after successful authentication.
    /// </summary>
    public string? PendingQuery { get; set; }

    /// <summary>When this session was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this session was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
