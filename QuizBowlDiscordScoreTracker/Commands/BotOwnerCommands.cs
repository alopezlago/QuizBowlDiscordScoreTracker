using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireOwner]
    public class BotOwnerCommands : BotCommandBase
    {
        public BotOwnerCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
            : base(manager, options, dbActionFactory)
        {
        }

        [Command("mapConfigToDatabase")]
        [Summary("Maps configuration information from config.txt to the database, so users can control their own " +
            "guild-specific settings.")]
        public Task MapConfigToDatabaseAsync()
        {
            return this.HandleCommandAsync(handler => handler.MapConfigToDatabaseAsync());
        }
    }
}
