using Discord;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    // This is an interface for mocking guild text channels. It needs to be public for Moq to use it.
    public interface IGuildTextChannel : IGuildChannel, IMessageChannel, ITextChannel
    {
    }
}
