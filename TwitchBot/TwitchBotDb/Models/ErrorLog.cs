﻿using System;

namespace TwitchBotDb.Models
{
    public partial class ErrorLog
    {
        public int Id { get; set; }
        public DateTime ErrorTime { get; set; }
        public int ErrorLine { get; set; }
        public string ErrorClass { get; set; }
        public string ErrorMethod { get; set; }
        public string ErrorMsg { get; set; }
        public int BroadcasterId { get; set; }
        public string Command { get; set; }
        public string UserMsg { get; set; }

        public virtual Broadcaster Broadcaster { get; set; }
    }
}
