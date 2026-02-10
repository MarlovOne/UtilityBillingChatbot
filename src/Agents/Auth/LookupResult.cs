// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Result from a customer lookup by identifier.
/// </summary>
public sealed class LookupResult
{
    /// <summary>Whether a customer was found.</summary>
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    /// <summary>The customer's name, if found.</summary>
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    /// <summary>Next action: "verify" or "not_found".</summary>
    [JsonPropertyName("next_action")]
    public string NextAction { get; set; } = "";

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
