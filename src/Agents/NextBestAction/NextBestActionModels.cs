// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.NextBestAction;

/// <summary>
/// A suggested follow-up question for the user.
/// </summary>
public class SuggestedAction
{
    /// <summary>ID of the verified question this suggestion maps to.</summary>
    public required string QuestionId { get; set; }

    /// <summary>The suggested question text to display to the user.</summary>
    public required string SuggestedQuestion { get; set; }
}

/// <summary>
/// Result from the NextBestActionAgent containing follow-up suggestions.
/// </summary>
public class NextBestActionResult
{
    /// <summary>List of suggested follow-up questions (0-2 items).</summary>
    public List<SuggestedAction> Suggestions { get; set; } = [];

    /// <summary>Agent's reasoning for the suggestions (for debugging).</summary>
    public string? Reasoning { get; set; }
}
