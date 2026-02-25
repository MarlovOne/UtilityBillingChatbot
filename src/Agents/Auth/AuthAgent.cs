// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Orchestration;
using static UtilityBillingChatbot.Infrastructure.ServiceCollectionExtensions;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Agent that verifies customer identity through conversational authentication.
/// Uses security questions (SSN, DOB) to authenticate before account access.
/// </summary>
public class AuthAgent : IStreamingAgent
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;
    private readonly ILogger<AuthAgent> _logger;
    private readonly ILogger<AuthenticationContextProvider> _providerLogger;

    public AuthAgent(
        IChatClient chatClient,
        MockCISDatabase cisDatabase,
        ILogger<AuthAgent> logger,
        ILogger<AuthenticationContextProvider> providerLogger)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
        _logger = logger;
        _providerLogger = providerLogger;
    }

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        string input, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Auth input (streaming, new session): {Input}", input);
        var session = await CreateSessionAsync(ct);

        await foreach (var evt in StreamWithSessionAsync(input, session, ct))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Streams the auth flow with an existing session for multi-turn authentication.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        string input, AuthSession session, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Auth input (streaming, existing session): {Input}", input);

        await foreach (var evt in StreamWithSessionAsync(input, session, ct))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<ChatEvent> StreamWithSessionAsync(
        string input,
        AuthSession session,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in session.Agent.RunStreamingAsync(
            input, session.AgentSession, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextChunk(update.Text);
            }
        }

        _logger.LogInformation("Auth state: {State}, Customer: {Customer}",
            session.Provider.AuthState, session.Provider.CustomerName);

        yield return new AuthStateEvent(
            session.Provider.AuthState,
            session.Provider.CustomerId,
            session.Provider.CustomerName);
    }

    /// <summary>
    /// Creates a new authentication session.
    /// </summary>
    public async Task<AuthSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var provider = new AuthenticationContextProvider(_cisDatabase, _providerLogger);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "AuthAgent",
            AIContextProviders = [provider]
        });

        var agentSession = await agent.CreateSessionAsync(cancellationToken);

        return new AuthSession(agent, agentSession, provider);
    }
}

/// <summary>
/// Holds the agent, session, and provider for an auth flow.
/// </summary>
public record AuthSession(
    ChatClientAgent Agent,
    AgentSession AgentSession,
    AuthenticationContextProvider Provider);

/// <summary>
/// Extension methods for registering the AuthAgent.
/// </summary>
public static class AuthAgentExtensions
{
    public static IServiceCollection AddAuthAgent(this IServiceCollection services)
    {
        services.AddSingleton<MockCISDatabase>();
        services.AddSingleton(sp =>
            ActivatorUtilities.CreateInstance<AuthAgent>(sp, GetAgentChatClient(sp, "Auth")));
        return services;
    }
}
