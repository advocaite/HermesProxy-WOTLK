using System;
using System.IO;
using Framework.Logging;
using HermesProxy;
using HermesProxy.Configuration;
using HermesProxy.Enums;

namespace Framework;

public static class Settings
{
	public static byte[] ClientSeed;

	public static ClientVersionBuild ClientBuild;

	public static ClientVersionBuild ServerBuild;

	public static string ServerAddress;

	public static int ServerPort;

	public static string ReportedOS;

	public static string ReportedPlatform;

	public static string ExternalAddress;

	public static string CertificatePfxPath;

	public static string CertificatePfxPassword;

	public static int RestPort;

	public static int BNetPort;

	public static int RealmPort;

	public static int InstancePort;

	public static bool DebugOutput;

	public static bool PacketsLog;

	public static int ServerSpellDelay;

	public static int ClientSpellDelay;

	public static bool LoadAndVerifyFrom(ConfigurationParser config)
	{
		Settings.ClientSeed = config.GetByteArray("ClientSeed", "179D3DC3235629D07113A9B3867F97A7".ParseAsByteArray());
		Settings.ClientBuild = config.GetEnum("ClientBuild", ClientVersionBuild.V2_5_2_40892);
		string serverBuildStr = config.GetString("ServerBuild", "auto");
		if (serverBuildStr == "auto")
		{
			Settings.ServerBuild = VersionChecker.GetBestLegacyVersion(Settings.ClientBuild);
		}
		else
		{
			Settings.ServerBuild = config.GetEnum("ServerBuild", ClientVersionBuild.Zero);
		}
		Settings.ServerAddress = config.GetString("ServerAddress", "127.0.0.1");
		Settings.ServerPort = config.GetInt("ServerPort", 3724);
		Settings.ReportedOS = config.GetString("ReportedOS", "OSX");
		Settings.ReportedPlatform = config.GetString("ReportedPlatform", "x86");
		Settings.ExternalAddress = config.GetString("ExternalAddress", "127.0.0.1");
		Settings.CertificatePfxPath = config.GetString("CertificatePfxPath", "");
		Settings.CertificatePfxPassword = config.GetString("CertificatePfxPassword", "");
		Settings.RestPort = config.GetInt("RestPort", 8081);
		Settings.BNetPort = config.GetInt("BNetPort", 1119);
		Settings.RealmPort = config.GetInt("RealmPort", 8084);
		Settings.InstancePort = config.GetInt("InstancePort", 8086);
		Settings.DebugOutput = config.GetBoolean("DebugOutput", defValue: false);
		Settings.PacketsLog = config.GetBoolean("PacketsLog", defValue: true);
		Settings.ServerSpellDelay = config.GetInt("ServerSpellDelay", 0);
		Settings.ClientSpellDelay = config.GetInt("ClientSpellDelay", 0);
		return Settings.VerifyConfig();
	}

	private static bool VerifyConfig()
	{
		if (Settings.ClientSeed.Length != 16)
		{
			Log.Print(LogType.Server, "ClientSeed must have byte length of 16 (32 characters)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!VersionChecker.IsSupportedModernVersion(Settings.ClientBuild))
		{
			Log.Print(LogType.Server, $"Unsupported ClientBuild '{Settings.ClientBuild}'", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!VersionChecker.IsSupportedLegacyVersion(Settings.ServerBuild))
		{
			Log.Print(LogType.Server, $"Unsupported ServerBuild '{Settings.ServerBuild}', use 'auto' to select best", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!string.IsNullOrEmpty(Settings.CertificatePfxPath) && !File.Exists(Settings.CertificatePfxPath))
		{
			Log.Print(LogType.Server, $"CertificatePfxPath file not found: '{Settings.CertificatePfxPath}'", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!IsValidPortNumber(Settings.RestPort))
		{
			Log.Print(LogType.Server, $"Specified battle.net port ({Settings.RestPort}) out of allowed range (1-65535)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!IsValidPortNumber(Settings.ServerPort))
		{
			Log.Print(LogType.Server, $"Specified battle.net port ({Settings.BNetPort}) out of allowed range (1-65535)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!IsValidPortNumber(Settings.BNetPort))
		{
			Log.Print(LogType.Server, $"Specified battle.net port ({Settings.BNetPort}) out of allowed range (1-65535)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!IsValidPortNumber(Settings.RealmPort))
		{
			Log.Print(LogType.Server, $"Specified battle.net port ({Settings.RealmPort}) out of allowed range (1-65535)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (!IsValidPortNumber(Settings.InstancePort))
		{
			Log.Print(LogType.Server, $"Specified battle.net port ({Settings.InstancePort}) out of allowed range (1-65535)", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (Settings.ServerSpellDelay < 0)
		{
			Log.Print(LogType.Server, "ServerSpellDelay must be larger than or equal to 0", "VerifyConfig", "Settings.cs");
			return false;
		}
		if (Settings.ClientSpellDelay < 0)
		{
			Log.Print(LogType.Server, "ClientSpellDelay must be larger than or equal to 0", "VerifyConfig", "Settings.cs");
			return false;
		}
		return true;
		static bool IsValidPortNumber(int someNumber)
		{
			return someNumber > 0 && someNumber < 65535;
		}
	}
}
