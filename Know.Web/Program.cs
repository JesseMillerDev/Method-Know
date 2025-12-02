using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Know.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => 
    new HttpClient { 
        BaseAddress = new Uri(builder.Configuration["BackendUrl"] ?? builder.HostEnvironment.BaseAddress) 
    });

await builder.Build().RunAsync();
