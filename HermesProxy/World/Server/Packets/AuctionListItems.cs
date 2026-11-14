using System.Collections.Generic;

namespace HermesProxy.World.Server.Packets;

internal class AuctionListItems : ClientPacket
{
	public uint Offset;

	public WowGuid128 Auctioneer;

	public byte MinLevel;

	public byte MaxLevel;

	public int Quality;

	public byte MaxPetLevel;

	public List<byte> KnownPets = new List<byte>();

	public string Name;

	public bool OnlyUsable;

	public bool ExactMatch;

	public List<ClassFilter> ClassFilters = new List<ClassFilter>();

	public List<AuctionSort> Sorts = new List<AuctionSort>();

	public AuctionListItems(WorldPacket packet)
		: base(packet)
	{
	}

	public override void Read()
	{
		this.Auctioneer = base._worldPacket.ReadPackedGuid128();
		this.Offset = base._worldPacket.ReadUInt32();
		this.MinLevel = base._worldPacket.ReadUInt8();
		this.MaxLevel = base._worldPacket.ReadUInt8();
		this.Quality = base._worldPacket.ReadInt32();
		byte sortCount = base._worldPacket.ReadUInt8();
		uint knownPetsCount = base._worldPacket.ReadUInt32();
		this.MaxPetLevel = base._worldPacket.ReadUInt8();
		for (int i = 0; i < knownPetsCount; i++)
		{
			this.KnownPets.Add(base._worldPacket.ReadUInt8());
		}

		// 3.4.3.54261 client writer (verified in IDA): {bit TaintedBy, 8 bits name length, flush},
		// name, {3 bits class filter count, bit OnlyUsable, bit ExactMatch, flush}, [AddOnInfo if
		// TaintedBy], class filters, sorts. The previous "peek the MSB" heuristic grouped all bits
		// before the name and read AddOnInfo after the filters, which rejected real searches.
		bool hasTaintedBy = base._worldPacket.HasBit();
		uint nameLength = base._worldPacket.ReadBits<uint>(8);
		base._worldPacket.ResetBitPos();
		this.Name = base._worldPacket.ReadString(nameLength);
		uint classFiltersCount = base._worldPacket.ReadBits<uint>(3);
		this.OnlyUsable = base._worldPacket.HasBit();
		this.ExactMatch = base._worldPacket.HasBit();
		base._worldPacket.ResetBitPos();
		if (hasTaintedBy)
		{
			uint taintNameLen = base._worldPacket.ReadBits<uint>(10);
			uint taintVersionLen = base._worldPacket.ReadBits<uint>(10);
			base._worldPacket.HasBit(); // Loaded
			base._worldPacket.HasBit(); // Disabled
			if (taintNameLen > 0)
				base._worldPacket.ReadBytes(taintNameLen);
			if (taintVersionLen > 0)
				base._worldPacket.ReadBytes(taintVersionLen);
		}

		for (int j = 0; j < classFiltersCount; j++)
		{
			ClassFilter classFilter = new ClassFilter();
			classFilter.ItemClass = base._worldPacket.ReadInt32();
			uint subClassFiltersCount = base._worldPacket.ReadBits<uint>(5);
			for (uint j2 = 0u; j2 < subClassFiltersCount; j2++)
			{
				SubClassFilter filter = new SubClassFilter
				{
					InvTypeMask = (uint)base._worldPacket.ReadUInt64(), // 3.4.3 uses uint64
					ItemSubclass = base._worldPacket.ReadInt32()
				};
				classFilter.SubClassFilters.Add(filter);
			}
			this.ClassFilters.Add(classFilter);
		}

		uint size = base._worldPacket.ReadUInt32();
		byte[] data = base._worldPacket.ReadBytes(size);
		WorldPacket sorts = new WorldPacket(base._worldPacket.GetOpcode(), data);
		for (int k = 0; k < sortCount; k++)
		{
			AuctionSort sort = new AuctionSort
			{
				Type = sorts.ReadUInt8(),
				Direction = sorts.ReadUInt8()
			};
			this.Sorts.Add(sort);
		}
	}
}
