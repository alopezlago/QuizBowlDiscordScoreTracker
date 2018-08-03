using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using QuizBowlDiscordScoreTracker;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    public class MockCommandContextWrapper : ICommandContextWrapper
    {
        private bool canPerformReaderActions;

        public MockCommandContextWrapper()
        {
            this.SentMessages = new List<string>();
            this.SentEmbeds = new List<DiscordEmbed>();
            this.ExistingUserIds = new HashSet<ulong>();

            this.CanPerformReaderActions = false;
        }

        public bool CanPerformReaderActions
        {
            get
            {
                return this.State != null && this.canPerformReaderActions;
            }

            set
            {
                this.canPerformReaderActions = value;
            }
        }

        public ConfigOptions Options { get; set; }

        public GameState State { get; set; }

        public ulong UserId { get; set; }

        public string UserMention
        {
            get
            {
                Task<string> result = this.GetUserMention(this.UserId);
                result.Wait();
                return result.Result;
            }
        }

        public IList<string> SentMessages { get; private set; }

        public IList<DiscordEmbed> SentEmbeds { get; private set; }

        public ISet<ulong> ExistingUserIds { get; set; }

        public async Task<string> GetUserMention(ulong userId)
        {
            string mention = await this.HasUserId(userId) ? $"Mention_{userId}" : null;
            return mention;
        }

        public async Task<string> GetUserNickname(ulong userId)
        {
            string nickname = await this.HasUserId(userId) ? $"Nickname_{userId}" : null;
            return nickname;
        }

        public Task<bool> HasUserId(ulong userId)
        {
            return Task.FromResult(this.ExistingUserIds.Contains(userId));
        }

        public Task RespondAsync(string message)
        {
            this.SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task RespondAsync(DiscordEmbed embed)
        {
            this.SentEmbeds.Add(embed);
            return Task.CompletedTask;
        }
    }
}
