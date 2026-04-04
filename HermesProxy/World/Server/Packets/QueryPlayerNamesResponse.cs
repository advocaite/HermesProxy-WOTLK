using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

// Plural/batch version of name query response for 3.4.3 (SMSG_QUERY_PLAYER_NAMES_RESPONSE)
// TC343 format: uint32 count + per-entry { Result, Guid, WriteBit(hasData), WriteBit(hasUnused920), FlushBits, [Data] }
public class QueryPlayerNamesResponse : ServerPacket
{
	public class NameCacheLookupResult
	{
		public WowGuid128 Player;
		public byte Result;
		public PlayerGuidLookupData Data;
	}

	public List<NameCacheLookupResult> Players = new List<NameCacheLookupResult>();

	public QueryPlayerNamesResponse()
		: base(Opcode.SMSG_QUERY_PLAYER_NAMES_RESPONSE)
	{
	}

	public override void Write()
	{
		base._worldPacket.WriteUInt32((uint)this.Players.Count);
		foreach (NameCacheLookupResult result in this.Players)
		{
			base._worldPacket.WriteUInt8(result.Result);
			base._worldPacket.WritePackedGuid128(result.Player);
			base._worldPacket.WriteBit(result.Result == 0 && result.Data != null); // hasData
			base._worldPacket.WriteBit(false); // hasUnused920
			base._worldPacket.FlushBits();
			if (result.Result == 0 && result.Data != null)
			{
				result.Data.Write(base._worldPacket);
			}
		}
	}
}
