using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Threads;
using Cloud5mins.ShortenerTools.Functions;
using LinkedIn;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();


ShortenerSettings shortenerSettings = new ShortenerSettings();

var inAspire = bool.Parse(Environment.GetEnvironmentVariable("IN_ASPIRE") ?? "False");
var useOllama = bool.Parse(Environment.GetEnvironmentVariable("USE_OLLAMA") ?? "False");

if (inAspire)
{
    builder.AddServiceDefaults();
}
else
{
    builder.Services
        .AddApplicationInsightsTelemetryWorkerService()
        .ConfigureFunctionsApplicationInsights();
}

builder.Configuration.Bind(shortenerSettings);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILinkedInManager, LinkedInManager>();
builder.Services.AddSingleton<IThreadsManager, ThreadsManager>();
builder.Services.AddSingleton<EmailService, EmailService>();
builder.Services.AddOptions<KestrelServerOptions>()
.Configure<IConfiguration>((settings, configuration) =>
{
    settings.AllowSynchronousIO = true;
    configuration.Bind(settings);
});

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

builder.Services.AddSingleton(options => { return shortenerSettings; });

builder.AddAIServices();

builder.Build().Run();
