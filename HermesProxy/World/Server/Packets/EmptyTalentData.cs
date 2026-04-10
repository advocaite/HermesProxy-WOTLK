using Framework.Constants;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class EmptyTalentData : ServerPacket
{
	public EmptyTalentData()
		: base(Opcode.SMSG_UPDATE_TALENT_DATA, ConnectionType.Instance)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt32(0u);        // UnspentTalentPoints
		base._worldPacket.WriteUInt8(0);          // ActiveGroup
		base._worldPacket.WriteUInt32(1u);        // GroupCount = 1
		base._worldPacket.WriteUInt8(0);          // TalentCount (byte)
		base._worldPacket.WriteUInt32(0u);        // TalentCount (dword)
		base._worldPacket.WriteUInt8(0);          // GlyphCount (byte)
		base._worldPacket.WriteUInt32(0u);        // GlyphCount (dword)
		base._worldPacket.WriteUInt8(4);          // SpecID = MAX_SPECIALIZATIONS (no spec)
		base._worldPacket.WriteBit(bit: false);   // IsPetTalents
		base._worldPacket.FlushBits();
	}
}
