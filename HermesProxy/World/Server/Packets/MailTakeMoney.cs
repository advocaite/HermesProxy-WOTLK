namespace HermesProxy.World.Server.Packets;

public class MailTakeMoney : ClientPacket
{
	public WowGuid128 Mailbox;

	public uint MailID;

	public long Money;

	public MailTakeMoney(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.Mailbox = base._worldPacket.ReadPackedGuid128();
		this.MailID = (uint)base._worldPacket.ReadUInt64();
		this.Money = base._worldPacket.ReadInt64();
	}
}
