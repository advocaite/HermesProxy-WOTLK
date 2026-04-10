namespace HermesProxy.World.Server.Packets;

public class MailMarkAsRead : ClientPacket
{
	public WowGuid128 Mailbox;

	public uint MailID;

	public MailMarkAsRead(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.Mailbox = base._worldPacket.ReadPackedGuid128();
		this.MailID = (uint)base._worldPacket.ReadUInt64();
	}
}
