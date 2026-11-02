using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Framework;

namespace BNetServer;

public static class BnetServerCertificate
{
	private const string BNET_SERVER_CERT_RESOURCE = "HermesProxy.BNetServer.pfx";

	public static X509Certificate2 Certificate { get; }

	static BnetServerCertificate()
	{
		// CertificatePfxPath picks another certificate, such as Certificates/TrinityCore.pfx; unset, the embedded one
		if (!string.IsNullOrEmpty(Settings.CertificatePfxPath))
		{
			BnetServerCertificate.Certificate = new X509Certificate2(Settings.CertificatePfxPath, string.IsNullOrEmpty(Settings.CertificatePfxPassword) ? null : Settings.CertificatePfxPassword);
			return;
		}
		Assembly currentAsm = Assembly.GetExecutingAssembly();
		using Stream stream = currentAsm.GetManifestResourceStream("HermesProxy.BNetServer.pfx");
		if (stream == null)
		{
			throw new Exception("Resource not found: 'HermesProxy.BNetServer.pfx'");
		}
		MemoryStream ms = new MemoryStream();
		stream.CopyTo(ms);
		byte[] bytes = ms.ToArray();
		BnetServerCertificate.Certificate = new X509Certificate2(bytes);
	}
}
