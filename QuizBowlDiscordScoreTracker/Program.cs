using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker
{
    public static class Program
    {
        // 100 MB file limit
        private const long maxLogfileSize = 1024 * 1024 * 100;

        // 30 seconds
        private const int configFileReloadDelayMs = 30 * 1000;

        // Following the example from https://dsharpplus.emzi0767.com/articles/first_bot.html
        public static void Main(string[] args)
        {
            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static async Task MainAsync(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(configBuilder =>
                {
                    // TODO: Get the token from an encrypted file. This could be done by using DPAPI and writing a tool to help
                    // convert the user access token into a token file using DPAPI. The additional entropy could be a config
                    // option.
                    // In preparation for this work the token is still taken from a separate file.
                    string botToken = File.ReadAllText("discordToken.txt");

                    configBuilder
                        .SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
                        .AddJsonFile(jsonConfiguration =>
                        {
                            jsonConfiguration.Path = "config.txt";
                            jsonConfiguration.ReloadOnChange = true;
                            jsonConfiguration.ReloadDelay = configFileReloadDelayMs;
                            jsonConfiguration.Optional = false;
                            jsonConfiguration.OnLoadException = fileLoadExceptionContext =>
                            {
                                Console.Error.WriteLine("Failed to load configuration file.");
                                Console.Error.WriteLine(fileLoadExceptionContext.Exception);
                                Environment.Exit(2);
                            };
                        })
                        .AddInMemoryCollection(new KeyValuePair<string, string>[]
                        {
                            // TODO: Harden this. We shouldn't have the token living in the configuration for a long time
                            new KeyValuePair<string, string>(BotConfiguration.TokenKey, botToken)
                        });
                })
                .ConfigureServices((hostContext, serviceCollection) =>
                {
                    serviceCollection.AddHostedService<Bot>();
                    serviceCollection.AddOptions<BotConfiguration>();
                    serviceCollection.Configure<BotConfiguration>(hostContext.Configuration);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseContentRoot(Directory.GetCurrentDirectory() + "/Web")
                              .UseStartup<Startup>();
                })
                .ConfigureLogging(loggingBuilder =>
                {
                    // We use Serilog, not the built-in logging framework
                    LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
                        .WriteTo.Console()
                        .WriteTo.File(
                            Path.Combine("logs", "bot.log"),
                            fileSizeLimitBytes: maxLogfileSize,
                            retainedFileCountLimit: 10);
                    Log.Logger = loggerConfiguration.CreateLogger();
                })
                .UseConsoleLifetime()
                .Build();

            await host.RunAsync();
        }
    }
}
