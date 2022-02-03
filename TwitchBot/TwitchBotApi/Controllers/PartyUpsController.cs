﻿using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TwitchBotDb.Context;

namespace TwitchBotApi.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]/[action]")]
    public class PartyUpsController : ControllerBase
    {
        private readonly SimpleBotContext _context;

        public PartyUpsController(SimpleBotContext context)
        {
            _context = context;
        }

        // GET: api/partyups/get/2
        // GET: api/partyups/get/2?gameid=2
        // GET: api/partyups/get/2?gameid=2?partymember=Sinon
        [HttpGet("{broadcasterId:int}")]
        public async Task<IActionResult> Get([FromRoute] int broadcasterId, [FromQuery] int gameId = 0, [FromQuery] string partyMember = "")
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var partyUp = new object();

            if (gameId > 0 && !string.IsNullOrEmpty(partyMember))
            {
                partyUp = await _context.PartyUps
                    .SingleOrDefaultAsync(m =>
                        m.BroadcasterId == broadcasterId
                            && m.GameId == gameId
                            && m.PartyMemberName.Contains(partyMember, StringComparison.CurrentCultureIgnoreCase));
            }
            else if (gameId > 0)
            {
                partyUp = await _context.PartyUps.Where(m => m.BroadcasterId == broadcasterId && m.GameId == gameId)
                    .Select(m => m.PartyMemberName)
                    .ToListAsync();
            }
            else
            {
                partyUp = await _context.PartyUps.Where(m => m.BroadcasterId == broadcasterId).ToListAsync();
            }

            if (partyUp == null)
            {
                return NotFound();
            }

            return Ok(partyUp);
        }
    }
}