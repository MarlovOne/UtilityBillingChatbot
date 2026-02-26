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

    /// <summary>
    /// Streams the auth flow. Creates a fresh session per turn from the provided state.
    /// If <paramref name="state"/> is null, starts a new auth flow.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        string input,
        AuthFlowState? state,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Auth input (state={HasState}): {Input}",
            state is not null, input);

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
            input, agentSession, cancellationToken: ct))
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

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        string input, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in StreamAsync(input, state: null, ct))
        {
            yield return evt;
        }
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
