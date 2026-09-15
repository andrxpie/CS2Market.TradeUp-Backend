var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddIngestion();

builder.Build().Run();
