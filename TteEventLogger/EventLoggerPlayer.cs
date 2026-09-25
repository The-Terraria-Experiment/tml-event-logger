using System.Collections.Generic;
using EventNotifier.Core.Events;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace TteEventLogger;

/// <summary>
/// The death and leave hooks. Both were checked to run on a dedicated server: the server calls
/// <c>Player.KillMe</c> for packet 118 (which runs <see cref="Kill"/>), and
/// <c>NetMessage.SyncDisconnectedPlayer</c> runs <see cref="PlayerDisconnect"/>, on the network
/// thread rather than the main thread.
/// </summary>
public sealed class EventLoggerPlayer : ModPlayer
{
	public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
	{
		if (!Main.dedServ)
		{
			return;
		}

		var who = Player.whoAmI;
		EventLoggerSystem.Guard(publisher =>
		{
			publisher.ObservePlayer(who);

			if (!publisher.Settings.Events.Death)
			{
				return;
			}

			publisher.PublishPlayerEvent(EventType.PlayerDeath, who, new Dictionary<string, object?>
			{
				["playerId"] = who,
				["damage"] = damage,
				["direction"] = hitDirection,
				["pvp"] = pvp,
				["deathReason"] = damageSource.GetDeathText(Player.name).ToString()
			});
		});
	}

	public override void PlayerDisconnect()
	{
		if (!Main.dedServ)
		{
			return;
		}

		var who = Player.whoAmI;
		EventLoggerSystem.Guard(publisher =>
		{
			if (publisher.Settings.Events.Leave)
			{
				// The client is gone, so the slot's Player object is no longer trustworthy: send the
				// last-known snapshot ("cached"), or "unknown" for a connection that never joined.
				publisher.PublishPlayerEvent(EventType.PlayerLeave, who, new Dictionary<string, object?>
				{
					["who"] = who
				}, allowLive: false);
			}

			// Vacate the slot even when leave events are off, so the next occupant can't inherit it.
			publisher.ForgetPlayer(who);
		});
	}
}
