using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.LinkedIn.Models;
using Cloud5mins.ShortenerTools.Core.Domain.Socials.Threads;
using LinkedIn;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitterScheduler.Models;


var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();


ShortenerSettings shortenerSettings = new ShortenerSettings();



builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();


builder.Configuration.Bind(shortenerSettings);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILinkedInManager, LinkedInManager>();
builder.Services.AddSingleton<IThreadsManager, ThreadsManager>();
builder.Services.AddSingleton<EmailService, EmailService>();

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