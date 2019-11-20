using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    public class MessageStore
    {
        public MessageStore()
        {
            this.ChannelEmbeds = new List<string>();
            this.ChannelMessages = new List<string>();
            this.DirectMessages = new List<string>();
        }

        public IList<string> ChannelEmbeds { get; }

        // We could add the channel name too, but for now we always reply to the same channel
        public IList<string> ChannelMessages { get; }

        public IList<string> DirectMessages { get; }

        public void Clear()
        {
            this.ChannelEmbeds.Clear();
            this.ChannelMessages.Clear();
            this.DirectMessages.Clear();
        }

        public void VerifyChannelEmbeds(params string[] channelEmbed)
        {
            VerifyMessages(this.ChannelEmbeds, channelEmbed, "channel message");
        }

        public void VerifyChannelMessages(params string[] channelMessages)
        {
            VerifyMessages(this.ChannelMessages, channelMessages, "channel message");
        }

        public void VerifyDirectMessages(params string[] directMessages)
        {
            VerifyMessages(this.DirectMessages, directMessages, "DM");
        }

        private static void VerifyMessages(IList<string> messages, string[] expectedMessages, string messageType)
        {
            Assert.AreEqual(expectedMessages.Length, messages.Count, $"Unexpected number of {messageType}s.");
            for (int i = 0; i < expectedMessages.Length; i++)
            {
                string message = expectedMessages[i];
                Assert.AreEqual(message, messages[i], $"Unexpected {messageType} at index {i}");
            }
        }
    }
}
