// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Connection options shared by all LLM providers (ApiKey + Endpoint).
/// Bound from LLM:{ProviderName} config section.
/// </summary>
public class LlmProviderOptions
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
}
