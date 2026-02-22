// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Represents an LLM provider that can create chat clients.
/// Each provider owns its config binding and client creation.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Root configuration section for all LLM providers.
    /// </summary>
    const string ConfigSection = "LLM";

    /// <summary>
    /// Provider name matching the config key (e.g. "OpenAI", "AzureOpenAI").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Connection options (ApiKey, Endpoint) bound from LLM:{Name}.
    /// </summary>
    LlmProviderOptions Options { get; }

    /// <summary>
    /// Creates a raw <see cref="IChatClient"/> without OTel wrapping.
    /// </summary>
    /// <param name="model">The model identifier to use (e.g. "gpt-4o-mini").</param>
    IChatClient CreateClient(string model);

    /// <summary>
    /// Binds <see cref="LlmProviderOptions"/> from the provider's config section (LLM:{providerName}).
    /// </summary>
    static LlmProviderOptions BindOptions(IConfiguration configuration, string providerName)
    {
        var options = new LlmProviderOptions();
        configuration.GetSection($"{ConfigSection}:{providerName}").Bind(options);
        return options;
    }
}
