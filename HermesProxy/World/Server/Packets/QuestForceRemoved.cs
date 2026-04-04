using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class QuestForceRemoved : ServerPacket
{
	public uint QuestID;

	public QuestForceRemoved(uint questId)
		: base(Opcode.SMSG_QUEST_FORCE_REMOVED)
	{
		this.QuestID = questId;
	}

	public override void Write()
	{
		base._worldPacket.WriteInt32((int)this.QuestID);
	}
}
