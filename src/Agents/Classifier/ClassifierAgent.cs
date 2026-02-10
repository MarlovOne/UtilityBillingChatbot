// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Agents.Classifier;

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
        catch (JsonException)
        {
            // LLM may return JSON wrapped in markdown code blocks - try to extract and parse
            return TryParseFromRawText<T>(response.Text, out result, out error);
        }
    }

    private static bool TryParseFromRawText<T>(
        string? rawText,
        [NotNullWhen(true)] out T? result,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            result = default;
            error = "Response text was empty";
            return false;
        }

        var json = ExtractJsonFromMarkdown(rawText);

        try
        {
            result = JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
            if (result is null)
            {
                error = $"Deserialized result was null. Raw response: {rawText}";
                return false;
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            result = default;
            error = $"JSON parsing failed: {ex.Message}. Raw response: {rawText}";
            return false;
        }
    }

    /// <summary>
    /// Extracts JSON from markdown code blocks if present, otherwise returns the original text.
    /// Handles formats like: ```json\n{...}\n``` or ```\n{...}\n```
    /// </summary>
    private static string ExtractJsonFromMarkdown(string text)
    {
        // Match ```json or ``` followed by content and closing ```
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
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
