using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var isStaging = builder.Environment.IsStaging();
// A new production deployment must not race the still-live legacy timer.
// Only an explicit cutover input may enable the ACA scheduler.
var schedulerDisabled = builder.AddParameter("scheduler-disabled", "true", publishValueAsDefault: true);
// Coffee cutover is independent of shortener posting and its legacy host.
var coffeeSchedulerDisabled = builder.AddParameter("coffee-scheduler-disabled", "true", publishValueAsDefault: true);
string[] coffeeTimers = ["PostTeaserTimer", "PostAnnouncementTimer", "PostArchiveTimer"];
var coffeePostingAllowed = !builder.ExecutionContext.IsRunMode && builder.Environment.IsProduction();
var aiDeploymentName = isStaging ? null : builder.ExecutionContext.IsRunMode
    ? builder.AddParameter("azure-openai-deployment-name",
        builder.Configuration["Parameters:azure-openai-deployment-name"] ?? "gpt-4o-mini")
    : builder.AddParameter("azure-openai-deployment-name");

var useOllama = !isStaging && builder.ExecutionContext.IsRunMode
    && builder.Configuration.GetValue("Ollama:UseLocalModel", true);
var useGpu = builder.Configuration.GetValue<bool>("Ollama:UseGpu");

var chat = isStaging ? null : useOllama
    ? builder.AddOllama("ollama")
        .WithDataVolume()
        .WithContainerRuntimeArgs(useGpu ? ["--gpus=all"] : [])
        .WithOpenWebUI()
        .AddModel("chat", "llama3")
    : builder.AddConnectionString("chat");

// The URL tables stay in their existing account. ACA references it by resource
// ID and grants data-plane access to managed identities; Functions host state
// uses a separate account.
var storage = builder.AddAzureStorage("storage");
if (builder.ExecutionContext.IsRunMode)
{
    storage.RunAsEmulator();
}
else if (!isStaging)
{
    var existingStorageName = builder.AddParameter("existing-storage-account");
    var existingStorageResourceGroup = builder.AddParameter("existing-storage-resource-group");
    storage.AsExisting(existingStorageName, existingStorageResourceGroup);
}

var tables = storage.AddTables("tables");
var useProductionData = !isStaging && builder.ExecutionContext.IsRunMode
    && builder.Configuration.GetValue<bool>("Storage:UseProduction");
var productionData = useProductionData ? builder.AddConnectionString("production-data") : null;
var hostStorage = builder.AddAzureStorage("functions-host");
if (builder.ExecutionContext.IsRunMode)
{
    hostStorage.RunAsEmulator();
}

var entraTenantId = builder.AddParameter("entra-tenant-id");
var entraClientId = builder.AddParameter("entra-client-id");
var entraClientSecret = builder.AddParameter("entra-client-secret", secret: true);
var administratorObjectId = builder.AddParameter("administrator-object-id");
var adminApiKey = builder.AddParameter("admin-api-key", secret: true);
// Reserved .invalid hosts prevent staging from defaulting to a production URL.
// Operators replace the short-link base after ACA assigns the redirect hostname.
var customDomain = isStaging
    ? builder.AddParameter("custom-domain",
        builder.Configuration["Parameters:custom-domain"] ?? "https://shortener.staging.invalid",
        publishValueAsDefault: true)
    : builder.AddParameter("custom-domain");
var defaultRedirectUrl = isStaging
    ? builder.AddParameter("default-redirect-url",
        builder.Configuration["Parameters:default-redirect-url"] ?? "https://redirect.staging.invalid",
        publishValueAsDefault: true)
    : builder.AddParameter("default-redirect-url");
var scheduleSettings = new Dictionary<string, IResourceBuilder<ParameterResource>>();
if (!builder.ExecutionContext.IsRunMode && !isStaging)
{
    scheduleSettings["TwitterConsumerKey"] = builder.AddParameter("twitter-consumer-key", secret: true);
    scheduleSettings["TwitterConsumerSecret"] = builder.AddParameter("twitter-consumer-secret", secret: true);
    scheduleSettings["TwitterAccessToken"] = builder.AddParameter("twitter-access-token", secret: true);
    scheduleSettings["TwitterAccessSecret"] = builder.AddParameter("twitter-access-secret", secret: true);
    scheduleSettings["MastodonAccessToken"] = builder.AddParameter("mastodon-access-token", secret: true);
    scheduleSettings["LinkedInAccessToken"] = builder.AddParameter("linkedin-access-token", secret: true);
    scheduleSettings["BlueskyUserName"] = builder.AddParameter("bluesky-username", secret: true);
    scheduleSettings["BlueskyPassword"] = builder.AddParameter("bluesky-password", secret: true);
    scheduleSettings["ThreadsToken"] = builder.AddParameter("threads-token", secret: true);
    scheduleSettings["COMMUNICATION_SERVICES_CONNECTION_STRING"] = builder.AddParameter(
        "communication-services-connection-string", secret: true);
    scheduleSettings["EmailFrom"] = builder.AddParameter("email-from", secret: true);
    scheduleSettings["EmailTo"] = builder.AddParameter("email-to", secret: true);
    scheduleSettings["TwitterViaHandle"] = builder.AddParameter("twitter-via-handle", "", publishValueAsDefault: true);
}

