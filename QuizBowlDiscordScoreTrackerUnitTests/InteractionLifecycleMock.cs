using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Moq;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    internal sealed class InteractionLifecycleMock
    {
        private readonly MessageStore messageStore;
        private bool originalExists;
        private bool deferred;
        private bool originalIsEphemeral;

        public InteractionLifecycleMock(MessageStore messageStore, string commandName = "buzz")
        {
            this.messageStore = messageStore;
            this.Interaction = new Mock<ISlashCommandInteraction>();
            Mock<IApplicationCommandInteractionData> data = new Mock<IApplicationCommandInteractionData>();
            data.SetupGet(value => value.Name).Returns(commandName);
            this.Interaction.SetupGet(interaction => interaction.Data).Returns(data.Object);
            this.Interaction.SetupGet(interaction => interaction.HasResponded).Returns(() => this.HasResponded);
            this.Interaction.SetupGet(interaction => interaction.CreatedAt).Returns(DateTimeOffset.UtcNow);

            this.Interaction.Setup(interaction => interaction.DeferAsync(It.IsAny<bool>(), It.IsAny<RequestOptions>()))
                .Returns<bool, RequestOptions>((ephemeral, options) =>
                {
                    this.Acknowledge(ephemeral);
                    this.deferred = true;
                    this.DeferCount++;
                    return Task.CompletedTask;
                });
            this.Interaction.Setup(interaction => interaction.RespondAsync(
                It.IsAny<string>(), It.IsAny<Embed[]>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(), It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(), It.IsAny<PollProperties>(), It.IsAny<MessageFlags>()))
                .Returns<string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions, PollProperties, MessageFlags>(
                    (text, embeds, isTTS, ephemeral, mentions, components, embed, options, poll, flags) =>
                    {
                        this.Acknowledge(ephemeral);
                        this.OriginalContent = text;
                        this.RecordMessage(text, embeds, embed);
                        return this.InitialResponseCompletion;
                    });
            this.Interaction.Setup(interaction => interaction.FollowupAsync(
                It.IsAny<string>(), It.IsAny<Embed[]>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(), It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(), It.IsAny<PollProperties>(), It.IsAny<MessageFlags>()))
                .Returns<string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions, PollProperties, MessageFlags>(
                    (text, embeds, isTTS, ephemeral, mentions, components, embed, options, poll, flags) =>
                    {
                        this.RequireAcknowledgment();
                        this.FollowupVisibilities.Add(this.deferred ? this.originalIsEphemeral : ephemeral);
                        this.deferred = false;
                        this.RecordMessage(text, embeds, embed);
                        return Task.FromResult(Mock.Of<IUserMessage>());
                    });
            this.Interaction.Setup(interaction => interaction.RespondWithFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Embed[]>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(),
                It.IsAny<Embed>(), It.IsAny<RequestOptions>(), It.IsAny<PollProperties>(), It.IsAny<MessageFlags>()))
                .Returns<Stream, string, string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions, PollProperties, MessageFlags>(
                    (stream, filename, text, embeds, isTTS, ephemeral, mentions, components, embed, options, poll, flags) =>
                    {
                        this.Acknowledge(ephemeral);
                        this.messageStore.Files.Add((stream, filename, text));
                        return Task.CompletedTask;
                    });
            this.Interaction.Setup(interaction => interaction.FollowupWithFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Embed[]>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(),
                It.IsAny<Embed>(), It.IsAny<RequestOptions>(), It.IsAny<PollProperties>(), It.IsAny<MessageFlags>()))
                .Returns<Stream, string, string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions, PollProperties, MessageFlags>(
                    (stream, filename, text, embeds, isTTS, ephemeral, mentions, components, embed, options, poll, flags) =>
                    {
                        this.RequireAcknowledgment();
                        this.FollowupVisibilities.Add(this.deferred ? this.originalIsEphemeral : ephemeral);
                        this.deferred = false;
                        this.messageStore.Files.Add((stream, filename, text));
                        return Task.FromResult(Mock.Of<IUserMessage>());
                    });
            this.Interaction.Setup(interaction => interaction.ModifyOriginalResponseAsync(
                It.IsAny<Action<MessageProperties>>(), It.IsAny<RequestOptions>()))
                .Returns<Action<MessageProperties>, RequestOptions>((modify, options) =>
                {
                    this.RequireOriginal();
                    this.ModifyCount++;
                    MessageProperties properties = new MessageProperties();
                    modify(properties);
                    this.OriginalContent = properties.Content.IsSpecified ? properties.Content.Value : this.OriginalContent;
                    this.deferred = false;
                    return Task.FromResult(Mock.Of<IUserMessage>());
                });
            this.Interaction.Setup(interaction => interaction.DeleteOriginalResponseAsync(It.IsAny<RequestOptions>()))
                .Returns<RequestOptions>(options =>
                {
                    this.RequireOriginal();
                    this.originalExists = false;
                    this.OriginalResponseDeleted = true;
                    return Task.CompletedTask;
                });
        }

        public Mock<ISlashCommandInteraction> Interaction { get; }

        public bool HasResponded { get; private set; }

        public int InitialResponseCount { get; private set; }

        public int DeferCount { get; private set; }

        public int ModifyCount { get; private set; }

        public Task InitialResponseCompletion { get; set; } = Task.CompletedTask;

        public bool InitialResponseIsEphemeral => this.originalIsEphemeral;

        public bool OriginalResponseDeleted { get; private set; }

        public string OriginalContent { get; private set; }

        public List<bool> FollowupVisibilities { get; } = new List<bool>();

        private void Acknowledge(bool ephemeral)
        {
            if (this.HasResponded)
            {
                throw new InvalidOperationException("An interaction can only have one initial response.");
            }

            this.HasResponded = true;
            this.originalExists = true;
            this.originalIsEphemeral = ephemeral;
            this.InitialResponseCount++;
        }

        private void RequireAcknowledgment()
        {
            if (!this.HasResponded)
            {
                throw new InvalidOperationException("The interaction must be acknowledged first.");
            }
        }

        private void RequireOriginal()
        {
            this.RequireAcknowledgment();
            if (!this.originalExists)
            {
                throw new InvalidOperationException("The original response does not exist.");
            }
        }

        private void RecordMessage(string text, Embed[] embeds, Embed embed)
        {
            if (text != null)
            {
                this.messageStore.ChannelMessages.Add(text);
            }

            if (embeds != null)
            {
                foreach (Embed item in embeds)
                {
                    this.messageStore.ChannelEmbeds.Add(CommandMocks.GetMockEmbedText(item));
                }
            }
            else if (embed != null)
            {
                this.messageStore.ChannelEmbeds.Add(CommandMocks.GetMockEmbedText(embed));
            }
        }
    }
}
