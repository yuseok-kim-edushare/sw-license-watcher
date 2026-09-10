using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SwLicenseWatcher.Admin;
using SwLicenseWatcher.Admin.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<AdminAuthHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AdminAuthHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        DefaultRequestHeaders =
        {
            Accept = { new MediaTypeWithQualityHeaderValue("application/json") }
        }
    };
});
builder.Services.AddScoped<AdminApiClient>();
builder.Services.AddScoped<AdminRequestHandler>();
builder.Services.AddScoped<DetailDrawerHost>();

await builder.Build().RunAsync();