var aca = builder.AddAzureContainerAppEnvironment("aca");
// Only publishing/deployment models Azure telemetry resources. Local runs use
// the Aspire dashboard without provisioning Azure or requiring an Insights secret.
var appInsights = builder.ExecutionContext.IsRunMode
    ? null
    : builder.AddAzureApplicationInsights("app-insights");

var redirect = builder.AddAzureFunctionsProject<Projects.ShortenerTools_Functions>("shortenertools-functions")
    .WithExternalHttpEndpoints()
    .WithHostStorage(hostStorage)
    .WithRoleAssignments(
        hostStorage,
        StorageBuiltInRole.StorageBlobDataContributor,
        StorageBuiltInRole.StorageQueueDataContributor,
        StorageBuiltInRole.StorageAccountContributor)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageTableDataContributor)
    .WithReference(tables)
    .WithEnvironment("AzureFunctionsJobHost__functions__0", "UrlRedirect")
    .WithEnvironment("AzureFunctionsWebHost__hostid", "shortener-redirect")
    .WithEnvironment("EnableDescriptionGeneration", "false")
    .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DisableExternalPosting", isStaging.ToString())
    .WithEnvironment("CustomDomain", customDomain)
    .WithEnvironment("DefaultRedirectUrl", defaultRedirectUrl)
    .WithEnvironment("AzureWebJobs.SchedulePostTimer.Disabled", "true")
    .WithEnvironment("IN_ASPIRE", "true")
    .WithEnvironment("USE_OLLAMA", "false")
    .PublishAsAzureContainerApp((_, app) =>
    {
        // The public redirect endpoint stays warm and has exactly one replica.
        app.Template.Scale.MinReplicas = 1;
        app.Template.Scale.MaxReplicas = 1;
        app.Template.Containers[0].Value.Resources.Cpu = 0.25;
        app.Template.Containers[0].Value.Resources.Memory = "0.5Gi";
    });
if (builder.ExecutionContext.IsRunMode)
{
    redirect.WithEnvironment("DataStorage",
        productionData?.Resource.ConnectionStringExpression ?? tables.Resource.ConnectionStringExpression);
}

var managementApi = builder.AddAzureFunctionsProject<Projects.ShortenerTools_Functions>("management-api")
    .WithHostStorage(hostStorage)
    .WithRoleAssignments(hostStorage,
        StorageBuiltInRole.StorageBlobDataContributor,
        StorageBuiltInRole.StorageQueueDataContributor,
        StorageBuiltInRole.StorageAccountContributor)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageTableDataContributor)
    .WithReference(tables)
    .WithEnvironment("AdminApiKey", adminApiKey)
    .WithEnvironment("CustomDomain", customDomain)
    .WithEnvironment("DefaultRedirectUrl", defaultRedirectUrl)
    .WithEnvironment("AzureFunctionsWebHost__hostid", "shortener-management")
    .WithEnvironment("AzureWebJobs.SchedulePostTimer.Disabled", "true")
    .WithEnvironment("IN_ASPIRE", "true")
    .WithEnvironment("USE_OLLAMA", useOllama.ToString())
    .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DisableExternalPosting", isStaging.ToString())
    // Production administrators may invoke Coffee manually; timer opt-in is
    // separate. Local/staging Coffee never sends, even with live-data opt-ins.
    .WithEnvironment("PostSocials", coffeePostingAllowed.ToString())
    .WithEnvironment("EnableDescriptionGeneration", (!isStaging).ToString())
    .PublishAsAzureContainerApp((_, app) =>
    {
        app.Template.Scale.MinReplicas = 0;
        app.Template.Scale.MaxReplicas = 1;
    });

string[] managementFunctions =
[
    "UrlList", "UrlCreate", "UrlUpdate", "UrlArchive", "UrlStats",
    "UrlClickStatsByDay", "CreateDescription", "SchedulePostHttp", "TestShortUrl",
    "PostPublishHttp", "PostTeaserHttp", "PostAnnouncementHttp", "PostArchiveHttp"
];
for (var index = 0; index < managementFunctions.Length; index++)
{
    managementApi.WithEnvironment($"AzureFunctionsJobHost__functions__{index}", managementFunctions[index]);
}
if (builder.ExecutionContext.IsRunMode)
{
    managementApi.WithEnvironment("DataStorage",
        productionData?.Resource.ConnectionStringExpression ?? tables.Resource.ConnectionStringExpression);
}
if (chat is not null)
{
    managementApi.WithReference(chat);
    managementApi.WithEnvironment("DeploymentName", aiDeploymentName!);
    if (useOllama)
    {
        managementApi.WaitFor(chat);
    }
}

