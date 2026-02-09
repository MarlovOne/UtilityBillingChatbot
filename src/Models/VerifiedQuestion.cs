// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Models;

/// <summary>
/// Represents a verified question type from the verified-questions.json file.
/// Contains patterns and metadata for classifying and routing customer questions.
/// </summary>
public class VerifiedQuestion
{
    /// <summary>
    /// Unique identifier for the question type (e.g., "payment-options", "balance-inquiry")
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the question type
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Example phrases/patterns that match this question type
    /// </summary>
    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = [];

    /// <summary>
    /// Plugins required to answer this question type (e.g., "CIS", "RAG", "CustomerData")
    /// </summary>
    [JsonPropertyName("requiredPlugins")]
    public List<string> RequiredPlugins { get; set; } = [];

    /// <summary>
    /// Required authentication level: "None", "Basic", or "Elevated"
    /// </summary>
    [JsonPropertyName("requiredAuthLevel")]
    public string RequiredAuthLevel { get; set; } = "None";

    /// <summary>
    /// Fallback message to display if the question cannot be answered
    /// </summary>
    [JsonPropertyName("fallbackMessage")]
    public string? FallbackMessage { get; set; }

    /// <summary>
    /// Whether this question type requires authentication
    /// </summary>
    [JsonIgnore]
    public bool RequiresAuth => RequiredAuthLevel != "None";
}
