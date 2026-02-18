// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UtilityBillingChatbot.Telemetry;

/// <summary>
/// OpenTelemetry configuration for the Utility Billing Chatbot.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// The ActivitySource name used by this application.
    /// Use this when creating spans: ActivitySource.StartActivity("MyOperation")
    /// </summary>
    public const string ServiceName = "UtilityBillingChatbot";

    /// <summary>
    /// ActivitySource for creating custom spans/traces.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    /// <summary>
    /// Meter for creating custom metrics.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName);

    /// <summary>
    /// Adds OpenTelemetry tracing, metrics, and logging to the service collection.
    /// Reads configuration from "OpenTelemetry" section in appsettings.json.
    /// </summary>
    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otelConfig = configuration.GetSection("OpenTelemetry");
        var enabled = otelConfig.GetValue("Enabled", true);

        if (!enabled)
        {
            return services;
        }

        // OTLP endpoint - config > env var > default
        var otlpEndpoint = otelConfig.GetValue<string>("Endpoint")
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? "http://localhost:4317";

        // Resource identifies this service in telemetry backends
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(ServiceName, serviceVersion: "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development"
            });

        // Configure tracing
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                // Our custom activity source
                .AddSource(ServiceName)
                // Agent Framework telemetry (captures agent runs, LLM calls)
                .AddSource("Microsoft.Agents.AI*")
                .AddSource("Microsoft.Extensions.AI*")
                // HTTP client calls (LLM API requests)
                .AddHttpClientInstrumentation()
                // Export to OTLP (Aspire Dashboard, Jaeger, etc.)
                .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                // Our custom metrics
                .AddMeter(ServiceName)
                // Agent Framework metrics
                .AddMeter("Microsoft.Agents.AI*")
                .AddMeter("Microsoft.Extensions.AI*")
                // HTTP and runtime metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Export to OTLP
                .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));

        // Configure logging to export to OpenTelemetry
        services.AddLogging(logging => logging
            .AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            }));

        return services;
    }
}
