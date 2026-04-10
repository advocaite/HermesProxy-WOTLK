using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class MailCommandResult : ServerPacket
{
	public uint MailID;

	public MailActionType Command;

	public MailErrorType ErrorCode;

	public InventoryResult BagResult;

	public uint AttachID;

	public uint QtyInInventory;

	public MailCommandResult()
		: base(Opcode.SMSG_MAIL_COMMAND_RESULT)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt64((ulong)this.MailID);
		base._worldPacket.WriteInt32((int)this.Command);
		base._worldPacket.WriteInt32((int)this.ErrorCode);
		base._worldPacket.WriteInt32((int)this.BagResult);
		base._worldPacket.WriteUInt64((ulong)this.AttachID);
		base._worldPacket.WriteInt32((int)this.QtyInInventory);
	}
}
