﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Google.Apis.YouTube.v3.Data;

using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;

using TwitchBotShared.ClientLibraries;
using TwitchBotShared.ClientLibraries.Singletons;
using TwitchBotShared.Config;
using TwitchBotShared.Extensions;

namespace TwitchBotShared.Threads
{
    public class LibVLCSharpPlayer
    {
        private Thread _vlcPlayerThread;
        private TwitchBotConfigurationSection _botConfig;
        private IrcClient _irc;
        private MediaPlayer _mediaPlayer;
        private List<PlaylistItem> _songRequestPlaylistVideoIds;
        private List<PlaylistItem> _personalYoutubePlaylistVideoIds;
        private bool _playerStatus;
        private bool _songSkipping;
        private bool _initialLoadYoutubePlaylist = false;
        private YoutubeClient _youTubeClientInstance = YoutubeClient.Instance;
        private ErrorHandler _errHndlrInstance = ErrorHandler.Instance;

        // Reference (LibVLC YouTube playback): https://forum.videolan.org/viewtopic.php?t=148637#p488319
        // Reference (VLC command line): https://wiki.videolan.org/VLC_command-line_help
        private readonly string[] _commandLineOptions =
        {
            //"--verbose=2",
            "--audio-filter=compressor",
            "--compressor-rms-peak=0.00",
            "--compressor-attack=24.00",
            "--compressor-release=250.00",
            "--compressor-threshold=-25.00",
            "--compressor-ratio=2.00",
            "--compressor-knee=4.50",
            "--compressor-makeup-gain=17.00"
        };

        public LibVLCSharpPlayer() { }

        public LibVLCSharpPlayer(TwitchBotConfigurationSection botConfig, IrcClient irc)
        {
            _botConfig = botConfig;
            _irc = irc;
        }

        public PlaylistItem CurrentSongRequestPlaylistItem { get; private set; }

        public PlaylistItem LastPlayedPlaylistItem { get; private set; }

        public LibVLC LibVlc { get; private set; }

