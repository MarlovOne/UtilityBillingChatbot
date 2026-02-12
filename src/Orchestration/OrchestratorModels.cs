// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Classifier;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Response from the orchestrator after processing a user message.
/// </summary>
public class ChatResponse
{
    /// <summary>The response message to show the user.</summary>
    public required string Message { get; set; }

    /// <summary>The category the question was classified as.</summary>
    public QuestionCategory Category { get; set; }

    /// <summary>Any required action the user must take.</summary>
    public RequiredAction RequiredAction { get; set; }
}

/// <summary>
/// A message in the conversation history.
/// </summary>
public class ConversationMessage
{
    /// <summary>The role of the message sender ("user" or "assistant").</summary>
    public required string Role { get; set; }

    /// <summary>The content of the message.</summary>
    public required string Content { get; set; }

    /// <summary>When the message was sent.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Actions that may be required from the user after a response.
/// </summary>
public enum RequiredAction
{
    /// <summary>No action required, response is complete.</summary>
    None,

    /// <summary>Authentication flow is in progress, awaiting user input.</summary>
    AuthenticationInProgress,

    /// <summary>Authentication failed (locked out or max retries).</summary>
    AuthenticationFailed,

    /// <summary>Request requires human agent assistance.</summary>
    HumanHandoffNeeded,

    /// <summary>Question was unclear, clarification needed.</summary>
    ClarificationNeeded
}

/// <summary>
/// Fire-and-forget package containing all information needed for CSR handoff.
/// This gets logged and "sent to Salesforce" (mock).
/// </summary>
public record HandoffPackage(
    string SessionId,
    string? CustomerName,
    string? AccountNumber,
    string Intent,
    string ConversationSummary,
    TimeSpan ConversationDuration,
    string RecommendedOpening,
    List<ConversationMessage> ConversationHistory
);
