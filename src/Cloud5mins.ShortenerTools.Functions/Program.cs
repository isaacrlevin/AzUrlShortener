using Azure.AI.OpenAI;
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
using OpenAI;
using System.ClientModel;

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

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

builder.Services.AddSingleton(options => { return shortenerSettings; });

if (inAspire)
{
    if (useOllama)
    {
        builder.AddKeyedOllamaSharpChatClient("chat");
        var sp = builder.Services.BuildServiceProvider();

        builder.Services.AddChatClient(sp.GetRequiredKeyedService<IChatClient>("chat"))
                  .UseFunctionInvocation()
                  .UseOpenTelemetry(configure: t => t.EnableSensitiveData = true)
                  //.UseLogging()
                  .Build();
    }
    else
    {
        builder.AddKeyedAzureOpenAIClient("chat");
        var sp = builder.Services.BuildServiceProvider();

        builder.Services.AddChatClient(sp.GetRequiredKeyedService<OpenAIClient>("chat").AsChatClient("gpt-4o-mini"))
                  .UseFunctionInvocation()
                  .UseOpenTelemetry(configure: t => t.EnableSensitiveData = true)
                  //.UseLogging()
                  .Build();
    }
}
else
{
    var endpoint = Environment.GetEnvironmentVariable("AzureOpenAIEndpoint");
    var key = Environment.GetEnvironmentVariable("AzureOpenAIKey");

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
    {

    }
    else
    {
        builder.Services.AddChatClient(new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key)).AsChatClient("gpt-4o-mini"))
                  .UseFunctionInvocation()
                  .UseOpenTelemetry(configure: t => t.EnableSensitiveData = true)
                  //.UseLogging()
                  .Build();
    }
}

builder.Build().Run();
