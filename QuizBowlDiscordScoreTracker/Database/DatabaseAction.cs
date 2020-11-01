using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace QuizBowlDiscordScoreTracker.Database
{
    public sealed class DatabaseAction : IDisposable, IAsyncDisposable
    {
        public DatabaseAction(string dataSource = null) : this(new BotConfigurationContext(dataSource))
        {
        }

        public DatabaseAction(BotConfigurationContext context)
        {
            this.Context = context;
        }

        private BotConfigurationContext Context { get; }

        private bool IsDisposed { get; set; }

        public async Task AddCommandBannedUser(ulong userId)
        {
            UserSetting user = await this.AddOrGetUserAsync(userId);
            user.CommandBanned = true;
            await this.Context.SaveChangesAsync();
        }

        public async Task ClearReaderRolePrefixAsync(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            if (guild == null)
            {
                return;
            }

            guild.ReaderRolePrefix = null;
            await this.RemoveGuildIfEmptyAsync(guild);
            await this.Context.SaveChangesAsync();
        }

        public async Task ClearTeamRolePrefixAsync(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            if (guild == null)
            {
                return;
            }

            guild.TeamRolePrefix = null;
            await this.RemoveGuildIfEmptyAsync(guild);
            await this.Context.SaveChangesAsync();
        }

        public void Dispose()
        {
            if (!this.IsDisposed)
            {
                this.Context.Dispose();
                this.IsDisposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!this.IsDisposed)
            {
                this.IsDisposed = true;
                await this.Context.DisposeAsync();
            }
        }

        public Task MigrateAsync()
        {
            return this.Context.Database.MigrateAsync();
        }

        public async Task<bool> GetCommandBannedAsync(ulong userId)
        {
            UserSetting user = await this.Context.FindAsync<UserSetting>(userId);
            return user?.CommandBanned ?? false;
        }

        public async Task<ulong?> GetPairedVoiceChannelIdOrNullAsync(ulong textChannelId)
        {
            TextChannelSetting textChannel = await this.Context.FindAsync<TextChannelSetting>(textChannelId);
            return textChannel?.VoiceChannelId;
        }

        public async Task<string> GetReaderRolePrefixAsync(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            return guild?.ReaderRolePrefix ?? null;
        }

        public async Task<string> GetTeamRolePrefixAsync(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            return guild?.TeamRolePrefix ?? null;
        }

        public async Task<bool> GetUseBonuses(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            return guild?.UseBonuses ?? false;
        }

        public async Task<int> GetGuildExportCount(ulong guildId)
        {
            GuildSetting guild = await this.AddOrGetGuildAsync(guildId);
            int currentDay = DateTime.UtcNow.Day;
            if (guild.LastExportDay != currentDay)
            {
                guild.LastExportDay = currentDay;
                guild.ExportCount = 0;
                await this.Context.SaveChangesAsync();
            }

            return guild.ExportCount;
        }

        public async Task<int> GetUserExportCount(ulong userId)
        {
            UserSetting user = await this.AddOrGetUserAsync(userId);
            int currentDay = DateTime.UtcNow.Day;
            if (user.LastExportDay != currentDay)
            {
                user.LastExportDay = currentDay;
                user.ExportCount = 0;
                await this.Context.SaveChangesAsync();
            }

            return user.ExportCount;
        }

        public async Task IncrementGuildExportCount(ulong guildId)
        {
            GuildSetting guild = await this.AddOrGetGuildAsync(guildId);
            int currentDay = DateTime.UtcNow.Day;
            if (guild.LastExportDay != currentDay)
            {
                guild.LastExportDay = currentDay;
                guild.ExportCount = 1;
            }
            else
            {
                guild.ExportCount++;
            }

            await this.Context.SaveChangesAsync();
        }

        public async Task IncrementUserExportCount(ulong userId)
        {
            UserSetting user = await this.AddOrGetUserAsync(userId);
            int currentDay = DateTime.UtcNow.Day;
            if (user.LastExportDay != currentDay)
            {
                user.LastExportDay = currentDay;
                user.ExportCount = 1;
            }
            else
            {
                user.ExportCount++;
            }

            await this.Context.SaveChangesAsync();
        }

        public async Task PairChannelsAsync(ulong guildId, ulong textChannelId, ulong voiceChannelId)
        {
            TextChannelSetting textChannel = await this.AddOrGetTextChannelAsync(guildId, textChannelId);
            textChannel.VoiceChannelId = voiceChannelId;
            await this.Context.SaveChangesAsync();
        }

        public async Task PairChannelsAsync(ulong guildId, (ulong textChannelId, ulong voiceChannelId)[] channelPairs)
        {
            Verify.IsNotNull(channelPairs, nameof(channelPairs));

            TextChannelSetting[] textChannels = await Task.WhenAll(
                channelPairs.Select(pair => this.AddOrGetTextChannelAsync(guildId, pair.textChannelId)));
            for (int i = 0; i < textChannels.Length; i++)
            {
                textChannels[i].VoiceChannelId = channelPairs[i].voiceChannelId;
            }

            await this.Context.SaveChangesAsync();
        }

        public async Task RemoveCommandBannedUser(ulong userId)
        {
            UserSetting user = await this.AddOrGetUserAsync(userId);
            user.CommandBanned = false;
            this.RemoveUserIfEmptyAsync(user);
            await this.Context.SaveChangesAsync();
        }

        public async Task SetReaderRolePrefixAsync(ulong guildId, string prefix)
        {
            GuildSetting guild = await this.AddOrGetGuildAsync(guildId);
            guild.ReaderRolePrefix = prefix;
            await this.Context.SaveChangesAsync();
        }

        public async Task SetTeamRolePrefixAsync(ulong guildId, string prefix)
        {
            GuildSetting guild = await this.AddOrGetGuildAsync(guildId);
            guild.TeamRolePrefix = prefix;
            await this.Context.SaveChangesAsync();
        }

        public async Task SetUseBonuses(ulong guildId, bool useBonuses)
        {
            GuildSetting guild = await this.AddOrGetGuildAsync(guildId);
            guild.UseBonuses = useBonuses;
            await this.Context.SaveChangesAsync();
        }

        public async Task UnpairChannelAsync(ulong textChannelId)
        {
            TextChannelSetting textChannel = await this.Context.FindAsync<TextChannelSetting>(textChannelId);
            if (textChannel == null)
            {
                return;
            }

            textChannel.VoiceChannelId = null;
            await this.RemoveTextChannelIfEmptyAsync(textChannel);
            await this.Context.SaveChangesAsync();
        }

        private async Task<TextChannelSetting> AddOrGetTextChannelAsync(ulong guildId, ulong textChannelId)
        {
            TextChannelSetting textChannel = await this.Context.FindAsync<TextChannelSetting>(textChannelId);
            if (textChannel != null)
            {
                return textChannel;
            }

            // Ensure we have a guild. We need this to exist for the foreign key validation to pass
            await this.AddOrGetGuildAsync(guildId);

            textChannel = new TextChannelSetting()
            {
                TextChannelSettingId = textChannelId,
                GuildSettingId = guildId
            };
            this.Context.TextChannels.Add(textChannel);

            await this.Context.SaveChangesAsync();
            return textChannel;
        }

        private async Task<GuildSetting> AddOrGetGuildAsync(ulong guildId)
        {
            GuildSetting guild = await this.Context.FindAsync<GuildSetting>(guildId);
            if (guild != null)
            {
                return guild;
            }

            guild = new GuildSetting()
            {
                GuildSettingId = guildId
            };
            this.Context.Guilds.Add(guild);

            await this.Context.SaveChangesAsync();
            return guild;
        }

        private async Task<UserSetting> AddOrGetUserAsync(ulong userId)
        {
            UserSetting user = await this.Context.FindAsync<UserSetting>(userId);
            if (user != null)
            {
                return user;
            }

            user = new UserSetting()
            {
                UserSettingId = userId,
                ExportCount = 0,
                LastExportDay = DateTime.UtcNow.Day
            };
            this.Context.Users.Add(user);

            await this.Context.SaveChangesAsync();
            return user;
        }

        private async Task RemoveGuildIfEmptyAsync(GuildSetting guild)
        {
            if (guild.TeamRolePrefix != null ||
                guild.ReaderRolePrefix != null ||
                guild.UseBonuses ||
                (guild.ExportCount > 0 && guild.LastExportDay == DateTime.UtcNow.Day))
            {
                return;
            }

            // The guild may not have included the text channels, so check if there are any
            GuildSetting guildWithTextChannels = await this.Context.Guilds
                .Include(g => g.TextChannels)
                .FirstOrDefaultAsync(g => g.GuildSettingId == guild.GuildSettingId);
            if (guildWithTextChannels.TextChannels == null || guildWithTextChannels.TextChannels.Count == 0)
            {
                this.Context.Remove(guild);
            }
        }

        private async Task RemoveTextChannelIfEmptyAsync(TextChannelSetting textChannel)
        {
            if (textChannel.TeamMessageId != null || textChannel.VoiceChannelId != null)
            {
                return;
            }

            this.Context.Remove(textChannel);

            // If we had to remove a text channel, then maybe we have to remove the guild, too. We will need to
            // save the fact that we removed the text channel for the guild to realize that it has no text channels
            // left.
            GuildSetting guild = await this.AddOrGetGuildAsync(textChannel.GuildSettingId);
            if (guild == null)
            {
                return;
            }

            await this.Context.SaveChangesAsync();
            await this.RemoveGuildIfEmptyAsync(guild);
        }

        private void RemoveUserIfEmptyAsync(UserSetting user)
        {
            if (user.CommandBanned || user.LastExportDay == DateTime.UtcNow.Day)
            {
                return;
            }

            this.Context.Remove(user);
        }
    }
}
