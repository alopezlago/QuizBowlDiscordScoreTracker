using System.IO;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public interface IFileScoresheetGenerator
    {
        Task<IResult<Stream>> TryCreateScoresheet(GameState game, string readerName, string roomName);
    }
}
