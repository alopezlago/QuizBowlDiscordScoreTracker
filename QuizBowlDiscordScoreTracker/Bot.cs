using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class Bot : IDisposable
    {
        private readonly SortedSet<PlayerEntry> playersQueue;
        private readonly HashSet<PlayerEntry> alreadyBuzzedPlayers;
        private readonly Dictionary<DiscordUser, int> score;

        private readonly DiscordClient discordClient;
        private readonly CommandsNextModule commandsModule;

        public Bot(string accessToken)
        {
            this.playersQueue = new SortedSet<PlayerEntry>();
            this.alreadyBuzzedPlayers = new HashSet<PlayerEntry>();
            this.score = new Dictionary<DiscordUser, int>();

            this.discordClient = new DiscordClient(new DiscordConfiguration()
            {
                Token = accessToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });
            this.commandsModule = this.discordClient.UseCommandsNext(new CommandsNextConfiguration()
            {
                StringPrefix = "!"
            });

            this.commandsModule.RegisterCommands<BotCommands>();
        }

        public Task ConnectAsync()
        {
            return this.discordClient.ConnectAsync();
        }

        public void Dispose()
        {
            this.discordClient?.Dispose();
        }

        private class PlayerEntry : IComparable<PlayerEntry>
        {
            public DiscordUser User { get; set; }

            public DateTime Timestamp { get; set; }

            public int CompareTo(PlayerEntry other)
            {
                if (other == null)
                {
                    return 1;
                }

                return this.Timestamp.CompareTo(other.Timestamp);
            }

            public override int GetHashCode()
            {
                return this.User.GetHashCode() ^ this.Timestamp.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is PlayerEntry entry)
                {
                    return this.User.Equals(entry.User) && this.Timestamp.Equals(entry.Timestamp);
                }

                return false;
            }
        }
    }
}
