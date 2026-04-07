// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Tests.Confirmation;

public sealed class ConfirmationOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(5);

    /// <remarks>
    /// The resolver itself does not consume this value.
    /// Callers (e.g., a test fixture) are responsible for passing it to the decision agent's
    /// <c>instructions</c> parameter when constructing the agent.
    /// </remarks>
    public string ResumeSystemPrompt { get; init; } = """
        You have proposed a sensitive action and are waiting for the user's confirmation.
        Your only job on this turn is to decide whether the user's reply is an
        unambiguous, specific affirmation of the exact action that was just proposed.

        Return Approve ONLY if the reply clearly confirms the specific proposed action
        (e.g. "yes", "go ahead", "confirm", "do it", "please proceed").

        Return Deny if the reply:
          - declines, hedges, or expresses doubt,
          - asks a question instead of answering,
          - changes topic,
          - proposes modified parameters,
          - is empty, unclear, or could be interpreted two ways.

        When in doubt, Deny. It is always safe to cancel and ask again.
        Output only the ConfirmationDecision schema. Do not call any tools.
        """;

    public static ConfirmationOptions Default { get; } = new();
}
