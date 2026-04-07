// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace UtilityBillingChatbot.Tests.Confirmation;

/// <summary>
/// Structured output schema for the resume-turn decision sub-agent.
/// Binary only — no "maybe" escape hatch. The rationale field is captured
/// for audit/debugging; it is not used for control flow.
/// </summary>
public sealed record ConfirmationDecision(
    [property: JsonPropertyName("decision")] Decision Decision,
    [property: JsonPropertyName("rationale")] string Rationale);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Decision
{
    Deny = 0,  // Default value — any parse failure falls through to Deny.
    Approve = 1,
}
