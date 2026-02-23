// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using UtilityBillingChatbot.Agents;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Helper for consuming streaming results in tests.
/// </summary>
internal static class StreamingTestHelper
{
    /// <summary>
    /// Consumes a streaming result, collecting all text and returning it with the metadata.
    /// </summary>
    public static async Task<(string Text, TMetadata Metadata)> ConsumeAsync<TMetadata>(
        StreamingResult<TMetadata> result)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in result.TextStream)
        {
            sb.Append(chunk);
        }
        var metadata = await result.Metadata;
        return (sb.ToString(), metadata);
    }

    /// <summary>
    /// Consumes an IAsyncEnumerable of string chunks into a single string.
    /// </summary>
    public static async Task<string> CollectAsync(IAsyncEnumerable<string> stream)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in stream)
        {
            sb.Append(chunk);
        }
        return sb.ToString();
    }
}
