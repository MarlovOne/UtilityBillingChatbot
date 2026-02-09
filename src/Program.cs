// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Set content root to application directory so appsettings.json is found
// regardless of current working directory
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddUtilityBillingChatbot(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
