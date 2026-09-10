using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SkyAPI.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new WebSession(builder.HostEnvironment.BaseAddress, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>()));

await builder.Build().RunAsync();
