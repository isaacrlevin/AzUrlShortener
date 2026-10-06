using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

bool useOllama = true;
var useGpu = builder.Configuration.GetValue<bool>("Ollama:UseGpu");

var chat = useOllama
    ? builder.AddOllama("ollama")
           .WithDataVolume()
           .WithContainerRuntimeArgs(useGpu ? ["--gpus=all"] : [])
           .WithOpenWebUI()
           .AddModel("chat", "llama3")
        : builder.AddConnectionString("chat");

var storage = builder.AddAzureStorage("storage")
                     .RunAsEmulator();

var table = storage.AddTables("tables");

// Production data is opt-in: reads and writes (including click stats) hit the real account.
var useProductionData = builder.Configuration.GetValue<bool>("Storage:UseProduction");
var dataStorage = useProductionData
    ? builder.AddConnectionString("production-data").Resource.ConnectionStringExpression
    : table.Resource.ConnectionStringExpression;

var functions = builder.AddAzureFunctionsProject<Projects.Cloud5mins_ShortenerTools_Functions>("cloud5mins-shortenertools-functions")
    .WithExternalHttpEndpoints()
    .WithHostStorage(storage)
    .WithReference(table)
    .WithEnvironment("DataStorage", dataStorage)
    .WithEnvironment("AzureWebJobs.SchedulePostTimer.Disabled", "true")
    .WithEnvironment("IN_ASPIRE", "true")
    .WithEnvironment("USE_OLLAMA", useOllama.ToString())
    .WithReference(chat);

if (useOllama)
{
    functions
    .WaitFor(chat);
}

var web = builder.AddProject<Projects.Cloud5mins_ShortenerTools_TinyBlazorAdmin>("admin")
    .WithExternalHttpEndpoints();

var sourceDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));
var adminDirectory = Path.Combine(sourceDirectory, "Cloud5mins.ShortenerTools.TinyBlazorAdmin");
var swaCli = Path.Combine(sourceDirectory, "node_modules", "@azure", "static-web-apps-cli", "dist", "cli", "bin.js");

if (!File.Exists(swaCli))
{
    throw new FileNotFoundException(
        $"The SWA CLI is missing. Run 'npm ci' in '{sourceDirectory}' before starting the AppHost.",
        swaCli);
}

builder.AddExecutable("swa", "node", adminDirectory)
    .WithArgs(context =>
    {
        context.Args.Add(swaCli);
        context.Args.Add("start");
        context.Args.Add("--app-devserver-url");
        context.Args.Add(web.GetEndpoint("http"));
        context.Args.Add("--api-devserver-url");
        context.Args.Add(functions.GetEndpoint("http"));
        context.Args.Add("--swa-config-location");
        context.Args.Add(adminDirectory);
        context.Args.Add("--host");
        context.Args.Add("127.0.0.1");
    })
    .WithHttpEndpoint(
        port: builder.Configuration.GetValue<int?>("Swa:Port"),
        env: "SWA_CLI_PORT",
        name: "http")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/.auth/me")
    .WaitFor(web)
    .WaitFor(functions);

builder.Build().Run();