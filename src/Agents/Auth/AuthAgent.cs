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
public class AuthAgent : IStreamingAgent<AuthFlowState?>
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

    /// <summary>
    /// Streams the auth flow. Creates a fresh agent session per turn,
    /// populated with conversation history from the ChatSession.
    /// Reads AuthFlowState from session to reconstruct provider state.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        AuthFlowState? state,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Auth input (state={HasState}): {Count} messages",
            state is not null, messages.Count);

        if (state is not null)
        {
            _logger.LogInformation("Reconstructing from AuthFlowState: ProviderState={State}, FailedAttempts={Attempts}, VerifiedFactors=[{Factors}], Customer={Customer} ({Id}), IdentifyingInfo={Info}",
                state.ProviderState, state.FailedAttempts,
                string.Join(", ", state.VerifiedFactors),
                state.CustomerName, state.CustomerId, state.IdentifyingInfo);
        }

        var provider = state is not null
            ? CreateProviderFromState(state)
            : new AuthenticationContextProvider(_cisDatabase, _providerLogger);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "AuthAgent",
            AIContextProviders = [provider]
        });

        var agentSession = await agent.CreateSessionAsync(ct);

        await foreach (var update in agent.RunStreamingAsync(
            messages, agentSession, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextChunk(update.Text);
            }
        }

        _logger.LogInformation("Auth state: {State}, Customer: {Customer}",
            provider.AuthState, provider.CustomerName);

        yield return new AuthStateEvent(
            provider.AuthState,
            provider.CustomerId,
            provider.CustomerName,
            ExtractFlowState(provider));
    }

    private AuthenticationContextProvider CreateProviderFromState(AuthFlowState state)
    {
        var stateJson = JsonSerializer.SerializeToElement(new
        {
            authState = state.ProviderState.ToString(),
            failedAttempts = state.FailedAttempts,
            verifiedFactors = state.VerifiedFactors,
            identifyingInfo = state.IdentifyingInfo,
            customerId = state.CustomerId,
            customerName = state.CustomerName
        });

        return new AuthenticationContextProvider(_cisDatabase, stateJson, _providerLogger);
    }

    private static AuthFlowState ExtractFlowState(AuthenticationContextProvider provider)
    {
        var providerState = provider.AuthState == AuthenticationState.InProgress
            ? AuthenticationState.Anonymous
            : provider.AuthState;

        return new AuthFlowState(
            ProviderState: providerState,
            FailedAttempts: provider.FailedAttempts,
            VerifiedFactors: provider.VerifiedFactors.ToList(),
            IdentifyingInfo: provider.IdentifyingInfo,
            CustomerId: provider.CustomerId,
            CustomerName: provider.CustomerName);
    }
}

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
