// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Models;

namespace UtilityBillingChatbot.Agents;

/// <summary>
/// Agent that classifies utility billing customer questions into routing categories.
/// Categories: BillingFAQ, AccountData, ServiceRequest, OutOfScope, HumanRequested.
/// </summary>
public class ClassifierAgent
{
    private readonly ChatClientAgent _agent;
    private readonly IReadOnlyList<VerifiedQuestion> _verifiedQuestions;
    private readonly ILogger<ClassifierAgent> _logger;

    public ClassifierAgent(
        IChatClient chatClient,
        IReadOnlyList<VerifiedQuestion> verifiedQuestions,
        ILogger<ClassifierAgent> logger)
    {
        _verifiedQuestions = verifiedQuestions;
        _logger = logger;

        var instructions = ClassifierPrompts.BuildInstructions(verifiedQuestions);
        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ClassifierAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<QuestionClassification>()
            }
        });
    }

    /// <summary>
    /// Classifies the user's input into a question category.
    /// </summary>
    /// <param name="input">The user's question</param>
    /// <param name="cancellationToken">Cancellation token (not used by underlying agent but kept for API consistency)</param>
    /// <returns>The classification result, or null if classification failed</returns>
    public async Task<ClassificationResult> ClassifyAsync(string input, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Classifying input: {Length} chars", input.Length);

        // Note: RunAsync<T> doesn't support cancellation tokens directly
        var response = await _agent.RunAsync<QuestionClassification>(input);

        if (!TryGetResult(response, out var classification, out var parseError))
        {
            _logger.LogWarning("Failed to parse classifier response: {Error}", parseError);
            return new ClassificationResult(null, parseError);
        }

        _logger.LogInformation("Classification: {Category}, Confidence: {Confidence:F2}",
            classification.Category, classification.Confidence);

        return new ClassificationResult(classification, null);
    }

    /// <summary>
    /// Finds the verified question that matches the classification, if any.
    /// </summary>
    public VerifiedQuestion? FindMatchingQuestion(QuestionClassification classification)
    {
        if (string.IsNullOrEmpty(classification.QuestionType))
        {
            return null;
        }

        return _verifiedQuestions.FirstOrDefault(q =>
            q.Id.Equals(classification.QuestionType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetResult<T>(
        ChatClientAgentResponse<T> response,
        [NotNullWhen(true)] out T? result,
        out string? error)
    {
        try
        {
            result = response.Result;
            if (result is null)
            {
                error = $"Structured output was null. Raw response: {response.Text}";
                return false;
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            result = default;
            error = $"JSON parsing failed: {ex.Message}. Raw response: {response.Text}";
            return false;
        }
    }
}

/// <summary>
/// Result of a classification operation.
/// </summary>
/// <param name="Classification">The classification result, or null if failed</param>
/// <param name="Error">Error message if classification failed</param>
public record ClassificationResult(QuestionClassification? Classification, string? Error)
{
    public bool IsSuccess => Classification is not null;
}

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

/// <summary>
/// Extension methods for registering the ClassifierAgent.
/// </summary>
public static class ClassifierAgentExtensions
{
    /// <summary>
    /// Adds the ClassifierAgent to the service collection.
    /// </summary>
    public static IServiceCollection AddClassifierAgent(this IServiceCollection services)
    {
        services.AddSingleton<ClassifierAgent>();
        return services;
    }
}
