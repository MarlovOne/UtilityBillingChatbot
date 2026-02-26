// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Agents;

/// <summary>
/// Interface for agents that receive conversation messages and stream typed events.
/// Used by agents that need no per-turn state beyond the messages (e.g. Classifier, FAQ).
/// </summary>
public interface IStreamingAgent
{
    IAsyncEnumerable<ChatEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);
}

/// <summary>
/// Interface for agents that receive conversation messages plus typed per-turn state.
/// Used by agents that need additional context (e.g. AuthFlowState, UtilityDataSession).
/// </summary>
public interface IStreamingAgent<TState>
{
    IAsyncEnumerable<ChatEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        TState state,
        CancellationToken ct = default);
}
