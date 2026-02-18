// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Agents.Summarization;

/// <summary>
/// Agent that summarizes conversations for human handoff.
/// Produces concise summaries that help human agents understand context quickly.
/// </summary>
public class SummarizationAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<SummarizationAgent> _logger;

    public SummarizationAgent(
        IChatClient chatClient,
        ILogger<SummarizationAgent> logger)
    {
        _logger = logger;

        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SummarizationAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = BuildInstructions(),
                ResponseFormat = ChatResponseFormat.ForJsonSchema<SummaryResponse>(
                    schemaName: "summary_response",
                    schemaDescription: "Structured summary for human handoff")
            }
        });
    }

    /// <summary>
    /// Summarizes a conversation for human agent handoff.
    /// </summary>
    /// <param name="conversation">Full conversation history as formatted text.</param>
    /// <param name="escalationReason">Why the conversation is being escalated.</param>
    /// <param name="currentQuestion">The current question or request from the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured summary response.</returns>
    public async Task<SummaryResponse> SummarizeAsync(
        string conversation,
        string escalationReason,
        string currentQuestion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Summarizing conversation for handoff. Reason: {Reason}", escalationReason);

        var prompt = $"""
            Summarize this customer support conversation for handoff to a human agent.

            ESCALATION REASON: {escalationReason}

            CURRENT CUSTOMER REQUEST: {currentQuestion}

            CONVERSATION:
            {conversation}

            Provide a concise summary that helps the human agent understand:
            1. What the customer was trying to accomplish
            2. What has been attempted or discussed so far
            3. Any relevant account or context information shared
            4. Why they're being transferred to a human
            """;

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync<SummaryResponse>(
            message: prompt,
            session: session,
            cancellationToken: cancellationToken);

        if (!AgentResponseParser.TryGetResult(response, out var summary, out var error))
        {
            _logger.LogWarning("Failed to parse summary response: {Error}", error);
            // Return a fallback summary
            return new SummaryResponse
            {
                Summary = $"Customer conversation requiring human assistance. Reason: {escalationReason}",
                EscalationReason = escalationReason,
                OriginalQuestion = currentQuestion,
                KeyFacts = []
            };
        }

        _logger.LogInformation("Generated summary for handoff: {Summary}", summary.Summary);
        return summary;
    }

    private static string BuildInstructions()
    {
        return """
            You are a conversation summarization assistant for a utility billing customer support system.
            Your job is to create concise, actionable summaries for human agents who will take over conversations.

            SUMMARY GUIDELINES:
            1. Be concise - human agents need to quickly understand the situation
            2. Focus on actionable information - what does the customer need?
            3. Include any account information shared (but never include sensitive data like full SSN)
            4. Note what the bot has already tried or explained
            5. Highlight any customer frustration or urgency

            RESPONSE FORMAT:
            - summary: 2-4 sentences capturing the essential context
            - escalationReason: Why this is being transferred (customer requested, complex issue, etc.)
            - originalQuestion: The customer's main request or question
            - suggestedDepartment: If applicable, which department should handle this (billing, technical, etc.)
            - keyFacts: List of important facts (verified identity, account status, prior attempts, etc.)
            """;
    }
}

/// <summary>
/// Extension methods for registering the SummarizationAgent.
/// </summary>
public static class SummarizationAgentExtensions
{
    /// <summary>
    /// Adds the SummarizationAgent to the service collection.
    /// </summary>
    public static IServiceCollection AddSummarizationAgent(this IServiceCollection services)
    {
        services.AddSingleton<SummarizationAgent>();
        return services;
    }
}
