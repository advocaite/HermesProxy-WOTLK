using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class InspectResult : ServerPacket
{
	public PlayerModelDisplayInfo DisplayInfo = new PlayerModelDisplayInfo();

	public List<ushort> Glyphs = new List<ushort>();

	public List<byte> Talents = new List<byte>();

	// 3.4.3: TalentInfoUpdate (same body as SMSG_UPDATE_TALENT_DATA)
	public uint UnspentTalentPoints;

	public byte ActiveTalentGroup;

	public List<TalentGroupInfoData> TalentGroups = new List<TalentGroupInfoData>();

	// 3.4.3: TraitInspectInfo
	public int TraitLevel;

	public int TraitChrSpecializationID;

	public InspectGuildData GuildData;

	public Array<PVPBracketData> Bracket = new Array<PVPBracketData>(ModernVersion.IsVersion(3, 4, 3) ? 7 : 6, default(PVPBracketData));

	public uint? AzeriteLevel;

	public int ItemLevel;

	public uint LifetimeHK;

	public uint HonorLevel = 1u;

	public ushort TodayHK;

	public ushort YesterdayHK;

	public byte LifetimeMaxRank;

	public InspectResult()
		: base(Opcode.SMSG_INSPECT_RESULT)
	{
	}

	public override void Write()
	{
		if (ModernVersion.IsVersion(3, 4, 3))
		{
			this.WriteWotLKClassic();
			return;
		}
		this.DisplayInfo.Write(base._worldPacket);
		base._worldPacket.WriteInt32(this.Glyphs.Count);
		base._worldPacket.WriteInt32(this.Talents.Count);
		base._worldPacket.WriteInt32(this.ItemLevel);
		base._worldPacket.WriteUInt8(this.LifetimeMaxRank);
		base._worldPacket.WriteUInt16(this.TodayHK);
		base._worldPacket.WriteUInt16(this.YesterdayHK);
		base._worldPacket.WriteUInt32(this.LifetimeHK);
		base._worldPacket.WriteUInt32(this.HonorLevel);
		for (int i = 0; i < this.Glyphs.Count; i++)
		{
			base._worldPacket.WriteUInt16(this.Glyphs[i]);
		}
		for (int j = 0; j < this.Talents.Count; j++)
		{
			base._worldPacket.WriteUInt8(this.Talents[j]);
		}
		base._worldPacket.WriteBit(this.GuildData != null);
		base._worldPacket.WriteBit(this.AzeriteLevel.HasValue);
		base._worldPacket.FlushBits();
		foreach (PVPBracketData item in this.Bracket)
		{
			item.Write(base._worldPacket);
		}
		if (this.GuildData != null)
		{
			this.GuildData.Write(base._worldPacket);
		}
		if (this.AzeriteLevel.HasValue)
		{
			base._worldPacket.WriteUInt32(this.AzeriteLevel.Value);
		}
	}

	// SMSG_INSPECT_RESULT as the 3.4.3.54261 client reads it (IDA InspectResult reader 0x1406FF210):
	// PlayerModelDisplayInfo, u32 GlyphsCount, i32 ItemLevel (no Talents count), honor fields,
	// glyphs, TalentInfoUpdate (reader 0x140709E20), {bit HasGuildData, bit HasAzeriteLevel},
	// 7 x PVPBracketData, [guild], [azerite], TraitInspectInfo (TraitConfig reader 0x14071A580).
	private void WriteWotLKClassic()
	{
		this.DisplayInfo.Write(base._worldPacket);
		base._worldPacket.WriteInt32(this.Glyphs.Count);
		base._worldPacket.WriteInt32(this.ItemLevel);
		base._worldPacket.WriteUInt8(this.LifetimeMaxRank);
		base._worldPacket.WriteUInt16(this.TodayHK);
		base._worldPacket.WriteUInt16(this.YesterdayHK);
		base._worldPacket.WriteUInt32(this.LifetimeHK);
		base._worldPacket.WriteUInt32(this.HonorLevel);
		for (int i = 0; i < this.Glyphs.Count; i++)
		{
			base._worldPacket.WriteUInt16(this.Glyphs[i]);
		}
		UpdateTalentData.WriteTalentInfoUpdate(base._worldPacket, this.UnspentTalentPoints, this.ActiveTalentGroup, this.TalentGroups, isPetTalents: false);
		base._worldPacket.WriteBit(this.GuildData != null);
		base._worldPacket.WriteBit(this.AzeriteLevel.HasValue);
		base._worldPacket.FlushBits();
		for (int j = 0; j < this.Bracket.Count; j++)
		{
			PVPBracketData bracket = this.Bracket[j];
			bracket.Bracket = (byte)j;
			bracket.Write(base._worldPacket);
		}
		if (this.GuildData != null)
		{
			this.GuildData.Write(base._worldPacket);
		}
		if (this.AzeriteLevel.HasValue)
		{
			base._worldPacket.WriteUInt32(this.AzeriteLevel.Value);
		}
		// TraitInspectInfo: i32 Level, i32 ChrSpecializationID, TraitConfig. The config is
		// empty: i32 ID, i32 Type (0 = no type fields), u32 EntriesCount, 9-bit name length.
		base._worldPacket.WriteInt32(this.TraitLevel);
		base._worldPacket.WriteInt32(this.TraitChrSpecializationID);
		base._worldPacket.WriteInt32(0);
		base._worldPacket.WriteInt32(0);
		base._worldPacket.WriteUInt32(0u);
		base._worldPacket.WriteBits(0, 9);
		base._worldPacket.FlushBits();
	}
}
