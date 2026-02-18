// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Agents.NextBestAction;

/// <summary>
/// Agent that suggests relevant follow-up questions after resolving user intents.
/// Suggests 0-2 questions based on conversation context and category.
/// </summary>
public class NextBestActionAgent
{
    private readonly ChatClientAgent _agent;
    private readonly IReadOnlyList<VerifiedQuestion> _verifiedQuestions;
    private readonly ILogger<NextBestActionAgent> _logger;

    public NextBestActionAgent(
        IChatClient chatClient,
        IReadOnlyList<VerifiedQuestion> verifiedQuestions,
        ILogger<NextBestActionAgent> logger)
    {
        _verifiedQuestions = verifiedQuestions;
        _logger = logger;

        var instructions = BuildInstructions(verifiedQuestions);
        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "NextBestActionAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<NextBestActionResult>()
            }
        });
    }

    /// <summary>
    /// Suggests follow-up questions based on conversation context.
    /// </summary>
    /// <param name="conversationHistory">The conversation history.</param>
    /// <param name="category">The category of the just-answered question.</param>
    /// <param name="isAuthenticated">Whether the user is currently authenticated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of suggested follow-up actions (0-2 items).</returns>
    public async Task<List<SuggestedAction>> SuggestAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        QuestionCategory category,
        bool isAuthenticated,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating next best action suggestions for category {Category}", category);

        var prompt = BuildPrompt(conversationHistory, category, isAuthenticated);
        var response = await _agent.RunAsync<NextBestActionResult>(prompt);

        if (!AgentResponseParser.TryGetResult(response, out var result, out var parseError))
        {
            _logger.LogWarning("Failed to parse NextBestAction response: {Error}", parseError);
            return [];
        }

        // Validate suggestions against valid question IDs and auth requirements
        var validSuggestions = ValidateSuggestions(result.Suggestions, isAuthenticated);

        _logger.LogDebug("Generated {Count} valid suggestions", validSuggestions.Count);
        return validSuggestions;
    }

    private static string BuildInstructions(IReadOnlyList<VerifiedQuestion> questions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a next-best-action suggestion agent for a utility billing chatbot.");
        sb.AppendLine("After a user's question is answered, suggest 1-2 relevant follow-up questions they might want to ask.");
        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("- Suggest questions that logically follow from the current conversation");
        sb.AppendLine("- Do not suggest questions the user has already asked");
        sb.AppendLine("- Prefer questions in the same category unless a related category makes sense");
        sb.AppendLine("- Each suggestion must use a QuestionId EXACTLY as shown in the valid list below");
        sb.AppendLine("- IMPORTANT: If user is NOT authenticated, only suggest questions that do NOT require auth");
        sb.AppendLine("- Provide a natural, friendly phrasing for SuggestedQuestion");
        sb.AppendLine("- Return 0 suggestions if no relevant follow-ups exist");
        sb.AppendLine("- Return at most 2 suggestions");
        sb.AppendLine();
        sb.AppendLine("VALID QUESTION IDS:");

        foreach (var q in questions)
        {
            var authNote = q.RequiresAuth ? " [REQUIRES AUTH]" : "";
            sb.AppendLine($"- {q.Id}: {q.Description}{authNote}");
        }

        sb.AppendLine();
        sb.AppendLine("Respond in JSON format with:");
        sb.AppendLine("- suggestions: array of {questionId, suggestedQuestion}");
        sb.AppendLine("- reasoning: brief explanation of why these suggestions are relevant");

        return sb.ToString();
    }

    private static string BuildPrompt(
        IReadOnlyList<ConversationMessage> history,
        QuestionCategory category,
        bool isAuthenticated)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Current category: {category}");
        sb.AppendLine($"User is authenticated: {isAuthenticated}");
        sb.AppendLine();
        sb.AppendLine("Recent conversation:");

        // Include last few exchanges for context
        var recentHistory = history.TakeLast(6).ToList();
        foreach (var msg in recentHistory)
        {
            sb.AppendLine($"{msg.Role}: {msg.Content}");
        }

        sb.AppendLine();
        sb.AppendLine("Based on this conversation, suggest 1-2 relevant follow-up questions the user might want to ask.");

        return sb.ToString();
    }

    private List<SuggestedAction> ValidateSuggestions(
        List<SuggestedAction> suggestions,
        bool isAuthenticated)
    {
        var validQuestionIds = _verifiedQuestions
            .Select(q => q.Id.ToLowerInvariant())
            .ToHashSet();

        var result = new List<SuggestedAction>();

        foreach (var suggestion in suggestions)
        {
            // Check if question ID is valid
            if (!validQuestionIds.Contains(suggestion.QuestionId.ToLowerInvariant()))
            {
                _logger.LogDebug("Filtering out invalid question ID: {Id}", suggestion.QuestionId);
                continue;
            }

            // Check auth requirements if user is not authenticated
            if (!isAuthenticated)
            {
                var question = _verifiedQuestions.FirstOrDefault(q =>
                    q.Id.Equals(suggestion.QuestionId, StringComparison.OrdinalIgnoreCase));

                if (question?.RequiresAuth == true)
                {
                    _logger.LogDebug("Filtering out auth-required question for unauthenticated user: {Id}",
                        suggestion.QuestionId);
                    continue;
                }
            }

            result.Add(suggestion);

            // Cap at 2 suggestions
            if (result.Count >= 2)
                break;
        }

        return result;
    }
}

/// <summary>
/// Extension methods for registering the NextBestActionAgent.
/// </summary>
public static class NextBestActionAgentExtensions
{
    /// <summary>
    /// Adds the NextBestActionAgent to the service collection.
    /// </summary>
    public static IServiceCollection AddNextBestActionAgent(this IServiceCollection services)
    {
        services.AddSingleton<NextBestActionAgent>();
        return services;
    }
}
