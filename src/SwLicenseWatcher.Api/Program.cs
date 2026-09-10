using SwLicenseWatcher.Api;

#if NATIVE_AOT
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();
builder.WebHost.UseWebRoot("wwwroot");
#else
var builder = WebApplication.CreateBuilder(args);
#endif

builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher.Api");
builder.Services
    .AddApiOptions(builder.Configuration)
    .AddApiServices();

var app = builder.Build();
app.UseApiPipeline();
app.MapApiEndpoints();
await app.InitializeApiAsync();
app.Run();
