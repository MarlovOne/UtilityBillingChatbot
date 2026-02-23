// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static UtilityBillingChatbot.Infrastructure.ServiceCollectionExtensions;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Agent that verifies customer identity through conversational authentication.
/// Uses security questions (SSN, DOB) to authenticate before account access.
/// </summary>
public class AuthAgent : IStreamingAgent<AuthMetadata>
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

    public StreamingResult<AuthMetadata> StreamAsync(string input, CancellationToken ct = default)
    {
        var metadataTcs = new TaskCompletionSource<AuthMetadata>();

        return new StreamingResult<AuthMetadata>
        {
            TextStream = StreamNewSessionAsync(input, metadataTcs, ct),
            Metadata = metadataTcs.Task
        };
    }

    /// <summary>
    /// Streams the auth flow with an existing session for multi-turn authentication.
    /// </summary>
    public StreamingResult<AuthMetadata> StreamAsync(
        string input, AuthSession session, CancellationToken ct = default)
    {
        _logger.LogDebug("Auth input (streaming, existing session): {Input}", input);

        var metadataTcs = new TaskCompletionSource<AuthMetadata>();

        return new StreamingResult<AuthMetadata>
        {
            TextStream = StreamWithSessionAsync(input, session, metadataTcs, ct),
            Metadata = metadataTcs.Task
        };
    }

    private async IAsyncEnumerable<string> StreamNewSessionAsync(
        string input,
        TaskCompletionSource<AuthMetadata> metadataTcs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("Auth input (streaming, new session): {Input}", input);
        var session = await CreateSessionAsync(ct);

        await foreach (var chunk in StreamWithSessionAsync(input, session, metadataTcs, ct))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<string> StreamWithSessionAsync(
        string input,
        AuthSession session,
        TaskCompletionSource<AuthMetadata> metadataTcs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in session.Agent.RunStreamingAsync(
            input, session.AgentSession, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }

        _logger.LogInformation("Auth state: {State}, Customer: {Customer}",
            session.Provider.AuthState, session.Provider.CustomerName);

        var metadata = new AuthMetadata(
            session.Provider.AuthState,
            session.Provider.CustomerId,
            session.Provider.CustomerName);

        metadataTcs.TrySetResult(metadata);
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
            AIContextProviderFactory = (ctx, ct) =>
            {
                if (ctx.SerializedState.ValueKind == JsonValueKind.Object)
                {
                    return new ValueTask<AIContextProvider>(
                        new AuthenticationContextProvider(_cisDatabase, ctx.SerializedState, _providerLogger));
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
