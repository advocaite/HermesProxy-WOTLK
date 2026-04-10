namespace HermesProxy.World.Server.Packets;

public class MailTakeItem : ClientPacket
{
	public WowGuid128 Mailbox;

	public uint MailID;

	public uint AttachID;

	public MailTakeItem(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.Mailbox = base._worldPacket.ReadPackedGuid128();
		this.MailID = (uint)base._worldPacket.ReadUInt64();
		this.AttachID = (uint)base._worldPacket.ReadUInt64();
	}
}
