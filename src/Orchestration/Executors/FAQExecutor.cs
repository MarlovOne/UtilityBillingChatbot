// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Agents.FAQ;

namespace UtilityBillingChatbot.Orchestration.Executors;

/// <summary>
/// Workflow executor that runs the FAQAgent to completion,
/// collects all ChatEvents, and extracts the answer confidence.
/// </summary>
public sealed class FAQExecutor : Executor<ClassifierResult, FAQResult>
{
    private readonly FAQAgent _faqAgent;

    public FAQExecutor(FAQAgent faqAgent)
        : base("FAQExecutor")
    {
        _faqAgent = faqAgent;
    }

    public override async ValueTask<FAQResult> HandleAsync(
        ClassifierResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var events = new List<ChatEvent>();
        var foundAnswer = true;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, message.OriginalMessage)
        };

        await foreach (var evt in _faqAgent.StreamAsync(messages, cancellationToken))
        {
            events.Add(evt);
            if (evt is AnswerConfidenceEvent ace)
            {
                foundAnswer = ace.FoundAnswer;
            }
        }

        return new FAQResult(foundAnswer, events);
    }
}
