namespace HermesProxy.World.Server.Packets;

public class MailReturnToSender : ClientPacket
{
	public uint MailID;

	public WowGuid128 SenderGUID;

	public MailReturnToSender(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.MailID = (uint)base._worldPacket.ReadUInt64();
		this.SenderGUID = base._worldPacket.ReadPackedGuid128();
	}
}
