using System.Collections.Generic;
using Framework.Constants;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class LearnedSpellInfo
{
	public int SpellID;
	public bool IsFavorite;
	public int? Superceded;
}

public class LearnedSpells : ServerPacket
{
	public List<LearnedSpellInfo> ClientLearnedSpellData = new List<LearnedSpellInfo>();

	public uint SpecializationID;

	public bool SuppressMessaging;

	public LearnedSpells()
		: base(Opcode.SMSG_LEARNED_SPELLS, ConnectionType.Instance)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteInt32(this.ClientLearnedSpellData.Count);
		base._worldPacket.WriteUInt32(this.SpecializationID);
		base._worldPacket.WriteBit(this.SuppressMessaging);
		base._worldPacket.FlushBits();
		foreach (var info in this.ClientLearnedSpellData)
		{
			base._worldPacket.WriteInt32(info.SpellID);
			base._worldPacket.WriteBit(info.IsFavorite);
			base._worldPacket.WriteBit(false); // field_8
			base._worldPacket.WriteBit(info.Superceded.HasValue); // Superceded
			base._worldPacket.WriteBit(false); // TraitDefinitionID
			base._worldPacket.FlushBits();
			if (info.Superceded.HasValue)
				base._worldPacket.WriteInt32(info.Superceded.Value);
		}
	}
}
