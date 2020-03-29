using System;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.Extensions.Options;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireContext(ContextType.Guild)]
    public abstract class BotCommandBase : ModuleBase
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(BotCommandHandler));

        private readonly GameStateManager manager;
        private readonly IOptionsMonitor<BotConfiguration> options;

        public BotCommandBase(GameStateManager manager, IOptionsMonitor<BotConfiguration> options)
        {
            this.manager = manager;
            this.options = options;
        }

        protected Task HandleCommandAsync(Func<BotCommandHandler, Task> handleCommandFunction)
        {
            // If we're not in a supported channel, we should exit early
            if (!this.options.CurrentValue.IsTextSupportedChannel(this.Context.Guild.Name, this.Context.Channel.Name))
            {
                return Task.CompletedTask;
            }

            if (!this.manager.TryGet(this.Context.Channel.Id, out GameState gameState))
            {
                gameState = null;
            }

            // Discord.Net complains if a task takes too long while handling the command. Unfortunately, the current
            // tournament lock may block certain commands, and other commands are just long-running (like !start).
            // To work around this (and to keep the command handler unblocked), we have to run the task in a separate
            // thread, which requires us running it through Task.Run.
            BotCommandHandler commandHandler = new BotCommandHandler(this.Context, this.manager, gameState, Logger);
            Task.Run(async () =>
            {
                try
                {
                    await handleCommandFunction(commandHandler);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error while handling a command");
                    throw;
                }
            });

            // If we return the task created by Task.Run the command handler will still be blocked. It seems like
            // Discord.Net will wait for the returned task to complete, which will block the Discord.Net's command
            // handler for too long. This does mean that we never know when a command is truly handled. This also
            // means that any data structures commands modify need to be thread-safe.
            return Task.CompletedTask;
        }
    }
}
