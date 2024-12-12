using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

var chat = Debugger.IsAttached
    ? builder.AddOllama("ollama")
           .WithDataVolume()
           .WithContainerRuntimeArgs("--gpus=all")
           .WithOpenWebUI()
           .AddModel("chat", "llama3")
        : builder.AddConnectionString("chat");

var storage = builder.AddAzureStorage("storage")
                     .RunAsEmulator();

var table = storage.AddTables("tables");


var functions = builder.AddAzureFunctionsProject<Projects.Cloud5mins_ShortenerTools_Functions>("cloud5mins-shortenertools-functions")
    .WithExternalHttpEndpoints()
    .WithHostStorage(storage)
    .WithReference(table)
    .WithReference(chat);

if (Debugger.IsAttached)
{
    functions
    .WaitFor(chat);
}

var web = builder.AddProject<Projects.Cloud5mins_ShortenerTools_TinyBlazorAdmin>("admin")
    .WithExternalHttpEndpoints()
    .WithReference(functions)
    .WithReference(table)
    .WaitFor(functions)
    .WaitFor(table);

_ = builder
    .AddSwaEmulator("swa")
    .WithAppResource(web)
    .WithApiResource(functions);



builder.Build().Run();


