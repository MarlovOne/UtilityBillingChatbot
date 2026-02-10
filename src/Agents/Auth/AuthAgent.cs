// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Agent that verifies customer identity through conversational authentication.
/// Uses security questions (SSN, DOB) to authenticate before account access.
/// </summary>
public class AuthAgent
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;
    private readonly ILogger<AuthAgent> _logger;

    public AuthAgent(
        IChatClient chatClient,
        MockCISDatabase cisDatabase,
        ILogger<AuthAgent> logger)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
        _logger = logger;
    }

    /// <summary>
    /// Runs the authentication conversation.
    /// </summary>
    /// <param name="input">User's message</param>
    /// <param name="session">Optional session for multi-turn auth flow</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Auth response with current state</returns>
    public async Task<AuthResponse> RunAsync(
        string input,
        AuthSession? session = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Auth input: {Input}", input);

        // Create new session with provider if not provided
        session ??= await CreateSessionAsync(cancellationToken);

        var response = await session.Agent.RunAsync(
            message: input,
            session: session.AgentSession,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Auth state: {State}, Customer: {Customer}",
            session.Provider.AuthState, session.Provider.CustomerName);

        return new AuthResponse(
            Text: response.Text ?? string.Empty,
            Session: session,
            AuthState: session.Provider.AuthState,
            CustomerId: session.Provider.CustomerId,
            CustomerName: session.Provider.CustomerName,
            IsAuthenticated: session.Provider.IsAuthenticated);
    }

    /// <summary>
    /// Creates a new authentication session.
    /// </summary>
    public async Task<AuthSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var provider = new AuthenticationContextProvider(_cisDatabase);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "AuthAgent",
            AIContextProviderFactory = (ctx, ct) =>
            {
                // Restore from serialized state if available
                if (ctx.SerializedState.ValueKind == JsonValueKind.Object)
                {
                    return new ValueTask<AIContextProvider>(
                        new AuthenticationContextProvider(_cisDatabase, ctx.SerializedState, ctx.JsonSerializerOptions));
                }
                return new ValueTask<AIContextProvider>(provider);
            }
        });

        var agentSession = await agent.CreateSessionAsync(cancellationToken);

        return new AuthSession(agent, agentSession, provider);
    }
}

/// <summary>
/// Holds the agent, session, and provider for an auth flow.
/// </summary>
public record AuthSession(
    AIAgent Agent,
    AgentSession AgentSession,
    AuthenticationContextProvider Provider);

/// <summary>
/// Response from the auth agent.
/// </summary>
public record AuthResponse(
    string Text,
    AuthSession Session,
    AuthenticationState AuthState,
    string? CustomerId,
    string? CustomerName,
    bool IsAuthenticated);

/// <summary>
/// Extension methods for registering the AuthAgent.
/// </summary>
public static class AuthAgentExtensions
{
    public static IServiceCollection AddAuthAgent(this IServiceCollection services)
    {
        services.AddSingleton<MockCISDatabase>();
        services.AddSingleton<AuthAgent>();
        return services;
    }
}
