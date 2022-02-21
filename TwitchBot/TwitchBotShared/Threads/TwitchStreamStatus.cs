﻿using System;
using System.Threading;
using System.Threading.Tasks;

using TwitchBotShared.ClientLibraries.Singletons;
using TwitchBotShared.ClientLibraries;
using TwitchBotShared.Models;
using TwitchBotShared.Models.JSON;

namespace TwitchBotShared.Threads
{
    public class TwitchStreamStatus
    {
        private readonly IrcClient _irc;
        private readonly Thread _checkStreamStatus;
        private readonly TwitchInfoService _twitchInfo;
        private readonly string _broadcasterName;
        private readonly DelayedMessageSingleton _delayedMessagesInstance = DelayedMessageSingleton.Instance;

        public static bool IsLive { get; private set; } = false;
        public static string CurrentCategory { get; private set; }
        public static string CurrentTitle { get; private set; }

        public TwitchStreamStatus(IrcClient irc, TwitchInfoService twitchInfo, string broadcasterName)
        {
            _irc = irc;
            _twitchInfo = twitchInfo;
            _broadcasterName = broadcasterName;
            _checkStreamStatus = new Thread(new ThreadStart(this.Run));
        }

        #region Public Methods
        public void Start()
        {
            _checkStreamStatus.IsBackground = true;
            _checkStreamStatus.Start();
        }

        public async Task LoadChannelInfoAsync()
        {
            ChannelJSON channelJSON = await _twitchInfo.GetBroadcasterChannelByIdAsync();

            if (channelJSON != null)
            {
                CurrentCategory = channelJSON.GameName;
                CurrentTitle = channelJSON.Title;
            }
        }
        #endregion

        private async void Run()
        {
            while (true)
            {
                StreamJSON streamJSON = await _twitchInfo.GetBroadcasterStreamAsync();

                if (streamJSON == null)
                {
                    if (IsLive)
                    {
                        // ToDo: Clear greeted user list
                    }

                    IsLive = false;
                }
                else
                {
                    CurrentCategory = streamJSON.GameName;
                    CurrentTitle = streamJSON.Title;

                    // Tell the chat the stream is now live
                    if (!IsLive)
                    {
                        // ToDo: Add setting if user wants preset reminder
                        _delayedMessagesInstance.DelayedMessages.Add(new DelayedMessage
                        {
                            Message = $"Did you remind Twitter you're \"!live\"? @{_broadcasterName}",
                            SendDate = DateTime.Now.AddMinutes(5)
                        });

                        _irc.SendPublicChatMessage($"Live on Twitch playing {CurrentCategory} \"{CurrentTitle}\"");
                    }

                    IsLive = true;
                }

                Thread.Sleep(15000); // check every 15 seconds
            }
        }
    }
}
