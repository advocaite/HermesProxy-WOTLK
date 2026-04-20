using System;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public struct PartyPlayerInfo
{
	public WowGuid128 GUID;

	public string Name;

	public string VoiceStateID;

	public Class ClassId;

	public GroupMemberOnlineStatus Status;

	public byte Subgroup;

	public GroupMemberFlags Flags;

	public byte RolesAssigned;

    public byte FactionGroup; // Unhandled, not sure what it's for

    public bool FromSocialQueue;

	public bool VoiceChatSilenced;

    public bool Connected;

    public void Write(WorldPacket data)
    {
        data.WriteBits(this.Name.GetByteCount(), 6);
        data.WriteBits(this.VoiceStateID.GetByteCount() + 1, 6);
        if (ModernVersion.ExpansionVersion == 3)
        {
            bool isConnected = this.Connected || this.Status != GroupMemberOnlineStatus.Offline;
            data.WriteBit(isConnected);
            data.WriteBit(this.VoiceChatSilenced);
            data.WriteBit(this.FromSocialQueue);
        }
        else
        {
            data.WriteBit(this.FromSocialQueue);
            data.WriteBit(this.VoiceChatSilenced);
        }
        data.WritePackedGuid128(this.GUID);
        if (ModernVersion.ExpansionVersion < 3)
        {
            data.WriteUInt8((byte)this.Status);
        }
        data.WriteUInt8(this.Subgroup);
        data.WriteUInt8((byte)this.Flags);
        data.WriteUInt8(this.RolesAssigned);
        data.WriteUInt8((byte)this.ClassId);
        if (ModernVersion.ExpansionVersion == 3)
        {
            data.WriteUInt8(this.FactionGroup);
        }
        data.WriteString(this.Name);
        if (!this.VoiceStateID.IsEmpty())
        {
            data.WriteString(this.VoiceStateID);
        }
    }
}
