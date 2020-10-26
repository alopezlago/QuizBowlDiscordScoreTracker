using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public class BotOwnerCommands : ModuleBase
    {
        public BotOwnerCommands(IOptionsMonitor<BotConfiguration> options, IDatabaseActionFactory dbActionFactory)
        {
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        [Command("banUser")]
        [Summary("Bans a user from using commands.")]
        public Task BanUserAsync([Summary("The Discord ID of the user to ban")] ulong userId)
        {
            return this.GetHandler().BanUserAsync(userId);
        }

        // This is a temporary command to transition between v1/v2 and v3. Few if any users will use this, so no tests
        // are currently planned (and therefore no command handler is being written).
        // This converts the channel pair mappings in the config file into database entries
        [Command("mapConfigToDatabase")]
        [Summary("Maps configuration information from config.txt to the database, so users can control their own " +
            "guild-specific settings.")]
        public Task MapConfigToDatabaseAsync()
        {
            return this.GetHandler().MapConfigToDatabaseAsync();
        }

        [Command("unbanUser")]
        [Summary("Unbans a user from using commands.")]
        public Task UnbanUserAsync([Summary("The Discord ID of the user to unban")] ulong userId)
        {
            return this.GetHandler().UnbanUserAsync(userId);
        }

        private BotOwnerCommandHandler GetHandler()
        {
            return new BotOwnerCommandHandler(this.Context, this.Options, this.DatabaseActionFactory);
        }
    }
}
