using System.Collections.Generic;
using Framework.Constants;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class TalentInfoData
{
	public uint TalentID;
	public byte Rank;
}

public class TalentGroupInfoData
{
	public byte SpecID;
	public List<TalentInfoData> Talents = new List<TalentInfoData>();
	public List<ushort> GlyphIDs = new List<ushort>();
}

public class UpdateTalentData : ServerPacket
{
	public uint UnspentTalentPoints;
	public byte ActiveGroup;
	public bool IsPetTalents;
	public List<TalentGroupInfoData> TalentGroups = new List<TalentGroupInfoData>();

	public UpdateTalentData()
		: base(Opcode.SMSG_UPDATE_TALENT_DATA, ConnectionType.Instance)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt32(this.UnspentTalentPoints);
		base._worldPacket.WriteUInt8(this.ActiveGroup);
		base._worldPacket.WriteUInt32((uint)this.TalentGroups.Count);

		foreach (var group in this.TalentGroups)
		{
			base._worldPacket.WriteUInt8((byte)group.Talents.Count);
			base._worldPacket.WriteUInt32((uint)group.Talents.Count);

			base._worldPacket.WriteUInt8((byte)group.GlyphIDs.Count);
			base._worldPacket.WriteUInt32((uint)group.GlyphIDs.Count);

			base._worldPacket.WriteUInt8(group.SpecID);

			foreach (var talent in group.Talents)
			{
				base._worldPacket.WriteUInt32(talent.TalentID);
				base._worldPacket.WriteUInt8(talent.Rank);
			}

			foreach (ushort glyphId in group.GlyphIDs)
			{
				base._worldPacket.WriteUInt16(glyphId);
			}
		}

		base._worldPacket.WriteBit(this.IsPetTalents);
		base._worldPacket.FlushBits();
	}
}
