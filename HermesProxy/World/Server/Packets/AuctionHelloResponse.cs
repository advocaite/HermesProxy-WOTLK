using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

internal class AuctionHelloResponse : ServerPacket
{
	public WowGuid128 Guid;

	public uint PurchasedItemDeliveryDelay;

	public uint CancelledItemDeliveryDelay;

	public uint AuctionHouseID;

	public bool OpenForBusiness = true;

	public AuctionHelloResponse()
		: base(Opcode.SMSG_AUCTION_HELLO_RESPONSE)
	{
	}

	public override void Write()
	{
		base._worldPacket.WritePackedGuid128(this.Guid);
		base._worldPacket.WriteUInt32(this.PurchasedItemDeliveryDelay);
		base._worldPacket.WriteUInt32(this.CancelledItemDeliveryDelay);
		// 3.4.3.54261 reads a third u32 (verified in IDA; the hello handler passes it into the
		// auctioneer interaction). Without it the client reads past the end, drops the hello and
		// never refreshes the lists after sell/cancel nor computes the deposit.
		base._worldPacket.WriteUInt32(this.AuctionHouseID);
		base._worldPacket.WriteBit(this.OpenForBusiness);
		base._worldPacket.FlushBits();
	}
}
