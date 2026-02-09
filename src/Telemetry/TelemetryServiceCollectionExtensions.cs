// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UtilityBillingChatbot.Telemetry;

/// <summary>
/// Extension methods for configuring telemetry services.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds telemetry services including logging, tracing, and metrics.
    /// </summary>
    public static IServiceCollection AddTelemetryServices(
        this IServiceCollection services,
        TelemetryOptions options)
    {
        if (!options.Enabled)
        {
            // Even if telemetry is disabled, register AgentMetrics as a no-op singleton
            services.AddSingleton<AgentMetrics>();
            services.AddLogging(builder => builder
                .SetMinimumLevel(options.MinimumLogLevel)
                .AddConsole());
            return services;
        }

        // Determine OTLP endpoint
        var otlpEndpoint = options.OtlpEndpoint
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        // Create resource builder for service identification
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(options.ServiceName, serviceVersion: "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.instance.id"] = Environment.MachineName,
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development"
            });

        // Configure logging
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.SetMinimumLevel(options.MinimumLogLevel);
            loggingBuilder.AddConsole();

            loggingBuilder.AddOpenTelemetry(otelOptions =>
            {
                otelOptions.SetResourceBuilder(resourceBuilder);
                otelOptions.IncludeScopes = true;
                otelOptions.IncludeFormattedMessage = true;

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    otelOptions.AddOtlpExporter(otlpOptions =>
                        otlpOptions.Endpoint = new Uri(otlpEndpoint));
                }

                if (options.EnableConsoleExporter)
                {
                    otelOptions.AddConsoleExporter();
                }
            });
        });

        // Configure tracing
        services.AddOpenTelemetry()
            .WithTracing(tracingBuilder =>
            {
                tracingBuilder.SetResourceBuilder(resourceBuilder);
                tracingBuilder.AddSource(options.ServiceName);
                tracingBuilder.AddSource("*Microsoft.Agents.AI");
                tracingBuilder.AddHttpClientInstrumentation();

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    tracingBuilder.AddOtlpExporter(otlpOptions =>
                        otlpOptions.Endpoint = new Uri(otlpEndpoint));
                }

                if (options.EnableConsoleExporter)
                {
                    tracingBuilder.AddConsoleExporter();
                }
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder.SetResourceBuilder(resourceBuilder);
                metricsBuilder.AddMeter(AgentMetrics.MeterName);
                metricsBuilder.AddMeter("*Microsoft.Agents.AI");
                metricsBuilder.AddHttpClientInstrumentation();
                metricsBuilder.AddRuntimeInstrumentation();

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    metricsBuilder.AddOtlpExporter(otlpOptions =>
                        otlpOptions.Endpoint = new Uri(otlpEndpoint));
                }

                if (options.EnableConsoleExporter)
                {
                    metricsBuilder.AddConsoleExporter();
                }
            });

        // Register AgentMetrics as singleton
        services.AddSingleton<AgentMetrics>();

        return services;
    }
}
