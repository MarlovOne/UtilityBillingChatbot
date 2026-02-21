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
    /// Display name for the model (e.g. "gpt-4o-mini").
    /// </summary>
    string ModelDisplayName { get; }

    /// <summary>
    /// Creates a raw <see cref="IChatClient"/> without OTel wrapping.
    /// Validation happens here, not in the constructor.
    /// </summary>
    IChatClient CreateClient();
}

/// <summary>
/// Base class for LLM providers. Binds <typeparamref name="TOptions"/> once from
/// the provider's config section (LLM:{Name}) and exposes it via <see cref="Options"/>.
/// </summary>
public abstract class LlmProviderBase<TOptions> : ILlmProvider where TOptions : new()
{
    private readonly Lazy<TOptions> _options;

    protected LlmProviderBase(IConfiguration configuration)
    {
        _options = new Lazy<TOptions>(() =>
        {
            var options = new TOptions();
            configuration.GetSection($"{ILlmProvider.ConfigSection}:{Name}").Bind(options);
            return options;
        });
    }

    protected TOptions Options => _options.Value;

    public abstract string Name { get; }
    public abstract string ModelDisplayName { get; }
    public abstract IChatClient CreateClient();
}
