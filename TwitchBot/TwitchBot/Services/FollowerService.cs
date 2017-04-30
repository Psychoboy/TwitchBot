﻿using System;
using System.Collections.Generic;
using System.Linq;

using TwitchBot.Models;
using TwitchBot.Repositories;

namespace TwitchBot.Services
{
    public class FollowerService
    {
        private FollowerRepository _followerDb;

        public FollowerService(FollowerRepository followerDb)
        {
            _followerDb = followerDb;
        }

        public int CurrExp(string chatter, int broadcasterId)
        {
            return _followerDb.CurrExp(chatter, broadcasterId);
        }

        public void UpdateExp(string chatter, int broadcasterId, int currExp)
        {
            _followerDb.UpdateExp(chatter, broadcasterId, currExp);
        }

        public void EnlistRecruit(string chatter, int broadcasterId)
        {
            _followerDb.EnlistRecruit(chatter, broadcasterId);
        }

        public List<Rank> GetRankList(int broadcasterId)
        {
            return _followerDb.GetRankList(broadcasterId);
        }

        public Rank GetCurrRank(List<Rank> rankList, int currExp)
        {
            Rank currFollowerRank = new Rank();

            // find the user's current rank by experience cap
            foreach (Rank followerRank in rankList.OrderBy(r => r.ExpCap))
            {
                // search until current experience < experience cap
                if (currExp >= followerRank.ExpCap)
                {
                    continue;
                }
                else
                {
                    currFollowerRank.Name = followerRank.Name;
                    currFollowerRank.ExpCap = followerRank.ExpCap;
                    break;
                }
            }

            return currFollowerRank;
        }

        public decimal GetHoursWatched(int currExp)
        {
            return Math.Round(Convert.ToDecimal(currExp) / (decimal)12.0, 2);
        }
    }
}
