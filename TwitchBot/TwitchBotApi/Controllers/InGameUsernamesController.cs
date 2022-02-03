﻿using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TwitchBotDb.Context;
using TwitchBotDb.Models;

namespace TwitchBotApi.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]/[action]")]
    public class InGameUsernamesController : ControllerBase
    {
        private readonly SimpleBotContext _context;

        public InGameUsernamesController(SimpleBotContext context)
        {
            _context = context;
        }

        // GET: api/ingameusernames/get/5
        // GET: api/ingameusernames/get/5?gameId=1
        [HttpGet("{broadcasterId:int}")]
        public async Task<IActionResult> Get([FromRoute] int broadcasterId, [FromQuery] int? gameId = 0)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var inGameUsername = new object();

            if (gameId == 0)
                inGameUsername = await _context.InGameUsernames.Where(m => m.BroadcasterId == broadcasterId).ToListAsync();
            else if (gameId != null)
                inGameUsername = await _context.InGameUsernames.SingleOrDefaultAsync(m => m.BroadcasterId == broadcasterId && m.GameId == gameId);
            else if (gameId == null)
                inGameUsername = await _context.InGameUsernames.SingleOrDefaultAsync(m => m.BroadcasterId == broadcasterId && m.GameId == null);

            if (inGameUsername == null && gameId != null)
            {
                // try getting the generic username message
                inGameUsername = await _context.InGameUsernames.SingleOrDefaultAsync(m => m.BroadcasterId == broadcasterId && m.GameId == null);

                if (inGameUsername == null)
                {
                    return NotFound();
                }
            }

            return Ok(inGameUsername);
        }

        // PUT: api/ingameusernames/update/2?id=1
        // Body (JSON): { "id": 1, "message": "GenericUsername123", "broadcasterid": 2, "gameid": null }
        // Body (JSON): { "id": 1, "message": "UniqueUsername456", "broadcasterid": 2, "gameid": 2 }
        [HttpPut("{broadcasterId:int}")]
        public async Task<IActionResult> Update([FromRoute] int broadcasterId, [FromQuery] int id, [FromBody] InGameUsername inGameUsername)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != inGameUsername.Id && broadcasterId != inGameUsername.BroadcasterId)
            {
                return BadRequest();
            }

            _context.Entry(inGameUsername).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InGameUsernameExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/ingameusernames/create
        // Body (JSON): { "message": "GenericUsername123", "broadcasterId": 2 }
        // Body (JSON): { "message": "UniqueUsername456", "broadcasterId": 2, "gameid": 2 }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] InGameUsername inGameUsername)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _context.InGameUsernames.Add(inGameUsername);
            await _context.SaveChangesAsync();

            return CreatedAtAction("Get", new { broadcasterId = inGameUsername.BroadcasterId, gameId = inGameUsername.Game }, inGameUsername);
        }

        // DELETE: api/ingameusernames/delete/5?id=2
        [HttpDelete("{broadcasterId:int}")]
        public async Task<IActionResult> Delete([FromRoute] int broadcasterId, [FromQuery] int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            InGameUsername inGameUsername = await _context.InGameUsernames.SingleOrDefaultAsync(m => m.Id == id && m.BroadcasterId == broadcasterId);
            if (inGameUsername == null)
            {
                return NotFound();
            }

            _context.InGameUsernames.Remove(inGameUsername);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool InGameUsernameExists(int id)
        {
            return _context.InGameUsernames.Any(e => e.Id == id);
        }
    }
}