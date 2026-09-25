using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using EventNotifier.Core.Configuration;
using EventNotifier.Core.Events;
using EventNotifier.Core.Transport;
using Terraria;
using Terraria.ModLoader;
using Terraria.Net;

namespace TteEventLogger;

/// <summary>
/// The tModLoader side of EventNotifier.Core: builds envelopes from game state and hands them to
/// Core's bounded dispatch queue. Hooks call in from the main thread, the network thread (leave) and
/// the world-save thread, so everything here is safe to call concurrently and never blocks.
/// </summary>
public sealed class EventPublisher : IDisposable
{
	private readonly Mod _mod;
	private readonly NotifierSettings _settings = new();
	private readonly HttpClient _httpClient = new();
	private readonly NotificationDispatchQueue _queue;

	/// <summary>
	/// Last live snapshot per player slot, so <c>player.leave</c> still has a name once the client is
	/// gone. Cleared when a slot is claimed by a new join and after its leave is sent.
	/// </summary>
	private readonly ConcurrentDictionary<int, PlayerInfo> _lastKnown = new();

	private int _warnedNoEndpoint;
	private string _serverName = "";

	public EventPublisher(Mod mod, EventLoggerConfig config)
	{
		_mod = mod;
		ApplyConfig(config);

		// Core's sender reads _settings on every request, so later ApplyConfig calls take effect
		// without rebuilding it. The timeout and queue capacity are fixed here (ReloadRequired).
		var sender = new HttpNotificationSender(_httpClient, _settings);
		_queue = new NotificationDispatchQueue(sender, _settings.QueueCapacity, LogInfo, LogWarn);
	}

	public NotifierSettings Settings => _settings;

	public void ApplyConfig(EventLoggerConfig config)
	{
		config.CopyTo(_settings);
		_serverName = config.ServerName?.Trim() ?? "";
		_warnedNoEndpoint = 0;
	}

	/// <summary>
	/// Publishes a player-scoped event. With <paramref name="allowLive"/> false (leave), the live player
	/// object is not trusted and only the cached snapshot is used.
	/// </summary>
	public void PublishPlayerEvent(string eventType, int who, Dictionary<string, object?> eventData, bool allowLive = true)
	{
		PlayerInfo? live = allowLive ? BuildLivePlayerInfo(who) : null;
		PlayerInfo player;
		string dataSource;

		if (live is not null)
		{
			_lastKnown[who] = live;
			player = live;
			dataSource = PlayerDataSource.Live;
		}
		else if (_lastKnown.TryGetValue(who, out var cached))
		{
			player = cached;
			dataSource = PlayerDataSource.Cached;
		}
		else
		{
			player = new PlayerInfo { Index = who, Name = "unknown" };
			dataSource = PlayerDataSource.Unknown;
		}

		var envelope = CreateBase(eventType, eventData);
		envelope.Player = player;
		envelope.PlayerDataSource = dataSource;
		Enqueue(envelope);
	}

	public void PublishServerEvent(string eventType, Dictionary<string, object?> eventData)
	{
		Enqueue(CreateBase(eventType, eventData));
	}

	/// <summary>
	/// Refreshes a slot's cached snapshot from the live player. Called from every hook with a live
	/// player, whatever that hook's toggle, so a leave has a name even when join events are off.
	/// </summary>
	public void ObservePlayer(int who)
	{
		var info = BuildLivePlayerInfo(who);
		if (info is not null)
		{
			_lastKnown[who] = info;
		}
	}

	public void ForgetPlayer(int who)
	{
		_lastKnown.TryRemove(who, out _);
	}

	public string BuildStatusMessage()
	{
		return $"Dispatch stats: success={_queue.SuccessCount}, failed={_queue.FailureCount}, dropped={_queue.DroppedCount}.";
	}

	public void Dispose()
	{
		_queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_httpClient.Dispose();
	}

	private void Enqueue(EventEnvelope envelope)
	{
		if (string.IsNullOrWhiteSpace(_settings.EndpointUrl))
		{
			if (System.Threading.Interlocked.Exchange(ref _warnedNoEndpoint, 1) == 0)
			{
				LogWarn("EndpointUrl is not configured; events are being discarded.");
			}

			return;
		}

		_queue.TryEnqueue(envelope);
	}

	private EventEnvelope CreateBase(string eventType, Dictionary<string, object?> eventData)
	{
		return new EventEnvelope
		{
			SchemaVersion = _settings.SchemaVersion,
			EventType = eventType,
			OccurredAtUtc = DateTimeOffset.UtcNow,
			PluginVersion = _mod.Version.ToString(),
			Server = BuildServerInfo(),
			EventData = eventData
		};
	}

	private ServerInfo BuildServerInfo()
	{
		var configuredName = _serverName;

		return new ServerInfo
		{
			Name = string.IsNullOrEmpty(configuredName) ? Main.worldName : configuredName,
			WorldName = Main.worldName,
			ActivePlayers = CountActivePlayers(),
			MaxSlots = Main.maxNetPlayers,
			Version = BuildInfo.tMLVersion.ToString()
		};
	}

	private static int CountActivePlayers()
	{
		var count = 0;
		for (var i = 0; i < Main.maxPlayers; i++)
		{
			if (Main.player[i].active)
			{
				count++;
			}
		}

		return count;
	}

	private static PlayerInfo? BuildLivePlayerInfo(int who)
	{
		if (who < 0 || who >= Main.maxPlayers)
		{
			return null;
		}

		var player = Main.player[who];
		if (player is null || !player.active || string.IsNullOrEmpty(player.name))
		{
			return null;
		}

		return new PlayerInfo
		{
			Index = who,
			Name = player.name,
			AccountName = null,
			GroupName = null,
			IpAddress = GetIpAddress(who),
			IsLoggedIn = false
		};
	}

	private static string? GetIpAddress(int who)
	{
		try
		{
			// TcpAddress.ToString() appends the port; the contract wants the bare IP.
			var address = Netplay.Clients[who].Socket?.GetRemoteAddress();
			return address switch
			{
				TcpAddress tcp => tcp.Address.ToString(),
				null => null,
				_ => address.ToString()
			};
		}
		catch (Exception)
		{
			// The socket can be mid-teardown; the IP is optional.
			return null;
		}
	}

	private void LogInfo(string message) => _mod.Logger.Info(message);

	private void LogWarn(string message) => _mod.Logger.Warn(message);
}
