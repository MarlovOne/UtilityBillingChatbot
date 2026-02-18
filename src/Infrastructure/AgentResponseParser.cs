// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Shared utilities for parsing ChatClientAgent responses.
/// Handles JSON extraction from markdown code blocks and structured output parsing.
/// </summary>
public static class AgentResponseParser
{
    /// <summary>
    /// Attempts to extract the typed result from a ChatClientAgent response.
    /// Falls back to parsing raw text if structured output fails.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="response">The agent response.</param>
    /// <param name="result">The parsed result if successful.</param>
    /// <param name="error">Error message if parsing failed.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryGetResult<T>(
        ChatClientAgentResponse<T> response,
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
        catch (JsonException)
        {
            // LLM may return JSON wrapped in markdown code blocks - try to extract and parse
            return TryParseFromRawText<T>(response.Text, out result, out error);
        }
    }

    /// <summary>
    /// Attempts to parse JSON from raw text, handling markdown code block wrapping.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="rawText">The raw text to parse.</param>
    /// <param name="result">The parsed result if successful.</param>
    /// <param name="error">Error message if parsing failed.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseFromRawText<T>(
        string? rawText,
        [NotNullWhen(true)] out T? result,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            result = default;
            error = "Response text was empty";
            return false;
        }

        var json = ExtractJsonFromMarkdown(rawText);

        try
        {
            result = JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
            if (result is null)
            {
                error = $"Deserialized result was null. Raw response: {rawText}";
                return false;
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            result = default;
            error = $"JSON parsing failed: {ex.Message}. Raw response: {rawText}";
            return false;
        }
    }

    /// <summary>
    /// Extracts JSON from markdown code blocks if present, otherwise returns the original text.
    /// Handles formats like: ```json\n{...}\n``` or ```\n{...}\n```
    /// </summary>
    /// <param name="text">The text potentially containing markdown code blocks.</param>
    /// <returns>The extracted JSON or trimmed original text.</returns>
    public static string ExtractJsonFromMarkdown(string text)
    {
        // Match ```json or ``` followed by content and closing ```
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }
}
