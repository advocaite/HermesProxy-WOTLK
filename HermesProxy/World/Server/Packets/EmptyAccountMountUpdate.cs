using System.Collections.Generic;
using Framework.Constants;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class EmptyAccountMountUpdate : ServerPacket
{
	public EmptyAccountMountUpdate()
		: base(Opcode.SMSG_ACCOUNT_MOUNT_UPDATE, ConnectionType.Instance)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteBit(bit: true);
		base._worldPacket.WriteUInt32(0u);
		base._worldPacket.FlushBits();
	}
}

public class AccountMountUpdate : ServerPacket
{
	public List<uint> MountSpellIDs = new List<uint>();

	public AccountMountUpdate()
		: base(Opcode.SMSG_ACCOUNT_MOUNT_UPDATE, ConnectionType.Instance)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteBit(true); // IsFullUpdate
		base._worldPacket.WriteUInt32((uint)this.MountSpellIDs.Count);
		foreach (uint spellId in this.MountSpellIDs)
		{
			base._worldPacket.WriteInt32((int)spellId);
			base._worldPacket.WriteBits(0u, 4); // flags: none
		}
		base._worldPacket.FlushBits();
	}
}
