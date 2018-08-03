using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class DiscordCommandContextWrapper : ICommandContextWrapper
    {
        CommandContext context;
        GameState cachedGameState;

        public DiscordCommandContextWrapper(CommandContext context)
        {
            this.context = context;
        }

        public GameState State
        {
            get
            {
                if (cachedGameState == null)
                {
                    Dictionary<DiscordChannel, GameState> games = this.context.Dependencies.GetDependency<Dictionary<DiscordChannel, GameState>>();
                    games.TryGetValue(this.context.Channel, out GameState state);
                    cachedGameState = state;
                }

                return cachedGameState;
            }
            set
            {
                Dictionary<DiscordChannel, GameState> games =
                    this.context.Dependencies.GetDependency<Dictionary<DiscordChannel, GameState>>();

                if (value == null && this.CanPerformReaderActions)
                {
                    // Remove this game from the dictionary of games
                    if (games.TryGetValue(this.context.Channel, out GameState state))
                    {
                        state.ClearAll();
                        games.Remove(this.context.Channel);
                    }

                    return;
                }

                games[this.context.Channel] = value;
                this.cachedGameState = value;
            }
        }

        public ConfigOptions Options
        {
            get { return this.context.Dependencies.GetDependency<ConfigOptions>(); }
        }

        public ulong UserId
        {
            get { return this.context.User.Id; }
        }

        public string UserMention
        {
            get { return this.context.User.Mention; }
        }

        // We may want to make this a property, since we don't need to use async here.
        public bool CanPerformReaderActions
        {
            get
            {
                if (this.State == null)
                {
                    // If there's no game there are no reader actions that can be performed.
                    return false;
                }

                if (this.State.ReaderId == this.context.User.Id ||
                    this.context.Channel.PermissionsFor(this.context.Member) == Permissions.Administrator)
                {
                    return true;
                }

                // We can't rely on Email because the bot may not have acess to it
                return this.Options.AdminIds != null &&
                    this.Options.AdminIds.Contains(context.User.Id.ToString(CultureInfo.InvariantCulture));
            }
        }

        public async Task<string> GetUserMention(ulong userId)
        {
            DiscordMember member = await this.context.Guild.GetMemberAsync(userId);
            return member?.Mention;
        }

        public async Task<string> GetUserNickname(ulong userId)
        {
            DiscordMember member = await this.context.Guild.GetMemberAsync(userId);
            if (member == null)
            {
                return null;
            }

            return member.Nickname ?? member.DisplayName;
        }

        public async Task<bool> HasUserId(ulong userId)
        {
            DiscordMember member = await this.context.Guild.GetMemberAsync(userId);
            return member != null;
        }

        public async Task RespondAsync(string message)
        {
            await this.context.RespondAsync(content: message);
        }

        public async Task RespondAsync(DiscordEmbed embed)
        {
            await this.context.RespondAsync(embed: embed);
        }
    }
}
