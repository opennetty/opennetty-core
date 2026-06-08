/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Reactive.Linq;
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
    // Load the configuration files from the content root and the "Configuration" directory, if available.
    ImmutableArray<IFileInfo> files = [.. GetConfigurationFiles(builder.Environment.ContentRootFileProvider)];

    options.ImportFromXmlConfiguration(files).ValidateOnStart();

    options.AddMqttIntegration(options => options.ImportFromXmlConfiguration(files).ValidateOnStart());

    static IEnumerable<IFileInfo> GetConfigurationFiles(IFileProvider provider)
    {
        var file = provider.GetFileInfo("OpenNettyConfiguration.xml");
        if (file.Exists)
        {
            yield return file;
        }

        var directory = provider.GetDirectoryContents("Configuration");
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
