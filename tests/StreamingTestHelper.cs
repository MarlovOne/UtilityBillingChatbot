// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Helper for consuming streaming results in tests.
/// </summary>
internal static class StreamingTestHelper
{
    /// <summary>
    /// Creates a minimal ChatSession for testing agents in isolation.
    /// </summary>
    public static ChatSession CreateTestSession() => new()
    {
        SessionId = "test",
        UserContext = new UserSessionContext { SessionId = "test" }
    };

    /// <summary>
    /// Runs a full turn: adds user message to session history, builds messages,
    /// calls the agent, collects the response, and records assistant response.
    /// Mirrors what the orchestrator does each turn.
    /// </summary>
    public static async Task<(string Text, List<ChatEvent> Events)> RunTurnAsync(
        ChatSession session,
        string userInput,
        Func<IReadOnlyList<ChatMessage>, IAsyncEnumerable<ChatEvent>> streamFactory)
    {
        // Record user message (mirrors orchestrator)
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userInput
        });

        // Build messages and stream
        var messages = session.BuildAgentMessages();
        return await CollectAsync(streamFactory(messages), session);
    }

    /// <summary>
    /// Consumes a ChatEvent stream, collecting all text and returning it with all events.
    /// Optionally records the assistant response to the session's ConversationHistory.
    /// </summary>
    public static async Task<(string Text, List<ChatEvent> Events)> CollectAsync(
        IAsyncEnumerable<ChatEvent> stream,
        ChatSession? session = null)
    {
        var sb = new StringBuilder();
        var events = new List<ChatEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
            if (evt is TextChunk t) sb.Append(t.Text);
        }

        // Record assistant response (mirrors orchestrator)
        if (session is not null && sb.Length > 0)
        {
            session.ConversationHistory.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = sb.ToString()
            });
        }

        return (sb.ToString(), events);
    }
}
