// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Agents;

/// <summary>
/// Interface for agents that support streaming typed events.
/// </summary>
public interface IStreamingAgent
{
    IAsyncEnumerable<ChatEvent> StreamAsync(string input, CancellationToken ct = default);
}
