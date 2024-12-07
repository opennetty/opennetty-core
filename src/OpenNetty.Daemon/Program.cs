using System.Reactive.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddSystemd()
    .AddWindowsService();

builder.Services.AddOpenNetty(options =>
{
    var file = builder.Environment.ContentRootFileProvider.GetFileInfo("OpenNettyConfiguration.xml");
    options.ImportFromXmlConfiguration(file);

    options.AddMqttIntegration(options => options.ImportFromXmlConfiguration(file));
});

var app = builder.Build();
await app.RunAsync();