        public async Task StartAsync()
        {
            try
            {
                if (LibVlc == null)
                {
                    _initialLoadYoutubePlaylist = true;
                    _vlcPlayerThread = new Thread(new ThreadStart(this.Run));

                    Core.Initialize();
                    LibVlc = new LibVLC(_commandLineOptions);
                    _mediaPlayer = new MediaPlayer(LibVlc)
                    {
                        AspectRatio = "16:9"
                    };

                    await SetAudioOutputDeviceAsync(_botConfig.LibVLCAudioOutputDevice);
                    _playerStatus = true;

                    _vlcPlayerThread.IsBackground = true;
                    _vlcPlayerThread.Start();
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Start()", false);
            }
        }

        public async void Stop()
        {
            try
            {
                if (LibVlc != null)
                {
                    _playerStatus = false;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Stop()", false);
            }
        }

        private async void Run()
        {
            try
            {
                _songRequestPlaylistVideoIds = await _youTubeClientInstance.GetPlaylistItemsAsync(_botConfig.YouTubeBroadcasterPlaylistId);

                if (!string.IsNullOrEmpty(_botConfig.YouTubePersonalPlaylistId))
                {
                    if (_botConfig.EnablePersonalPlaylistShuffle)
                    {
                        List<PlaylistItem> shuffledList = await _youTubeClientInstance.GetPlaylistItemsAsync(_botConfig.YouTubePersonalPlaylistId);
                        shuffledList.Shuffle();

                        _personalYoutubePlaylistVideoIds = shuffledList;
                    }
                    else
                    {
                        _personalYoutubePlaylistVideoIds = await _youTubeClientInstance.GetPlaylistItemsAsync(_botConfig.YouTubePersonalPlaylistId);
                    }
                }

                _initialLoadYoutubePlaylist = false;

                SetNextVideoId(); // set current song request playlist item

                if (CurrentSongRequestPlaylistItem == null)
                {
                    _playerStatus = false;
                    return; // don't try to start the VLC video player until there is something to play
                }

                while (true)
                {
                    if (CurrentSongRequestPlaylistItem != null)
                    {
                        await PlayMediaAsync();

                        if (_mediaPlayer?.Media?.State != VLCState.Ended)
                        {
                            _songSkipping = false;
                        }
                    }

                    while (_mediaPlayer?.Media?.State != VLCState.Ended && _playerStatus && !_songSkipping)
                    {
                        // wait
                    }

                    LastPlayedPlaylistItem = CurrentSongRequestPlaylistItem;

                    if (_playerStatus)
                        SetNextVideoId();
                    else
                        break;
                }

                // Clean up
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
                LibVlc.Dispose();
                LibVlc = null;
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Run()", false);
            }
        }

        private async void SetNextVideoId()
        {
            try
            {
                if (_songRequestPlaylistVideoIds.Count > 0)
                {
                    CurrentSongRequestPlaylistItem = _songRequestPlaylistVideoIds.First();
                    _songRequestPlaylistVideoIds.RemoveAt(0);
                }
                else if (_personalYoutubePlaylistVideoIds?.Count > 0)
                {
                    CurrentSongRequestPlaylistItem = _personalYoutubePlaylistVideoIds.First();
                    _personalYoutubePlaylistVideoIds.RemoveAt(0);
                }
                else
                {
                    CurrentSongRequestPlaylistItem = null;
                }

                // Write to a text file to allow users to show the currently playing song as a song ticker
                // ToDo: Add config variables
                string filename = "Twitch Chat Bot Song Request.txt";
                string startingText = "Currently Playing: ";
                string separator = " - ";
                string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                bool showChannelTitle = false;

                using (StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, filename)))
                {
                    string songTitle = "";

                    if (CurrentSongRequestPlaylistItem != null)
                    {
                        songTitle = $"{startingText}\"{CurrentSongRequestPlaylistItem.Snippet.Title}\"";

                        if (showChannelTitle)
                        {
                            songTitle += $" by {CurrentSongRequestPlaylistItem.Snippet.ChannelTitle}";
                        }

                        songTitle += separator;
                    }

                    await outputFile.WriteAsync(songTitle);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "SetNextVideoId()", false);
            }
        }

        private async Task PlayMediaAsync(int recursiveCount = 0)
        {
            try
            {
                if (CurrentSongRequestPlaylistItem?.ContentDetails?.VideoId != null)
                {
                    if (_mediaPlayer.State == VLCState.Stopped || _mediaPlayer.State == VLCState.Paused)
                    {
                        _mediaPlayer.Play();
                    }
                    else
                    {
                        Media media = new Media(LibVlc, "https://youtu.be/" + CurrentSongRequestPlaylistItem.ContentDetails.VideoId, FromType.FromLocation);
                        await media.Parse(MediaParseOptions.ParseNetwork);
                        _mediaPlayer.Media = media.SubItems.First();
                        _mediaPlayer.Play();

                        await Task.Delay(2000);

                        if (_mediaPlayer.State == VLCState.Ended)
                        {
                            Console.WriteLine($"\nError: The song \"{CurrentSongRequestPlaylistItem.Snippet.Title}\" was unable to load at this time\n");

                            if (recursiveCount < 2)
                            {
                                await PlayMediaAsync(++recursiveCount);
                            }
                            else
                            {
                                _irc.SendPublicChatMessage($"I'm sorry @{CurrentSongRequestPlaylistItem.ContentDetails.Note} "
                                    + $"I wasn't able to load your song \"{CurrentSongRequestPlaylistItem.Snippet.Title}\" even after 3 attempts. "
                                    + "https://youtu.be/" + CurrentSongRequestPlaylistItem.ContentDetails.VideoId);
                            }
                        }

                        if (media != null)
                        {
                            media.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "PlayMedia(int)", false);
            }
        }

        public async Task PlayAsync()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    await PlayMediaAsync();
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Play()", false);
            }
        }

