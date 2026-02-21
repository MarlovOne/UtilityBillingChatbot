// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Handles approval requests for sensitive operations.
/// </summary>
public interface IApprovalHandler
{
    /// <summary>
    /// Prompts the user for approval and returns their decision.
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if approved, false if denied.</returns>
    Task<bool> RequestApprovalAsync(string prompt, CancellationToken cancellationToken = default);
}
