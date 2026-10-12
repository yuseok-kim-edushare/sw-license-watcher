using SwLicenseWatcher.Api;

#if NATIVE_AOT
var contentRoot = AppContext.BaseDirectory;
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot"),
});
builder.WebHost.UseKestrelHttpsConfiguration();
#else
var builder = WebApplication.CreateBuilder(args);
#endif

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher.Api");
}
builder.Services
    .AddApiOptions(builder.Configuration)
    .AddApiServices();

var app = builder.Build();
app.UseApiPipeline();
app.MapApiEndpoints();
await app.InitializeApiAsync();
app.Run();
