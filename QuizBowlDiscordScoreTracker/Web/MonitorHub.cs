using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace QuizBowlDiscordScoreTracker.Web
{
    public class MonitorHub : Hub
    {
        public async Task AddToChannelAsync(string channelId)
        {
            await this.Groups.AddToGroupAsync(this.Context.ConnectionId, channelId);

            await this.Clients.Client(this.Context.ConnectionId).SendAsync("JoinSuccess", channelId);
        }
    }
}
