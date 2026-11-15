namespace HermesProxy.World.Server.Packets;

public struct PVPBracketData
{
	public int Unused3;

	public int Rating;

	public int Rank;

	public int WeeklyPlayed;

	public int WeeklyWon;

	public int SeasonPlayed;

	public int SeasonWon;

	public int WeeklyBestRating;

	public int SeasonBestRating;

	public int PvpTierID;

	public int WeeklyBestWinPvpTierID;

	public int Unused1;

	public int Unused2;

	public int RoundsSeasonPlayed;

	public int RoundsSeasonWon;

	public int RoundsWeeklyPlayed;

	public int RoundsWeeklyWon;

	public byte Bracket;

	public bool Disqualified;

	public void Write(WorldPacket data)
	{
		data.WriteUInt8(this.Bracket);
		// 3.4.3.54261 reads 17 x i32 (IDA PVPBracketData reader 0x140715AE0), as TC 3.4.3.
		bool wotlkClassic = ModernVersion.IsVersion(3, 4, 3);
		if (wotlkClassic)
		{
			data.WriteInt32(this.Unused3);
		}
		data.WriteInt32(this.Rating);
		data.WriteInt32(this.Rank);
		data.WriteInt32(this.WeeklyPlayed);
		data.WriteInt32(this.WeeklyWon);
		data.WriteInt32(this.SeasonPlayed);
		data.WriteInt32(this.SeasonWon);
		data.WriteInt32(this.WeeklyBestRating);
		data.WriteInt32(this.SeasonBestRating);
		data.WriteInt32(this.PvpTierID);
		if (ModernVersion.AddedInVersion(9, 1, 0, 1, 14, 0, 2, 5, 2))
		{
			data.WriteInt32(this.WeeklyBestWinPvpTierID);
		}
		if (ModernVersion.AddedInVersion(9, 1, 5, 1, 14, 1, 2, 5, 3))
		{
			data.WriteInt32(this.Unused1);
			data.WriteInt32(this.Unused2);
		}
		if (wotlkClassic)
		{
			data.WriteInt32(this.RoundsSeasonPlayed);
			data.WriteInt32(this.RoundsSeasonWon);
			data.WriteInt32(this.RoundsWeeklyPlayed);
			data.WriteInt32(this.RoundsWeeklyWon);
		}
		data.WriteBit(this.Disqualified);
		data.FlushBits();
	}
}
