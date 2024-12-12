using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using LinkedIn;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using System.Diagnostics;
using static System.Environment;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.AddServiceDefaults();

ShortenerSettings shortenerSettings = new ShortenerSettings();

if (!Debugger.IsAttached)
{
    builder.Services.AddApplicationInsightsTelemetryWorkerService();
    builder.Services.ConfigureFunctionsApplicationInsights();
}

builder.Configuration.Bind(shortenerSettings);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILinkedInManager, LinkedInManager>();

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

builder.Services.AddSingleton(options => { return shortenerSettings; });


builder.Build().Run();
