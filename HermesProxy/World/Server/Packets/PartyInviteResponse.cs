namespace HermesProxy.World.Server.Packets;

internal class PartyInviteResponse : ClientPacket
{
	public byte? PartyIndex;

	public bool Accept;

	public byte? RolesDesired;

	public PartyInviteResponse(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		bool hasPartyIndex = base._worldPacket.HasBit();
		this.Accept = base._worldPacket.HasBit();
		bool hasRolesDesired = base._worldPacket.HasBit();

		if (hasPartyIndex)
			this.PartyIndex = base._worldPacket.ReadUInt8();

		if (hasRolesDesired)
			this.RolesDesired = base._worldPacket.ReadUInt8();
	}
}