        public async void Pause()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Pause();
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Pause()", false);
            }
        }

        public async Task SkipAsync(int songSkipCount = 0)
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    // skip songs if skip count was specified
                    SkipSongRequestPlaylistVideoIds(ref songSkipCount);
                    SkipPersonalPlaylistVideoIds(ref songSkipCount);

                    _songSkipping = true;

                    do
                    {
                        await Task.Delay(500); // wait
                    } while (_songSkipping);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "Skip(int)", false);
            }
        }

        public async Task<bool> SetVolumeAsync(int volumePercentage)
        {
            try
            {
                if (_mediaPlayer != null && volumePercentage > 0 && volumePercentage <= 100)
                {
                    _mediaPlayer.Volume = volumePercentage;
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "SetVolume(int)", false);
            }

            return false;
        }

        public async Task<int> GetVolumeAsync()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    return _mediaPlayer.Volume;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "GetVolume()", false);
            }

            return 0;
        }

        public void ResetSongRequestQueue()
        {
            _songRequestPlaylistVideoIds = new List<PlaylistItem>();
        }

        public async Task<string> SetAudioOutputDeviceAsync(string audioOutputDeviceName)
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    AudioOutputDevice defaultAudioOutputDevice = _mediaPlayer.AudioOutputDeviceEnum.FirstOrDefault(a => a.Description == "Default");

                    if (audioOutputDeviceName.ToLower() == "default")
                    {
                        _mediaPlayer.SetOutputDevice(defaultAudioOutputDevice.DeviceIdentifier);
                        return $"Successfully defaulted the song request media player's audio output device";
                    }

                    AudioOutputDevice audioOutputDevice = _mediaPlayer.AudioOutputDeviceEnum.FirstOrDefault(a => a.Description == audioOutputDeviceName);

                    if (audioOutputDevice.Description != audioOutputDeviceName)
                    {
                        _mediaPlayer.SetOutputDevice(defaultAudioOutputDevice.DeviceIdentifier);
                        return "Cannot set the requested audio output device for the song request media player. "
                            + $"Setting it to \"{defaultAudioOutputDevice.Description}\"";
                    }
                    else
                    {
                        _mediaPlayer.SetOutputDevice(audioOutputDevice.DeviceIdentifier);
                        return $"Successfully set the song request media player's audio output device to \"{audioOutputDeviceName}\"";
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "SetAudioOutputDevice(string)", false);
            }

            return "I cannot find the song request media player";
        }

        public async Task<int> AddSongRequestAsync(PlaylistItem playlistItem)
        {
            try
            {
                await WaitForInitialPlaylistLoadAsync();

                if (_songRequestPlaylistVideoIds != null)
                {
                    _songRequestPlaylistVideoIds = _songRequestPlaylistVideoIds.Append(playlistItem).ToList();
                    return _songRequestPlaylistVideoIds.Count;
                }
                else
                {
                    _songRequestPlaylistVideoIds = new List<PlaylistItem> { playlistItem };
                    return 1;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "AddSongRequest(PlaylistItem)", false);
            }

            return 0;
        }

        public VLCState MediaPlayerStatus()
        {
            return _mediaPlayer != null ? _mediaPlayer.State : VLCState.Error;
        }

        public async Task<bool> SetVideoTimeAsync(int timeInSec)
        {
            try
            {
                if (_mediaPlayer != null && _mediaPlayer.Time > -1
                    && timeInSec > -1 && timeInSec * 1000 < _mediaPlayer.Length)
                {
                    _mediaPlayer.Time = timeInSec * 1000;
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "SetVideoTime(int)", false);
            }

            return false;
        }

        public async Task<string> GetVideoTimeAsync()
        {
            try
            {
                if (_mediaPlayer != null && _mediaPlayer.Time > -1)
                {
                    TimeSpan currentTimeSpan = new TimeSpan(0, 0, 0, 0, (int)_mediaPlayer.Time);
                    TimeSpan durationTimeSpan = new TimeSpan(0, 0, 0, 0, (int)_mediaPlayer.Length);

                    return $"{_mediaPlayer.Media.State.ToString().ToLower()}" +
                        $" at {currentTimeSpan.ReformatTimeSpan()} of {durationTimeSpan.ReformatTimeSpan()}";
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "GetVideoTime()", false);
            }

            return "A YouTube video hasn't been loaded yet";
        }

        public async Task SetPersonalPlaylistShuffleAsync(bool shuffle)
        {
            try
            {
                if (CurrentSongRequestPlaylistItem != null && !string.IsNullOrEmpty(_botConfig.YouTubePersonalPlaylistId))
                {
                    List<PlaylistItem> personalPlaylist = null;

                    if (!shuffle)
                    {
                        personalPlaylist = await _youTubeClientInstance.GetPlaylistItemsAsync(_botConfig.YouTubePersonalPlaylistId);
                        int lastPlayedItemIndex = personalPlaylist.FindIndex(p => p.Id == CurrentSongRequestPlaylistItem.Id);

                        if (lastPlayedItemIndex > -1)
                            personalPlaylist.RemoveRange(0, lastPlayedItemIndex + 1);
                    }
                    else
                    {
                        personalPlaylist = _personalYoutubePlaylistVideoIds.ToList();
                        personalPlaylist.Shuffle();
                    }

                    _personalYoutubePlaylistVideoIds = new List<PlaylistItem>(personalPlaylist);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "SetPersonalPlaylistShuffle(bool)", false);
            }
        }

        public async Task<PlaylistItem> RemoveWrongSongAsync(string username)
        {
            try
            {
                PlaylistItem removedWrongSong = _songRequestPlaylistVideoIds.LastOrDefault(p => p.ContentDetails.Note.Contains(username));

                if (removedWrongSong != null && _songRequestPlaylistVideoIds.Remove(removedWrongSong))
                {
                    return removedWrongSong;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "RemoveWrongSong(string)", false);
            }

            return null;
        }

        public async Task<bool> HasUserRequestedTooManyAsync(string username, int songRequestLimit)
        {
            try
            {
                if (_songRequestPlaylistVideoIds == null && !_initialLoadYoutubePlaylist)
                {
                    _songRequestPlaylistVideoIds = await _youTubeClientInstance.GetPlaylistItemsAsync(_botConfig.YouTubeBroadcasterPlaylistId);
                }

                await WaitForInitialPlaylistLoadAsync();

                if (_songRequestPlaylistVideoIds?.FindAll(p => !string.IsNullOrEmpty(p.ContentDetails.Note) && p.ContentDetails.Note.Contains(username))?.Count >= songRequestLimit)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogErrorAsync(ex, "LibVLCSharpPlayer", "HasUserRequestedTooMany(string, int)", false);
            }

            return false;
        }

        private void SkipSongRequestPlaylistVideoIds(ref int songSkipCount)
        {
            if (_songRequestPlaylistVideoIds.Count > 0 && songSkipCount > 0)
            {
                if (_songRequestPlaylistVideoIds.Count < songSkipCount)
                {
                    songSkipCount -= _songRequestPlaylistVideoIds.Count + 1; // use "+1" to include currently playing song
                    _songRequestPlaylistVideoIds.Clear();
                }
                else
                {
                    _songRequestPlaylistVideoIds.RemoveRange(0, songSkipCount - 1); // use "-1" to include currently playing song
                    songSkipCount = 0;
                }
            }
        }

        private void SkipPersonalPlaylistVideoIds(ref int songSkipCount)
        {
            if (_personalYoutubePlaylistVideoIds.Count > 0 && songSkipCount > 0)
            {
                if (_personalYoutubePlaylistVideoIds.Count < songSkipCount)
                {
                    _personalYoutubePlaylistVideoIds.Clear();
                }
                else
                {
                    _personalYoutubePlaylistVideoIds.RemoveRange(0, songSkipCount - 1); // use "-1" to include currently playing song
                }
            }
        }

        private async Task WaitForInitialPlaylistLoadAsync()
        {
            if (_initialLoadYoutubePlaylist)
            {
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(1000); // wait

                    if (!_initialLoadYoutubePlaylist)
                    {
                        break;
                    }
                }
            }
        }
    }
}
