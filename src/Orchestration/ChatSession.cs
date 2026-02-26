// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Captures the full state of an in-progress auth flow for persistence/reconstruction.
/// Null when no auth flow is active.
/// </summary>
public record AuthFlowState(
    AuthenticationState ProviderState,
    int FailedAttempts,
    List<string> VerifiedFactors,
    string? IdentifyingInfo,
    string? CustomerId,
    string? CustomerName);

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

    /// <summary>State of an in-progress authentication flow. Null when no auth is active.</summary>
    public AuthFlowState? AuthFlowState { get; set; }

    /// <summary>
    /// Query that was pending before authentication started.
    /// Will be answered after successful authentication.
    /// </summary>
    public string? PendingQuery { get; set; }

    /// <summary>Category from the last processed message (for post-stream actions).</summary>
    public QuestionCategory? LastCategory { get; set; }

    /// <summary>Required action from the last processed message.</summary>
    public RequiredAction LastRequiredAction { get; set; }

    /// <summary>When this session was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this session was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
