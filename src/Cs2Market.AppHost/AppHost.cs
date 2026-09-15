var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("pg")
    .WithDataVolume()
    .AddDatabase("cs2market");

var redis = builder.AddRedis("cache").WithDataVolume();

var api = builder.AddProject<Projects.Cs2Market_Api>("api")
    .WithReference(postgres).WithReference(redis).WaitFor(postgres)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Cs2Market_Ingestion>("ingestion")
    .WithReference(postgres).WithReference(redis).WaitFor(postgres);

builder.AddProject<Projects.Cs2Market_Mcp>("mcp").WithReference(api)
    .WithHttpHealthCheck("/health");

// The "web" resource (Next.js) joins in M5-1.

builder.Build().Run();
