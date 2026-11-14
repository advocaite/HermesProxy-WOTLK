using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class AuctionListMyItemsResult : ServerPacket
{
	public List<AuctionItem> Items = new List<AuctionItem>();

	public List<AuctionItem> SoldItems = new List<AuctionItem>();

	public int TotalItemsCount;

	public uint DesiredDelay = 300u;

	public bool HasMoreResults;

	public AuctionListMyItemsResult(Opcode opcode)
		: base(opcode)
	{
	}

	public override void Write()
	{
		// 3.4.3.54261 client (verified in IDA, owner and bidder list handlers): u32 count,
		// u32 TotalCount, u32 DesiredDelay, items. No SoldItems list and no HasMoreResults bit
		// (that is the retail layout); this is also what mainline HermesProxy writes.
		base._worldPacket.WriteInt32(this.Items.Count);
		base._worldPacket.WriteInt32(this.TotalItemsCount);
		base._worldPacket.WriteUInt32(this.DesiredDelay);
		foreach (AuctionItem item in this.Items)
		{
			item.Write(base._worldPacket);
		}
	}
}
