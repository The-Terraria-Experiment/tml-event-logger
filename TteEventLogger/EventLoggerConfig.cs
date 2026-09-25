using System.ComponentModel;
using EventNotifier.Core.Configuration;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace TteEventLogger;

/// <summary>
/// Server config, stored at <c>ModConfigs/TteEventLogger_EventLoggerConfig.json</c> under the tModLoader
/// save directory.
/// </summary>
/// <remarks>
/// On the fleet, <c>EndpointUrl</c> and <c>ApiKey</c> come from the root-owned file named by
/// <c>TTE_EVENT_LOGGER_ENDPOINT_FILE</c> instead (see <see cref="EndpointFile"/>), which overrides
/// them here. <c>ModConfigs/</c> is browsable from the web app, so the key must not live in it there.
/// The two fields remain for a local dev server with no such file.
///
/// tModLoader sends a ServerSide config's JSON to every client, but only for mods with
/// <c>side = Both</c>. This mod is <c>side = Server</c>, so the API key never leaves the server. Keep it
/// that way.
/// </remarks>
public sealed class EventLoggerConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ServerSide;

	/// <summary>Full pushLog URL, instance ID included: <c>{API base}/logging/{instanceId}/players/push</c>.</summary>
	[Header("Endpoint")]
	[DefaultValue("")]
	public string EndpointUrl { get; set; } = "";

	/// <summary>API Gateway key. A secret: never log or print it.</summary>
	[DefaultValue("")]
	public string ApiKey { get; set; } = "";

	[DefaultValue("x-api-key")]
	public string ApiKeyHeaderName { get; set; } = "x-api-key";

	/// <summary><c>server.name</c> in payloads. tModLoader has no server name, so empty means the world name.</summary>
	[DefaultValue("")]
	public string ServerName { get; set; } = "";

	[Header("Delivery")]
	[Range(500, 60000)]
	[DefaultValue(5000)]
	[ReloadRequired]
	public int RequestTimeoutMs { get; set; } = 5000;

	[Range(0, 10)]
	[DefaultValue(1)]
	public int RetryCount { get; set; } = 1;

	[Range(0, 10000)]
	[DefaultValue(400)]
	public int RetryDelayMs { get; set; } = 400;

	[Range(32, 8192)]
	[DefaultValue(512)]
	[ReloadRequired]
	public int QueueCapacity { get; set; } = 512;

	[Header("Events")]
	[DefaultValue(true)]
	public bool Join { get; set; } = true;

	[DefaultValue(true)]
	public bool Leave { get; set; } = true;

	[DefaultValue(true)]
	public bool Chat { get; set; } = true;

	[DefaultValue(true)]
	public bool Death { get; set; } = true;

	[DefaultValue(true)]
	public bool Spawn { get; set; } = true;

	[DefaultValue(true)]
	public bool WorldSave { get; set; } = true;

	public override void OnChanged()
	{
		EventLoggerSystem.Publisher?.ApplyConfig(this);
	}

	/// <summary>No client can change this config (clients don't load the mod, but refuse anyway).</summary>
	public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref Terraria.Localization.NetworkText message)
	{
		return false;
	}

	/// <summary>Copies these values onto Core's settings object in place, so the running sender sees them.</summary>
	internal void CopyTo(NotifierSettings settings)
	{
		settings.EndpointUrl = EndpointUrl?.Trim() ?? "";
		settings.ApiKey = ApiKey?.Trim() ?? "";
		settings.ApiKeyHeaderName = string.IsNullOrWhiteSpace(ApiKeyHeaderName) ? "x-api-key" : ApiKeyHeaderName.Trim();
		settings.RequestTimeoutMs = RequestTimeoutMs;
		settings.RetryCount = RetryCount;
		settings.RetryDelayMs = RetryDelayMs;
		settings.QueueCapacity = QueueCapacity;
		settings.Events.Join = Join;
		settings.Events.Leave = Leave;
		settings.Events.Chat = Chat;
		settings.Events.Death = Death;
		settings.Events.Spawn = Spawn;
		settings.Events.WorldSave = WorldSave;
		settings.Events.Reload = false;
	}
}
