using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireContext(ContextType.Guild)]
    public class AdminCommands : ModuleBase
    {
        public AdminCommands(IDatabaseActionFactory dbActionFactory)
        {
            this.DatabaseActionFactory = dbActionFactory;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        [Command("checkPermissions")]
        [Summary("Checks if the bot has all the required permissions. " + 
                 "Omitting a channel mention will check the current channel's permissions.")]
        public Task CheckPermissionsAsync([Optional][Summary("Text channel mention (#textChannelName)")] ITextChannel messageChannel)
        {
            return this.GetHandler().CheckPermissionsAsync(messageChannel);
        }

        [Command("clearReaderRolePrefix")]
        [Summary("Disables restricting readers to those who have a role with the same prefix. Only server admins can" +
            "invoke this.")]
        public Task ClearReaderRolePrefixAsync()
        {
            return this.GetHandler().ClearReaderRolePrefixAsync();
        }

        [Command("clearTeamRolePrefix")]
        [Summary("Disables pairing players together based on sharing a role with the same prefix. Only server " +
            "admins can invoke this.")]
        public Task ClearTeamRolePrefixAsync()
        {
            return this.GetHandler().ClearTeamRolePrefixAsync();
        }

        [Command("disableBonusesByDefault")]
        [Summary("Ensures that bonuses are not tracked by default in this server.")]
        public Task DisableBonusesAsync()
        {
            return this.GetHandler().DisableBonusesByDefaultAsync();
        }

        [Command("enableBonusesByDefault")]
        [Summary("Makes scoring bonuses in a game enabled by default in this server.")]
        public Task EnableBonusesAsync()
        {
            return this.GetHandler().EnableBonusesByDefaultAsync();
        }

        [Command("getPairedChannel")]
        [Summary("Gets the name of the paired voice channel, if it exists. Only server admins can invoke this.")]
        public Task GetPairedChannelAsync([Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel)
        {
            return this.GetHandler().GetPairedChannelAsync(textChannel);
        }

        [Command("getReaderRolePrefix")]
        [Summary("Posts the prefix for the role name used to restrict who can read, if it exists. Only server admins " +
            "can invoke this.")]
        public Task GetReaderRolePrefixAsync()
        {
            return this.GetHandler().GetReaderRolePrefixAsync();
        }

        [Command("getTeamRolePrefix")]
        [Summary("Posts the prefix for the role name used to assign teams, if it exists. Only server admins can " +
            "invoke this.")]
        public Task GetTeamRolePrefixAsync()
        {
            return this.GetHandler().GetTeamRolePrefixAsync();
        }

        [Command("getDefaultFormat")]
        [Summary("Posts the default format for games in this server (such as if bonuses are used).")]
        public Task GetDefaultFormatAsync()
        {
            return this.GetHandler().GetDefaultFormatAsync();
        }

        [Command("pairChannels")]
        [Summary("Pairs a text channel with a voice channel, so buzzes will mute the reader. Only server admins can" +
            "invoke this.")]
        public Task PairChannelsAsync(
            [Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel,
            [Remainder][Summary("Name of the voice channel (no # included)")] string voiceChannelName)
        {
            return this.GetHandler().PairChannelsAsync(textChannel, voiceChannelName);
        }

        [Command("setReaderRolePrefix")]
        [Summary("Only users who have a role with this prefix will be allowed to use !read. For example, if a user " +
            @"has the role ""Readers"", and the prefix is set to ""Reader"", then the user will be able to use !read." +
            @"Only server admins can invoke this.")]
        public Task SetReaderRolePrefixAsync(
            [Remainder][Summary("Prefix for roles that are used to group players into teams")] string prefix)
        {
            return this.GetHandler().SetReaderRolePrefixAsync(prefix);
        }

        [Command("setTeamRolePrefix")]
        [Summary("Players who have a role whose name shares the specified prefix will be on the same team. For " +
            @"example, if a user has the role ""Team Alpha"", and the prefix is set to ""Team"", then the player " +
            @"will be on a team with everyone else who has the role ""Team Alpha"". Only server admins can invoke this.")]
        public Task SetTeamRolePrefixAsync(
            [Remainder][Summary("Prefix for roles that are used to group players into teams")] string prefix)
        {
            return this.GetHandler().SetTeamRolePrefixAsync(prefix);
        }

        [Command("unpairChannel")]
        [Summary("Unpairs a text channel with its voice channel. Only server admins can invoke this.")]
        public Task UnpairChannelAsync(
            [Summary("Text channel mention (#textChannelName) of the text channel to unpair")] ITextChannel textChannel)
        {
            return this.GetHandler().UnpairChannelAsync(textChannel);
        }

        private AdminCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new AdminCommandHandler(this.Context, this.DatabaseActionFactory);
        }
    }
}
