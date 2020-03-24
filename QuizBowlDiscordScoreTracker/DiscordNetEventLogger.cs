using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Serilog;
using Serilog.Events;

namespace QuizBowlDiscordScoreTracker
{
    public sealed class DiscordNetEventLogger : IDisposable
    {
        private readonly ILogger logger;
        private DiscordSocketClient client;
        private CommandService commandService;

        public DiscordNetEventLogger(DiscordSocketClient client, CommandService commandService)
        {
            if (client == null)
            {
                // TODO: Throwing exceptions in constructors is generally frowned upon; see if there is another way
                // to deal with this.
                throw new ArgumentNullException(nameof(client));
            }
            else if (commandService == null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }

            this.logger = Log.ForContext(this.GetType());
            this.client = client;
            this.client.Log += this.LogMessageAsync;

            this.commandService = commandService;
            this.commandService.Log += this.LogMessageAsync;
        }

        public void Dispose()
        {
            if (this.client != null)
            {
                this.client.Log -= this.LogMessageAsync;
                this.client = null;
                this.commandService.Log -= this.LogMessageAsync;
                this.commandService = null;
            }
        }

        private Task LogMessageAsync(LogMessage message)
        {
            LogEventLevel logLevel = ConvertLogLevels(message.Severity);
            this.logger.Write(logLevel, "Discord.Net message: {0}", message.Message);
            if (message.Exception != null)
            {
                this.logger.Write(logLevel, message.Exception, "Exception occurred on Discord.Net side");
            }

            return Task.CompletedTask;
        }

        private static LogEventLevel ConvertLogLevels(LogSeverity discordSeverity)
        {
            switch (discordSeverity)
            {
                case LogSeverity.Critical:
                    return LogEventLevel.Fatal;
                case LogSeverity.Error:
                    return LogEventLevel.Error;
                case LogSeverity.Warning:
                    return LogEventLevel.Warning;
                case LogSeverity.Info:
                    return LogEventLevel.Information;
                case LogSeverity.Verbose:
                    // Verbose and Debug are swapped between the two levels. Verbose is for debug level events
                    return LogEventLevel.Debug;
                case LogSeverity.Debug:
                    return LogEventLevel.Verbose;
                default:
                    throw new ArgumentOutOfRangeException(nameof(discordSeverity));
            }
        }
    }
}
