var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
                     .RunAsEmulator();

var table = storage.AddTables("tables");


var functions = builder.AddAzureFunctionsProject<Projects.Cloud5mins_ShortenerTools_Functions>("cloud5mins-shortenertools-functions")
    .WithExternalHttpEndpoints()
    .WithHostStorage(storage)
    .WithReference(table);

//builder.AddProject<Projects.Cloud5mins_ShortenerTools_TinyBlazorAdmin>("admin")
//       .WithReference(functions)
//       .WithReference(table)
//       .WaitFor(functions)
//       .WaitFor(table);

builder.Build().Run();


