namespace HermesProxy.World.Server.Packets;

public class MailCreateTextItem : ClientPacket
{
	public WowGuid128 Mailbox;

	public uint MailID;

	public MailCreateTextItem(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.Mailbox = base._worldPacket.ReadPackedGuid128();
		this.MailID = (uint)base._worldPacket.ReadUInt64();
	}
}
