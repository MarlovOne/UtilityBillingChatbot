// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Agents.FAQ;

/// <summary>
/// Agent that answers utility billing FAQ questions using a knowledge base.
/// Does not require authentication - handles general billing questions only.
/// </summary>
public class FAQAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<FAQAgent> _logger;

    public FAQAgent(
        IChatClient chatClient,
        string knowledgeBase,
        ILogger<FAQAgent> logger)
    {
        _logger = logger;

        var instructions = BuildInstructions(knowledgeBase);
        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "FAQAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions
            }
        });
    }

    /// <summary>
    /// Answers a user's FAQ question based on the knowledge base.
    /// </summary>
    /// <param name="input">The user's question</param>
    /// <param name="session">Optional session for multi-turn conversations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The agent's response</returns>
    public async Task<FAQResponse> AnswerAsync(
        string input,
        AgentSession? session = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FAQ question: {Input}", input);

        session ??= await _agent.CreateSessionAsync(cancellationToken);

        var response = await _agent.RunAsync(message: input, session: session, cancellationToken: cancellationToken);

        _logger.LogInformation("FAQ response: {Response}", response.Text);

        return new FAQResponse(response.Text ?? string.Empty, session);
    }

    private static string BuildInstructions(string knowledgeBase)
    {
        return $"""
            You are a utility billing customer support assistant. Answer questions
            based ONLY on the following knowledge base. If the answer is not in the
            knowledge base, say "I don't have information about that specific topic."

            Be concise and helpful. If a question is partially covered, answer what
            you can and mention what's not covered.

            KNOWLEDGE BASE:
            {knowledgeBase}

            IMPORTANT RULES:
            1. Never make up information not in the knowledge base
            2. If asked about their specific account (balance, usage, payments),
               explain you'll need to verify their identity first to access that
            3. Keep responses under 200 words unless more detail is requested
            4. For questions about payment arrangements or extensions, explain the
               general policy but note that specific eligibility requires account access
            """;
    }
}

/// <summary>
/// Response from the FAQ agent.
/// </summary>
/// <param name="Text">The response text</param>
/// <param name="Session">The conversation session for follow-up questions</param>
public record FAQResponse(string Text, AgentSession Session);

/// <summary>
/// Extension methods for registering the FAQAgent.
/// </summary>
public static class FAQAgentExtensions
{
    /// <summary>
    /// Adds the FAQAgent to the service collection.
    /// Requires configuration of "FAQKnowledgeBasePath" in configuration.
    /// </summary>
    public static IServiceCollection AddFAQAgent(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var logger = sp.GetRequiredService<ILogger<FAQAgent>>();
            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

            var knowledgeBasePath = config["FAQKnowledgeBasePath"] ?? "Data/faq-knowledge-base.md";
            if (!Path.IsPathRooted(knowledgeBasePath))
            {
                knowledgeBasePath = Path.Combine(AppContext.BaseDirectory, knowledgeBasePath);
            }

            var knowledgeBase = File.ReadAllText(knowledgeBasePath);
            return new FAQAgent(chatClient, knowledgeBase, logger);
        });

        return services;
    }
}
