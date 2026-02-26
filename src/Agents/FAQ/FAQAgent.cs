// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Orchestration;
using static UtilityBillingChatbot.Infrastructure.ServiceCollectionExtensions;

namespace UtilityBillingChatbot.Agents.FAQ;

/// <summary>
/// Agent that answers utility billing FAQ questions using a knowledge base.
/// Streams the answer text; reports metadata via ReportAnswerNotFound tool.
/// </summary>
public class FAQAgent : IStreamingAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _knowledgeBase;
    private readonly ILogger<FAQAgent> _logger;

    public FAQAgent(
        IChatClient chatClient,
        string knowledgeBase,
        ILogger<FAQAgent> logger)
    {
        _chatClient = chatClient;
        _knowledgeBase = knowledgeBase;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("FAQ question (streaming): {Count} messages", messages.Count);

        var provider = new FAQContextProvider(_knowledgeBase);
        var agent = CreateAgent(provider);
        var agentSession = await agent.CreateSessionAsync(ct);

        await foreach (var update in agent.RunStreamingAsync(messages, agentSession, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextChunk(update.Text);
            }
        }

        _logger.LogInformation("FAQ response (FoundAnswer={FoundAnswer})", provider.FoundAnswer);

        yield return new AnswerConfidenceEvent(provider.FoundAnswer);
    }

    internal ChatClientAgent CreateAgent(FAQContextProvider provider)
    {
        return _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "FAQAgent",
            AIContextProviders = [provider]
        });
    }
}

/// <summary>
/// Context provider for FAQ agent. Holds the knowledge base and the ReportAnswerNotFound tool.
/// </summary>
public sealed class FAQContextProvider : AIContextProvider
{
    private readonly string _knowledgeBase;

    /// <summary>Defaults to true; set to false by ReportAnswerNotFound tool.</summary>
    public bool FoundAnswer { get; private set; } = true;

    public FAQContextProvider(string knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken ct)
    {
        var instructions = $"""
            You are a utility billing customer support assistant. Answer questions
            based ONLY on the following knowledge base.

            Be concise and helpful. If a question is partially covered, answer what
            you can and mention what's not covered.

            KNOWLEDGE BASE:
            {_knowledgeBase}

            IMPORTANT RULES:
            1. Never make up information not in the knowledge base
            2. If asked about their specific account (balance, usage, payments),
               call the ReportAnswerNotFound tool and explain you'll need to verify
               their identity first to access that
            3. Keep responses under 200 words unless more detail is requested
            4. For questions about payment arrangements or extensions, explain the
               general policy but note that specific eligibility requires account access
            5. If the question is outside your knowledge base, call the
               ReportAnswerNotFound tool before responding
            """;

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions,
            Tools =
            [
                AIFunctionFactory.Create(ReportAnswerNotFound,
                    description: "Call this when the question is outside the knowledge base " +
                                 "or requires account-specific data you cannot access.")
            ]
        });
    }

    [Description("Report that the question cannot be answered from the knowledge base")]
    private string ReportAnswerNotFound()
    {
        FoundAnswer = false;
        return "Noted: this question is outside the FAQ knowledge base.";
    }
}

/// <summary>
/// Extension methods for registering the FAQAgent.
/// </summary>
public static class FAQAgentExtensions
{
    /// <summary>
    /// Adds the FAQAgent to the service collection.
    /// </summary>
    public static IServiceCollection AddFAQAgent(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var knowledgeBasePath = config["FAQKnowledgeBasePath"] ?? "Data/faq-knowledge-base.md";
            if (!Path.IsPathRooted(knowledgeBasePath))
            {
                knowledgeBasePath = Path.Combine(AppContext.BaseDirectory, knowledgeBasePath);
            }

            var knowledgeBase = File.ReadAllText(knowledgeBasePath);
            return ActivatorUtilities.CreateInstance<FAQAgent>(sp,
                GetAgentChatClient(sp, "FAQ"), knowledgeBase);
        });

        return services;
    }
}
