// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Helper for consuming streaming results in tests.
/// </summary>
internal static class StreamingTestHelper
{
    /// <summary>
    /// Consumes a ChatEvent stream, collecting all text and returning it with all events.
    /// </summary>
    public static async Task<(string Text, List<ChatEvent> Events)> CollectAsync(
        IAsyncEnumerable<ChatEvent> stream)
    {
        var sb = new StringBuilder();
        var events = new List<ChatEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
            if (evt is TextChunk t) sb.Append(t.Text);
        }
        return (sb.ToString(), events);
    }
}
