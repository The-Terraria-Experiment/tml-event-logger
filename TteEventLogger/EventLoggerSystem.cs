using System;
using System.Collections.Generic;
using EventNotifier.Core.Events;
using Terraria;
using Terraria.Chat;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TteEventLogger;

/// <summary>
/// Owns the publisher and the server-side hooks that aren't <see cref="ModPlayer"/> hooks.
/// </summary>
/// <remarks>
/// Every hook here was checked against tModLoader's source to run on a dedicated server:
/// <list type="bullet">
///   <item><c>NetMessage.greetPlayer</c>: the server's "join complete" point, when the client first
///   spawns (packet 12). <c>ModPlayer.OnEnterWorld</c> only runs on the joining client.</item>
///   <item><c>ChatCommandProcessor.ProcessIncomingMessage</c>: every chat message a client sends,
///   called from <c>NetTextModule.DeserializeAsServer</c>.</item>
///   <item><c>Player.Spawn</c>: called for packet 12, i.e. both first spawn and respawn, like
///   TShock's <c>PlayerSpawn</c>. <c>ModPlayer.OnRespawn</c> would miss the first spawn.</item>
///   <item><c>SaveWorldData</c>: every world save, including autosaves.</item>
/// </list>
/// </remarks>
public sealed class EventLoggerSystem : ModSystem
{
	/// <summary>Null on clients and in single player, and until content setup finishes.</summary>
	internal static EventPublisher? Publisher { get; private set; }

	public override void Load()
	{
		if (!Main.dedServ)
		{
			return;
		}

		On_NetMessage.greetPlayer += OnGreetPlayer;
		On_ChatCommandProcessor.ProcessIncomingMessage += OnProcessIncomingMessage;
		On_Player.Spawn += OnPlayerSpawn;
	}

	public override void PostSetupContent()
	{
		if (!Main.dedServ)
		{
			return;
		}

		Publisher = new EventPublisher(Mod, ModContent.GetInstance<EventLoggerConfig>());
		Mod.Logger.Info("Event publisher started.");
	}

	public override void Unload()
	{
		// tModLoader removes On_ detours registered during Load automatically.
		Publisher?.Dispose();
		Publisher = null;
	}

	public override void SaveWorldData(TagCompound tag)
	{
		Guard(publisher =>
		{
			if (!publisher.Settings.Events.WorldSave)
			{
				return;
			}

			publisher.PublishServerEvent(EventType.WorldSave, new Dictionary<string, object?>
			{
				["worldId"] = Main.worldID
			});
		});
	}

	/// <summary>Nothing to load; tModLoader requires overriding this together with SaveWorldData.</summary>
	public override void LoadWorldData(TagCompound tag)
	{
	}

	private static void OnGreetPlayer(On_NetMessage.orig_greetPlayer orig, int plr)
	{
		orig(plr);

		Guard(publisher =>
		{
			// A new connection has claimed this slot: drop any previous occupant's snapshot.
			publisher.ForgetPlayer(plr);
			publisher.ObservePlayer(plr);

			if (!publisher.Settings.Events.Join)
			{
				return;
			}

			publisher.PublishPlayerEvent(EventType.PlayerJoin, plr, new Dictionary<string, object?>
			{
				["who"] = plr
			});
		});
	}

	private static void OnProcessIncomingMessage(On_ChatCommandProcessor.orig_ProcessIncomingMessage orig, ChatCommandProcessor self, ChatMessage message, int clientId)
	{
		Guard(publisher =>
		{
			publisher.ObservePlayer(clientId);

			if (!publisher.Settings.Events.Chat)
			{
				return;
			}

			publisher.PublishPlayerEvent(EventType.PlayerChat, clientId, new Dictionary<string, object?>
			{
				["rawText"] = message.Text
			});
		});

		orig(self, message, clientId);
	}

	private static void OnPlayerSpawn(On_Player.orig_Spawn orig, Player self, PlayerSpawnContext context)
	{
		// Spawn clears the respawn timer, so read it first.
		var respawnTimer = self.respawnTimer;
		orig(self, context);

		var who = self.whoAmI;
		if (who < 0 || who >= Main.maxPlayers || !Netplay.Clients[who].IsActive)
		{
			return;
		}

		Guard(publisher =>
		{
			publisher.ObservePlayer(who);

			if (!publisher.Settings.Events.Spawn)
			{
				return;
			}

			publisher.PublishPlayerEvent(EventType.PlayerSpawn, who, new Dictionary<string, object?>
			{
				["playerId"] = who,
				["spawnX"] = self.SpawnX,
				["spawnY"] = self.SpawnY,
				["respawnTimer"] = respawnTimer,
				["deathsPve"] = self.numberOfDeathsPVE,
				["deathsPvp"] = self.numberOfDeathsPVP,
				["team"] = self.team,
				["spawnContext"] = context.ToString()
			});
		});
	}

	/// <summary>
	/// Runs a hook body against the publisher, if there is one, and never lets an exception escape
	/// into the game: a broken event feed must not break chat, joins or saves.
	/// </summary>
	internal static void Guard(Action<EventPublisher> body)
	{
		var publisher = Publisher;
		if (publisher is null)
		{
			return;
		}

		try
		{
			body(publisher);
		}
		catch (Exception ex)
		{
			ModContent.GetInstance<TteEventLogger>()?.Logger.Error("Event hook failed.", ex);
		}
	}
}
