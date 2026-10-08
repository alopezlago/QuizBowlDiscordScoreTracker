using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireContext(ContextType.Guild)]
    public class AdminSlashCommands : InteractionModuleBase
    {
        public AdminSlashCommands(
            IDatabaseActionFactory dbActionFactory, IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory)
        {
            this.DatabaseActionFactory = dbActionFactory;
            this.GoogleSheetsGeneratorFactory = googleSheetsGeneratorFactory;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IGoogleSheetsGeneratorFactory GoogleSheetsGeneratorFactory { get; }

        [SlashCommand("check-permissions", "Checks if the bot has all the required permissions.")]
        public Task CheckPermissionsAsync([Optional][Summary("message-channel", "Text channel")] ITextChannel messageChannel)
        {
            return this.GetHandler().CheckPermissionsAsync(messageChannel);
        }

        [SlashCommand(
            "clear-reader-role-prefix",
            "Disables restricting readers to those who have a role with the same prefix.")]
        public Task ClearReaderRolePrefixAsync()
        {
            return this.GetHandler().ClearReaderRolePrefixAsync();
        }

        [SlashCommand("clear-team-role-prefix",
            "Disables pairing players together based on sharing a role with the same prefix.")]
        public Task ClearTeamRolePrefixAsync()
        {
            return this.GetHandler().ClearTeamRolePrefixAsync();
        }

        [SlashCommand("disable-bonuses-by-default", "Ensures that bonuses are not tracked by default in this server.")]
        public Task DisableBonusesAsync()
        {
            return this.GetHandler().DisableBonusesByDefaultAsync();
        }

        [SlashCommand("disable-buzz-queue",
            "Ensures the bot will not queue buzzes, so it only recognizes and remember the buzzer it prompted.")]
        public Task DisableBuzzQueueAsync()
        {
            return this.GetHandler().DisableBuzzQueueAsync();
        }

        [SlashCommand("enable-bonuses-by-default", "Makes scoring bonuses in a game enabled by default in this server.")]
        public Task EnableBonusesAsync()
        {
            return this.GetHandler().EnableBonusesByDefaultAsync();
        }

        [SlashCommand("enable-buzz-queue",
            "Ensures that the bot will queue buzzes. Players will be recognized in the order they buzzed in.")]
        public Task EnableBuzzQueueAsync()
        {
            return this.GetHandler().EnableBuzzQueueAsync();
        }

        [SlashCommand("get-paired-channel", "Gets the name of the paired voice channel, if it exists.")]
        public Task GetPairedChannelAsync([Summary("text-channel")] ITextChannel textChannel)
        {
            return this.GetHandler().GetPairedChannelAsync(textChannel);
        }

        [SlashCommand("get-reader-role-prefix",
            "Posts the prefix for the role name used to restrict who can read, if it exists.")]
        public Task GetReaderRolePrefixAsync()
        {
            return this.GetHandler().GetReaderRolePrefixAsync();
        }

        [SlashCommand("get-team-role-prefix",
            "Posts the prefix for the role name used to assign teams, if it exists")]
        public Task GetTeamRolePrefixAsync()
        {
            return this.GetHandler().GetTeamRolePrefixAsync();
        }

        [SlashCommand("get-default-format",
            "Posts the default format for games in this server (such as if bonuses are used).")]
        public Task GetDefaultFormatAsync()
        {
            return this.GetHandler().GetDefaultFormatAsync();
        }

        [SlashCommand("pair-channels",
            "Pairs a text channel with a voice channel, so buzzes will mute the reader.")]
        public Task PairChannelsAsync(
            [Summary("text-channel")] ITextChannel textChannel,
            [Summary("voice-channel-name")] string voiceChannelName)
        {
            return this.GetHandler().PairChannelsAsync(textChannel, voiceChannelName);
        }

        [SlashCommand("set-reader-role-prefix",
            "Only users who have a role with this prefix will be allowed to use /read.")]
        public Task SetReaderRolePrefixAsync(
            [Summary(description: "Prefix for roles that are used to group players into teams")] string prefix)
        {
            return this.GetHandler().SetReaderRolePrefixAsync(prefix);
        }

        [HumanOnly]
        [SlashCommand("set-rosters-for-tj", "Sets the rosters for the Rosters sheet for TJ Sheets.")]
        public Task SetRostersFromRolesForTJ(
            [Summary("sheets-url", "The URL to the TJ Rosters Google Sheet.")] string sheetsUrl)
        {
            return this.GetHandler().SetRostersFromRolesForTJ(sheetsUrl);
        }

        [HumanOnly]
        [SlashCommand("set-rosters-for-ucsd", "Sets the rosters for the Rosters sheet for the UCSD scoresheets.")]
        public Task SetRostersFromRolesForUCSD(
            [Summary("sheets-url", "The URL to the UCSD Rosters Google Sheet.")] string sheetsUrl)
        {
            return this.GetHandler().SetRostersFromRolesForUCSD(sheetsUrl);
        }

        [SlashCommand("set-team-role-prefix",
            "Players who have a role whose name shares the specified prefix will be on the same team.")]
        public Task SetTeamRolePrefixAsync(
            [Summary(description: "Prefix for roles that are used to group players into teams")] string prefix)
        {
            return this.GetHandler().SetTeamRolePrefixAsync(prefix);
        }

        [SlashCommand("unpair-channel", "Unpairs a text channel with its voice channel.")]
        public Task UnpairChannelAsync(
            [Summary("text-channel")] ITextChannel textChannel)
        {
            return this.GetHandler().UnpairChannelAsync(textChannel);
        }

        private AdminCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new AdminCommandHandler(this.Context, this.DatabaseActionFactory, this.GoogleSheetsGeneratorFactory);
        }
    }
}
