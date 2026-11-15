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
		UpdateTalentData.WriteTalentInfoUpdate(base._worldPacket, this.UnspentTalentPoints, this.ActiveGroup, this.TalentGroups, this.IsPetTalents);
	}

	// TalentInfoUpdate: the body of SMSG_UPDATE_TALENT_DATA, also embedded in SMSG_INSPECT_RESULT.
	public static void WriteTalentInfoUpdate(WorldPacket data, uint unspentTalentPoints, byte activeGroup, List<TalentGroupInfoData> talentGroups, bool isPetTalents)
	{
		data.WriteUInt32(unspentTalentPoints);
		data.WriteUInt8(activeGroup);
		data.WriteUInt32((uint)talentGroups.Count);

		foreach (var group in talentGroups)
		{
			data.WriteUInt8((byte)group.Talents.Count);
			data.WriteUInt32((uint)group.Talents.Count);

			data.WriteUInt8((byte)group.GlyphIDs.Count);
			data.WriteUInt32((uint)group.GlyphIDs.Count);

			data.WriteUInt8(group.SpecID);

			foreach (var talent in group.Talents)
			{
				data.WriteUInt32(talent.TalentID);
				data.WriteUInt8(talent.Rank);
			}

			foreach (ushort glyphId in group.GlyphIDs)
			{
				data.WriteUInt16(glyphId);
			}
		}

		data.WriteBit(isPetTalents);
		data.FlushBits();
	}
}
