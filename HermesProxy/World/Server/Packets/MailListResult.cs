using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class MailListResult : ServerPacket
{
	public int TotalNumRecords;

	public List<MailListEntry> Mails = new List<MailListEntry>();

	public MailListResult()
		: base(Opcode.SMSG_MAIL_LIST_RESULT)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt32((uint)this.Mails.Count);
		base._worldPacket.WriteInt32(this.TotalNumRecords);
		this.Mails.ForEach(delegate(MailListEntry p)
		{
			p.Write(base._worldPacket);
		});
	}
}
