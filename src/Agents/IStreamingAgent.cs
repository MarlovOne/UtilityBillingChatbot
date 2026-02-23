// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;

namespace UtilityBillingChatbot.Agents;

/// <summary>
/// Interface for agents that support token-by-token streaming with typed metadata.
/// Text streams to the user; metadata is reported via tool calls during the stream.
/// </summary>
/// <typeparam name="TMetadata">The metadata type reported by tools during streaming.</typeparam>
public interface IStreamingAgent<TMetadata>
{
    StreamingResult<TMetadata> StreamAsync(string input, CancellationToken ct = default);
}

/// <summary>
/// Result of a streaming agent invocation.
/// </summary>
/// <typeparam name="TMetadata">The metadata type.</typeparam>
public class StreamingResult<TMetadata>
{
    /// <summary>Async stream of text chunks to display to the user.</summary>
    public required IAsyncEnumerable<string> TextStream { get; init; }

    /// <summary>
    /// Task that completes when the stream is fully consumed, yielding the metadata
    /// reported by tool calls during the stream.
    /// </summary>
    public required Task<TMetadata> Metadata { get; init; }
}

/// <summary>
/// Metadata from the ClassifierAgent, reported via the ReportClassification tool.
/// Category is null for greetings/chitchat (tool was never called).
/// </summary>
public record ClassificationMetadata(
    QuestionCategory? Category,
    double Confidence);

/// <summary>
/// Metadata from the FAQAgent. FoundAnswer defaults to true;
/// the ReportAnswerNotFound tool sets it to false.
/// </summary>
public record FAQMetadata(bool FoundAnswer);

/// <summary>
/// Metadata from the AuthAgent, read from AuthenticationContextProvider state.
/// </summary>
public record AuthMetadata(
    AuthenticationState State,
    string? CustomerId,
    string? CustomerName);

/// <summary>
/// Metadata from the UtilityDataAgent. FoundAnswer defaults to true;
/// the ReportAnswerNotFound tool sets it to false.
/// </summary>
public record UtilityDataMetadata(bool FoundAnswer);
