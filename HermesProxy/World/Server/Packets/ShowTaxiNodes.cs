using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class ShowTaxiNodes : ServerPacket
{
	public ShowTaxiNodesWindowInfo WindowInfo;

	public List<byte> CanLandNodes = new List<byte>();

	public List<byte> CanUseNodes = new List<byte>();

	public ShowTaxiNodes()
		: base(Opcode.SMSG_SHOW_TAXI_NODES)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteBit(this.WindowInfo != null);
		base._worldPacket.FlushBits();
		List<byte> canLandNodes = new List<byte>(this.CanLandNodes);
		this.PadToUInt64Alignment(canLandNodes);
		base._worldPacket.WriteInt32(canLandNodes.Count / 8);
		List<byte> canUseNodes = new List<byte>(this.CanUseNodes);
		this.PadToUInt64Alignment(canUseNodes);
		base._worldPacket.WriteInt32(canUseNodes.Count / 8);
		if (this.WindowInfo != null)
		{
			base._worldPacket.WritePackedGuid128(this.WindowInfo.UnitGUID);
			base._worldPacket.WriteUInt32(this.WindowInfo.CurrentNode);
		}
		foreach (byte node in canLandNodes)
		{
			base._worldPacket.WriteUInt8(node);
		}
		foreach (byte node2 in canUseNodes)
		{
			base._worldPacket.WriteUInt8(node2);
		}
	}

	private void PadToUInt64Alignment(List<byte> nodes)
	{
		int remainder = nodes.Count % 8;
		if (remainder != 0)
		{
			for (int i = 0; i < 8 - remainder; i++)
			{
				nodes.Add(0);
			}
		}
	}
}
