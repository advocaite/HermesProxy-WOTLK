using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BNetServer;
using BNetServer.Networking;
using Framework;
using Framework.Logging;
using Framework.Networking;
using HermesProxy.Configuration;
using HermesProxy.World;
using HermesProxy.World.Server;

namespace HermesProxy;

internal class Server
{
	private static readonly string? _buildTag;

	public static void ServerMain(CommandLineArguments args)
	{
		Log.Print(LogType.Server, "Starting Hermes Proxy...", "ServerMain", "Server.cs");
		Log.Print(LogType.Server, "Version " + Server.GetVersionInformation(), "ServerMain", "Server.cs");
		String workingDirectory = args.WorkingDirectory ?? Path.GetDirectoryName(AppContext.BaseDirectory) ?? ".";

		if (Environment.CurrentDirectory != workingDirectory)
		{
			Log.Print(LogType.Storage, "Switching working directory", "ServerMain", "Server.cs");
			Log.Print(LogType.Storage, "Old: " + Environment.CurrentDirectory, "ServerMain", "Server.cs");
			Directory.SetCurrentDirectory(workingDirectory);
			Environment.CurrentDirectory = workingDirectory;
			Log.Print(LogType.Storage, "New: " + Environment.CurrentDirectory, "ServerMain", "Server.cs");
			Thread.Sleep(TimeSpan.FromSeconds(1.0));
		}
		Log.Start();
		
		ConfigurationParser config;
		try
		{
			config = ConfigurationParser.ParseFromFile(args.ConfigFileLocation, args.OverwrittenConfigValues);
		}
		catch (FileNotFoundException)
		{
			Log.Print(LogType.Error, "Config loading failed", "ServerMain", "Server.cs");
			return;
		}
		if (!Settings.LoadAndVerifyFrom(config))
		{
			Log.Print(LogType.Error, "The verification of the config failed", "ServerMain", "Server.cs");
			return;
		}
		Log.DebugLogEnabled = Settings.DebugOutput;
		Log.Print(LogType.Debug, "Debug logging enabled", "ServerMain", "Server.cs");
		if (!AesGcm.IsSupported)
		{
			Log.Print(LogType.Error, "AesGcm is not supported on your platform", "ServerMain", "Server.cs");
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				Log.Print(LogType.Error, "Since you are on MacOS, you can install openssl@3 via homebrew", "ServerMain", "Server.cs");
				Log.Print(LogType.Error, "Run this:      brew install openssl@3", "ServerMain", "Server.cs");
				Log.Print(LogType.Error, "Start Hermes:  DYLD_LIBRARY_PATH=/opt/homebrew/opt/openssl@3/lib ./HermesProxy", "ServerMain", "Server.cs");
			}
			return;
		}
		Log.Print(LogType.Server, $"Modern (Client) Build: {Settings.ClientBuild}", "ServerMain", "Server.cs");
		Log.Print(LogType.Server, $"Legacy (Server) Build: {Settings.ServerBuild}", "ServerMain", "Server.cs");
		GameData.LoadEverything();
		IPAddress bindIp = NetworkUtils.ResolveOrDirectIPv64(Settings.ExternalAddress);
		if (!IPAddress.IsLoopback(bindIp))
		{
			bindIp = IPAddress.Any;
		}
		Log.Print(LogType.Network, "External IP: " + Settings.ExternalAddress, "ServerMain", "Server.cs");
		try
		{
			X509Certificate2 certificate = BnetServerCertificate.Certificate;
			Log.Print(LogType.Server, $"BNet TLS certificate: '{certificate.Subject}' issued by '{certificate.Issuer}', expires {certificate.NotAfter:yyyy-MM-dd}" + (string.IsNullOrEmpty(Settings.CertificatePfxPath) ? "" : $" (from {Settings.CertificatePfxPath})"), "ServerMain", "Server.cs");
		}
		catch (TypeInitializationException e)
		{
			Log.Print(LogType.Error, $"Could not load the BNet TLS certificate: {e.InnerException?.Message ?? e.Message}", "ServerMain", "Server.cs");
			return;
		}
		Singleton<LoginServiceManager>.Instance.Initialize();
		SocketManager<BnetTcpSession> bnetSocketServer = Server.StartServer<BnetTcpSession>(new IPEndPoint(bindIp, Settings.BNetPort));
		SocketManager<BnetRestApiSession> restSocketServer = Server.StartServer<BnetRestApiSession>(new IPEndPoint(bindIp, Settings.RestPort));
		SocketManager<RealmSocket> realmSocketServer = Server.StartServer<RealmSocket>(new IPEndPoint(bindIp, Settings.RealmPort));
		SocketManager<WorldSocket> worldSocketServer = Server.StartServer<WorldSocket>(new IPEndPoint(bindIp, Settings.InstancePort));
		while (restSocketServer.IsListening || bnetSocketServer.IsListening || realmSocketServer.IsListening || worldSocketServer.IsListening)
		{
			Thread.Sleep(TimeSpan.FromSeconds(10.0));
		}
		Console.WriteLine($"(restSocketServer.IsListening: {restSocketServer.IsListening}");
		Console.WriteLine($"(bnetSocketServer.IsListening: {bnetSocketServer.IsListening}");
		Console.WriteLine($"(realmSocketServer.IsListening: {realmSocketServer.IsListening}");
		Console.WriteLine($"(worldSocketServer.IsListening: {worldSocketServer.IsListening}");
	}

	private static SocketManager<TSocketType> StartServer<TSocketType>(IPEndPoint bindIp) where TSocketType : ISocket
	{
		SocketManager<TSocketType> socketManager = new SocketManager<TSocketType>();
		Log.Print(LogType.Server, $"Starting {typeof(TSocketType).Name} service on {bindIp}...", "StartServer", "Server.cs");
		if (!socketManager.StartNetwork(bindIp.Address.ToString(), bindIp.Port))
		{
			throw new Exception("Failed to start " + typeof(TSocketType).Name + " service");
		}
		Thread.Sleep(50);
		return socketManager;
	}

	private static async Task CheckForUpdate()
	{
		try
		{
			if (GitVersionInformation.CommitsSinceVersionSource != "0" || GitVersionInformation.UncommittedChanges != "0")
			{
				return;
			}
			using HttpClient client = new HttpClient();
			client.Timeout = TimeSpan.FromSeconds(5.0);
			client.DefaultRequestHeaders.Add("User-Agent", "curl/7.0.0");
			HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/advocaite/HermesProxy-WOTLK/releases/latest");
			response.EnsureSuccessStatusCode();
			Dictionary<string, object> parsedJson = JsonSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
			string commitDateStr = parsedJson["created_at"].ToString();
			DateTime commitDate = DateTime.Parse(commitDateStr, CultureInfo.InvariantCulture).ToUniversalTime();
			string myCommitDateStr = GitVersionInformation.CommitDate;
			DateTime myCommitDate = DateTime.Parse(myCommitDateStr, CultureInfo.InvariantCulture).ToUniversalTime();
			if (commitDate > myCommitDate)
			{
				Console.WriteLine("------------------------");
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"HermesProxy update available v{GitVersionInformation.Major}.{GitVersionInformation.Minor} => {parsedJson["tag_name"]} ({commitDate:yyyy-MM-dd})");
				Console.WriteLine("Please download new version from https://github.com/advocaite/HermesProxy-WOTLK/releases/latest");
				Console.ResetColor();
				Console.WriteLine("------------------------");
				Console.WriteLine();
				Thread.Sleep(10000);
			}
		}
		catch
		{
		}
	}

	private static string GetVersionInformation()
	{
		DateTime commitDate = DateTime.Parse(GitVersionInformation.CommitDate, CultureInfo.InvariantCulture).ToUniversalTime();
		string version = $"{commitDate:yyyy-MM-dd} {Server._buildTag}{GitVersionInformation.MajorMinorPatch}";
		if (GitVersionInformation.CommitsSinceVersionSource != "0")
		{
			version += $"+{GitVersionInformation.CommitsSinceVersionSource}({GitVersionInformation.ShortSha})";
		}
		if (GitVersionInformation.UncommittedChanges != "0")
		{
			version += " dirty";
		}
		return version;
	}
}
