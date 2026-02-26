// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Orchestration;
using static UtilityBillingChatbot.Infrastructure.ServiceCollectionExtensions;

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Agent that answers account-specific billing questions for authenticated customers.
/// Requires an authenticated customer ID.
/// Handles payment approval internally during streaming.
/// </summary>
public class UtilityDataAgent
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;
    private readonly IApprovalHandler _approvalHandler;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UtilityDataAgent> _logger;

    public UtilityDataAgent(
        IChatClient chatClient,
        MockCISDatabase cisDatabase,
        IApprovalHandler approvalHandler,
        ILoggerFactory loggerFactory,
        ILogger<UtilityDataAgent> logger)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
        _approvalHandler = approvalHandler;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Streams a utility data query for an authenticated customer.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        UtilityDataSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("UtilityData query for {Customer}: {Count} messages",
            session.Provider.CustomerName, messages.Count);

        // Stream with approval handling
        await foreach (var evt in StreamWithApprovalAsync(messages, session, ct))
        {
            yield return evt;
        }

        _logger.LogInformation("UtilityData response (FoundAnswer={FoundAnswer}) for {Customer} ({Account})",
            session.Provider.FoundAnswer, session.Provider.CustomerName, session.Provider.AccountNumber);

        yield return new AnswerConfidenceEvent(session.Provider.FoundAnswer);
    }

#pragma warning disable MEAI001 // FunctionApprovalRequestContent is experimental
    private async IAsyncEnumerable<ChatEvent> StreamWithApprovalAsync(
        IReadOnlyList<ChatMessage> messages,
        UtilityDataSession session,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in session.Agent.RunStreamingAsync(
            messages, session.AgentSession, cancellationToken: ct))
        {
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextChunk(update.Text);
            }
        }

        // Check for approval requests
        var userInputRequests = updates
            .SelectMany(u => u.Contents)
            .OfType<UserInputRequestContent>()
            .ToList();

        while (userInputRequests.Count > 0)
        {
            var approvalMessages = new List<ChatMessage>();

            foreach (var request in userInputRequests.OfType<FunctionApprovalRequestContent>())
            {
                var prompt = FormatApprovalPrompt(request);
                var approved = await _approvalHandler.RequestApprovalAsync(prompt, ct);

                _logger.LogInformation("Payment approval: {Approved} for {Tool}",
                    approved, request.FunctionCall.Name);

                approvalMessages.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]));
            }

            if (approvalMessages.Count == 0)
                break;

            // Continue streaming with approval responses
            updates.Clear();
            await foreach (var update in session.Agent.RunStreamingAsync(
                approvalMessages, session.AgentSession, cancellationToken: ct))
            {
                updates.Add(update);
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new TextChunk(update.Text);
                }
            }

            userInputRequests = updates
                .SelectMany(u => u.Contents)
                .OfType<UserInputRequestContent>()
                .ToList();
        }
    }

    private static string FormatApprovalPrompt(FunctionApprovalRequestContent request)
    {
        var functionName = request.FunctionCall.Name;
        var args = request.FunctionCall.Arguments;

        if (functionName == "MakePayment" && args is not null)
        {
            if (args.TryGetValue("amount", out var amountObj) &&
                args.TryGetValue("billingPeriod", out var periodObj))
            {
                var amount = Convert.ToDecimal(amountObj);
                var period = periodObj?.ToString() ?? "unknown period";
                return $"I'm about to submit a payment of ${amount:F2} for {period}. Should I proceed?";
            }
        }

        return $"I need your approval to proceed with {functionName}. Should I continue?";
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Creates a new UtilityDataSession from a customer ID.
    /// </summary>
    public async Task<UtilityDataSession> CreateSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = _cisDatabase.FindByIdentifier(customerId)
            ?? throw new InvalidOperationException($"Customer {customerId} not found in CIS database");

        var provider = new UtilityDataContextProvider(
            customer,
            _loggerFactory.CreateLogger<UtilityDataContextProvider>());

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "UtilityDataAgent",
            AIContextProviders = [provider]
        });

        var agentSession = await agent.CreateSessionAsync(cancellationToken);

        _logger.LogInformation("Created UtilityDataSession for {Customer} ({Account})",
            customer.Name, customer.AccountNumber);

        return new UtilityDataSession(agent, agentSession, provider);
    }
}

/// <summary>
/// Holds the agent, session, and provider for a utility data query session.
/// </summary>
public record UtilityDataSession(
    ChatClientAgent Agent,
    AgentSession AgentSession,
    UtilityDataContextProvider Provider);

/// <summary>
/// Extension methods for registering the UtilityDataAgent.
/// </summary>
public static class UtilityDataAgentExtensions
{
    public static IServiceCollection AddUtilityDataAgent(this IServiceCollection services)
    {
        // Note: MockCISDatabase is already registered by AddAuthAgent
        services.AddSingleton(sp =>
            ActivatorUtilities.CreateInstance<UtilityDataAgent>(sp, GetAgentChatClient(sp, "UtilityData")));
        return services;
    }
}
