// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace UtilityBillingChatbot.Extensions;

/// <summary>
/// Extension methods for working with agent responses.
/// </summary>
public static class AgentResponseExtensions
{
    /// <summary>
    /// Tries to get the structured result from a response, handling JSON parsing failures.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="response">The agent response</param>
    /// <param name="result">The parsed result if successful</param>
    /// <param name="error">Error message if parsing failed</param>
    /// <returns>True if the result was successfully parsed, false otherwise</returns>
    public static bool TryGetResult<T>(
        this ChatClientAgentResponse<T> response,
        [NotNullWhen(true)] out T? result,
        out string? error)
    {
        try
        {
            result = response.Result;
            if (result is null)
            {
                error = $"Structured output was null. Raw response: {response.Text}";
                return false;
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            result = default;
            error = $"JSON parsing failed: {ex.Message}. Raw response: {response.Text}";
            return false;
        }
    }
}
