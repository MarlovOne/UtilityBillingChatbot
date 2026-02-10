// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Agents.Classifier;

/// <summary>
/// Structured output from the Classifier Agent for utility billing questions
/// </summary>
[Description("Classification result for a utility billing customer question")]
public class QuestionClassification
{
    /// <summary>
    /// Category of the user's question
    /// </summary>
    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("The category of the question: BillingFAQ, AccountData, ServiceRequest, OutOfScope, or HumanRequested")]
    public QuestionCategory Category { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    [JsonPropertyName("confidence")]
    [Description("Confidence score between 0.0 and 1.0")]
    public double Confidence { get; set; }

    /// <summary>
    /// Whether the question requires user authentication
    /// </summary>
    [JsonPropertyName("requiresAuth")]
    [Description("Whether the question requires user authentication to answer")]
    public bool RequiresAuth { get; set; }

    /// <summary>
    /// Specific question type from top 20 (for analytics)
    /// </summary>
    [JsonPropertyName("questionType")]
    [Description("Specific question type ID if it matches a known type (e.g., 'payment-options', 'balance-inquiry'), or null if not a known type")]
    public string? QuestionType { get; set; }

    /// <summary>
    /// Brief explanation of the classification decision
    /// </summary>
    [JsonPropertyName("reasoning")]
    [Description("Brief explanation of why this classification was chosen")]
    public string Reasoning { get; set; } = string.Empty;
}
