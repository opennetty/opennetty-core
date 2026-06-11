/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Reactive.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddSystemd();

builder.Services.AddOpenNetty(options =>
{
    // Load the configuration files from the specified location(s).
    ImmutableArray<IFileInfo> files = [.. GetConfigurationFiles(builder.Configuration, builder.Environment.ContentRootFileProvider)];
    if (files.IsDefaultOrEmpty)
    {
        throw new InvalidOperationException(SR.FormatID0125(
            builder.Configuration["ConfigurationFile"] ?? "configuration.xml", ".xml",
            builder.Configuration["ConfigurationDirectory"] ?? "configuration"));
    }

    options.ImportFromXmlConfiguration(files).ValidateOnStart();

    options.AddMqttIntegration(options => options.ImportFromXmlConfiguration(files).ValidateOnStart());

    static IEnumerable<IFileInfo> GetConfigurationFiles(IConfiguration configuration, IFileProvider provider)
    {
        var file = provider.GetFileInfo(configuration["ConfigurationFile"] ?? "configuration.xml");   
        if (file.Exists)
        {
            yield return file;
        }

        var directory = provider.GetDirectoryContents(configuration["ConfigurationDirectory"] ?? "configuration");
        if (directory.Exists)
        {
            using var enumerator = directory.OrderBy(static file => file.Name, StringComparer.Ordinal).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.IsDirectory)
                {
                    continue;
                }

                if (!string.Equals(Path.GetExtension(enumerator.Current.Name), ".xml", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return enumerator.Current;
            }
        }
    }
});

var app = builder.Build();
await app.RunAsync();
