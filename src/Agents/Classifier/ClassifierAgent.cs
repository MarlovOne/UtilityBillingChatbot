// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static UtilityBillingChatbot.Infrastructure.ServiceCollectionExtensions;

namespace UtilityBillingChatbot.Agents.Classifier;

/// <summary>
/// Agent that classifies utility billing customer questions into routing categories.
/// For utility questions, calls ReportClassification tool (no streamed text).
/// For greetings/chitchat, streams a conversational response (no tool call).
/// </summary>
public class ClassifierAgent(
    IChatClient chatClient,
    IReadOnlyList<VerifiedQuestion> verifiedQuestions,
    ILogger<ClassifierAgent> logger) : IStreamingAgent<ClassificationMetadata>
{
    public StreamingResult<ClassificationMetadata> StreamAsync(string input, CancellationToken ct = default)
    {
        logger.LogDebug("Classifying input (streaming): {Length} chars", input.Length);

        var metadataTcs = new TaskCompletionSource<ClassificationMetadata>();

        return new StreamingResult<ClassificationMetadata>
        {
            TextStream = StreamCoreAsync(input, metadataTcs, ct),
            Metadata = metadataTcs.Task
        };
    }

    private async IAsyncEnumerable<string> StreamCoreAsync(
        string input,
        TaskCompletionSource<ClassificationMetadata> metadataTcs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var toolResult = new ClassificationToolResult();

        var agent = chatClient.AsAIAgent(
            name: "ClassifierAgent",
            instructions: ClassifierPrompts.BuildStreamingInstructions(verifiedQuestions),
            tools:
            [
                AIFunctionFactory.Create(toolResult.ReportClassification,
                    description: "Report the classification of a utility billing question. " +
                                 "Call this for any utility billing question (BillingFAQ, AccountData, ServiceRequest, OutOfScope, HumanRequested). " +
                                 "Do NOT call this for greetings, chitchat, or pleasantries.")
            ]);

        var session = await agent.CreateSessionAsync(ct);

        await foreach (var update in agent.RunStreamingAsync(input, session, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }

        logger.LogInformation("Classification: Category={Category}, Confidence={Confidence:F2}",
            toolResult.Metadata.Category, toolResult.Metadata.Confidence);

        metadataTcs.TrySetResult(toolResult.Metadata);
    }

    private class ClassificationToolResult
    {
        public ClassificationMetadata Metadata { get; private set; } = new(null, 0);

        [Description("Report the classification of a utility billing question")]
        public string ReportClassification(
            [Description("Category: BillingFAQ, AccountData, ServiceRequest, OutOfScope, or HumanRequested")]
            string category,
            [Description("Confidence score between 0.0 and 1.0")]
            double confidence,
            [Description("Specific question type ID if it matches a known type, or empty string")]
            string questionType,
            [Description("Brief explanation of the classification")]
            string reasoning)
        {
            var parsed = Enum.TryParse<QuestionCategory>(category, true, out var c)
                ? c
                : QuestionCategory.OutOfScope;
            Metadata = new ClassificationMetadata(parsed, confidence);
            return $"Classification recorded: {category} (confidence: {confidence:F2})";
        }
    }

    /// <summary>
    /// Finds the verified question that matches the classification, if any.
    /// </summary>
    public VerifiedQuestion? FindMatchingQuestion(QuestionCategory category, string? questionType)
    {
        if (string.IsNullOrEmpty(questionType))
        {
            return null;
        }

        return verifiedQuestions.FirstOrDefault(q =>
            q.Id.Equals(questionType, StringComparison.OrdinalIgnoreCase));
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
        services.AddSingleton(sp =>
            ActivatorUtilities.CreateInstance<ClassifierAgent>(sp, GetAgentChatClient(sp, "Classifier")));
        return services;
    }
}