// Keep shortener and Coffee schedules on one non-public Functions app.
// Neither is copied into the public redirect app; opt in to each at cutover.
var scheduledPosts = builder.AddAzureFunctionsProject<Projects.ShortenerTools_Functions>("shortenertools-scheduled-posts")
    .WithHostStorage(hostStorage)
    .WithRoleAssignments(
        hostStorage,
        StorageBuiltInRole.StorageBlobDataContributor,
        StorageBuiltInRole.StorageQueueDataContributor,
        StorageBuiltInRole.StorageAccountContributor)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageTableDataContributor)
    .WithReference(tables)
    .WithEnvironment("CustomDomain", customDomain)
    .WithEnvironment("DefaultRedirectUrl", defaultRedirectUrl)
    .WithEnvironment("IN_ASPIRE", "true")
    .WithEnvironment("USE_OLLAMA", useOllama.ToString())
    .WithEnvironment("EnableDescriptionGeneration", "false")
    .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DisableExternalPosting", isStaging.ToString())
    .WithEnvironment("PostSocials", coffeePostingAllowed.ToString())
    .WithEnvironment("AzureFunctionsWebHost__hostid", "shortener-scheduler")
    .WithEnvironment("AzureFunctionsJobHost__functions__0", "SchedulePostTimer")
    .PublishAsAzureContainerApp((infrastructure, app) =>
    {
        app.Template.Scale.MinReplicas = 1;
        app.Template.Scale.MaxReplicas = 1;
        if (!isStaging)
        {
            // Retain the safe default in the standalone Container App module,
            // too: Aspire publishes application modules separately from main.
            infrastructure.GetProvisionableResources().OfType<ProvisioningParameter>()
                .Single(parameter => parameter.BicepIdentifier == "scheduler_disabled_value").Value = "true";
            infrastructure.GetProvisionableResources().OfType<ProvisioningParameter>()
                .Single(parameter => parameter.BicepIdentifier == "coffee_scheduler_disabled_value").Value = "true";
        }
    });
for (var index = 0; index < coffeeTimers.Length; index++)
{
    scheduledPosts.WithEnvironment($"AzureFunctionsJobHost__functions__{index + 1}", coffeeTimers[index]);
}
foreach (var timer in coffeeTimers)
{
    redirect.WithEnvironment($"AzureWebJobs.{timer}.Disabled", "true");
    managementApi.WithEnvironment($"AzureWebJobs.{timer}.Disabled", "true");
    if (builder.ExecutionContext.IsRunMode || isStaging)
    {
        scheduledPosts.WithEnvironment($"AzureWebJobs.{timer}.Disabled", "true");
    }
    else
    {
        scheduledPosts.WithEnvironment($"AzureWebJobs.{timer}.Disabled", coffeeSchedulerDisabled);
    }
}
if (builder.ExecutionContext.IsRunMode || isStaging)
{
    scheduledPosts.WithEnvironment("AzureWebJobs.SchedulePostTimer.Disabled", "true");
}
else
{
    scheduledPosts.WithEnvironment("AzureWebJobs.SchedulePostTimer.Disabled", schedulerDisabled);
}
if (builder.ExecutionContext.IsRunMode)
{
    scheduledPosts.WithEnvironment("DataStorage", tables.Resource.ConnectionStringExpression);
}

foreach (var (settingName, parameter) in scheduleSettings)
{
    scheduledPosts.WithEnvironment(settingName, parameter);
    managementApi.WithEnvironment(settingName, parameter);
}

var admin = builder.AddProject<Projects.ShortenerTools_Admin>("admin")
    .WithExternalHttpEndpoints()
    .WithReference(managementApi)
    .WithEnvironment("Entra__TenantId", entraTenantId)
    .WithEnvironment("Entra__ClientId", entraClientId)
    .WithEnvironment("Entra__ClientSecret", entraClientSecret)
    .WithEnvironment("Entra__AdministratorObjectId", administratorObjectId)
    .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
    .WithEnvironment("AdminApiKey", adminApiKey)
    .PublishAsAzureContainerApp((_, app) =>
    {
        app.Template.Scale.MinReplicas = 0;
        app.Template.Scale.MaxReplicas = 1;
    });

if (appInsights is not null)
{
    admin.WithReference(appInsights);
    foreach (var functions in new[] { redirect, managementApi, scheduledPosts })
    {
        functions.WithReference(appInsights);
    }
}

// The host and worker share a role and trace context; each app has a distinct
// service.name even though the three Functions apps share the same executable.
admin.WithEnvironment("OTEL_SERVICE_NAME", admin.Resource.Name);
foreach (var functions in new[] { redirect, managementApi, scheduledPosts })
{
    functions.WithEnvironment("OTEL_SERVICE_NAME", functions.Resource.Name);
    functions.WithEnvironment("AzureFunctionsJobHost__telemetryMode", "OpenTelemetry");
}

builder.Build().Run();
