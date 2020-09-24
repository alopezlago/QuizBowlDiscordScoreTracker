using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace QuizBowlDiscordScoreTracker.Web
{
    public class MonitorHub : Hub
    {
        // Use the old name (without Async) so it's found
        public async Task AddToChannel(string channelId)
        {
            await this.Groups.AddToGroupAsync(this.Context.ConnectionId, channelId);

            await this.Clients.Client(this.Context.ConnectionId).SendAsync("JoinSuccess", channelId);
        }
    }
}
