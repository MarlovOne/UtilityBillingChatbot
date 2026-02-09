// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Models;

namespace UtilityBillingChatbot.MultiAgent.Agents;

/// <summary>
/// Agent definition for the Classifier Agent that categorizes utility billing questions
/// into routing categories (BillingFAQ, AccountData, ServiceRequest, OutOfScope, HumanRequested).
/// </summary>
public class ClassifierAgentDefinition : IAgentDefinition
{
    private readonly IReadOnlyList<VerifiedQuestion> _verifiedQuestions;

    public ClassifierAgentDefinition(IReadOnlyList<VerifiedQuestion> verifiedQuestions)
    {
        _verifiedQuestions = verifiedQuestions;
    }

    public string Id => "classifier";

    public string Name => "ClassifierAgent";

    public string Description => "Categorizes utility billing customer questions into routing categories";

    public ChatClientAgent Build(IChatClient chatClient)
    {
        var instructions = BuildInstructions();
        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<QuestionClassification>()
            }
        });
    }

    private string BuildInstructions()
    {
        // Take only first 5 verified questions as examples
        var examples = _verifiedQuestions.Take(5).ToList();

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
