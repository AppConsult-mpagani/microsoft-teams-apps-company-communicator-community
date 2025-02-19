// <copyright file="UserTeamsActivityHandler.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Bot
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Bot.Builder;
    using Microsoft.Bot.Builder.Teams;
    using Microsoft.Bot.Schema;
    using Microsoft.Bot.Schema.Teams;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.SentNotificationData;

    /// <summary>
    /// Company Communicator User Bot.
    /// Captures user data, team data.
    /// </summary>
    public class UserTeamsActivityHandler : TeamsActivityHandler
    {
        private static readonly string TeamRenamedEventType = "teamRenamed";

        private readonly ISentNotificationDataRepository sentNotificationDataRepository;

        private readonly TeamsDataCapture teamsDataCapture;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserTeamsActivityHandler"/> class.
        /// </summary>
        /// <param name="teamsDataCapture">Teams data capture service.</param>
        public UserTeamsActivityHandler(TeamsDataCapture teamsDataCapture, ISentNotificationDataRepository sentNotificationDataRepository)
        {
            this.teamsDataCapture = teamsDataCapture ?? throw new ArgumentNullException(nameof(teamsDataCapture));
            this.sentNotificationDataRepository = sentNotificationDataRepository ?? throw new ArgumentNullException(nameof(sentNotificationDataRepository));

        }

        /// <summary>
        /// Invoked when a conversation update activity is received from the channel.
        /// </summary>
        /// <param name="turnContext">The context object for this turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        protected override async Task OnConversationUpdateActivityAsync(
            ITurnContext<IConversationUpdateActivity> turnContext,
            CancellationToken cancellationToken)
        {
            // base.OnConversationUpdateActivityAsync is useful when it comes to responding to users being added to or removed from the conversation.
            // For example, a bot could respond to a user being added by greeting the user.
            // By default, base.OnConversationUpdateActivityAsync will call <see cref="OnMembersAddedAsync(IList{ChannelAccount}, ITurnContext{IConversationUpdateActivity}, CancellationToken)"/>
            // if any users have been added or <see cref="OnMembersRemovedAsync(IList{ChannelAccount}, ITurnContext{IConversationUpdateActivity}, CancellationToken)"/>
            // if any users have been removed. base.OnConversationUpdateActivityAsync checks the member ID so that it only responds to updates regarding members other than the bot itself.
            await base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);

            var activity = turnContext.Activity;

            var isTeamRenamed = this.IsTeamInformationUpdated(activity);
            if (isTeamRenamed)
            {
                await this.teamsDataCapture.OnTeamInformationUpdatedAsync(activity);
            }

            if (activity.MembersAdded != null)
            {
                await this.teamsDataCapture.OnBotAddedAsync(turnContext, activity, cancellationToken);
            }

            if (activity.MembersRemoved != null)
            {
                await this.teamsDataCapture.OnBotRemovedAsync(activity);
            }
        }

        /// <inheritdoc/>
        protected override async Task OnReactionsAddedAsync(IList<MessageReaction> messageReactions, ITurnContext<IMessageReactionActivity> turnContext, CancellationToken cancellationToken)
        {
            // we can have multiple reactions for a specific message, specially when posted to channels
            // most of the cases this makes sense with 1:1 messages
            // TODO: review the code for the case when posting messages to teams general channel
            foreach (var reaction in messageReactions)
            {
                // get the replytoid
                var originalActivityId = turnContext.Activity.ReplyToId;

                // performs a search to get the activityid that was saved by the send function
                string filterQuery = TableQuery.GenerateFilterCondition("ActivityId", QueryComparisons.Equal, originalActivityId);
                var result = await this.sentNotificationDataRepository.GetWithFilterWithoutPartitionAsync(filterQuery);
                SentNotificationDataEntity sentNotificationDataEntity = result?.FirstOrDefault();
                string notificationId = sentNotificationDataEntity?.PartitionKey;

                // get the channelData to check if the bot is posting in a channel or sending messages to users
                TeamsChannelData channelData = turnContext.Activity.GetChannelData<TeamsChannelData>();

                // this version is not tracking reactions for posts in the general channel
                // to track that code to calculate the number of specific reactions need to be created also on the OnReactionsRemovedAsync event
                if (channelData.Channel == null)
                {
                    // the bot is sending a message to users
                    // save the reaction added to the database
                    if (sentNotificationDataEntity != null)
                    {
                        sentNotificationDataEntity.Reactions = reaction.Type;
                        await this.sentNotificationDataRepository.InsertOrMergeAsync(sentNotificationDataEntity);
                    }

                    // the code below can be used if you want to send a reply message back to the channel 
                    // string newReaction = $"You reacted with '{reaction.Type}' to the following message: '{notificationId}' in the conversation ID: '{turnContext.Activity.Conversation.Id}'. This is a 1:1 message!'";
                    // Activity replyActivity = MessageFactory.Text(newReaction);
                    // await turnContext.SendActivityAsync(replyActivity, cancellationToken);
                }
            }
        }

        private bool IsTeamInformationUpdated(IConversationUpdateActivity activity)
        {
            if (activity == null)
            {
                return false;
            }

            var channelData = activity.GetChannelData<TeamsChannelData>();
            if (channelData == null)
            {
                return false;
            }

            return UserTeamsActivityHandler.TeamRenamedEventType.Equals(channelData.EventType, StringComparison.OrdinalIgnoreCase);
        }


    }
}