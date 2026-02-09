// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Models;

/// <summary>
/// Configuration options for LLM providers
/// </summary>
public class LlmOptions
{
    /// <summary>
    /// The LLM provider to use: "AzureOpenAI", "OpenAI", or "HuggingFace"
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// Azure OpenAI configuration
    /// </summary>
    public AzureOpenAIOptions? AzureOpenAI { get; set; }

    /// <summary>
    /// OpenAI configuration
    /// </summary>
    public OpenAIOptions? OpenAI { get; set; }

    /// <summary>
    /// HuggingFace configuration
    /// </summary>
    public HuggingFaceOptions? HuggingFace { get; set; }
}

/// <summary>
/// Azure OpenAI configuration options
/// </summary>
public class AzureOpenAIOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string DeploymentName { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// OpenAI configuration options
/// </summary>
public class OpenAIOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string? Endpoint { get; set; }
}

/// <summary>
/// HuggingFace configuration options
/// </summary>
public class HuggingFaceOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "meta-llama/Llama-3.1-8B-Instruct";
    public string Endpoint { get; set; } = "https://api-inference.huggingface.co/v1/";
}
