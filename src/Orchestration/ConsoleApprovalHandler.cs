// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Console-based approval handler that prompts for user input.
/// </summary>
public class ConsoleApprovalHandler : IApprovalHandler
{
    private readonly ILogger<ConsoleApprovalHandler> _logger;

    public ConsoleApprovalHandler(ILogger<ConsoleApprovalHandler> logger)
    {
        _logger = logger;
    }

    public async Task<bool> RequestApprovalAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.Write("> ");

        var inputTask = Task.Run(() => Console.ReadLine()?.Trim() ?? string.Empty, cancellationToken);
        var input = await inputTask;

        var approved = IsApprovalResponse(input);

        _logger.LogInformation("Approval request: {Approved} (input: {Input})", approved, input);

        return approved;
    }

    /// <summary>
    /// Interprets natural language response as approval or denial.
    /// </summary>
    private static bool IsApprovalResponse(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        // Empty input = denial (safe default)
        if (string.IsNullOrEmpty(normalized))
            return false;

        // Denial keywords (check first - explicit denial takes precedence)
        string[] denyKeywords = ["no", "cancel", "stop", "don't", "dont", "wait", "nevermind", "never mind", "nope", "nah"];
        if (denyKeywords.Any(k => normalized.Contains(k)))
            return false;

        // Approval keywords
        string[] approveKeywords = ["yes", "yeah", "sure", "ok", "okay", "proceed", "go ahead", "do it", "confirm", "yep", "yup", "please", "approved"];
        return approveKeywords.Any(k => normalized.Contains(k));
    }
}
