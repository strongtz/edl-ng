using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QCEDL.GUI.Helper;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddHostedService<HelperHostService>();

await builder.Build().RunAsync();