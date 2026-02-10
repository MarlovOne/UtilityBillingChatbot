// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Auth;

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Agent that answers account-specific billing questions for authenticated customers.
/// Requires a completed AuthSession from AuthAgent.
/// </summary>
public class UtilityDataAgent
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;
    private readonly ILogger<UtilityDataAgent> _logger;

    public UtilityDataAgent(
        IChatClient chatClient,
        MockCISDatabase cisDatabase,
        ILogger<UtilityDataAgent> logger)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
        _logger = logger;
    }

    /// <summary>
    /// Runs a utility data query for an authenticated customer.
    /// </summary>
    /// <param name="input">User's question about their account</param>
    /// <param name="session">Existing UtilityDataSession for multi-turn conversations</param>
    /// <param name="authSession">Authenticated AuthSession (required if session is null)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with account data</returns>
    /// <exception cref="InvalidOperationException">Thrown if user is not authenticated</exception>
    public async Task<UtilityDataResponse> RunAsync(
        string input,
        UtilityDataSession? session = null,
        AuthSession? authSession = null,
        CancellationToken cancellationToken = default)
    {
        // Create new session if not provided
        if (session is null)
        {
            if (authSession is null)
            {
                throw new InvalidOperationException(
                    "Either an existing UtilityDataSession or an authenticated AuthSession is required.");
            }

            session = await CreateSessionAsync(authSession, cancellationToken);
        }

        _logger.LogDebug("UtilityData query for {Customer}: {Input}",
            session.Provider.CustomerName, input);

        var response = await session.Agent.RunAsync(
            message: input,
            session: session.AgentSession,
            cancellationToken: cancellationToken);

        _logger.LogInformation("UtilityData response for {Customer} ({Account})",
            session.Provider.CustomerName, session.Provider.AccountNumber);

        return new UtilityDataResponse(
            Text: response.Text ?? string.Empty,
            Session: session,
            CustomerName: session.Provider.CustomerName,
            AccountNumber: session.Provider.AccountNumber);
    }

    /// <summary>
    /// Creates a new UtilityDataSession from an authenticated AuthSession.
    /// </summary>
    /// <param name="authSession">Completed authentication session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New UtilityDataSession</returns>
    /// <exception cref="InvalidOperationException">Thrown if AuthSession is not authenticated</exception>
    public async Task<UtilityDataSession> CreateSessionAsync(
        AuthSession authSession,
        CancellationToken cancellationToken = default)
    {
        if (!authSession.Provider.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "Cannot create UtilityDataSession: AuthSession is not authenticated. " +
                $"Current state: {authSession.Provider.AuthState}");
        }

        var customerId = authSession.Provider.CustomerId
            ?? throw new InvalidOperationException("Authenticated session has no CustomerId");

        var customer = _cisDatabase.FindByIdentifier(customerId)
            ?? throw new InvalidOperationException($"Customer {customerId} not found in CIS database");

        var provider = new UtilityDataContextProvider(customer);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "UtilityDataAgent",
            AIContextProviderFactory = (ctx, ct) =>
                new ValueTask<AIContextProvider>(provider)
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
    AIAgent Agent,
    AgentSession AgentSession,
    UtilityDataContextProvider Provider);

/// <summary>
/// Response from the utility data agent.
/// </summary>
public record UtilityDataResponse(
    string Text,
    UtilityDataSession Session,
    string CustomerName,
    string AccountNumber);

/// <summary>
/// Extension methods for registering the UtilityDataAgent.
/// </summary>
public static class UtilityDataAgentExtensions
{
    public static IServiceCollection AddUtilityDataAgent(this IServiceCollection services)
    {
        // Note: MockCISDatabase is already registered by AddAuthAgent
        services.AddSingleton<UtilityDataAgent>();
        return services;
    }
}
