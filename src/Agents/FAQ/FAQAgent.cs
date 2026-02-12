// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
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
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<FAQStructuredOutput>()
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

        var response = await _agent.RunAsync<FAQStructuredOutput>(message: input, session: session);

        var output = response.Result;
        _logger.LogInformation("FAQ response (FoundAnswer={FoundAnswer}): {Response}",
            output?.FoundAnswer ?? false, output?.Response);

        return new FAQResponse(
            Text: output?.Response ?? response.Text ?? string.Empty,
            FoundAnswer: output?.FoundAnswer ?? false,
            Session: session);
    }

    private static string BuildInstructions(string knowledgeBase)
    {
        return $"""
            You are a utility billing customer support assistant. Answer questions
            based ONLY on the following knowledge base.

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
            5. Set foundAnswer to true if you can answer from the knowledge base,
               false if the question is outside your knowledge base
            """;
    }
}

/// <summary>
/// Structured output from the FAQ agent for JSON schema validation.
/// </summary>
public class FAQStructuredOutput
{
    /// <summary>Whether the answer was found in the knowledge base.</summary>
    [JsonPropertyName("foundAnswer")]
    public bool FoundAnswer { get; set; }

    /// <summary>The response text to show the user.</summary>
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
}

/// <summary>
/// Response from the FAQ agent.
/// </summary>
/// <param name="Text">The response text</param>
/// <param name="FoundAnswer">Whether the agent found an answer in the knowledge base</param>
/// <param name="Session">The conversation session for follow-up questions</param>
public record FAQResponse(string Text, bool FoundAnswer, AgentSession Session);

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
