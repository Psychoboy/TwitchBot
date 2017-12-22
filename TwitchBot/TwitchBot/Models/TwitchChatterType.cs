﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TwitchBot.Enums;

namespace TwitchBot.Models
{
    public class TwitchChatterType
    {
        public List<TwitchChatter> TwitchChatters { get; set; }
        public ChatterType ChatterType { get; set; }
    }
}
