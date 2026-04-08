using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class PreRessurect : ServerPacket
{
	public WowGuid128 PlayerGUID;

	public PreRessurect()
		: base(Opcode.SMSG_PRE_RESSURECT)
	{
	}

	public override void Write()
	{
		base._worldPacket.WritePackedGuid128(this.PlayerGUID);
	}
}
