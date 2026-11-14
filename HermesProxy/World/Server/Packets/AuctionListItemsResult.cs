using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class AuctionListItemsResult : ServerPacket
{
	public List<AuctionItem> Items = new List<AuctionItem>();

	public int TotalItemsCount;

	public uint DesiredDelay = 300u;

	public bool HasMoreResults;

	public AuctionListItemsResult()
		: base(Opcode.SMSG_AUCTION_LIST_ITEMS_RESULT)
	{
	}

	public override void Write()
	{
		// 3.4.3.54261 client reader (verified in IDA): u32 count, u32 TotalCount, u32 DesiredDelay,
		// bit HasMoreResults, flush, items. No Unknown830 / ListType / AuctionBucketKey: those are
		// the retail layout and shifted every item by 6 bytes (the client listed nothing).
		base._worldPacket.WriteInt32(this.Items.Count);
		base._worldPacket.WriteInt32(this.TotalItemsCount);
		base._worldPacket.WriteUInt32(this.DesiredDelay);
		base._worldPacket.WriteBit(this.HasMoreResults);
		base._worldPacket.FlushBits();
		foreach (AuctionItem item in this.Items)
		{
			item.Write(base._worldPacket);
		}
	}
}
