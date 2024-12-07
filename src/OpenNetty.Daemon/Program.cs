/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

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
