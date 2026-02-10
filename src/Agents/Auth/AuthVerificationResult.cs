// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Result from a verification attempt (SSN or DOB).
/// </summary>
public sealed class AuthVerificationResult
{
    /// <summary>Whether the verification succeeded.</summary>
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    /// <summary>Number of attempts remaining before lockout.</summary>
    [JsonPropertyName("remaining_attempts")]
    public int RemainingAttempts { get; set; }

    /// <summary>Next action: "ask_ssn", "ask_dob", "complete", "locked_out", "retry", "error".</summary>
    [JsonPropertyName("next_action")]
    public string NextAction { get; set; } = "";

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
