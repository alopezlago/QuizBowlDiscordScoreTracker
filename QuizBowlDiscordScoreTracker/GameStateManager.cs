using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace QuizBowlDiscordScoreTracker
{
    public class GameStateManager
    {
        private readonly ConcurrentDictionary<ulong, GameState> gamesInChannel;

        public GameStateManager()
        {
            this.gamesInChannel = new ConcurrentDictionary<ulong, GameState>();
        }

        public IEnumerable<KeyValuePair<ulong, GameState>> GetGameChannelPairs()
        {
            return ImmutableList<KeyValuePair<ulong, GameState>>.Empty.AddRange(this.gamesInChannel);
        }

        public bool TryCreate(ulong channelId, out GameState gameState)
        {
            gameState = new GameState();
            if (this.gamesInChannel.TryAdd(channelId, gameState))
            {
                return true;
            }

            gameState = null;
            return false;
        }

        public bool TryRemove(ulong channelId)
        {
            return this.gamesInChannel.TryRemove(channelId, out _);
        }

        public bool TryGet(ulong channelId, out GameState gameState)
        {
            return this.gamesInChannel.TryGetValue(channelId, out gameState);
        }
    }
}
