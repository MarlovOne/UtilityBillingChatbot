// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Agents.Summarization;

/// <summary>
/// Response from the summarization agent containing a conversation summary
/// suitable for human agent handoff.
/// </summary>
public class SummaryResponse
{
    /// <summary>
    /// Concise summary of the conversation for the human agent.
    /// Should include key context, what the customer was trying to accomplish,
    /// and what has been attempted so far.
    /// </summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; set; }

    /// <summary>
    /// The reason for escalation to a human agent.
    /// </summary>
    [JsonPropertyName("escalationReason")]
    public required string EscalationReason { get; set; }

    /// <summary>
    /// The original question or request that led to this conversation.
    /// </summary>
    [JsonPropertyName("originalQuestion")]
    public required string OriginalQuestion { get; set; }

    /// <summary>
    /// Suggested department or specialist for handling this request.
    /// </summary>
    [JsonPropertyName("suggestedDepartment")]
    public string? SuggestedDepartment { get; set; }

    /// <summary>
    /// Key facts extracted from the conversation that may be useful.
    /// </summary>
    [JsonPropertyName("keyFacts")]
    public List<string> KeyFacts { get; set; } = [];
}
