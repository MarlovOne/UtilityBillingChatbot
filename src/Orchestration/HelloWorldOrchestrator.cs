// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Orchestration.Executors;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Orchestrator that demonstrates the Agent Framework's WorkflowBuilder DAG pattern.
/// Wires ClassifierAgent → AddSwitch → FAQAgent (BillingFAQ) or DefaultHandler (everything else).
/// </summary>
public class HelloWorldOrchestrator(
    ClassifierAgent classifierAgent,
    FAQAgent faqAgent,
    ILogger<HelloWorldOrchestrator> logger) : IChatbotOrchestrator
{
    public async IAsyncEnumerable<ChatEvent> ProcessMessageStreamingAsync(
        string sessionId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogDebug("HelloWorldOrchestrator processing message for session {SessionId}", sessionId);

        // Create fresh executor instances per call (agents have internal mutable state)
        var classifierExecutor = new ClassifierExecutor(classifierAgent);
        var faqExecutor = new FAQExecutor(faqAgent);
        var defaultHandler = new DefaultHandlerExecutor();

        // Build the DAG
        var builder = new WorkflowBuilder(classifierExecutor);
        builder.AddSwitch(classifierExecutor, sw => sw
            .AddCase<ClassifierResult>(
                r => r?.Category == QuestionCategory.BillingFAQ,
                faqExecutor)
            .WithDefault(defaultHandler));
        builder.WithOutputFrom(faqExecutor, defaultHandler);

        var workflow = builder.Build();

        // Execute the workflow
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, userMessage, sessionId: null, ct);

        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                // Extract collected events from the output and replay them
                List<ChatEvent>? collectedEvents = null;

                if (outputEvent.Is<FAQResult>(out var faqResult))
                {
                    logger.LogInformation("FAQ workflow completed (FoundAnswer={FoundAnswer})", faqResult.FoundAnswer);
                    collectedEvents = faqResult.CollectedEvents;
                }
                else if (outputEvent.Is<DefaultHandlerResult>(out var defaultResult))
                {
                    logger.LogInformation("Default handler completed: {Message}", defaultResult.Message);
                    collectedEvents = defaultResult.CollectedEvents;
                }

                if (collectedEvents is not null)
                {
                    foreach (var chatEvent in collectedEvents)
                    {
                        yield return chatEvent;
                    }
                }
            }
        }
    }
}
