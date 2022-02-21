﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TwitchBotDb.Services;

using TwitchBotShared.ClientLibraries;
using TwitchBotShared.ClientLibraries.Singletons;
using TwitchBotShared.Config;

namespace TwitchBotShared.Threads
{
    public class BankHeist
    {
        private IrcClient _irc;
        private int _broadcasterId;
        private string _resultMessage;
        private readonly Thread _thread;
        private readonly BankService _bank;
        private readonly TwitchBotConfigurationSection _botConfig;
        private readonly BankHeistSingleton _heistSettings = BankHeistSingleton.Instance;

        public BankHeist() { }

        public BankHeist(BankService bank, TwitchBotConfigurationSection botConfig)
        {
            _thread = new Thread(new ThreadStart(this.Run));
            _bank = bank;
            _botConfig = botConfig;
        }

        #region Public Methods
        public void Start(IrcClient irc, int broadcasterId)
        {
            _irc = irc;
            _broadcasterId = broadcasterId;
            _heistSettings.CooldownTimePeriod = DateTime.Now;
            _heistSettings.Robbers = new BlockingCollection<BankRobber>();
            _resultMessage = _heistSettings.ResultsMessage;

            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Produce(BankRobber robber)
        {
            _heistSettings.Robbers.Add(robber);
        }

        public bool HasRobberAlreadyEntered(string username)
        {
            return _heistSettings.Robbers.Any(u => u.Username == username);
        }

        public bool IsEntryPeriodOver()
        {
            return _heistSettings.Robbers.IsAddingCompleted;
        }

        public int HeistLevel()
        {
            if (_heistSettings.Robbers.Count <= _heistSettings.Levels[0].MaxUsers)
                return 1;
            else if (_heistSettings.Robbers.Count <= _heistSettings.Levels[1].MaxUsers)
                return 2;
            else if (_heistSettings.Robbers.Count <= _heistSettings.Levels[2].MaxUsers)
                return 3;
            else if (_heistSettings.Robbers.Count <= _heistSettings.Levels[3].MaxUsers)
                return 4;
            else
                return 5;
        }

        public string NextLevelMessage()
        {
            if (_heistSettings.Robbers.Count == _heistSettings.Levels[0].MaxUsers + 1)
                return _heistSettings.NextLevelMessages[0]
                    .Replace("@bankname@", _heistSettings.Levels[1].LevelBankName)
                    .Replace("@nextbankname@", _heistSettings.Levels[2].LevelBankName);
            else if (_heistSettings.Robbers.Count == _heistSettings.Levels[1].MaxUsers + 1)
                return _heistSettings.NextLevelMessages[1]
                    .Replace("@bankname@", _heistSettings.Levels[2].LevelBankName)
                    .Replace("@nextbankname@", _heistSettings.Levels[3].LevelBankName);
            else if (_heistSettings.Robbers.Count == _heistSettings.Levels[2].MaxUsers + 1)
                return _heistSettings.NextLevelMessages[2]
                    .Replace("@bankname@", _heistSettings.Levels[3].LevelBankName)
                    .Replace("@nextbankname@", _heistSettings.Levels[4].LevelBankName);
            else if (_heistSettings.Robbers.Count == _heistSettings.Levels[3].MaxUsers + 1)
                return _heistSettings.NextLevelMessages[3]
                    .Replace("@bankname@", _heistSettings.Levels[4].LevelBankName)
                    .Replace("@nextbankname@", _heistSettings.Levels[5].LevelBankName);

            return "";
        }
        #endregion

        #region Private Methods
        private async void Run()
        {
            while (true)
            {
                if (_heistSettings.IsHeistOnCooldown())
                {
                    double cooldownTime = (_heistSettings.CooldownTimePeriod.Subtract(DateTime.Now)).TotalMilliseconds;
                    Thread.Sleep((int)cooldownTime);
                    _irc.SendPublicChatMessage(_heistSettings.CooldownOver);
                }
                else if (_heistSettings.Robbers.Count > 0 && _heistSettings.IsEntryPeriodOver())
                {
                    _heistSettings.Robbers.CompleteAdding();
                    await ConsumeAsync();

                    // refresh the list and reset the cooldown time period
                    _heistSettings.Robbers = new BlockingCollection<BankRobber>();
                    _heistSettings.CooldownTimePeriod = DateTime.Now.AddMinutes(_heistSettings.CooldownTimePeriodMinutes);
                    _resultMessage = _heistSettings.ResultsMessage;
                }

                Thread.Sleep(200);
            }
        }

        private async Task ConsumeAsync()
        {
            BankHeistLevel heistLevel = _heistSettings.Levels[HeistLevel() - 1];
            BankHeistPayout payout = _heistSettings.Payouts[HeistLevel() - 1];

            _irc.SendPublicChatMessage(_heistSettings.GameStart
                .Replace("@bankname@", heistLevel.LevelBankName));

            Thread.Sleep(5000); // wait in anticipation

            Random rnd = new Random();
            int chance = rnd.Next(1, 101); // 1 - 100

            if (chance >= payout.SuccessRate) // failed
            {
                if (_heistSettings.Robbers.Count == 1)
                {
                    _irc.SendPublicChatMessage(_heistSettings.SingleUserFail
                        .Replace("user@", _heistSettings.Robbers.First().Username)
                        .Replace("@bankname@", heistLevel.LevelBankName));
                }
                else
                {
                    _irc.SendPublicChatMessage(_heistSettings.Success0);
                }

                return;
            }

            int numWinners = (int)Math.Ceiling(_heistSettings.Robbers.Count * (payout.SuccessRate / 100));
            IEnumerable<BankRobber> winners = _heistSettings.Robbers.OrderBy(x => rnd.Next()).Take(numWinners);

            foreach (BankRobber winner in winners)
            {
                int funds = await _bank.CheckBalanceAsync(winner.Username.ToLower(), _broadcasterId);
                decimal earnings = Math.Ceiling(winner.Gamble * payout.WinMultiplier);

                await _bank.UpdateFundsAsync(winner.Username.ToLower(), _broadcasterId, (int)earnings + funds);

                _resultMessage += $" {winner.Username} ({(int)earnings} {_botConfig.CurrencyType}),";
            }

            // remove extra ","
            _resultMessage = _resultMessage.Remove(_resultMessage.LastIndexOf(','), 1);

            decimal numWinnersPercentage = numWinners / (decimal)_heistSettings.Robbers.Count;

            // display success outcome
            if (winners.Count() == 1 && numWinners == _heistSettings.Robbers.Count)
            {
                BankRobber onlyWinner = winners.First();
                int earnings = (int)Math.Ceiling(onlyWinner.Gamble * payout.WinMultiplier);

                _irc.SendPublicChatMessage(_heistSettings.SingleUserSuccess
                    .Replace("user@", onlyWinner.Username)
                    .Replace("@bankname@", heistLevel.LevelBankName)
                    .Replace("@winamount@", earnings.ToString())
                    .Replace("@pointsname@", _botConfig.CurrencyType));
            }
            else if (numWinners == _heistSettings.Robbers.Count)
            {
                _irc.SendPublicChatMessage(_heistSettings.Success100 + " " + _resultMessage);
            }
            else if (numWinnersPercentage >= 0.34m)
            {
                _irc.SendPublicChatMessage(_heistSettings.Success34 + " " + _resultMessage);
            }
            else if (numWinnersPercentage > 0)
            {
                _irc.SendPublicChatMessage(_heistSettings.Success1 + " " + _resultMessage);
            }

            // show in case Twitch deletes the message because of exceeding character length
            Console.WriteLine("\n" + _resultMessage + "\n");
        }
        #endregion
    }
}
