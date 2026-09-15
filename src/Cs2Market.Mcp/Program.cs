var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMcpSurface();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcpSurface();

app.Run();
