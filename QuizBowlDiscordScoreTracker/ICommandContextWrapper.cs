using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker
{
    // TODO: Create this, then create IBotCommandHandler. (may not need the latter).
    // TODO: For the Mock, use the GameState directly or create an IGameState.
    public interface ICommandContextWrapper
    {
        /// <summary>
        /// Returns true if the sender can perform action as the reader, either because they are the reader
        /// or because they are an administrator. Returns false otherwise. Returns false if State is null.
        /// </summary>
        bool CanPerformReaderActions { get; }

        ConfigOptions Options { get; }

        GameState State { get; set; }

        ulong UserId { get; }

        string UserMention { get; }

        Task<bool> HasUserId(ulong userId);

        Task<string> GetUserMention(ulong userId);

        Task<string> GetUserNickname(ulong userId);

        // TODO: May need something else for embed, or have an overload that takes a DiscordEmbed.
        Task RespondAsync(string message);

        Task RespondAsync(DiscordEmbed embed);
    }
}
