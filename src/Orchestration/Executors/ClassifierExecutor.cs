// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Agents.Classifier;

namespace UtilityBillingChatbot.Orchestration.Executors;

/// <summary>
/// Workflow executor that runs the ClassifierAgent to completion,
/// collects all ChatEvents, and extracts the classification result
/// for downstream routing.
/// </summary>
public sealed class ClassifierExecutor : Executor<string, ClassifierResult>
{
    private readonly ClassifierAgent _classifierAgent;

    public ClassifierExecutor(ClassifierAgent classifierAgent)
        : base("ClassifierExecutor")
    {
        _classifierAgent = classifierAgent;
    }

    public override async ValueTask<ClassifierResult> HandleAsync(
        string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var events = new List<ChatEvent>();
        QuestionCategory? category = null;
        double confidence = 0;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, message)
        };

        await foreach (var evt in _classifierAgent.StreamAsync(messages, cancellationToken))
        {
            events.Add(evt);
            if (evt is ClassificationEvent ce)
            {
                category = ce.Category;
                confidence = ce.Confidence;
            }
        }

        return new ClassifierResult(category, confidence, message, events);
    }
}
