using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private CommandContext context;
        private GameState cachedGameState;

        public DiscordCommandContextWrapper(CommandContext context)
        {
            this.context = context;
        }

        public GameState State
        {
            get
            {
                if (this.cachedGameState == null)
                {
                    Dictionary<DiscordChannel, GameState> games = this.context.Dependencies.GetDependency<Dictionary<DiscordChannel, GameState>>();
                    games.TryGetValue(this.context.Channel, out GameState state);
                    this.cachedGameState = state;
                }

                return this.cachedGameState;
            }
            set
            {
                Dictionary<DiscordChannel, GameState> games =
                    this.context.Dependencies.GetDependency<Dictionary<DiscordChannel, GameState>>();

                if (value == null)
                {
                    // Remove this game from the dictionary of games
                    // TODO: Possible issue: this.CanPerformReaderActions relies on the state. It might be best to keep this check out
                    // of this class.
                    if (this.CanPerformReaderActions && games.TryGetValue(this.context.Channel, out GameState state))
                    {
                        state.ClearAll();
                        bool removed = games.Remove(this.context.Channel);
                        Debug.Assert(removed, "Game wasn't removed.");
                        this.cachedGameState = null;
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
