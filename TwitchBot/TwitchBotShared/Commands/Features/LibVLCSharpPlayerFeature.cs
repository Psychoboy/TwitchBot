﻿using System;
using System.Configuration;
using System.Threading.Tasks;

using Google.Apis.YouTube.v3.Data;

using LibVLCSharp.Shared;

using TwitchBotShared.ClientLibraries;
using TwitchBotShared.ClientLibraries.Singletons;
using TwitchBotShared.Config;
using TwitchBotShared.Enums;
using TwitchBotShared.Models;
using TwitchBotShared.Threads;

namespace TwitchBotShared.Commands.Features
{
    /// <summary>
    /// The "Command Subsystem" for the "LibVLCSharp Player" feature
    /// </summary>
    public sealed class LibVLCSharpPlayerFeature : BaseFeature
    {
        private readonly LibVLCSharpPlayer _libVLCSharpPlayer;
        private readonly Configuration _appConfig;
        private readonly YoutubeClient _youTubeClientInstance = YoutubeClient.Instance;
        private readonly ErrorHandler _errHndlrInstance = ErrorHandler.Instance;

        public LibVLCSharpPlayerFeature(IrcClient irc, TwitchBotConfigurationSection botConfig, Configuration appConfig,
            LibVLCSharpPlayer libVLCSharpPlayer) : base(irc, botConfig)
        {
            _libVLCSharpPlayer = libVLCSharpPlayer;
            _appConfig = appConfig;
            _rolePermissions.Add("!srstart", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!srstop", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!srpause", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!sraod", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!srshuffle", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!srplay", new CommandPermission { General = ChatterType.Broadcaster });
            _rolePermissions.Add("!srvolume", new CommandPermission { General = ChatterType.Viewer, Elevated = ChatterType.Moderator });
            _rolePermissions.Add("!srskip", new CommandPermission { General = ChatterType.Moderator });
            _rolePermissions.Add("!srtime", new CommandPermission { General = ChatterType.Viewer, Elevated = ChatterType.Moderator });
        }

        public override async Task<(bool, DateTime)> ExecCommandAsync(TwitchChatter chatter, string requestedCommand)
        {
            try
            {
                switch (requestedCommand)
                {
                    case "!srstart":
                        return (true, await StartAsync());
                    case "!srstop":
                        return (true, await StopAsync());
                    case "!srpause":
                        return (true, await PauseAsync());
                    case "!sraod":
                        return (true, await SetAudioOutputDeviceAsync(chatter));
                    case "!srshuffle":
                        return (true, await PersonalPlaylistShuffleAsync(chatter));
                    case "!srplay":
                        return (true, await PlayAsync());
                    case "!srvolume":
                        if ((chatter.Message.StartsWith("!srvolume ")) && HasPermission("!srvolume", DetermineChatterPermissions(chatter), _rolePermissions, true))
                        {
                            return (true, await SetVolumeAsync(chatter));
                        }
                        else if (chatter.Message == "!srvolume")
                        {
                            return (true, await ShowVolumeAsync(chatter));
                        }
                        break;
                    case "!srskip":
                        return (true, await Skip(chatter));
                    case "!srtime":
                        if ((chatter.Message.StartsWith("!srtime ")) && HasPermission("!srtime", DetermineChatterPermissions(chatter), _rolePermissions, true))
                        {
                            return (true, await SetTimeAsync(chatter));
                        }
                        else if (chatter.Message == "!srtime")
                        {
                            return (true, await ShowTimeAsync(chatter));
                        }
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "ExecCommand(TwitchChatter, string)", false, requestedCommand, chatter.Message);
            }

            return (false, DateTime.Now);
        }

        private async Task<DateTime> StartAsync()
        {
            try
            {
                await _libVLCSharpPlayer.StartAsync();
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "Start()", false, "!srstart");
            }

            return DateTime.Now;
        }

        private async Task<DateTime> StopAsync()
        {
            try
            {
                _libVLCSharpPlayer.Stop();
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "Stop()", false, "!srstop");
            }

            return DateTime.Now;
        }

