// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Interface for chatbot orchestrators that process user messages
/// and stream responses as typed events.
/// </summary>
public interface IChatbotOrchestrator
{
    IAsyncEnumerable<ChatEvent> ProcessMessageStreamingAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct = default);
}
