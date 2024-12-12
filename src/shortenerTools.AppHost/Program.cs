using Google.Protobuf.WellKnownTypes;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
                     .RunAsEmulator();

var table = storage.AddTables("tables");


var functions = builder.AddAzureFunctionsProject<Projects.Cloud5mins_ShortenerTools_Functions>("cloud5mins-shortenertools-functions")
    .WithExternalHttpEndpoints()
    .WithHostStorage(storage)
    .WithReference(table);

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


