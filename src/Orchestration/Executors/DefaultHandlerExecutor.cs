// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;

namespace UtilityBillingChatbot.Orchestration.Executors;

/// <summary>
/// Workflow executor for non-BillingFAQ categories.
/// For null category (greetings): passes through the classifier's collected events
/// (greeting text is already there). For other categories: appends a "not yet supported" message.
/// </summary>
public sealed class DefaultHandlerExecutor : Executor<ClassifierResult, DefaultHandlerResult>
{
    public DefaultHandlerExecutor()
        : base("DefaultHandlerExecutor")
    {
    }

    public override ValueTask<DefaultHandlerResult> HandleAsync(
        ClassifierResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var events = new List<ChatEvent>(message.CollectedEvents);

        if (message.Category is not null)
        {
            // Non-BillingFAQ category — add "not yet supported" message
            events.Add(new TextChunk(
                $"I'm sorry, but {message.Category} questions are not yet supported in this demo. " +
                "Please try asking a billing FAQ question instead."));
        }
        // For null category (greetings), the classifier's text chunks already contain the greeting

        var resultMessage = message.Category is null
            ? "Greeting handled"
            : $"{message.Category} not yet supported";

        return ValueTask.FromResult(new DefaultHandlerResult(resultMessage, events));
    }
}
