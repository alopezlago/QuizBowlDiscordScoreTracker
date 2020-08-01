using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace QuizBowlDiscordScoreTracker.Web
{
    public class MonitorHub : Hub
    {
        public async Task AddToChannel(string channelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, channelId);

            await Clients.Client(Context.ConnectionId).SendAsync("JoinSuccess", channelId);
        }
    }
}
