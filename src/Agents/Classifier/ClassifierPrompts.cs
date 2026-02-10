// Copyright (c) Microsoft. All rights reserved.

using System.Text;

namespace UtilityBillingChatbot.Agents.Classifier;

/// <summary>
/// Prompt building for the classifier agent.
/// </summary>
internal static class ClassifierPrompts
{
    public static string BuildInstructions(IReadOnlyList<VerifiedQuestion> verifiedQuestions)
    {
        // Take only first 5 verified questions as examples
        var examples = verifiedQuestions.Take(5).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("""
            You are a utility billing customer support classifier.

            Categories:
            - BillingFAQ: General questions (no auth needed). Example: "How can I pay my bill?"
            - AccountData: Questions needing customer data (auth required). Example: "What's my balance?"
            - ServiceRequest: Complex requests needing action. Example: "Set up payment arrangement"
            - OutOfScope: Not related to utility billing
            - HumanRequested: Customer asks for human representative

            Known question types (use these IDs for questionType):
            """);

        foreach (var q in examples)
        {
            sb.AppendLine($"- {q.Id}: {q.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("""
            Set confidence 0.0-1.0. If below 0.6, use OutOfScope.
            Respond with JSON only: {"category":"...","confidence":...,"requiresAuth":...,"questionType":"...","reasoning":"..."}
            """);

        return sb.ToString();
    }
}
