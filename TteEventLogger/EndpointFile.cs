using System;
using System.IO;
using System.Text.Json;

namespace TteEventLogger;

/// <summary>
/// Loads the push endpoint and API key from the file named by <see cref="EnvironmentVariable"/>:
/// <c>{ "endpointUrl": "…", "apiKey": "…" }</c>, written root-owned by the fleet's <c>setup.sh</c>
/// outside every path the backend's file browser exposes. That's why these two values don't live
/// only in the <c>ModConfig</c>: <c>ModConfigs/</c> is browsable and gets copied into S3. Same
/// pattern as TteControl's <c>TTE_CONTROL_CREDENTIAL_FILE</c>.
///
/// When the file loads, its values override the ModConfig's <c>EndpointUrl</c> and <c>ApiKey</c>.
/// When the variable isn't set (a local dev server), the ModConfig is used as before.
///
/// Every failure comes back as a reason that names the path but never the file's contents, so the
/// caller can log it as is. <see cref="JsonException"/> messages can quote the offending text, so
/// they are never passed through.
/// </summary>
public static class EndpointFile
{
	public const string EnvironmentVariable = "TTE_EVENT_LOGGER_ENDPOINT_FILE";

	public sealed record Endpoint(string Path, string EndpointUrl, string ApiKey);

	private sealed class Shape
	{
		public string? EndpointUrl { get; set; }
		public string? ApiKey { get; set; }
	}

	private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	/// Reads the file named by the environment variable. Both results null means the variable isn't
	/// set, which is not a problem: the ModConfig is the source then.
	/// </summary>
	public static (Endpoint? Endpoint, string? Problem) LoadFromEnvironment() =>
		Load(Environment.GetEnvironmentVariable(EnvironmentVariable));

	public static (Endpoint? Endpoint, string? Problem) Load(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return (null, null);

		string json;
		try
		{
			json = File.ReadAllText(path);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return (null, $"The endpoint file {path} doesn't exist.");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return (null, $"The endpoint file {path} can't be read ({ex.GetType().Name}).");
		}

		Shape? shape;
		try
		{
			shape = JsonSerializer.Deserialize<Shape>(json, Options);
		}
		catch (JsonException)
		{
			return (null, $"The endpoint file {path} isn't a JSON object of the form {{ \"endpointUrl\", \"apiKey\" }}.");
		}

		var url = shape?.EndpointUrl?.Trim() ?? "";
		var key = shape?.ApiKey?.Trim() ?? "";
		if (url.Length == 0 || key.Length == 0)
			return (null, $"The endpoint file {path} has an empty or missing endpointUrl or apiKey.");

		return (new Endpoint(path, url, key), null);
	}
}
