using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireUserPermission(GuildPermission.Administrator)]
    public class AdminCommands : BotCommandBase
    {
        public AdminCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
            : base(manager, options, dbActionFactory)
        {
        }

        [Command("checkPermissions")]
        [Summary("Checks if the bot has all the required permissions.")]
        public Task CheckPermissionsAsync()
        {
            return this.HandleCommandAsync(handler => handler.CheckPermissionsAsync());
        }

        [Command("clearTeamRolePrefix")]
        [Summary("Disables pairing players together based on sharing a role with the same prefix. Only server " +
            "admins can invoke this.")]
        public Task ClearTeamRolePrefixAsync()
        {
            return this.HandleCommandAsync(handler => handler.ClearTeamRolePrefixAsync());
        }

        [Command("getPairedChannel")]
        [Summary("Gets the name of the paired voice channel, if it exists. Only server admins can invoke this.")]
        public Task GetPairedChannelAsync([Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel)
        {
            return this.HandleCommandAsync(handler => handler.GetPairedChannelAsync(textChannel));
        }

        [Command("getTeamRolePrefix")]
        [Summary("Posts the prefix for the role name used to assign teams, if it exists. Only server admins can " +
            "invoke this.")]
        public Task GetTeamRolePrefixAsync()
        {
            return this.HandleCommandAsync(handler => handler.GetTeamRolePrefixAsync());
        }

        [Command("pairChannels")]
        [Summary("Pairs a text channel with a voice channel, so buzzes will mute the reader. Only server admins can" +
            "invoke this.")]
        public Task PairChannelsAsync(
            [Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel,
            [Remainder][Summary("Name of the voice channel (no # included)")] string voiceChannel)
        {
            return this.HandleCommandAsync(handler => handler.PairChannelsAsync(textChannel, voiceChannel));
        }

        [Command("setTeamRolePrefix")]
        [Summary("Players who have a role whose name shares the specified prefix will be on the same team. For " +
            @"example, if a user has the role ""Team Alpha"", and the prefix is set to ""Team"", then the player " +
            @"will be on a team with everyone else who has the role ""Team Alpha"". Only server admins can invoke this.")]
        public Task SetTeamRolePrefixAsync(
            [Remainder][Summary("Prefix for roles that are used to group players into teams")] string prefix)
        {
            return this.HandleCommandAsync(handler => handler.SetTeamRolePrefixAsync(prefix));
        }

        [Command("unpairChannel")]
        [Summary("Unpairs a text channel with its voice channel. Only server admins can invoke this.")]
        public Task UnpairChannelAsync(
            [Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel)
        {
            return this.HandleCommandAsync(handler => handler.UnpairChannelAsync(textChannel));
        }
    }
}
