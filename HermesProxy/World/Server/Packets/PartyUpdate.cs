using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class PartyUpdate : ServerPacket
{
	public GroupFlags PartyFlags;

	public byte PartyIndex;

	public GroupType PartyType;

	public WowGuid128 PartyGUID;

	public WowGuid128 LeaderGUID;

	public int MyIndex;

	public int SequenceNum;

    public byte LeaderFactionGroup;

    public List<PartyPlayerInfo> PlayerList = new List<PartyPlayerInfo>();

	public PartyLFGInfo LfgInfos;

	public PartyLootSettings LootSettings;

	public PartyDifficultySettings DifficultySettings;

	public PartyUpdate()
		: base(Opcode.SMSG_PARTY_UPDATE)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt16((ushort)this.PartyFlags);
		base._worldPacket.WriteUInt8(this.PartyIndex);
		base._worldPacket.WriteUInt8((byte)this.PartyType);
		base._worldPacket.WriteInt32(this.MyIndex);
		base._worldPacket.WritePackedGuid128(this.PartyGUID);
		base._worldPacket.WriteUInt32((uint)this.SequenceNum);
		base._worldPacket.WritePackedGuid128(this.LeaderGUID);
        if (ModernVersion.ExpansionVersion == 3)
        {
            base._worldPacket.WriteUInt8(this.LeaderFactionGroup);
        }
        base._worldPacket.WriteUInt32((uint)this.PlayerList.Count);
		base._worldPacket.WriteBit(this.LfgInfos != null);
		base._worldPacket.WriteBit(this.LootSettings != null);
		base._worldPacket.WriteBit(this.DifficultySettings != null);
		base._worldPacket.FlushBits();
		foreach (PartyPlayerInfo player in this.PlayerList)
		{
			player.Write(base._worldPacket);
		}
		if (this.LootSettings != null)
		{
			this.LootSettings.Write(base._worldPacket);
		}
		if (this.DifficultySettings != null)
		{
			this.DifficultySettings.Write(base._worldPacket);
		}
		if (this.LfgInfos != null)
		{
			this.LfgInfos.Write(base._worldPacket);
		}
	}
}