        private async Task<DateTime> PauseAsync()
        {
            try
            {
                if (_libVLCSharpPlayer.MediaPlayerStatus() == VLCState.Paused && _libVLCSharpPlayer.MediaPlayerStatus() == VLCState.Stopped)
                {
                    _irc.SendPublicChatMessage($"This media player is already {await _libVLCSharpPlayer.GetVideoTimeAsync()} @{_botConfig.Broadcaster}");
                    return DateTime.Now;
                }

                _libVLCSharpPlayer.Pause();
                _irc.SendPublicChatMessage($"Paused current song request @{_botConfig.Broadcaster}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "Pause()", false, "!srpause");
            }

            return DateTime.Now;
        }

        private async Task<DateTime> SetAudioOutputDeviceAsync(TwitchChatter chatter)
        {
            try
            {
                string audioOutputDevice = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);

                _irc.SendPublicChatMessage($"{await _libVLCSharpPlayer.SetAudioOutputDeviceAsync(audioOutputDevice)} @{_botConfig.Broadcaster}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "SetAudioOutputDevice(string)", false, "!sraod");
            }

            return DateTime.Now;
        }

        private async Task<DateTime> PersonalPlaylistShuffleAsync(TwitchChatter chatter)
        {
            try
            {
                string message = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);
                bool shuffle = SetBooleanFromMessage(message);

                if (_botConfig.EnablePersonalPlaylistShuffle == shuffle)
                {
                    if (shuffle)
                        _irc.SendPublicChatMessage($"Your personal playlist has already been shuffled @{_botConfig.Broadcaster}");
                    else
                        _irc.SendPublicChatMessage($"Your personal playlist is already in order @{_botConfig.Broadcaster}");
                }
                else
                {
                    await _libVLCSharpPlayer.SetPersonalPlaylistShuffleAsync(shuffle);

                    _botConfig.EnablePersonalPlaylistShuffle = shuffle;
                    _appConfig.AppSettings.Settings.Remove("enablePersonalPlaylistShuffle");

                    if (shuffle)
                    {
                        _appConfig.AppSettings.Settings.Add("enablePersonalPlaylistShuffle", "true");

                        _irc.SendPublicChatMessage("Your personal playlist queue has been shuffled. "
                            + $"Don't worry, I didn't touch your actual YouTube playlist @{_botConfig.Broadcaster} ;)");
                    }
                    else
                    {
                        _appConfig.AppSettings.Settings.Add("enablePersonalPlaylistShuffle", "false");

                        _irc.SendPublicChatMessage("Your personal playlist queue has been reset to its proper order continuing on from this video. "
                            + $"Don't worry, I didn't touch your actual YouTube playlist @{_botConfig.Broadcaster} ;)");
                    }

                    _appConfig.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("TwitchBotConfiguration");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "PersonalPlaylistShuffle(bool)", false, "!srshuffle");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Play the video
        /// </summary>
        /// <returns></returns>
        private async Task<DateTime> PlayAsync()
        {
            try
            {
                if (_libVLCSharpPlayer.MediaPlayerStatus() == VLCState.Playing)
                {
                    _irc.SendPublicChatMessage($"This media player is already {await _libVLCSharpPlayer.GetVideoTimeAsync()} @{_botConfig.Broadcaster}");
                    return DateTime.Now;
                }

                await _libVLCSharpPlayer.PlayAsync();

                PlaylistItem playlistItem = _libVLCSharpPlayer.CurrentSongRequestPlaylistItem;

                string songRequest = _youTubeClientInstance.ShowPlayingSongRequest(playlistItem);

                if (!string.IsNullOrEmpty(songRequest))
                    _irc.SendPublicChatMessage($"@{_botConfig.Broadcaster} <-- Now playing: {songRequest} Currently {await _libVLCSharpPlayer.GetVideoTimeAsync()}");
                else
                    _irc.SendPublicChatMessage($"Unable to display the current song @{_botConfig.Broadcaster}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "Play()", false, "!srplay");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Set the volume for the player
        /// </summary>
        /// <param name="chatter"></param>
        /// <returns></returns>
        private async Task<DateTime> SetVolumeAsync(TwitchChatter chatter)
        {
            try
            {
                bool validMessage = int.TryParse(chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1), out int volumePercentage);

                if (validMessage)
                {
                    if (await _libVLCSharpPlayer.SetVolumeAsync(volumePercentage))
                        _irc.SendPublicChatMessage($"Song request volume set to {volumePercentage}% @{chatter.DisplayName}");
                    else if (volumePercentage < 1 || volumePercentage > 100)
                        _irc.SendPublicChatMessage($"Please use a value for the song request volume from 1-100 @{chatter.DisplayName}");
                }
                else
                {
                    _irc.SendPublicChatMessage($"Requested volume not valid. Please set the song request volume from 1-100 @{chatter.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "SetVolume(TwitchChatter)", false, "!srvolume", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Skip to the next video in the queue
        /// </summary>
        /// <param name="chatter"></param>
        /// <returns></returns>
        private async Task<DateTime> Skip(TwitchChatter chatter)
        {
            try
            {
                bool validMessage = int.TryParse(chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1), out int songSkipCount);

                if (!validMessage)
                    await _libVLCSharpPlayer.SkipAsync();
                else
                    await _libVLCSharpPlayer.SkipAsync(songSkipCount);

                string songRequest = _youTubeClientInstance.ShowPlayingSongRequest(_libVLCSharpPlayer.CurrentSongRequestPlaylistItem);

                if (!string.IsNullOrEmpty(songRequest))
                    _irc.SendPublicChatMessage($"@{chatter.DisplayName} <-- Now playing: {songRequest}");
                else
                    _irc.SendPublicChatMessage($"Unable to display the current song @{chatter.DisplayName}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "Skip(TwitchChatter)", false, "!srskip", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Seek to a specific time in the video
        /// </summary>
        /// <param name="chatter"></param>
        /// <returns></returns>
        private async Task<DateTime> SetTimeAsync(TwitchChatter chatter)
        {
            try
            {
                bool validMessage = int.TryParse(chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1), out int seekVideoTime);

                if (validMessage && await _libVLCSharpPlayer.SetVideoTimeAsync(seekVideoTime))
                    _irc.SendPublicChatMessage($"Video seek time set to {seekVideoTime} second(s) @{chatter.DisplayName}");
                else
                    _irc.SendPublicChatMessage($"Time not valid. Please set the time (in seconds) between 0 and the length of the video @{chatter.DisplayName}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "SetTime(TwitchChatter)", false, "!srtime", chatter.Message);
            }

            return DateTime.Now;
        }

        private async Task<DateTime> ShowVolumeAsync(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"Song request volume is currently at {await _libVLCSharpPlayer.GetVolumeAsync()}% @{chatter.DisplayName}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "ShowVolume(TwitchChatter)", false, "!srvolume");
            }

            return DateTime.Now;
        }

        private async Task<DateTime> ShowTimeAsync(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"Currently {await _libVLCSharpPlayer.GetVideoTimeAsync()} @{chatter.DisplayName}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayerFeature", "ShowTime(TwitchChatter)", false, "!srtime");
            }

            return DateTime.Now;
        }
    }
}
