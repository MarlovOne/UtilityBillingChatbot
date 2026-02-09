// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Telemetry.Middleware;

/// <summary>
/// Middleware for logging function/tool invocations.
/// </summary>
public static class FunctionLoggingMiddleware
{
    /// <summary>
    /// Creates a function invocation middleware that logs before and after function calls.
    /// </summary>
    public static Func<AIAgent, FunctionInvocationContext, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>, CancellationToken, ValueTask<object?>> Create(
        ILogger logger,
        bool enableSensitiveData)
    {
        return async (agent, context, next, ct) =>
        {
            var functionName = context.Function.Name;

            if (enableSensitiveData)
            {
                logger.LogDebug("Function {FunctionName} invoked with arguments: {Arguments}",
                    functionName,
                    context.Arguments);
            }
            else
            {
                logger.LogDebug("Function {FunctionName} invoked", functionName);
            }

            try
            {
                var result = await next(context, ct);

                if (enableSensitiveData)
                {
                    logger.LogDebug("Function {FunctionName} completed with result: {Result}",
                        functionName,
                        result);
                }
                else
                {
                    logger.LogDebug("Function {FunctionName} completed", functionName);
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Function {FunctionName} failed: {Error}",
                    functionName,
                    ex.Message);
                throw;
            }
        };
    }
}
