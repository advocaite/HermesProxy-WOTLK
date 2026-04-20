using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Framework;
using Framework.Constants;
using Framework.Cryptography;
using Framework.GameMath;
using Framework.IO;
using Framework.Logging;
using Framework.Networking;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Enums.Classic;
using HermesProxy.World.Enums.TBC;
using HermesProxy.World.Enums.Vanilla;
using HermesProxy.World.Enums.WotLK;
using HermesProxy.World.Objects;
using HermesProxy.World.Server;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Client;

public class WorldClient
{
	private uint _requestBgPlayerPosCounter = 0u;

	private Socket _clientSocket;

	private bool? _isSuccessful;

	private uint _queuePosition;

	private string _username;

	private Realm _realm;

	private LegacyWorldCrypt _worldCrypt;

	private Dictionary<Opcode, Action<WorldPacket>> _packetHandlers;

	private GlobalSessionData _globalSession;

	private Mutex _sendMutex = new Mutex();

	private Dictionary<Opcode, List<WorldPacket>> _delayedPacketsToServer;

	private Dictionary<Opcode, List<ServerPacket>> _delayedPacketsToClient;

	public GlobalSessionData Session => this._globalSession;

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_QUERY_RESPONSE)]
	private void HandleArenaTeamQueryResponse(WorldPacket packet)
	{
		uint teamId = packet.ReadUInt32();
		if (!this.GetSession().GameState.ArenaTeams.TryGetValue(teamId, out var team))
		{
			team = new ArenaTeamData();
			this.GetSession().GameState.ArenaTeams.Add(teamId, team);
		}
		team.Name = packet.ReadCString();
		team.TeamSize = packet.ReadUInt32();
		team.BackgroundColor = packet.ReadUInt32();
		team.EmblemStyle = packet.ReadUInt32();
		team.EmblemColor = packet.ReadUInt32();
		team.BorderStyle = packet.ReadUInt32();
		team.BorderColor = packet.ReadUInt32();
	}

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_STATS)]
	private void HandleArenaTeamStats(WorldPacket packet)
	{
		uint teamId = packet.ReadUInt32();
		if (!this.GetSession().GameState.ArenaTeams.TryGetValue(teamId, out var team))
		{
			team = new ArenaTeamData();
			this.GetSession().GameState.ArenaTeams.Add(teamId, team);
		}
		team.Rating = packet.ReadUInt32();
		team.WeekPlayed = packet.ReadUInt32();
		team.WeekWins = packet.ReadUInt32();
		team.SeasonPlayed = packet.ReadUInt32();
		team.SeasonWins = packet.ReadUInt32();
		team.Rank = packet.ReadUInt32();
	}

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_ROSTER)]
	private void HandleArenaTeamRoster(WorldPacket packet)
	{
		ArenaTeamRosterResponse arena = new ArenaTeamRosterResponse();
		arena.TeamId = packet.ReadUInt32();
		bool hiddenRating = false;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
		{
			packet.ReadBool();
		}
		uint count = packet.ReadUInt32();
		arena.TeamSize = packet.ReadUInt32();
		for (int i = 0; i < count; i++)
		{
			ArenaTeamMember member = default(ArenaTeamMember);
			PlayerCache cache = new PlayerCache();
			member.MemberGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			member.Online = packet.ReadBool();
			member.Name = (cache.Name = packet.ReadCString());
			member.Captain = packet.ReadInt32();
			member.Level = (cache.Level = packet.ReadUInt8());
			member.ClassId = (cache.ClassId = (Class)packet.ReadUInt8());
			this.GetSession().GameState.UpdatePlayerCache(member.MemberGUID, cache);
			member.WeekGamesPlayed = packet.ReadUInt32();
			member.WeekGamesWon = packet.ReadUInt32();
			member.SeasonGamesPlayed = packet.ReadUInt32();
			member.SeasonGamesWon = packet.ReadUInt32();
			member.PersonalRating = packet.ReadUInt32();
			if (hiddenRating)
			{
				member.dword60 = packet.ReadFloat();
				member.dword68 = packet.ReadFloat();
			}
			arena.Members.Add(member);
		}
		if (this.GetSession().GameState.ArenaTeams.TryGetValue(arena.TeamId, out var team))
		{
			arena.TeamPlayed = team.WeekPlayed;
			arena.TeamWins = team.WeekWins;
			arena.SeasonPlayed = team.SeasonPlayed;
			arena.SeasonWins = team.SeasonWins;
			arena.TeamRating = team.Rating;
			arena.PlayerRating = team.Rank;
		}
		this.SendPacketToClient(arena);
	}

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_EVENT)]
	private void HandleArenaTeamEvent(WorldPacket packet)
	{
		ArenaTeamEvent arena = new ArenaTeamEvent();
		ArenaTeamEventLegacy eventType = (ArenaTeamEventLegacy)packet.ReadUInt8();
		arena.Event = (ArenaTeamEventModern)Enum.Parse(typeof(ArenaTeamEventModern), eventType.ToString());
		byte count = packet.ReadUInt8();
		for (byte i = 0; i < count; i++)
		{
			string str = packet.ReadCString();
			switch (i)
			{
			case 0:
				arena.Param1 = str;
				break;
			case 1:
				arena.Param2 = str;
				break;
			case 2:
				arena.Param3 = str;
				break;
			}
		}
		if (packet.CanRead())
		{
			packet.ReadGuid();
		}
		this.SendPacketToClient(arena);
	}

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_COMMAND_RESULT)]
	private void HandleArenaTeamCommandResult(WorldPacket packet)
	{
		ArenaTeamCommandResult arena = new ArenaTeamCommandResult();
		arena.Action = (ArenaTeamCommandType)packet.ReadUInt32();
		arena.TeamName = packet.ReadCString();
		arena.PlayerName = packet.ReadCString();
		ArenaTeamCommandErrorLegacy errorType = (ArenaTeamCommandErrorLegacy)packet.ReadUInt32();
		arena.Error = (ArenaTeamCommandErrorModern)Enum.Parse(typeof(ArenaTeamCommandErrorModern), errorType.ToString());
		this.SendPacketToClient(arena);
	}

	[PacketHandler(Opcode.SMSG_ARENA_TEAM_INVITE)]
	private void HandleArenaTeamInvite(WorldPacket packet)
	{
		ArenaTeamInvite arena = new ArenaTeamInvite();
		arena.PlayerName = packet.ReadCString();
		arena.TeamName = packet.ReadCString();
		arena.PlayerGuid = this.GetSession().GameState.GetPlayerGuidByName(arena.PlayerName);
		if (arena.PlayerGuid == null)
		{
			arena.PlayerGuid = WowGuid128.Empty;
		}
		arena.PlayerVirtualAddress = this.GetSession().RealmId.GetAddress();
		arena.TeamGuid = WowGuid128.Create(HighGuidType703.ArenaTeam, 1uL);
		this.SendPacketToClient(arena);
	}

	[PacketHandler(Opcode.MSG_AUCTION_HELLO)]
	private void HandleAuctionHello(WorldPacket packet)
	{
		AuctionHelloResponse auction = new AuctionHelloResponse();
		auction.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = auction.Guid;
		packet.ReadUInt32(); // AuctionHouseID - not used by modern client
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			auction.OpenForBusiness = packet.ReadBool();
		}
		// Modern client requires NPC interaction open result before the AH frame will work
		ShowBank npcInteraction = new ShowBank();
		npcInteraction.Guid = auction.Guid;
		npcInteraction.InteractionType = 21; // PlayerInteractionType::Auctioneer
		this.SendPacketToClient(npcInteraction);
		this.SendPacketToClient(auction);
		WorldPacket packet2 = new WorldPacket(Opcode.CMSG_AUCTION_LIST_OWNED_ITEMS);
		packet2.WriteGuid(auction.Guid.To64());
		packet2.WriteUInt32(0u);
		this.SendPacketToServer(packet2);
	}

	private AuctionItem ReadAuctionItem(WorldPacket packet)
	{
		AuctionItem item = new AuctionItem();
		item.AuctionID = packet.ReadUInt32();
		item.Item = new ItemInstance();
		item.Item.ItemID = packet.ReadUInt32();
		byte enchantmentCount = (byte)(LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? 7 : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? 1 : 6));
		for (byte j = 0; j < enchantmentCount; j++)
		{
			ItemEnchantData enchant = new ItemEnchantData();
			enchant.Slot = j;
			enchant.ID = packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				enchant.Expiration = packet.ReadUInt32();
				enchant.Charges = packet.ReadInt32();
			}
			if (enchant.ID != 0)
			{
				item.Enchantments.Add(enchant);
			}
		}
		item.Item.RandomPropertiesID = packet.ReadUInt32();
		item.Item.RandomPropertiesSeed = packet.ReadUInt32();
		item.Count = packet.ReadInt32();
		item.Charges = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			item.Flags = packet.ReadUInt32();
		}
		item.Owner = packet.ReadGuid().To128(this.GetSession().GameState);
		item.OwnerAccountID = this.GetSession().GetGameAccountGuidForPlayer(item.Owner);
		item.MinBid = packet.ReadUInt32();
		item.MinIncrement = packet.ReadUInt32();
		item.BuyoutPrice = packet.ReadUInt32();
		item.DurationLeft = packet.ReadInt32();
		item.Bidder = packet.ReadGuid().To128(this.GetSession().GameState);
		item.BidAmount = packet.ReadUInt32();
		if (item.Item.ItemID == 0)
		{
			item.Item = null;
		}
		return item;
	}

	[PacketHandler(Opcode.SMSG_AUCTION_LIST_BIDDED_ITEMS_RESULT)]
	[PacketHandler(Opcode.SMSG_AUCTION_LIST_OWNED_ITEMS_RESULT)]
	private void HandleAuctionListMyItemsResult(WorldPacket packet)
	{
		AuctionListMyItemsResult auction = new AuctionListMyItemsResult(packet.GetUniversalOpcode(isModern: false));
		uint count = packet.ReadUInt32();
		for (uint i = 0u; i < count; i++)
		{
			AuctionItem item = this.ReadAuctionItem(packet);
			auction.Items.Add(item);
		}
		int totalCount = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_3_0_7561))
		{
			auction.DesiredDelay = packet.ReadUInt32();
		}
		auction.HasMoreResults = totalCount > (int)count;
		this.SendPacketToClient(auction);
	}

	[PacketHandler(Opcode.SMSG_AUCTION_LIST_ITEMS_RESULT)]
	private void HandleAuctionListItemsResult(WorldPacket packet)
	{
		AuctionListItemsResult auction = new AuctionListItemsResult();
		uint count = packet.ReadUInt32();
		for (uint i = 0u; i < count; i++)
		{
			AuctionItem item = this.ReadAuctionItem(packet);
			item.CensorServerSideInfo = true;
			auction.Items.Add(item);
		}
		int totalCount = packet.ReadInt32();
		auction.TotalItemsCount = totalCount;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_3_0_7561))
		{
			auction.DesiredDelay = packet.ReadUInt32();
		}
		auction.HasMoreResults = totalCount > (int)count;
		this.SendPacketToClient(auction);
	}

	[PacketHandler(Opcode.SMSG_AUCTION_COMMAND_RESULT)]
	private void HandleAuctionCommandResult(WorldPacket packet)
	{
		AuctionCommandResult auction = new AuctionCommandResult();
		auction.AuctionID = packet.ReadUInt32();
		auction.Command = (AuctionHouseAction)packet.ReadUInt32();
		auction.ErrorCode = (AuctionHouseError)packet.ReadUInt32();
		switch (auction.ErrorCode)
		{
		case AuctionHouseError.Ok:
			if (auction.Command == AuctionHouseAction.Bid)
			{
				auction.MinIncrement = packet.ReadUInt32();
			}
			break;
		case AuctionHouseError.Inventory:
			auction.BagResult = LegacyVersion.ConvertInventoryResult(packet.ReadUInt32());
			break;
		case AuctionHouseError.HigherBid:
			auction.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			auction.Money = packet.ReadUInt32();
			auction.MinIncrement = packet.ReadUInt32();
			break;
		}
		this.SendPacketToClient(auction);
	}

	[PacketHandler(Opcode.SMSG_AUCTION_OWNER_NOTIFICATION)]
	private void HandleAuctionOwnerNotification(WorldPacket packet)
	{
		AuctionOwnerNotification info = new AuctionOwnerNotification();
		info.AuctionID = packet.ReadUInt32();
		info.BidAmount = packet.ReadUInt32();
		uint minIncrement = packet.ReadUInt32();
		WowGuid buyer = packet.ReadGuid();
		info.Item.ItemID = packet.ReadUInt32();
		info.Item.RandomPropertiesID = packet.ReadUInt32();
		float mailDelay = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056)) ? 3600f : packet.ReadFloat());
		if (buyer.IsEmpty())
		{
			AuctionClosedNotification auction = new AuctionClosedNotification();
			auction.Info = info;
			auction.Sold = info.BidAmount != 0;
			auction.ProceedsMailDelay = mailDelay;
			this.SendPacketToClient(auction);
		}
		else
		{
			AuctionOwnerBidNotification auction2 = new AuctionOwnerBidNotification();
			auction2.Info = info;
			auction2.MinIncrement = minIncrement;
			auction2.Bidder = buyer.To128(this.GetSession().GameState);
			this.SendPacketToClient(auction2);
		}
	}

	[PacketHandler(Opcode.SMSG_AUCTION_BIDDER_NOTIFICATION)]
	private void HandleAuctionBidderNotification(WorldPacket packet)
	{
		AuctionBidderNotification info = new AuctionBidderNotification();
		uint auctionHouseId = packet.ReadUInt32();
		info.AuctionID = packet.ReadUInt32();
		info.Bidder = packet.ReadGuid().To128(this.GetSession().GameState);
		uint bidAmount = packet.ReadUInt32();
		uint minIncrement = packet.ReadUInt32();
		info.Item.ItemID = packet.ReadUInt32();
		info.Item.RandomPropertiesID = packet.ReadUInt32();
		if (bidAmount == 0)
		{
			AuctionWonNotification auction = new AuctionWonNotification();
			auction.Info = info;
			this.SendPacketToClient(auction);
		}
		else
		{
			AuctionOutbidNotification auction2 = new AuctionOutbidNotification();
			auction2.Info = info;
			auction2.BidAmount = bidAmount;
			auction2.MinIncrement = minIncrement;
			this.SendPacketToClient(auction2);
		}
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_LIST, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleBattlefieldListVanilla(WorldPacket packet)
	{
		BattlefieldList bglist = new BattlefieldList();
		bglist.BattlemasterGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = bglist.BattlemasterGuid;
		bglist.BattlemasterListID = GameData.GetBattlegroundIdFromMapId(packet.ReadUInt32());
		packet.ReadUInt8();
		uint instancesCount = packet.ReadUInt32();
		for (int i = 0; i < instancesCount; i++)
		{
			int instanceId = packet.ReadInt32();
			bglist.BattlefieldInstances.Add(instanceId);
		}
		this.SendPacketToClient(bglist);
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_LIST, ClientVersionBuild.V2_0_1_6180, ClientVersionBuild.V3_0_2_9056)]
	private void HandleBattlefieldListTBC(WorldPacket packet)
	{
		BattlefieldList bglist = new BattlefieldList();
		bglist.BattlemasterGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = bglist.BattlemasterGuid;
		bglist.BattlemasterListID = packet.ReadUInt32();
		packet.ReadUInt8();
		uint instancesCount = packet.ReadUInt32();
		for (int i = 0; i < instancesCount; i++)
		{
			int instanceId = packet.ReadInt32();
			bglist.BattlefieldInstances.Add(instanceId);
		}
		this.SendPacketToClient(bglist);
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_LIST, ClientVersionBuild.V3_0_2_9056)]
	private void HandleBattlefieldListWotLK(WorldPacket packet)
	{
		BattlefieldList bglist = new BattlefieldList();
		bglist.BattlemasterGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = bglist.BattlemasterGuid;
		bglist.PvpAnywhere = packet.ReadBool();
		bglist.BattlemasterListID = packet.ReadUInt32();
		bglist.MinLevel = packet.ReadUInt8();
		bglist.MaxLevel = packet.ReadUInt8();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			packet.ReadBool();
			packet.ReadInt32();
			packet.ReadInt32();
			packet.ReadInt32();
			if (packet.ReadBool())
			{
				bglist.HasRandomWinToday = packet.ReadBool();
				packet.ReadInt32();
				packet.ReadInt32();
				packet.ReadInt32();
			}
		}
		uint instancesCount = packet.ReadUInt32();
		for (int i = 0; i < instancesCount; i++)
		{
			int instanceId = packet.ReadInt32();
			bglist.BattlefieldInstances.Add(instanceId);
		}
		this.SendPacketToClient(bglist);
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_STATUS, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleBattlefieldStatusVanilla(WorldPacket packet)
	{
		BattlefieldStatusHeader hdr = new BattlefieldStatusHeader();
		hdr.Ticket.Id = 1 + packet.ReadUInt32();
		hdr.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		hdr.Ticket.Time = this.GetSession().GameState.GetBattleFieldQueueTime(hdr.Ticket.Id);
		hdr.Ticket.Type = RideType.Battlegrounds;
		uint mapId = packet.ReadUInt32();
		if (mapId != 0)
		{
			uint battlefieldListId = GameData.GetBattlegroundIdFromMapId(mapId);
			hdr.BattlefieldListIDs.Add(battlefieldListId);
			packet.ReadUInt8();
			hdr.InstanceID = packet.ReadUInt32();
			BattleGroundStatus status = (BattleGroundStatus)packet.ReadUInt32();
			switch (status)
			{
			case BattleGroundStatus.WaitQueue:
			{
				BattlefieldStatusQueued queue = new BattlefieldStatusQueued();
				queue.Hdr = hdr;
				queue.AverageWaitTime = packet.ReadUInt32();
				queue.WaitTime = packet.ReadUInt32();
				this.SendPacketToClient(queue);
				break;
			}
			case BattleGroundStatus.WaitJoin:
			{
				BattlefieldStatusNeedConfirmation confirm = new BattlefieldStatusNeedConfirmation();
				confirm.Hdr = hdr;
				confirm.Mapid = mapId;
				confirm.Timeout = packet.ReadUInt32();
				this.SendPacketToClient(confirm);
				break;
			}
			case BattleGroundStatus.InProgress:
			{
				BattlefieldStatusActive active = new BattlefieldStatusActive();
				active.Hdr = hdr;
				active.Mapid = mapId;
				active.ShutdownTimer = packet.ReadUInt32();
				active.StartTimer = packet.ReadUInt32();
				if (active.ShutdownTimer == 0)
				{
					BattlegroundInit init = new BattlegroundInit();
					init.Milliseconds = 1154756799u;
					this.SendPacketToClient(init);
				}
				this.SendPacketToClient(active);
				break;
			}
			default:
				Log.Print(LogType.Error, $"Unexpected BG status {status}.", "HandleBattlefieldStatusVanilla", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\BattleGroundHandler.cs");
				break;
			}
		}
		else
		{
			uint queuedMapId = this.GetSession().GameState.GetBattleFieldQueueType(hdr.Ticket.Id);
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) && queuedMapId == this.GetSession().GameState.CurrentMapId)
			{
				PartyUpdate bgGroup = this.GetSession().GameState.CurrentGroups[1];
				if (bgGroup != null)
				{
					PartyUpdate party = new PartyUpdate();
					party.SequenceNum = this.GetSession().GameState.GroupUpdateCounter++;
					party.PartyFlags = GroupFlags.FakeRaid | GroupFlags.Destroyed;
					party.PartyIndex = 1;
					party.PartyGUID = bgGroup.PartyGUID;
					party.LeaderGUID = WowGuid128.Empty;
					party.MyIndex = -1;
					this.GetSession().GameState.CurrentGroups[1] = null;
					this.SendPacketToClient(party);
				}
			}
			BattlefieldStatusFailed failed = new BattlefieldStatusFailed();
			failed.Ticket = hdr.Ticket;
			failed.Reason = 30;
			failed.BattlefieldListId = GameData.GetBattlegroundIdFromMapId(queuedMapId);
			this.SendPacketToClient(failed);
			this.GetSession().GameState.BattleFieldQueueTimes.Remove(hdr.Ticket.Id);
		}
		this.GetSession().GameState.StoreBattleFieldQueueType(hdr.Ticket.Id, mapId);
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_STATUS, ClientVersionBuild.V2_0_1_6180)]
	private void HandleBattlefieldStatusTBC(WorldPacket packet)
	{
		BattlefieldStatusHeader hdr = new BattlefieldStatusHeader();
		hdr.Ticket.Id = 1 + packet.ReadUInt32();
		hdr.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		hdr.Ticket.Time = this.GetSession().GameState.GetBattleFieldQueueTime(hdr.Ticket.Id);
		hdr.Ticket.Type = RideType.Battlegrounds;
		hdr.ArenaTeamSize = packet.ReadUInt8();
		packet.ReadUInt8();
		uint battlefieldListId = packet.ReadUInt32();
		packet.ReadUInt16();
		if (battlefieldListId != 0)
		{
			hdr.BattlefieldListIDs.Add(battlefieldListId);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
			{
				hdr.RangeMin = packet.ReadUInt8();
				hdr.RangeMax = packet.ReadUInt8();
			}
			hdr.InstanceID = packet.ReadUInt32();
			hdr.IsArena = packet.ReadBool();
			BattleGroundStatus status = (BattleGroundStatus)packet.ReadUInt32();
			switch (status)
			{
			case BattleGroundStatus.WaitQueue:
			{
				BattlefieldStatusQueued queue = new BattlefieldStatusQueued();
				queue.Hdr = hdr;
				queue.AverageWaitTime = packet.ReadUInt32();
				queue.WaitTime = packet.ReadUInt32();
				this.SendPacketToClient(queue);
				break;
			}
			case BattleGroundStatus.WaitJoin:
			{
				BattlefieldStatusNeedConfirmation confirm = new BattlefieldStatusNeedConfirmation();
				confirm.Hdr = hdr;
				confirm.Mapid = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_5_12213))
				{
					packet.ReadUInt64();
				}
				confirm.Timeout = packet.ReadUInt32();
				this.SendPacketToClient(confirm);
				break;
			}
			case BattleGroundStatus.InProgress:
			{
				BattlefieldStatusActive active = new BattlefieldStatusActive();
				active.Hdr = hdr;
				active.Mapid = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_5_12213))
				{
					packet.ReadUInt64();
				}
				active.ShutdownTimer = packet.ReadUInt32();
				active.StartTimer = packet.ReadUInt32();
				active.ArenaFaction = packet.ReadUInt8();
				if (active.ShutdownTimer == 0)
				{
					BattlegroundInit init = new BattlegroundInit();
					init.Milliseconds = 1154756799u;
					this.SendPacketToClient(init);
				}
				this.SendPacketToClient(active);
				break;
			}
			default:
				Log.Print(LogType.Error, $"Unexpected BG status {status}.", "HandleBattlefieldStatusTBC", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\BattleGroundHandler.cs");
				break;
			}
		}
		else
		{
			BattlefieldStatusFailed failed = new BattlefieldStatusFailed();
			failed.Ticket = hdr.Ticket;
			failed.Reason = 30;
			failed.BattlefieldListId = this.GetSession().GameState.GetBattleFieldQueueType(hdr.Ticket.Id);
			this.SendPacketToClient(failed);
			this.GetSession().GameState.BattleFieldQueueTimes.Remove(hdr.Ticket.Id);
		}
		this.GetSession().GameState.StoreBattleFieldQueueType(hdr.Ticket.Id, battlefieldListId);
	}

	[PacketHandler(Opcode.MSG_PVP_LOG_DATA, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePvPLogDataVanilla(WorldPacket packet)
	{
		PVPMatchStatisticsMessage pvp = new PVPMatchStatisticsMessage();
		if (packet.ReadBool())
		{
			pvp.Winner = packet.ReadUInt8();
		}
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			PVPMatchStatisticsMessage.PVPMatchPlayerStatistics player = new PVPMatchStatisticsMessage.PVPMatchPlayerStatistics();
			player.PlayerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			player.Rank = packet.ReadInt32();
			player.Kills = packet.ReadUInt32();
			player.Honor = new PVPMatchStatisticsMessage.HonorData();
			player.Honor.HonorKills = packet.ReadUInt32();
			player.Honor.Deaths = packet.ReadUInt32();
			player.Honor.ContributionPoints = packet.ReadUInt32();
			int statsCount = packet.ReadInt32();
			for (int j = 0; j < statsCount; j++)
			{
				player.Stats.Add(packet.ReadUInt32());
			}
			if (this.GetSession().GameState.CachedPlayers.TryGetValue(player.PlayerGUID, out var cache))
			{
				player.Sex = cache.SexId;
				player.PlayerRace = cache.RaceId;
				player.PlayerClass = cache.ClassId;
				player.Faction = GameData.IsAllianceRace(cache.RaceId);
			}
			else
			{
				player.Sex = Gender.Male;
				player.PlayerRace = Race.Human;
				player.PlayerClass = Class.Warrior;
			}
			pvp.Statistics.Add(player);
		}
		this.SendPacketToClient(pvp);
	}

	[PacketHandler(Opcode.MSG_PVP_LOG_DATA, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePvPLogDataTBC(WorldPacket packet)
	{
		PVPMatchStatisticsMessage pvp = new PVPMatchStatisticsMessage();
		if (packet.ReadBool())
		{
			pvp.ArenaTeams = new PVPMatchStatisticsMessage.ArenaTeamsInfo();
			pvp.ArenaTeams.Guids[0] = WowGuid128.Empty;
			pvp.ArenaTeams.Guids[1] = WowGuid128.Empty;
			for (int i = 0; i < 2; i++)
			{
				packet.ReadUInt32();
				packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					packet.ReadUInt32();
				}
			}
			for (int j = 0; j < 2; j++)
			{
				pvp.ArenaTeams.Names[j] = packet.ReadCString();
			}
		}
		if (packet.ReadBool())
		{
			pvp.Winner = packet.ReadUInt8();
		}
		int count = packet.ReadInt32();
		for (int k = 0; k < count; k++)
		{
			PVPMatchStatisticsMessage.PVPMatchPlayerStatistics player = new PVPMatchStatisticsMessage.PVPMatchPlayerStatistics();
			player.PlayerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			player.Kills = packet.ReadUInt32();
			if (pvp.ArenaTeams == null)
			{
				player.Honor = new PVPMatchStatisticsMessage.HonorData();
				player.Honor.HonorKills = packet.ReadUInt32();
				player.Honor.Deaths = packet.ReadUInt32();
				player.Honor.ContributionPoints = packet.ReadUInt32();
			}
			else
			{
				player.Faction = packet.ReadBool();
				pvp.PlayerCount[player.Faction ? 1 : 0]++;
			}
			player.DamageDone = packet.ReadUInt32();
			player.HealingDone = packet.ReadUInt32();
			int statsCount = packet.ReadInt32();
			for (int l = 0; l < statsCount; l++)
			{
				player.Stats.Add(packet.ReadUInt32());
			}
			if (this.GetSession().GameState.CachedPlayers.TryGetValue(player.PlayerGUID, out var cache))
			{
				player.Sex = cache.SexId;
				player.PlayerRace = cache.RaceId;
				player.PlayerClass = cache.ClassId;
				if (pvp.ArenaTeams == null)
				{
					player.Faction = GameData.IsAllianceRace(cache.RaceId);
				}
			}
			else
			{
				player.Sex = Gender.Male;
				player.PlayerRace = Race.Human;
				player.PlayerClass = Class.Warrior;
			}
			pvp.Statistics.Add(player);
		}
		this.SendPacketToClient(pvp);
	}

	private BattlegroundPlayerPosition ReadBattlegroundPlayerPosition(WorldPacket packet)
	{
		return new BattlegroundPlayerPosition
		{
			Guid = packet.ReadGuid().To128(this.GetSession().GameState),
			Pos = packet.ReadVector2()
		};
	}

	[PacketHandler(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleBattlegroundPlayerPositionsVanilla(WorldPacket packet)
	{
		this.GetSession().GameState.FlagCarrierGuids.Clear();
		BattlegroundPlayerPositions bglist = new BattlegroundPlayerPositions();
		uint teamMembersCount = packet.ReadUInt32();
		for (uint i = 0u; i < teamMembersCount; i++)
		{
			this.ReadBattlegroundPlayerPosition(packet);
		}
		if (packet.ReadBool())
		{
			BattlegroundPlayerPosition position = this.ReadBattlegroundPlayerPosition(packet);
			if (this.GetSession().GameState.IsAlliancePlayer(position.Guid))
			{
				position.IconID = 1;
				position.ArenaSlot = 3;
			}
			else
			{
				position.IconID = 2;
				position.ArenaSlot = 2;
			}
			bglist.FlagCarriers.Add(position);
			this.GetSession().GameState.FlagCarrierGuids.Add(position.Guid);
		}
		this.SendPacketToClient(bglist);
	}

	[PacketHandler(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS, ClientVersionBuild.V2_0_1_6180)]
	private void HandleBattlegroundPlayerPositionsTBC(WorldPacket packet)
	{
		BattlegroundPlayerPositions bglist = new BattlegroundPlayerPositions();
		uint teamMembersCount = packet.ReadUInt32();
		uint flagCarriersCount = packet.ReadUInt32();
		for (uint i = 0u; i < teamMembersCount; i++)
		{
			this.ReadBattlegroundPlayerPosition(packet);
		}
		this.GetSession().GameState.FlagCarrierGuids.Clear();
		for (uint i2 = 0u; i2 < flagCarriersCount; i2++)
		{
			BattlegroundPlayerPosition position = this.ReadBattlegroundPlayerPosition(packet);
			if (this.GetSession().GameState.IsAlliancePlayer(position.Guid))
			{
				position.IconID = 1;
				position.ArenaSlot = 3;
			}
			else
			{
				position.IconID = 2;
				position.ArenaSlot = 2;
			}
			bglist.FlagCarriers.Add(position);
			this.GetSession().GameState.FlagCarrierGuids.Add(position.Guid);
		}
		this.SendPacketToClient(bglist);
	}

	[PacketHandler(Opcode.SMSG_BATTLEGROUND_PLAYER_JOINED)]
	[PacketHandler(Opcode.SMSG_BATTLEGROUND_PLAYER_LEFT)]
	private void HandleBattlegroundPlayerLeftOrJoined(WorldPacket packet)
	{
		BattlegroundPlayerLeftOrJoined player = new BattlegroundPlayerLeftOrJoined(packet.GetUniversalOpcode(isModern: false));
		player.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(player);
	}

	[PacketHandler(Opcode.SMSG_AREA_SPIRIT_HEALER_TIME)]
	private void HandleAreaSpiritHealerTime(WorldPacket packet)
	{
		AreaSpiritHealerTime healer = new AreaSpiritHealerTime();
		healer.HealerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		healer.TimeLeft = packet.ReadUInt32();
		this.SendPacketToClient(healer);
	}

	[PacketHandler(Opcode.SMSG_PVP_CREDIT)]
	private void HandlePvPCredit(WorldPacket packet)
	{
		PvPCredit credit = new PvPCredit();
		credit.OriginalHonor = packet.ReadInt32();
		credit.Target = packet.ReadGuid().To128(this.GetSession().GameState);
		credit.Rank = packet.ReadUInt32();
		this.SendPacketToClient(credit);
	}

	[PacketHandler(Opcode.SMSG_PLAYER_SKINNED)]
	private void HandlePlayerSkinned(WorldPacket packet)
	{
		PlayerSkinned skinned = new PlayerSkinned();
		if (packet.CanRead())
		{
			skinned.FreeRepop = packet.ReadBool();
		}
		this.SendPacketToClient(skinned);
	}

	[PacketHandler(Opcode.SMSG_ENUM_CHARACTERS_RESULT)]
	private void HandleEnumCharactersResult(WorldPacket packet)
	{
		EnumCharactersResult charEnum = new EnumCharactersResult();
		charEnum.Success = true;
		charEnum.IsDeletedCharacters = false;
		charEnum.IsNewPlayerRestrictionSkipped = false;
		charEnum.IsNewPlayerRestricted = false;
		charEnum.IsNewPlayer = false;
		charEnum.IsAlliedRacesCreationAllowed = false;
		charEnum.DisabledClassesMask = null;
		this.GetSession().GameState.OwnCharacters.Clear();
		byte count = packet.ReadUInt8();
		for (byte i = 0; i < count; i++)
		{
			EnumCharactersResult.CharacterInfo char1 = new EnumCharactersResult.CharacterInfo();
			char1.ListPosition = i;
			PlayerCache cache = new PlayerCache();
			char1.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			char1.Name = (cache.Name = packet.ReadCString());
			char1.RaceId = (cache.RaceId = (Race)packet.ReadUInt8());
			char1.ClassId = (cache.ClassId = (Class)packet.ReadUInt8());
			char1.SexId = (cache.SexId = (Gender)packet.ReadUInt8());
			byte skin = packet.ReadUInt8();
			byte face = packet.ReadUInt8();
			byte hairStyle = packet.ReadUInt8();
			byte hairColor = packet.ReadUInt8();
			byte facialHair = packet.ReadUInt8();
			char1.Customizations = CharacterCustomizations.ConvertLegacyCustomizationsToModern(char1.RaceId, char1.SexId, skin, face, hairStyle, hairColor, facialHair);
			char1.ExperienceLevel = (cache.Level = packet.ReadUInt8());
			if (char1.ExperienceLevel > charEnum.MaxCharacterLevel)
			{
				charEnum.MaxCharacterLevel = char1.ExperienceLevel;
			}
			this.GetSession().GameState.UpdatePlayerCache(char1.Guid, cache);
			char1.ZoneId = packet.ReadUInt32();
			char1.MapId = packet.ReadUInt32();
			char1.PreloadPos = packet.ReadVector3();
			uint guildId = packet.ReadUInt32();
			this.GetSession().GameState.StorePlayerGuildId(char1.Guid, guildId);
			char1.GuildGuid = ((guildId != 0) ? WowGuid128.Create(HighGuidType703.Guild, guildId) : WowGuid128.Empty);
			char1.Flags = (CharacterFlags)packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				char1.Flags2 = packet.ReadUInt32();
			}
			char1.FirstLogin = packet.ReadUInt8() != 0;
			char1.PetCreatureDisplayId = packet.ReadUInt32();
			char1.PetExperienceLevel = packet.ReadUInt32();
			char1.PetCreatureFamilyId = packet.ReadUInt32();
			for (int j = 0; j < 19; j++)
			{
				char1.VisualItems[j].DisplayId = packet.ReadUInt32();
				char1.VisualItems[j].InvType = packet.ReadUInt8();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					char1.VisualItems[j].DisplayEnchantId = packet.ReadUInt32();
				}
			}
			int bagCount = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685)) ? 1 : 4);
			for (int k = 0; k < bagCount; k++)
			{
				char1.VisualItems[19 + k].DisplayId = packet.ReadUInt32();
				char1.VisualItems[19 + k].InvType = packet.ReadUInt8();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					char1.VisualItems[19 + k].DisplayEnchantId = packet.ReadUInt32();
				}
			}
			char1.Flags2 = 0u;
			char1.Flags3 = 0u;
			char1.Flags4 = 0u;
			char1.ProfessionIds[0] = 0u;
			char1.ProfessionIds[1] = 0u;
			char1.LastPlayedTime = (ulong)Time.UnixTime;
			char1.SpecID = 0;
			char1.Unknown703 = 0u;
			char1.LastLoginVersion = (uint)Settings.ClientBuild;
			char1.OverrideSelectScreenFileDataID = 0u;
			char1.BoostInProgress = false;
			char1.unkWod61x = 0;
			char1.ExpansionChosen = true;
			charEnum.Characters.Add(char1);
			this.GetSession().GameState.OwnCharacters.Add(new OwnCharacterInfo
			{
				AccountId = this.GetSession().GameAccountInfo.WoWAccountGuid,
				CharacterGuid = char1.Guid,
				Realm = this.GetSession().Realm,
				LastLoginUnixSec = char1.LastPlayedTime,
				Name = char1.Name,
				RaceId = char1.RaceId,
				ClassId = char1.ClassId,
				SexId = char1.SexId,
				Level = char1.ExperienceLevel
			});
		}
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(1, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(2, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(3, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(4, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(5, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(6, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(7, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(8, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		if (ModernVersion.ExpansionVersion >= 2 && LegacyVersion.ExpansionVersion >= 2)
		{
			charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(10, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
			charEnum.RaceUnlockData.Add(new EnumCharactersResult.RaceUnlock(11, hasExpansion: true, hasAchievement: false, hasHeritageArmor: false));
		}
		this.SendPacketToClient(charEnum);
	}

	[PacketHandler(Opcode.SMSG_CREATE_CHAR)]
	private void HandleCreateChar(WorldPacket packet)
	{
		byte result = packet.ReadUInt8();
		CreateChar createChar = new CreateChar();
		createChar.Guid = new WowGuid128();
		createChar.Code = ModernVersion.ConvertResponseCodesValue(result);
		this.SendPacketToClient(createChar);
	}

	[PacketHandler(Opcode.SMSG_DELETE_CHAR)]
	private void HandleDeleteChar(WorldPacket packet)
	{
		byte result = packet.ReadUInt8();
		DeleteChar deleteChar = new DeleteChar();
		deleteChar.Code = ModernVersion.ConvertResponseCodesValue(result);
		this.SendPacketToClient(deleteChar);
	}

	[PacketHandler(Opcode.SMSG_QUERY_PLAYER_NAME_RESPONSE)]
	private void HandleQueryPlayerNameResponse(WorldPacket packet)
	{
		WowGuid128 playerGuid;
		byte result = 0;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			playerGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			if (packet.ReadBool())
				result = 1;
		}
		else
		{
			playerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		}

		PlayerGuidLookupData data = new PlayerGuidLookupData();
		data.GuidActual = playerGuid;

		if (result != 0)
		{
			// Player not found - send error response
			if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_QUERY_PLAYER_NAME_RESPONSE) != 0)
			{
				QueryPlayerNameResponse response = new QueryPlayerNameResponse();
				response.Player = playerGuid;
				response.Result = 1;
				this.SendPacketToClient(response);
			}
			else
			{
				QueryPlayerNamesResponse response = new QueryPlayerNamesResponse();
				response.Players.Add(new QueryPlayerNamesResponse.NameCacheLookupResult
				{
					Player = playerGuid,
					Result = 1,
					Data = null
				});
				this.SendPacketToClient(response);
			}
			return;
		}

		PlayerCache cache = new PlayerCache();
		data.Name = (cache.Name = packet.ReadCString());
		packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			data.RaceID = (cache.RaceId = (Race)packet.ReadUInt8());
			data.Sex = (cache.SexId = (Gender)packet.ReadUInt8());
			data.ClassID = (cache.ClassId = (Class)packet.ReadUInt8());
		}
		else
		{
			data.RaceID = (cache.RaceId = (Race)packet.ReadUInt32());
			data.Sex = (cache.SexId = (Gender)packet.ReadUInt32());
			data.ClassID = (cache.ClassId = (Class)packet.ReadInt32());
		}
		if (this.GetSession().GameState.CachedPlayers.ContainsKey(playerGuid))
		{
			data.Level = this.GetSession().GameState.CachedPlayers[playerGuid].Level;
		}
		if (data.Level == 0)
		{
			data.Level = 1;
		}
		this.GetSession().GameState.UpdatePlayerCache(playerGuid, cache);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.ReadBool())
		{
			for (int i = 0; i < 5; i++)
			{
				data.DeclinedNames.name[i] = packet.ReadCString();
			}
		}
		data.IsDeleted = false;
		data.AccountID = this.GetSession().GetGameAccountGuidForPlayer(playerGuid);
		data.BnetAccountID = this.GetSession().GetBnetAccountGuidForPlayer(playerGuid);
		data.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();

		// Use plural format for 3.4.3 (singular opcode doesn't exist)
		if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_QUERY_PLAYER_NAME_RESPONSE) != 0)
		{
			QueryPlayerNameResponse response = new QueryPlayerNameResponse();
			response.Player = playerGuid;
			response.Result = 0;
			response.Data = data;
			this.SendPacketToClient(response);
		}
		else
		{
			QueryPlayerNamesResponse response = new QueryPlayerNamesResponse();
			response.Players.Add(new QueryPlayerNamesResponse.NameCacheLookupResult
			{
				Player = playerGuid,
				Result = 0,
				Data = data
			});
			this.SendPacketToClient(response);
		}
	}

	[PacketHandler(Opcode.SMSG_LOGIN_VERIFY_WORLD)]
	private void HandleLoginVerifyWorld(WorldPacket packet)
	{
		// Only reset buffer on first login, not on teleports
		// Teleports don't send a new player CreateObject so _playerObjectSent would never become true
		if (!this.GetSession().GameState.IsInWorld)
		{
			UpdateObject.ResetLoginBuffer(this.GetSession().GameState);
		}
		LoginVerifyWorld verify = new LoginVerifyWorld();
		verify.MapID = packet.ReadUInt32();
		this.GetSession().GameState.CurrentMapId = verify.MapID;
		verify.Pos.X = packet.ReadFloat();
		verify.Pos.Y = packet.ReadFloat();
		verify.Pos.Z = packet.ReadFloat();
		verify.Pos.Orientation = packet.ReadFloat();
		Log.Print(LogType.Server, $"[LoginVerifyWorld] Map={verify.MapID} Pos=({verify.Pos.X}, {verify.Pos.Y}, {verify.Pos.Z}) Orient={verify.Pos.Orientation}", "HandleLoginVerifyWorld", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\CharacterHandler.cs");
		this.SendPacketToClient(verify);
		this.GetSession().GameState.IsInWorld = true;
		if (ModernVersion.ExpansionVersion >= 3)
		{
			EmptyInitWorldStates worldStates = new EmptyInitWorldStates();
			worldStates.MapId = verify.MapID;
			worldStates.ZoneId = 0;
			worldStates.AreaId = 0;
			this.SendPacketToClient(worldStates);
		}
		WorldServerInfo info = new WorldServerInfo();
		if (verify.MapID > 1)
		{
			info.DifficultyID = 1u;
			info.InstanceGroupSize = 5u;
		}
		this.SendPacketToClient(info);
		if (ModernVersion.ExpansionVersion < 3)
		{
			SetAllTaskProgress tasks = new SetAllTaskProgress();
			this.SendPacketToClient(tasks);
		}
		InitialSetup setup = new InitialSetup();
		setup.ServerExpansionLevel = (byte)(LegacyVersion.ExpansionVersion - 1);
		this.SendPacketToClient(setup);
		LoadCUFProfiles cuf = new LoadCUFProfiles();
		cuf.Data = this.GetSession().AccountDataMgr.LoadCUFProfiles();
		this.SendPacketToClient(cuf);
		if (ModernVersion.ExpansionVersion >= 3)
		{
			this.SendPacketToClient(new EmptyAllAchievementData());
			this.SendPacketToClient(new EmptyAllAccountCriteria());
			this.SendPacketToClient(new EmptySetupCurrency());
			this.SendPacketToClient(new EmptySpellHistory());
			this.SendPacketToClient(new EmptySpellCharges());
			this.SendPacketToClient(new EmptyTalentData());
			this.SendPacketToClient(new EmptyActiveGlyphs());
			this.SendPacketToClient(new EmptyEquipmentSetList());
			this.SendPacketToClient(new EmptyAccountMountUpdate());
			this.SendPacketToClient(new EmptyAccountToyUpdate());
			this.SendPacketToClient(new EmptyAccountHeirloomUpdate());
			this.SendPacketToClient(new BattlePetJournalLockAcquired());
			PhaseShiftChange phaseShift = new PhaseShiftChange();
			phaseShift.Client = this.GetSession().GameState.CurrentPlayerGuid;
			this.SendPacketToClient(phaseShift);
		}
	}

	[PacketHandler(Opcode.SMSG_CHARACTER_LOGIN_FAILED)]
	private void HandleCharacterLoginFailed(WorldPacket packet)
	{
		CharacterLoginFailed failed = new CharacterLoginFailed();
		failed.Code = (LoginFailureReason)packet.ReadUInt8();
		this.SendPacketToClient(failed);
		this.GetSession().GameState.IsInWorld = false;
	}

	[PacketHandler(Opcode.SMSG_UPDATE_ACTION_BUTTONS)]
	private void HandleUpdateActionButtons(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			byte type = packet.ReadUInt8();
			if (type == 2)
			{
				return;
			}
		}
		List<int> buttons = new List<int>();
		int buttonCount = 120;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			buttonCount = 144;
		}
		else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			buttonCount = 132;
		}
		for (int i = 0; i < buttonCount; i++)
		{
			int packed = packet.ReadInt32();
			buttons.Add(packed);
		}
		while (buttons.Count < 180)
		{
			buttons.Add(0);
		}
		this.GetSession().GameState.ActionButtons = buttons;
		UpdateActionButtons updateButtons = new UpdateActionButtons();
		updateButtons.ActionButtons = buttons;
		updateButtons.Reason = 0;
		this.SendPacketToClient(updateButtons);
	}

	[PacketHandler(Opcode.SMSG_LOGOUT_RESPONSE)]
	private void HandleLogoutResponse(WorldPacket packet)
	{
		LogoutResponse logout = new LogoutResponse();
		logout.LogoutResult = packet.ReadInt32();
		logout.Instant = packet.ReadBool();
		this.SendPacketToClient(logout);
	}

	[PacketHandler(Opcode.SMSG_LOGOUT_COMPLETE)]
	private void HandleLogoutComplete(WorldPacket packet)
	{
		LogoutComplete logout = new LogoutComplete();
		this.SendPacketToClient(logout);
		this.GetSession().GameState = GameSessionData.CreateNewGameSessionData(this.GetSession());
		this.GetSession().InstanceSocket.CloseSocket();
		this.GetSession().InstanceSocket = null;
	}

	[PacketHandler(Opcode.SMSG_LOGOUT_CANCEL_ACK)]
	private void HandleLogoutCancelAck(WorldPacket packet)
	{
		LogoutCancelAck logout = new LogoutCancelAck();
		this.SendPacketToClient(logout);
	}

	[PacketHandler(Opcode.SMSG_LOG_XP_GAIN)]
	private void HandleLogXPGain(WorldPacket packet)
	{
		LogXPGain log = new LogXPGain();
		log.Victim = packet.ReadGuid().To128(this.GetSession().GameState);
		log.Original = packet.ReadInt32();
		log.Reason = (PlayerLogXPReason)packet.ReadUInt8();
		if (log.Reason == PlayerLogXPReason.Kill)
		{
			log.Amount = packet.ReadInt32();
			log.GroupBonus = packet.ReadFloat();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089) && packet.CanRead())
		{
			log.RAFBonus = packet.ReadUInt8();
		}
		this.SendPacketToClient(log);
	}

	[PacketHandler(Opcode.SMSG_PLAYED_TIME)]
	private void HandlePlayedTime(WorldPacket packet)
	{
		PlayedTime played = new PlayedTime();
		played.TotalTime = packet.ReadUInt32();
		played.LevelTime = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			played.TriggerEvent = packet.ReadBool();
		}
		else
		{
			played.TriggerEvent = this.GetSession().GameState.ShowPlayedTime;
		}
		this.SendPacketToClient(played);
	}

	[PacketHandler(Opcode.SMSG_LEVEL_UP_INFO)]
	private void HandleLevelUpInfo(WorldPacket packet)
	{
		LevelUpInfo info = new LevelUpInfo();
		info.Level = packet.ReadInt32();
		info.HealthDelta = packet.ReadInt32();
		for (int i = 0; i < LegacyVersion.GetPowersCount(); i++)
		{
			info.PowerDelta[i] = packet.ReadInt32();
		}
		for (int j = 0; j < 5; j++)
		{
			info.StatDelta[j] = packet.ReadInt32();
		}
		this.SendPacketToClient(info);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_COMBO_POINTS)]
	private void HandleUpdateComboPoints(WorldPacket packet)
	{
		ObjectUpdate updateData = new ObjectUpdate(this.GetSession().GameState.CurrentPlayerGuid, UpdateTypeModern.Values, this.GetSession());
		updateData.ActivePlayerData.ComboTarget = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		byte comboPoints = packet.ReadUInt8();
		sbyte powerSlot = ClassPowerTypes.GetPowerSlotForClass(this.GetSession().GameState.GetUnitClass(this.GetSession().GameState.CurrentPlayerGuid), PowerType.ComboPoints);
		if (powerSlot >= 0)
		{
			updateData.UnitData.Power[powerSlot] = comboPoints;
		}
		UpdateObject updatePacket = new UpdateObject(this.GetSession().GameState);
		updatePacket.ObjectUpdates.Add(updateData);
		this.SendPacketToClient(updatePacket);
	}

	[PacketHandler(Opcode.SMSG_INSPECT_RESULT)]
	[PacketHandler(Opcode.SMSG_INSPECT_TALENT)]
	private void HandleInspectResult(WorldPacket packet)
	{
		InspectResult inspect = new InspectResult();
		if (packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_INSPECT_RESULT)
		{
			inspect.DisplayInfo.GUID = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		else
		{
			inspect.DisplayInfo.GUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		if (!this.GetSession().GameState.CachedPlayers.TryGetValue(inspect.DisplayInfo.GUID, out var cache))
		{
			return;
		}
		inspect.DisplayInfo.Name = cache.Name;
		inspect.DisplayInfo.ClassId = cache.ClassId;
		inspect.DisplayInfo.RaceId = cache.RaceId;
		inspect.DisplayInfo.SexId = cache.SexId;
		Dictionary<int, UpdateField> updates = this.GetSession().GameState.GetCachedObjectFieldsLegacy(inspect.DisplayInfo.GUID);
		if (updates != null)
		{
			int PLAYER_VISIBLE_ITEM_1_0 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_0);
			if (PLAYER_VISIBLE_ITEM_1_0 >= 0)
			{
				byte offset = (byte)(LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 16u : 12u);
				for (byte i = 0; i < 19; i++)
				{
					if (updates.ContainsKey(PLAYER_VISIBLE_ITEM_1_0 + i * offset))
					{
						uint itemId = updates[PLAYER_VISIBLE_ITEM_1_0 + i * offset].UInt32Value;
						if (itemId != 0)
						{
							InspectItemData itemData = new InspectItemData();
							itemData.Index = i;
							itemData.Item.ItemID = itemId;
							inspect.DisplayInfo.Items.Add(itemData);
						}
					}
				}
			}
			int PLAYER_VISIBLE_ITEM_1_ENTRYID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID);
			if (PLAYER_VISIBLE_ITEM_1_ENTRYID >= 0)
			{
				int offset2 = 2;
				for (byte i2 = 0; i2 < 19; i2++)
				{
					if (updates.ContainsKey(PLAYER_VISIBLE_ITEM_1_ENTRYID + i2 * offset2))
					{
						uint itemId2 = updates[PLAYER_VISIBLE_ITEM_1_ENTRYID + i2 * offset2].UInt32Value;
						if (itemId2 != 0)
						{
							InspectItemData itemData2 = new InspectItemData();
							itemData2.Index = i2;
							itemData2.Item.ItemID = itemId2;
							inspect.DisplayInfo.Items.Add(itemData2);
						}
					}
				}
			}
			int PLAYER_GUILDID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_GUILDID);
			if (PLAYER_GUILDID >= 0 && updates.ContainsKey(PLAYER_GUILDID))
			{
				inspect.GuildData = new InspectGuildData();
				inspect.GuildData.GuildGUID = WowGuid128.Create(HighGuidType703.Guild, updates[PLAYER_GUILDID].UInt32Value);
			}
			int PLAYER_FIELD_BYTES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BYTES);
			if (PLAYER_FIELD_BYTES >= 0 && updates.ContainsKey(PLAYER_FIELD_BYTES))
			{
				inspect.LifetimeMaxRank = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 24) & 0xFF);
			}
		}
		if (packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_INSPECT_TALENT)
		{
			uint talentsCount = packet.ReadUInt32();
			for (uint i3 = 0u; i3 < talentsCount; i3++)
			{
				byte talent = packet.ReadUInt8();
				if (i3 < 25)
				{
					inspect.Talents.Add(talent);
				}
			}
		}
		this.SendPacketToClient(inspect);
	}

	[PacketHandler(Opcode.MSG_INSPECT_HONOR_STATS, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleInspectHonorStatsVanilla(WorldPacket packet)
	{
		WowGuid128 playerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		byte lifetimeHighestRank = packet.ReadUInt8();
		ushort todayHonorableKills = packet.ReadUInt16();
		ushort todayDishonorableKills = packet.ReadUInt16();
		ushort yesterdayHonorableKills = packet.ReadUInt16();
		ushort yesterdayDishonorableKills = packet.ReadUInt16();
		ushort lastWeekHonorableKills = packet.ReadUInt16();
		ushort lastWeekDishonorableKills = packet.ReadUInt16();
		ushort thisWeekHonorableKills = packet.ReadUInt16();
		ushort thisWeekDishonorableKills = packet.ReadUInt16();
		uint lifetimeHonorableKills = packet.ReadUInt32();
		uint lifetimeDishonorableKills = packet.ReadUInt32();
		uint yesterdayHonor = packet.ReadUInt32();
		uint lastWeekHonor = packet.ReadUInt32();
		uint thisWeekHonor = packet.ReadUInt32();
		uint standing = packet.ReadUInt32();
		byte rankProgress = packet.ReadUInt8();
		if (ModernVersion.ExpansionVersion == 1)
		{
			InspectHonorStatsResultClassic inspect = new InspectHonorStatsResultClassic();
			inspect.PlayerGUID = playerGuid;
			inspect.LifetimeHighestRank = lifetimeHighestRank;
			inspect.TodayHonorableKills = todayHonorableKills;
			inspect.TodayDishonorableKills = todayDishonorableKills;
			inspect.YesterdayHonorableKills = yesterdayHonorableKills;
			inspect.YesterdayDishonorableKills = yesterdayDishonorableKills;
			inspect.LastWeekHonorableKills = lastWeekHonorableKills;
			inspect.LastWeekDishonorableKills = lastWeekDishonorableKills;
			inspect.ThisWeekHonorableKills = thisWeekHonorableKills;
			inspect.ThisWeekDishonorableKills = thisWeekDishonorableKills;
			inspect.LifetimeHonorableKills = lifetimeHonorableKills;
			inspect.LifetimeDishonorableKills = lifetimeDishonorableKills;
			inspect.YesterdayHonor = yesterdayHonor;
			inspect.LastWeekHonor = lastWeekHonor;
			inspect.ThisWeekHonor = thisWeekHonor;
			inspect.Standing = standing;
			inspect.RankProgress = rankProgress;
			this.SendPacketToClient(inspect);
		}
		else
		{
			InspectHonorStatsResultTBC inspect2 = new InspectHonorStatsResultTBC();
			inspect2.PlayerGUID = playerGuid;
			inspect2.LifetimeHighestRank = lifetimeHighestRank;
			inspect2.YesterdayHonorableKills = yesterdayHonorableKills;
			inspect2.LifetimeHonorableKills = (ushort)lifetimeHonorableKills;
			this.SendPacketToClient(inspect2);
		}
	}

	[PacketHandler(Opcode.MSG_INSPECT_HONOR_STATS, ClientVersionBuild.V2_0_1_6180)]
	private void HandleInspectHonorStatsTBC(WorldPacket packet)
	{
		WowGuid128 playerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		byte lifetimeHighestRank = packet.ReadUInt8();
		ushort todayHonorableKills = packet.ReadUInt16();
		ushort yesterdayHonorableKills = packet.ReadUInt16();
		uint todayHonor = packet.ReadUInt32();
		uint yesterdayHonor = packet.ReadUInt32();
		uint lifetimeHonorableKills = packet.ReadUInt32();
		if (ModernVersion.ExpansionVersion == 1)
		{
			InspectHonorStatsResultClassic inspect = new InspectHonorStatsResultClassic();
			inspect.PlayerGUID = playerGuid;
			inspect.LifetimeHighestRank = lifetimeHighestRank;
			inspect.TodayHonorableKills = todayHonorableKills;
			inspect.YesterdayHonorableKills = yesterdayHonorableKills;
			inspect.LifetimeHonorableKills = lifetimeHonorableKills;
			inspect.YesterdayHonor = yesterdayHonor;
			inspect.LastWeekHonor = todayHonor;
			this.SendPacketToClient(inspect);
		}
		else
		{
			InspectHonorStatsResultTBC inspect2 = new InspectHonorStatsResultTBC();
			inspect2.PlayerGUID = playerGuid;
			inspect2.LifetimeHighestRank = lifetimeHighestRank;
			inspect2.YesterdayHonorableKills = yesterdayHonorableKills;
			inspect2.LifetimeHonorableKills = (ushort)lifetimeHonorableKills;
			this.SendPacketToClient(inspect2);
		}
	}

	[PacketHandler(Opcode.MSG_INSPECT_ARENA_TEAMS)]
	private void HandleInspectArenaTeams(WorldPacket packet)
	{
		InspectPvP inspect = new InspectPvP();
		inspect.PlayerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		ArenaTeamInspectData team = new ArenaTeamInspectData();
		byte slot = packet.ReadUInt8();
		uint teamId = packet.ReadUInt32();
		team.TeamGuid = WowGuid128.Create(HighGuidType703.ArenaTeam, teamId);
		team.TeamRating = packet.ReadInt32();
		team.TeamGamesPlayed = packet.ReadInt32();
		team.TeamGamesWon = packet.ReadInt32();
		team.PersonalGamesPlayed = packet.ReadInt32();
		team.PersonalRating = packet.ReadInt32();
		this.GetSession().GameState.StoreArenaTeamDataForPlayer(inspect.PlayerGUID, slot, team);
		for (byte i = 0; i < 3; i++)
		{
			inspect.ArenaTeams.Add(this.GetSession().GameState.GetArenaTeamDataForPlayer(inspect.PlayerGUID, slot));
		}
		this.SendPacketToClient(inspect);
	}

	[PacketHandler(Opcode.SMSG_CHARACTER_RENAME_RESULT)]
	private void HandleCharacterRenameResult(WorldPacket packet)
	{
		byte result = packet.ReadUInt8();
		CharacterRenameResult rename = new CharacterRenameResult();
		rename.Result = ModernVersion.ConvertResponseCodesValue(result);
		if (rename.Result == 0)
		{
			rename.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			rename.Name = packet.ReadCString();
		}
		this.SendPacketToClient(rename);
	}

	[PacketHandler(Opcode.SMSG_CHANNEL_NOTIFY)]
	private void HandleChannelNotify(WorldPacket packet)
	{
		ChatNotify type = (ChatNotify)packet.ReadUInt8();
		if (type == ChatNotify.InvalidName)
		{
			packet.ReadBytes(3u);
		}
		string channelName = packet.ReadCString();
		switch (type)
		{
		case ChatNotify.Joined:
		case ChatNotify.Left:
		case ChatNotify.PasswordChanged:
		case ChatNotify.OwnerChanged:
		case ChatNotify.AnnouncementsOn:
		case ChatNotify.AnnouncementsOff:
		case ChatNotify.ModerationOn:
		case ChatNotify.ModerationOff:
		case ChatNotify.PlayerAlreadyMember:
		case ChatNotify.Invite:
		case ChatNotify.VoiceOn:
		case ChatNotify.VoiceOff:
			packet.ReadGuid();
			break;
		case ChatNotify.YouJoined:
		{
			ChannelFlags flags = (ChannelFlags)((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? packet.ReadUInt32() : packet.ReadUInt8());
			int channelId = packet.ReadInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				packet.ReadInt32();
			}
			if (channelId == 0)
			{
				channelId = (int)GameData.GetChatChannelIdFromName(channelName);
			}
			this.GetSession().GameState.SetChannelId(channelName, channelId);
			ChannelNotifyJoined joined = new ChannelNotifyJoined();
			joined.Channel = channelName;
			joined.ChannelFlags = flags;
			joined.ChatChannelID = channelId;
			joined.ChannelGUID = WowGuid128.Create(HighGuidType703.ChatChannel, this.GetSession().GameState.CurrentMapId.Value, this.GetSession().GameState.CurrentZoneId, (ulong)channelId);
			this.SendPacketToClient(joined);
			break;
		}
		case ChatNotify.YouLeft:
		{
			ChannelNotifyLeft left = new ChannelNotifyLeft();
			left.Channel = channelName;
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				left.ChatChannelID = packet.ReadInt32();
				left.Suspended = packet.ReadBool();
			}
			else
			{
				left.ChatChannelID = this.GetSession().GameState.ChannelIds[channelName];
				left.Suspended = false;
			}
			if (string.Equals(this.GetSession().GameState.LeftChannelName, channelName) || GameData.GetChatChannelIdFromName(channelName) == 0)
			{
				this.SendPacketToClient(left);
			}
			break;
		}
		case ChatNotify.PlayerNotFound:
		case ChatNotify.ChannelOwner:
		case ChatNotify.PlayerNotBanned:
		case ChatNotify.PlayerInvited:
		case ChatNotify.PlayerInviteBanned:
			packet.ReadCString();
			break;
		case ChatNotify.ModeChange:
			packet.ReadGuid();
			packet.ReadUInt8();
			packet.ReadUInt8();
			break;
		case ChatNotify.PlayerKicked:
		case ChatNotify.PlayerBanned:
		case ChatNotify.PlayerUnbanned:
			packet.ReadGuid();
			packet.ReadGuid();
			break;
		case ChatNotify.TrialRestricted:
			packet.ReadGuid();
			break;
		case ChatNotify.WrongPassword:
		case ChatNotify.NotMember:
		case ChatNotify.NotModerator:
		case ChatNotify.NotOwner:
		case ChatNotify.Muted:
		case ChatNotify.Banned:
		case ChatNotify.InviteWrongFaction:
		case ChatNotify.WrongFaction:
		case ChatNotify.InvalidName:
		case ChatNotify.NotModerated:
		case ChatNotify.Throttled:
		case ChatNotify.NotInArea:
		case ChatNotify.NotInLfg:
			break;
		}
	}

	[PacketHandler(Opcode.SMSG_CHANNEL_LIST)]
	private void HandleChannelList(WorldPacket packet)
	{
		ChannelListResponse list = new ChannelListResponse();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			list.Display = packet.ReadBool();
		}
		else
		{
			list.Display = this.GetSession().GameState.ChannelDisplayList;
		}
		list.ChannelName = packet.ReadCString();
		list.ChannelFlags = (ChannelFlags)packet.ReadUInt8();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			ChannelListResponse.ChannelPlayer member = new ChannelListResponse.ChannelPlayer
			{
				Guid = packet.ReadGuid().To128(this.GetSession().GameState),
				VirtualRealmAddress = this.GetSession().RealmId.GetAddress(),
				Flags = packet.ReadUInt8()
			};
			list.Members.Add(member);
		}
		this.SendPacketToClient(list);
	}

	[PacketHandler(Opcode.SMSG_CHAT, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleServerChatMessageVanilla(WorldPacket packet)
	{
		ChatMessageTypeVanilla chatType = (ChatMessageTypeVanilla)packet.ReadUInt8();
		uint language = packet.ReadUInt32();
		string senderName = "";
		WowGuid128 sender = null;
		WowGuid128 receiver = null;
		string channelName = "";
		switch (chatType)
		{
		case ChatMessageTypeVanilla.MonsterEmote:
		case ChatMessageTypeVanilla.MonsterWhisper:
		case ChatMessageTypeVanilla.RaidBossEmote:
			packet.ReadUInt32();
			senderName = packet.ReadCString();
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		case ChatMessageTypeVanilla.Say:
		case ChatMessageTypeVanilla.Party:
		case ChatMessageTypeVanilla.Yell:
			sender = packet.ReadGuid().To128(this.GetSession().GameState);
			packet.ReadGuid();
			break;
		case ChatMessageTypeVanilla.MonsterSay:
		case ChatMessageTypeVanilla.MonsterYell:
			sender = packet.ReadGuid().To128(this.GetSession().GameState);
			packet.ReadUInt32();
			senderName = packet.ReadCString();
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		case ChatMessageTypeVanilla.Channel:
			channelName = packet.ReadCString();
			packet.ReadUInt32();
			sender = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		default:
			sender = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		}
		ChatMessageTypeVanilla chatMessageTypeVanilla = chatType;
		ChatMessageTypeVanilla chatMessageTypeVanilla2 = chatMessageTypeVanilla;
		if (chatMessageTypeVanilla2 - 83 <= ChatMessageTypeVanilla.Party)
		{
			Utility.Swap(ref sender, ref receiver);
		}
		uint textLength = packet.ReadUInt32();
		string text = packet.ReadString(textLength);
		ChatTag chatTag = (ChatTag)packet.ReadUInt8();
		ChatFlags chatFlags = (ChatFlags)Enum.Parse(typeof(ChatFlags), chatTag.ToString());
		if (this.Session.GameState.IgnoredPlayers.Contains(sender) && !chatFlags.HasFlag(ChatFlags.GM) && chatType != ChatMessageTypeVanilla.Ignored)
		{
			if (chatType == ChatMessageTypeVanilla.Whisper)
			{
				WorldPacket ignoreResponsePacket = new WorldPacket(Opcode.CMSG_CHAT_REPORT_IGNORED);
				ignoreResponsePacket.WriteGuid(sender.To64());
				this.SendPacketToServer(ignoreResponsePacket);
			}
			return;
		}
		string addonPrefix = "";
		if (ChatPkt.CheckAddonPrefix(this.GetSession().GameState.AddonPrefixes, ref language, ref text, ref addonPrefix))
		{
			ChatMessageTypeModern chatTypeModern = (ChatMessageTypeModern)Enum.Parse(typeof(ChatMessageTypeModern), chatType.ToString());
			ChatPkt chat = new ChatPkt(this.GetSession(), chatTypeModern, text, language, sender, senderName, receiver, "", channelName, chatFlags, addonPrefix);
			this.SendPacketToClient(chat);
		}
	}

	[PacketHandler(Opcode.SMSG_CHAT, ClientVersionBuild.V2_0_1_6180)]
	[PacketHandler(Opcode.SMSG_GM_MESSAGECHAT, ClientVersionBuild.V2_0_1_6180)]
	private void HandleServerChatMessageWotLK(WorldPacket packet)
	{
		ChatMessageTypeWotLK chatType = (ChatMessageTypeWotLK)packet.ReadUInt8();
		uint language = packet.ReadUInt32();
		WowGuid128 sender = packet.ReadGuid().To128(this.GetSession().GameState);
		string senderName = "";
		string receiverName = "";
		string channelName = "";
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_1_0_6692))
		{
			packet.ReadInt32();
		}
		WowGuid128 receiver;
		switch (chatType)
		{
		case ChatMessageTypeWotLK.Achievement:
		case ChatMessageTypeWotLK.GuildAchievement:
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		case ChatMessageTypeWotLK.WhisperForeign:
		{
			uint senderNameLength3 = packet.ReadUInt32();
			senderName = packet.ReadString(senderNameLength3);
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		}
		case ChatMessageTypeWotLK.BattlegroundNeutral:
		case ChatMessageTypeWotLK.BattlegroundAlliance:
		case ChatMessageTypeWotLK.BattlegroundHorde:
		{
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			HighGuidType highType = receiver.GetHighType();
			HighGuidType highGuidType = highType;
			if (highGuidType == HighGuidType.Transport || (uint)(highGuidType - 9) <= 3u)
			{
				uint senderNameLength2 = packet.ReadUInt32();
				senderName = packet.ReadString(senderNameLength2);
			}
			break;
		}
		case ChatMessageTypeWotLK.MonsterSay:
		case ChatMessageTypeWotLK.MonsterParty:
		case ChatMessageTypeWotLK.MonsterYell:
		case ChatMessageTypeWotLK.MonsterWhisper:
		case ChatMessageTypeWotLK.MonsterEmote:
		case ChatMessageTypeWotLK.RaidBossEmote:
		case ChatMessageTypeWotLK.RaidBossWhisper:
		case ChatMessageTypeWotLK.BattleNet:
		{
			uint senderNameLength = packet.ReadUInt32();
			senderName = packet.ReadString(senderNameLength);
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			switch (receiver.GetHighType())
			{
			case HighGuidType.Transport:
			case HighGuidType.Creature:
			case HighGuidType.Vehicle:
			case HighGuidType.GameObject:
			{
				uint receiverNameLength = packet.ReadUInt32();
				receiverName = packet.ReadString(receiverNameLength);
				break;
			}
			}
			break;
		}
		default:
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) && packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_GM_MESSAGECHAT)
			{
				uint gmNameLength = packet.ReadUInt32();
				packet.ReadString(gmNameLength);
			}
			if (chatType == ChatMessageTypeWotLK.Channel)
			{
				channelName = packet.ReadCString();
			}
			receiver = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		}
		ChatMessageTypeWotLK chatMessageTypeWotLK = chatType;
		ChatMessageTypeWotLK chatMessageTypeWotLK2 = chatMessageTypeWotLK;
		if (chatMessageTypeWotLK2 - 37 <= ChatMessageTypeWotLK.Say)
		{
			Utility.Swap(ref sender, ref receiver);
		}
		uint textLength = packet.ReadUInt32();
		string text = packet.ReadString(textLength);
		ChatFlags chatFlags = (ChatFlags)packet.ReadUInt8();
		if (LegacyVersion.InVersion(ClientVersionBuild.V2_0_1_6180, ClientVersionBuild.V3_0_2_9056) && packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_GM_MESSAGECHAT)
		{
			uint gmNameLength2 = packet.ReadUInt32();
			packet.ReadString(gmNameLength2);
		}
		uint achievementId = 0u;
		if (chatType == ChatMessageTypeWotLK.Achievement || chatType == ChatMessageTypeWotLK.GuildAchievement)
		{
			achievementId = packet.ReadUInt32();
		}
		if (this.Session.GameState.IgnoredPlayers.Contains(sender) && !chatFlags.HasFlag(ChatFlags.GM) && chatType != ChatMessageTypeWotLK.Ignored)
		{
			if (chatType == ChatMessageTypeWotLK.Whisper)
			{
				WorldPacket ignoreResponsePacket = new WorldPacket(Opcode.CMSG_CHAT_REPORT_IGNORED);
				ignoreResponsePacket.WriteGuid(sender.To64());
				ignoreResponsePacket.WriteUInt8(0);
				this.SendPacketToServer(ignoreResponsePacket);
			}
		}
		else
		{
			string addonPrefix = "";
			if (ChatPkt.CheckAddonPrefix(this.GetSession().GameState.AddonPrefixes, ref language, ref text, ref addonPrefix))
			{
				ChatMessageTypeModern chatTypeModern = (ChatMessageTypeModern)Enum.Parse(typeof(ChatMessageTypeModern), chatType.ToString());
				ChatPkt chat = new ChatPkt(this.GetSession(), chatTypeModern, text, language, sender, senderName, receiver, receiverName, channelName, chatFlags, addonPrefix, achievementId);
				this.SendPacketToClient(chat);
			}
		}
	}

	public void SendMessageChatVanilla(ChatMessageTypeVanilla type, uint lang, string msg, string channel, string to)
	{
		if (!this.HandleHermesInternalChatCommand(msg))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_MESSAGECHAT);
			packet.WriteUInt32((uint)type);
			packet.WriteUInt32(lang);
			switch (type)
			{
			case ChatMessageTypeVanilla.Channel:
				packet.WriteCString(channel);
				packet.WriteCString(msg);
				break;
			case ChatMessageTypeVanilla.Whisper:
				packet.WriteCString(to);
				packet.WriteCString(msg);
				break;
			case ChatMessageTypeVanilla.Say:
			case ChatMessageTypeVanilla.Party:
			case ChatMessageTypeVanilla.Raid:
			case ChatMessageTypeVanilla.Guild:
			case ChatMessageTypeVanilla.Officer:
			case ChatMessageTypeVanilla.Yell:
			case ChatMessageTypeVanilla.Emote:
			case ChatMessageTypeVanilla.Afk:
			case ChatMessageTypeVanilla.Dnd:
			case ChatMessageTypeVanilla.RaidLeader:
			case ChatMessageTypeVanilla.RaidWarning:
			case ChatMessageTypeVanilla.Battleground:
			case ChatMessageTypeVanilla.BattlegroundLeader:
				packet.WriteCString(msg);
				break;
			}
			this.SendPacket(packet);
		}
	}

	private bool HandleHermesInternalChatCommand(string msg)
	{
		if (msg.StartsWith("!qcomplete"))
		{
			string questIdStr = msg.Remove(0, "!qcomplete".Length);
			if (!uint.TryParse(questIdStr, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var questId))
			{
				this.GetSession().SendHermesTextMessage("Chat command invalid questId format '" + questIdStr + "'");
				return true;
			}
			this.GetSession().GameState.CurrentPlayerStorage.CompletedQuests.MarkQuestAsCompleted(questId);
			return true;
		}
		if (msg.StartsWith("!quncomplete"))
		{
			string questIdStr2 = msg.Remove(0, "!quncomplete".Length);
			if (!uint.TryParse(questIdStr2, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var questId2))
			{
				this.GetSession().SendHermesTextMessage("Chat command invalid questId format '" + questIdStr2 + "'");
				return true;
			}
			this.GetSession().GameState.CurrentPlayerStorage.CompletedQuests.MarkQuestAsNotCompleted(questId2);
			return true;
		}
		return false;
	}

	public void SendMessageChatWotLK(ChatMessageTypeWotLK type, uint lang, string msg, string channel, string to)
	{
		if (!this.HandleHermesInternalChatCommand(msg))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_MESSAGECHAT);
			packet.WriteUInt32((uint)type);
			packet.WriteUInt32(lang);
			switch (type)
			{
			case ChatMessageTypeWotLK.Channel:
				packet.WriteCString(channel);
				packet.WriteCString(msg);
				break;
			case ChatMessageTypeWotLK.Whisper:
				packet.WriteCString(to);
				packet.WriteCString(msg);
				break;
			case ChatMessageTypeWotLK.Say:
			case ChatMessageTypeWotLK.Party:
			case ChatMessageTypeWotLK.Raid:
			case ChatMessageTypeWotLK.Guild:
			case ChatMessageTypeWotLK.Officer:
			case ChatMessageTypeWotLK.Yell:
			case ChatMessageTypeWotLK.Emote:
			case ChatMessageTypeWotLK.Afk:
			case ChatMessageTypeWotLK.Dnd:
			case ChatMessageTypeWotLK.RaidLeader:
			case ChatMessageTypeWotLK.RaidWarning:
			case ChatMessageTypeWotLK.Battleground:
			case ChatMessageTypeWotLK.BattlegroundLeader:
			case ChatMessageTypeWotLK.PartyLeader:
				packet.WriteCString(msg);
				break;
			}
			this.SendPacket(packet);
		}
	}

	[PacketHandler(Opcode.SMSG_EMOTE)]
	private void HandleEmote(WorldPacket packet)
	{
		EmoteMessage emote = new EmoteMessage();
		emote.EmoteID = packet.ReadUInt32();
		emote.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(emote);
	}

	[PacketHandler(Opcode.SMSG_TEXT_EMOTE)]
	private void HandleTextEmote(WorldPacket packet)
	{
		STextEmote emote = new STextEmote();
		emote.SourceGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		emote.SourceAccountGUID = this.GetSession().GetGameAccountGuidForPlayer(emote.SourceGUID);
		emote.EmoteID = packet.ReadInt32();
		emote.SoundIndex = packet.ReadInt32();
		uint nameLength = packet.ReadUInt32();
		string targetName = packet.ReadString(nameLength);
		WowGuid128 targetGuid = this.GetSession().GameState.GetPlayerGuidByName(targetName);
		emote.TargetGUID = ((targetGuid != null) ? targetGuid : WowGuid128.Empty);
		this.SendPacketToClient(emote);
	}

	[PacketHandler(Opcode.SMSG_PRINT_NOTIFICATION)]
	private void HandlePrintNotification(WorldPacket packet)
	{
		PrintNotification notify = new PrintNotification();
		notify.NotifyText = packet.ReadCString();
		this.SendPacketToClient(notify);
	}

	[PacketHandler(Opcode.SMSG_CHAT_PLAYER_NOTFOUND)]
	private void HandleChatPlayerNotFound(WorldPacket packet)
	{
		ChatPlayerNotfound error = new ChatPlayerNotfound();
		error.Name = packet.ReadCString();
		this.SendPacketToClient(error);
	}

	[PacketHandler(Opcode.SMSG_DEFENSE_MESSAGE)]
	private void HandleDefenseMessage(WorldPacket packet)
	{
		DefenseMessage message = new DefenseMessage();
		message.ZoneID = packet.ReadUInt32();
		packet.ReadUInt32();
		message.MessageText = packet.ReadCString();
		this.SendPacketToClient(message);
	}

	[PacketHandler(Opcode.SMSG_CHAT_SERVER_MESSAGE)]
	private void HandleChatServerMessage(WorldPacket packet)
	{
		ChatServerMessage message = new ChatServerMessage();
		message.MessageID = packet.ReadInt32();
		message.StringParam = packet.ReadCString();
		this.SendPacketToClient(message);
	}

	public void SendChatJoinChannel(int channelId, string channelName, string password)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAT_JOIN_CHANNEL);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteInt32(channelId);
			packet.WriteUInt8(0);
			packet.WriteUInt8(0);
		}
		packet.WriteCString(channelName);
		packet.WriteCString(password);
		this.SendPacketToServer(packet);
	}

	public void SendChatLeaveChannel(int channelId, string channelName)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAT_LEAVE_CHANNEL);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteInt32(channelId);
		}
		packet.WriteCString(channelName);
		this.SendPacketToServer(packet);
	}

	private bool IsLocalPlayerOrPet(WowGuid128 guid)
	{
		if (guid == null) return false;
		return guid == this.GetSession().GameState.CurrentPlayerGuid ||
		       guid == this.GetSession().GameState.CurrentPetGuid;
	}

	private bool IsLocalPlayerInvolved(WowGuid128 a, WowGuid128 b)
	{
		return IsLocalPlayerOrPet(a) || IsLocalPlayerOrPet(b);
	}

	[PacketHandler(Opcode.SMSG_ATTACK_START)]
	private void HandleAttackStart(WorldPacket packet)
	{
		SAttackStart attack = new SAttackStart();
		attack.Attacker = packet.ReadGuid().To128(this.GetSession().GameState);
		attack.Victim = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_ATTACK_STOP)]
	private void HandleAttackStop(WorldPacket packet)
	{
		SAttackStop attack = new SAttackStop();
		if (packet.CanRead())
		{
			attack.Attacker = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		if (packet.CanRead())
		{
			attack.Victim = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		if (packet.CanRead())
		{
			attack.NowDead = packet.ReadUInt32() != 0;
		}
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_HIGHEST_THREAT_UPDATE)]
	private void HandleHighestThreatUpdate(WorldPacket packet)
	{
		// Consume packet to prevent "No handler" warning — client doesn't need this
		WowGuid128 unitGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		Log.Print(LogType.Debug, $"[Combat] HIGHEST_THREAT_UPDATE unit={unitGuid} (consumed, not forwarded)", "HandleHighestThreatUpdate", "");
	}

	[PacketHandler(Opcode.SMSG_THREAT_CLEAR)]
	private void HandleThreatClear(WorldPacket packet)
	{
		// Consume packet to prevent "No handler" warning — client doesn't need this
		WowGuid128 unitGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		Log.Print(LogType.Debug, $"[Combat] THREAT_CLEAR unit={unitGuid} (consumed, not forwarded)", "HandleThreatClear", "");
	}

	[PacketHandler(Opcode.SMSG_THREAT_UPDATE)]
	private void HandleThreatUpdate(WorldPacket packet)
	{
		// Consume packet to prevent "No handler" warning — client doesn't need this
		WowGuid128 unitGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		Log.Print(LogType.Debug, $"[Combat] THREAT_UPDATE unit={unitGuid} (consumed, not forwarded)", "HandleThreatUpdate", "");
	}

	[PacketHandler(Opcode.SMSG_THREAT_REMOVE)]
	private void HandleThreatRemove(WorldPacket packet)
	{
		ThreatRemove threat = new ThreatRemove();
		threat.UnitGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		threat.AboutGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(threat);
	}

	[PacketHandler(Opcode.SMSG_HEALTH_UPDATE)]
	private void HandleHealthUpdate(WorldPacket packet)
	{
		HealthUpdate health = new HealthUpdate();
		health.Guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		health.Health = packet.ReadUInt32();
		this.SendPacketToClient(health);
	}

	[PacketHandler(Opcode.SMSG_PET_ACTION_FEEDBACK)]
	private void HandlePetActionFeedback(WorldPacket packet)
	{
		PetActionFeedback feedback = new PetActionFeedback();
		feedback.Response = packet.ReadUInt8();
		feedback.SpellID = 0;
		this.SendPacketToClient(feedback);
	}

	[PacketHandler(Opcode.SMSG_PET_TAME_FAILURE)]
	private void HandlePetTameFailure(WorldPacket packet)
	{
		PetTameFailure tame = new PetTameFailure();
		tame.Result = packet.ReadUInt8();
		this.SendPacketToClient(tame);
	}

	[PacketHandler(Opcode.SMSG_PET_GUIDS)]
	private void HandlePetGuids(WorldPacket packet)
	{
		PetGuids guids = new PetGuids();
		uint count = packet.ReadUInt32();
		for (uint i = 0; i < count; i++)
		{
			guids.Guids.Add(packet.ReadGuid().To128(this.GetSession().GameState));
		}
		this.SendPacketToClient(guids);
	}

	[PacketHandler(Opcode.SMSG_TITLE_EARNED)]
	private void HandleTitleEarned(WorldPacket packet)
	{
		uint index = packet.ReadUInt32();
		uint earned = packet.ReadUInt32();
		TitleEarned title = new TitleEarned(earned != 0 ? Opcode.SMSG_TITLE_EARNED : Opcode.SMSG_TITLE_LOST);
		title.Index = index;
		this.SendPacketToClient(title);
	}

	[PacketHandler(Opcode.SMSG_MOUNT_RESULT)]
	private void HandleMountResult(WorldPacket packet)
	{
		MountResult mount = new MountResult();
		mount.Result = packet.ReadInt32();
		this.SendPacketToClient(mount);
	}

	[PacketHandler(Opcode.SMSG_ACHIEVEMENT_DELETED)]
	private void HandleAchievementDeleted(WorldPacket packet)
	{
		AchievementDeleted deleted = new AchievementDeleted();
		deleted.AchievementID = packet.ReadUInt32();
		deleted.Immunities = 0;
		this.SendPacketToClient(deleted);
	}

	[PacketHandler(Opcode.SMSG_CRITERIA_DELETED)]
	private void HandleCriteriaDeleted(WorldPacket packet)
	{
		CriteriaDeleted deleted = new CriteriaDeleted();
		deleted.CriteriaID = packet.ReadUInt32();
		this.SendPacketToClient(deleted);
	}

	[PacketHandler(Opcode.SMSG_GROUP_DESTROYED)]
	private void HandleGroupDestroyed(WorldPacket packet)
	{
		GroupDestroyed destroyed = new GroupDestroyed();
		this.SendPacketToClient(destroyed);
	}

	[PacketHandler(Opcode.SMSG_ON_CANCEL_EXPECTED_RIDE_VEHICLE_AURA)]
	private void HandleOnCancelExpectedRideVehicleAura(WorldPacket packet)
	{
		OnCancelExpectedRideVehicleAura cancel = new OnCancelExpectedRideVehicleAura();
		this.SendPacketToClient(cancel);
	}

	[PacketHandler(Opcode.SMSG_OVERRIDE_LIGHT)]
	private void HandleOverrideLight(WorldPacket packet)
	{
		OverrideLight light = new OverrideLight();
		light.AreaLightID = packet.ReadInt32();
		light.OverrideLightID = packet.ReadInt32();
		light.TransitionMilliseconds = packet.ReadInt32();
		this.SendPacketToClient(light);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_ACCOUNT_DATA)]
	private void HandleUpdateAccountData(WorldPacket packet)
	{
		WowGuid64 guid = packet.ReadGuid();
		uint type = packet.ReadUInt32();
		uint time = packet.ReadUInt32();
		uint size = packet.ReadUInt32();
		byte[] compressedData = null;
		if (packet.CanRead())
		{
			compressedData = packet.ReadToEnd();
		}
		AccountData data = new AccountData();
		data.Guid = guid.To128(this.GetSession().GameState);
		data.Type = type;
		data.Timestamp = time;
		data.UncompressedSize = size;
		data.CompressedData = compressedData;
		UpdateAccountData update = new UpdateAccountData(data);
		this.SendPacketToClient(update);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_LAST_INSTANCE)]
	private void HandleUpdateLastInstance(WorldPacket packet)
	{
		UpdateLastInstance update = new UpdateLastInstance();
		update.MapID = packet.ReadUInt32();
		this.SendPacketToClient(update);
	}

	[PacketHandler(Opcode.SMSG_QUEST_POI_QUERY_RESPONSE)]
	private void HandleQuestPOIQueryResponse(WorldPacket packet)
	{
		QuestPOIQueryResponse response = new QuestPOIQueryResponse();
		uint questCount = packet.ReadUInt32();
		for (uint q = 0; q < questCount; q++)
		{
			QuestPOIData questData = new QuestPOIData();
			questData.QuestID = (int)packet.ReadUInt32();
			uint poiCount = packet.ReadUInt32();
			for (uint p = 0; p < poiCount; p++)
			{
				QuestPOIBlobData blob = new QuestPOIBlobData();
				blob.BlobIndex = (int)packet.ReadUInt32();
				blob.ObjectiveIndex = packet.ReadInt32();
				blob.MapID = (int)packet.ReadUInt32();
				blob.UiMapID = (int)packet.ReadUInt32(); // areaId in legacy
				blob.Priority = 0;
				blob.Flags = (int)packet.ReadUInt32(); // floorId in legacy
				blob.WorldEffectID = 0;
				blob.PlayerConditionID = 0;
				blob.NavigationPlayerConditionID = 0;
				blob.SpawnTrackingID = 0;
				// Look up the QuestObjectiveID from our cached objectives
				// The modern client needs this to link POI blobs to specific objectives
				QuestTemplate poiQuest = GameData.GetQuestTemplate((uint)questData.QuestID);
				if (poiQuest != null)
				{
					QuestObjective matchedObj = poiQuest.Objectives.Find(o => o.StorageIndex == blob.ObjectiveIndex);
					if (matchedObj != null)
					{
						blob.QuestObjectiveID = (int)matchedObj.Id;
						blob.QuestObjectID = matchedObj.ObjectID;
					}
				}
				else
				{
					blob.QuestObjectiveID = 0;
					blob.QuestObjectID = 0;
				}
				packet.ReadUInt32(); // Unk3
				packet.ReadUInt32(); // Unk4
				uint pointCount = packet.ReadUInt32();
				for (uint pt = 0; pt < pointCount; pt++)
				{
					QuestPOIBlobPoint point = new QuestPOIBlobPoint();
					point.X = (short)packet.ReadInt32();
					point.Y = (short)packet.ReadInt32();
					point.Z = 0;
					blob.Points.Add(point);
				}
				questData.Blobs.Add(blob);
			}
			response.QuestPOIDataStats.Add(questData);
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_GM_TICKET_GET_SYSTEM_STATUS)]
	private void HandleGMTicketSystemStatus(WorldPacket packet)
	{
		GMTicketSystemStatus status = new GMTicketSystemStatus();
		status.Status = (int)packet.ReadUInt32();
		this.SendPacketToClient(status);
	}

	[PacketHandler(Opcode.SMSG_LFG_DISABLED)]
	private void HandleLfgDisabled(WorldPacket packet)
	{
		LfgDisabled disabled = new LfgDisabled();
		this.SendPacketToClient(disabled);
	}

	[PacketHandler(Opcode.SMSG_LFG_OFFER_CONTINUE)]
	private void HandleLfgOfferContinue(WorldPacket packet)
	{
		LfgOfferContinue offer = new LfgOfferContinue();
		offer.Slot = packet.ReadUInt32();
		this.SendPacketToClient(offer);
	}

	[PacketHandler(Opcode.SMSG_LFG_PLAYER_REWARD)]
	private void HandleLfgPlayerReward(WorldPacket packet)
	{
		LfgPlayerReward reward = new LfgPlayerReward();
		reward.QueuedSlot = packet.ReadUInt32(); // rdungeonEntry
		reward.ActualSlot = packet.ReadUInt32(); // sdungeonEntry
		byte done = packet.ReadUInt8();
		packet.ReadUInt32(); // always 1
		reward.RewardMoney = (int)packet.ReadUInt32();
		reward.AddedXP = (int)packet.ReadUInt32();
		packet.ReadUInt32(); // unknown
		packet.ReadUInt32(); // unknown
		byte itemNum = packet.ReadUInt8();
		for (byte i = 0; i < itemNum; i++)
		{
			LfgPlayerRewardItem item = new LfgPlayerRewardItem();
			item.ItemID = packet.ReadUInt32();
			packet.ReadUInt32(); // displayId
			item.Quantity = packet.ReadUInt32();
			item.IsCurrency = false;
			item.BonusCurrency = 0;
			reward.Rewards.Add(item);
		}
		this.SendPacketToClient(reward);
	}

	[PacketHandler(Opcode.SMSG_LFG_ROLE_CHECK_UPDATE)]
	private void HandleLfgRoleCheckUpdate(WorldPacket packet)
	{
		LfgRoleCheckUpdate roleCheck = new LfgRoleCheckUpdate();
		roleCheck.PartyIndex = 0;
		roleCheck.RoleCheckStatus = (byte)packet.ReadUInt32(); // state
		roleCheck.IsBeginning = packet.ReadBool();
		roleCheck.IsRequeue = false;
		roleCheck.GroupFinderActivityID = 0;
		byte dungeonCount = packet.ReadUInt8();
		for (byte i = 0; i < dungeonCount; i++)
		{
			roleCheck.JoinSlots.Add(packet.ReadUInt32());
		}
		byte memberCount = packet.ReadUInt8();
		for (byte i = 0; i < memberCount; i++)
		{
			LfgRoleCheckMember member = new LfgRoleCheckMember();
			member.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			bool ready = packet.ReadBool();
			member.RolesDesired = packet.ReadUInt32();
			member.Level = packet.ReadUInt8();
			member.RoleCheckComplete = ready;
			roleCheck.Members.Add(member);
		}
		this.SendPacketToClient(roleCheck);
	}

	[PacketHandler(Opcode.SMSG_LFG_PARTY_INFO)]
	private void HandleLfgPartyInfo(WorldPacket packet)
	{
		LfgPartyInfo partyInfo = new LfgPartyInfo();
		byte playerCount = packet.ReadUInt8();
		for (byte i = 0; i < playerCount; i++)
		{
			LfgBlackListEntry entry = new LfgBlackListEntry();
			entry.PlayerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
			uint lockCount = packet.ReadUInt32();
			for (uint j = 0; j < lockCount; j++)
			{
				LfgLockInfoData lockInfo = new LfgLockInfoData();
				lockInfo.Slot = packet.ReadUInt32(); // dungeonId
				lockInfo.LockStatus = packet.ReadUInt32(); // lockStatus
				lockInfo.SubReason1 = 0;
				lockInfo.SubReason2 = 0;
				entry.Locks.Add(lockInfo);
			}
			partyInfo.Players.Add(entry);
		}
		this.SendPacketToClient(partyInfo);
	}

	[PacketHandler(Opcode.SMSG_BATTLEFIELD_STATUS_QUEUED)]
	private void HandleBattlefieldStatusQueued(WorldPacket packet)
	{
		// Legacy sends unified SMSG_BATTLEFIELD_STATUS with StatusID field
		uint queueSlot = packet.ReadUInt32();
		byte arenaType = packet.ReadUInt8();
		packet.ReadUInt8(); // isRatedArena flag
		uint bgTypeId = packet.ReadUInt32();
		packet.ReadUInt16(); // unk
		byte minLevel = packet.ReadUInt8();
		byte maxLevel = packet.ReadUInt8();
		uint clientInstanceId = packet.ReadUInt32();
		packet.ReadUInt8(); // isRated
		uint statusId = packet.ReadUInt32();

		if (statusId == 1) // STATUS_WAIT_QUEUE
		{
			uint avgWaitTime = packet.ReadUInt32();
			uint waitTime = packet.ReadUInt32();

			BattlefieldStatusQueued queued = new BattlefieldStatusQueued();
			queued.Hdr.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
			queued.Hdr.Ticket.Id = queueSlot;
			queued.Hdr.Ticket.Type = RideType.Battlegrounds;
			queued.Hdr.Ticket.Time = (long)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			queued.Hdr.BattlefieldListIDs.Add(bgTypeId);
			queued.Hdr.RangeMin = minLevel;
			queued.Hdr.RangeMax = maxLevel;
			queued.Hdr.ArenaTeamSize = arenaType;
			queued.Hdr.InstanceID = clientInstanceId;
			queued.AverageWaitTime = avgWaitTime;
			queued.WaitTime = waitTime;
			queued.AsGroup = false;
			queued.EligibleForMatchmaking = true;
			queued.SuspendedQueue = false;
			this.SendPacketToClient(queued);
		}
	}

	[PacketHandler(Opcode.SMSG_ATTACKER_STATE_UPDATE)]
	private void HandleAttackerStateUpdate(WorldPacket packet)
	{
		AttackerStateUpdate attack = new AttackerStateUpdate();
		uint hitInfo = packet.ReadUInt32();
		attack.HitInfo = LegacyVersion.ConvertHitInfoFlags(hitInfo);
		attack.AttackerGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		attack.VictimGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		attack.Damage = packet.ReadInt32();
		attack.OriginalDamage = attack.Damage;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_3_9183))
		{
			attack.OverDamage = packet.ReadInt32();
		}
		else
		{
			attack.OverDamage = -1;
		}
		byte subDamageCount = packet.ReadUInt8();
		for (int i = 0; i < subDamageCount; i++)
		{
			SubDamage subDmg = new SubDamage();
			uint school = packet.ReadUInt32();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				school = (uint)(1 << (int)(byte)school);
			}
			subDmg.SchoolMask = school;
			subDmg.FloatDamage = packet.ReadFloat();
			subDmg.IntDamage = packet.ReadInt32();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_3_9183) || hitInfo.HasAnyFlag(HitInfo.FullAbsorb | HitInfo.PartialAbsorb))
			{
				subDmg.Absorbed = packet.ReadInt32();
			}
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_3_9183) || hitInfo.HasAnyFlag(HitInfo.FullResist | HitInfo.PartialResist))
			{
				subDmg.Resisted = packet.ReadInt32();
			}
			attack.SubDmg.Add(subDmg);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_3_9183))
		{
			attack.VictimState = packet.ReadUInt8();
		}
		else
		{
			attack.VictimState = (byte)packet.ReadUInt32();
		}
		attack.AttackerState = packet.ReadInt32();
		attack.MeleeSpellID = packet.ReadUInt32();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_3_9183) || hitInfo.HasAnyFlag(HitInfo.Block))
		{
			attack.BlockAmount = packet.ReadInt32();
		}
		if (hitInfo.HasAnyFlag(HitInfo.RageGain))
		{
			attack.RageGained = packet.ReadInt32();
		}
		if (hitInfo.HasAnyFlag(HitInfo.Unk0))
		{
			attack.UnkState = default(UnkAttackerState);
			attack.UnkState.State1 = packet.ReadUInt32();
			attack.UnkState.State2 = packet.ReadFloat();
			attack.UnkState.State3 = packet.ReadFloat();
			attack.UnkState.State4 = packet.ReadFloat();
			attack.UnkState.State5 = packet.ReadFloat();
			attack.UnkState.State6 = packet.ReadFloat();
			attack.UnkState.State7 = packet.ReadFloat();
			attack.UnkState.State8 = packet.ReadFloat();
			attack.UnkState.State9 = packet.ReadFloat();
			attack.UnkState.State10 = packet.ReadFloat();
			attack.UnkState.State11 = packet.ReadFloat();
			attack.UnkState.State12 = packet.ReadUInt32();
			packet.ReadUInt32();
			packet.ReadUInt32();
		}
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_ATTACKSWING_NOTINRANGE)]
	private void HandleAttackSwingNotInRange(WorldPacket packet)
	{
		// Don't forward "not in range" if player has a ranged weapon equipped
		// The modern client needs to stay in attack state to initiate Auto Shot
		if (this.GetSession().GameState.CurrentPlayerGuid != null)
		{
			var visibleItems = this.GetSession().GameState.GetCachedObjectFieldsLegacy(this.GetSession().GameState.CurrentPlayerGuid);
			int PLAYER_VISIBLE_ITEM_1_ENTRYID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID);
			if (PLAYER_VISIBLE_ITEM_1_ENTRYID >= 0)
			{
				int offset = LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? 2 : (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 16 : 12);
				int rangedIdx = PLAYER_VISIBLE_ITEM_1_ENTRYID + 17 * offset;
				if (visibleItems != null && visibleItems.ContainsKey(rangedIdx) && visibleItems[rangedIdx].UInt32Value != 0)
				{
					Log.Print(LogType.Debug, "[Combat] Suppressing ATTACKSWING_NOTINRANGE - player has ranged weapon equipped", "HandleAttackSwingNotInRange", "");
					return;
				}
			}
		}
		AttackSwingError attack = new AttackSwingError();
		attack.Reason = AttackSwingErr.NotInRange;
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_ATTACKSWING_BADFACING)]
	private void HandleAttackSwingBadFacing(WorldPacket packet)
	{
		AttackSwingError attack = new AttackSwingError();
		attack.Reason = AttackSwingErr.BadFacing;
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_ATTACKSWING_DEADTARGET)]
	private void HandleAttackSwingDeadTarget(WorldPacket packet)
	{
		AttackSwingError attack = new AttackSwingError();
		attack.Reason = AttackSwingErr.DeadTarget;
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_ATTACKSWING_CANT_ATTACK)]
	private void HandleAttackSwingCantAttack(WorldPacket packet)
	{
		AttackSwingError attack = new AttackSwingError();
		attack.Reason = AttackSwingErr.CantAttack;
		this.SendPacketToClient(attack);
	}

	[PacketHandler(Opcode.SMSG_CANCEL_COMBAT)]
	private void HandleCancelCombat(WorldPacket packet)
	{
		CancelCombat combat = new CancelCombat();
		this.SendPacketToClient(combat);
	}

	[PacketHandler(Opcode.SMSG_AI_REACTION)]
	private void HandleAIReaction(WorldPacket packet)
	{
		AIReaction reaction = new AIReaction();
		reaction.UnitGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		reaction.Reaction = packet.ReadUInt32();
		this.SendPacketToClient(reaction);
	}

	[PacketHandler(Opcode.SMSG_PARTY_KILL_LOG)]
	private void HandlePartyKillLog(WorldPacket packet)
	{
		PartyKillLog log = new PartyKillLog();
		log.Player = packet.ReadGuid().To128(this.GetSession().GameState);
		log.Victim = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(log);
	}

	[PacketHandler(Opcode.SMSG_DUEL_REQUESTED)]
	private void HandleDuelRequested(WorldPacket packet)
	{
		DuelRequested duel = new DuelRequested();
		duel.ArbiterGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		duel.RequestedByGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		duel.RequestedByWowAccount = this.GetSession().GetGameAccountGuidForPlayer(duel.RequestedByGUID);
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_DUEL_COUNTDOWN)]
	private void HandleDuelCountdown(WorldPacket packet)
	{
		DuelCountdown duel = new DuelCountdown();
		duel.Countdown = packet.ReadUInt32();
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_DUEL_COMPLETE)]
	private void HandleDuelComplete(WorldPacket packet)
	{
		DuelComplete duel = new DuelComplete();
		duel.Started = packet.ReadBool();
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_DUEL_WINNER)]
	private void HandleDuelWinner(WorldPacket packet)
	{
		DuelWinner duel = new DuelWinner();
		duel.Fled = packet.ReadBool();
		duel.BeatenName = packet.ReadCString();
		duel.WinnerName = packet.ReadCString();
		duel.BeatenVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		duel.WinnerVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_DUEL_IN_BOUNDS)]
	private void HandleDuelInBounds(WorldPacket packet)
	{
		DuelInBounds duel = new DuelInBounds();
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_DUEL_OUT_OF_BOUNDS)]
	private void HandleDuelOutOfBounds(WorldPacket packet)
	{
		DuelOutOfBounds duel = new DuelOutOfBounds();
		this.SendPacketToClient(duel);
	}

	[PacketHandler(Opcode.SMSG_GAME_OBJECT_DESPAWN)]
	private void HandleGameObjectDespawn(WorldPacket packet)
	{
		WowGuid64 guid = packet.ReadGuid();
		GameObjectDespawn despawn = new GameObjectDespawn();
		despawn.ObjectGUID = guid.To128(this.GetSession().GameState);
		this.SendPacketToClient(despawn);
		this.GetSession().GameState.DespawnedGameObjects.Add(guid);
	}

	[PacketHandler(Opcode.SMSG_GAME_OBJECT_RESET_STATE)]
	private void HandleGameObjectResetState(WorldPacket packet)
	{
		GameObjectResetState reset = new GameObjectResetState();
		reset.ObjectGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(reset);
	}

	[PacketHandler(Opcode.SMSG_GAME_OBJECT_CUSTOM_ANIM)]
	private void HandleGameObjectCustomAnim(WorldPacket packet)
	{
		GameObjectCustomAnim anim = new GameObjectCustomAnim();
		anim.ObjectGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		anim.CustomAnim = packet.ReadUInt32();
		this.SendPacketToClient(anim);
	}

	[PacketHandler(Opcode.SMSG_FISH_NOT_HOOKED)]
	private void HandleFishNotHooked(WorldPacket packet)
	{
		FishNotHooked fish = new FishNotHooked();
		this.SendPacketToClient(fish);
	}

	[PacketHandler(Opcode.SMSG_FISH_ESCAPED)]
	private void HandleFishEscaped(WorldPacket packet)
	{
		FishEscaped fish = new FishEscaped();
		this.SendPacketToClient(fish);
	}

	[PacketHandler(Opcode.SMSG_PARTY_COMMAND_RESULT)]
	private void HandlePartyCommandResult(WorldPacket packet)
	{
		PartyCommandResult party = new PartyCommandResult();
		party.Command = (byte)packet.ReadUInt32();
		party.Name = packet.ReadCString();
		uint partyResult = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			party.Result = (byte)partyResult;
		}
		else
		{
			Type typeFromHandle = typeof(PartyResultModern);
			PartyResultVanilla partyResultVanilla = (PartyResultVanilla)partyResult;
			party.Result = (byte)Enum.Parse(typeFromHandle, partyResultVanilla.ToString());
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			party.ResultData = packet.ReadUInt32();
		}
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_GROUP_DECLINE)]
	private void HandleGroupDecline(WorldPacket packet)
	{
		GroupDecline party = new GroupDecline();
		party.Name = packet.ReadCString();
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_PARTY_INVITE)]
	private void HandleGroupInvite(WorldPacket packet)
	{
		PartyInvite party = new PartyInvite();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			party.CanAccept = packet.ReadBool();
		}
		Realm realm = this.GetSession().RealmManager.GetRealm(this.GetSession().RealmId);
		party.InviterRealm = new VirtualRealmInfo(realm.Id.GetAddress(), isHomeRealm: true, isInternalRealm: false, realm.Name, realm.NormalizedName);
		party.InviterName = packet.ReadCString();
		party.InviterGUID = this.GetSession().GameState.GetPlayerGuidByName(party.InviterName);
		if (party.InviterGUID == null)
		{
			party.InviterGUID = WowGuid128.Empty;
			party.InviterBNetAccountId = WowGuid128.Empty;
		}
		else
		{
			party.InviterBNetAccountId = this.GetSession().GetBnetAccountGuidForPlayer(party.InviterGUID);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			party.ProposedRoles = packet.ReadUInt32();
			byte lfgSlotsCount = packet.ReadUInt8();
			for (int i = 0; i < lfgSlotsCount; i++)
			{
				party.LfgSlots.Add(packet.ReadInt32());
			}
			party.LfgCompletedMask = packet.ReadInt32();
		}
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_GROUP_LIST, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleGroupListVanilla(WorldPacket packet)
	{
		PartyUpdate party = new PartyUpdate();
		party.SequenceNum = this.GetSession().GameState.GroupUpdateCounter++;
		bool isRaid = packet.ReadBool();
		byte ownSubGroupAndFlags = packet.ReadUInt8();
		party.PartyIndex = (byte)((isRaid && this.GetSession().GameState.IsInBattleground()) ? 1u : 0u);
		party.PartyGUID = WowGuid128.Create(HighGuidType703.Party, (ulong)(1000 + party.PartyIndex));
		if (party.PartyIndex != 0)
		{
			party.PartyFlags |= GroupFlags.FakeRaid;
		}
		HashSet<WowGuid128> uniqueMembers = new HashSet<WowGuid128>();
		uint membersCount = packet.ReadUInt32();
		if (membersCount != 0)
		{
			if (isRaid)
			{
				party.PartyFlags |= GroupFlags.Raid;
			}
			party.DifficultySettings = new PartyDifficultySettings();
			party.DifficultySettings.DungeonDifficultyID = DifficultyModern.Normal;
			if (ModernVersion.ExpansionVersion > 1)
			{
				party.DifficultySettings.RaidDifficultyID = DifficultyModern.Raid25N;
			}
			else
			{
				party.DifficultySettings.RaidDifficultyID = DifficultyModern.Raid40;
			}
			if (party.PartyIndex != 0)
			{
				party.PartyType = GroupType.PvP;
			}
			else
			{
				party.PartyType = GroupType.Normal;
			}
			PartyPlayerInfo player = default(PartyPlayerInfo);
			player.GUID = this.GetSession().GameState.CurrentPlayerGuid;
			player.Name = this.GetSession().GameState.GetPlayerName(player.GUID);
			player.Subgroup = (byte)(ownSubGroupAndFlags & 0xF);
			player.Flags = (((ownSubGroupAndFlags & 0x80) != 0) ? GroupMemberFlags.Assistant : GroupMemberFlags.None);
			player.Status = GroupMemberOnlineStatus.Online;
			party.PlayerList.Add(player);
			bool allAssist = true;
			for (uint i = 0u; i < membersCount; i++)
			{
				PartyPlayerInfo member = default(PartyPlayerInfo);
				member.Name = packet.ReadCString();
				member.GUID = packet.ReadGuid().To128(this.GetSession().GameState);
				member.Status = (GroupMemberOnlineStatus)packet.ReadUInt8();
				byte subGroupAndFlags = packet.ReadUInt8();
				member.Subgroup = (byte)(subGroupAndFlags & 0xF);
				member.Flags = (((subGroupAndFlags & 0x80) != 0) ? GroupMemberFlags.Assistant : GroupMemberFlags.None);
				member.ClassId = this.GetSession().GameState.GetUnitClass(member.GUID);
				if (!member.Flags.HasAnyFlag(GroupMemberFlags.Assistant))
				{
					allAssist = false;
				}
				if (!uniqueMembers.Contains(member.GUID))
				{
					party.PlayerList.Add(member);
					uniqueMembers.Add(member.GUID);
				}
				this.Session.GameState.UpdatePlayerCache(member.GUID, new PlayerCache
				{
					Name = member.Name,
					ClassId = member.ClassId
				});
			}
			if (allAssist)
			{
				party.PartyFlags |= GroupFlags.EveryoneAssistant;
			}
			party.LeaderGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			party.LootSettings = new PartyLootSettings();
			party.LootSettings.Method = (LootMethod)packet.ReadUInt8();
			party.LootSettings.LootMaster = packet.ReadGuid().To128(this.GetSession().GameState);
			party.LootSettings.Threshold = packet.ReadUInt8();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958) && packet.CanRead())
			{
				packet.ReadUInt8(); // Dungeon Difficulty
				packet.ReadUInt8(); // Raid Difficulty
				if (packet.CanRead())
					packet.ReadUInt8(); // Dynamic Raid Difficulty (heroic flag)
			}
			this.GetSession().GameState.WeWantToLeaveGroup = false;
			this.GetSession().GameState.CurrentGroups[party.PartyIndex] = party;
		}
		else
		{
			party.PartyFlags |= GroupFlags.Destroyed;
			if (party.PartyIndex == 0)
			{
				party.PartyGUID = WowGuid128.Empty;
			}
			party.LeaderGUID = WowGuid128.Empty;
			party.MyIndex = -1;
			this.GetSession().GameState.CurrentGroups[party.PartyIndex] = null;
			if (!this.GetSession().GameState.WeWantToLeaveGroup)
			{
				this.SendPacketToClient(new GroupUninvite());
			}
		}
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_GROUP_LIST, ClientVersionBuild.V2_0_1_6180)]
	private void HandleGroupListTBC(WorldPacket packet)
	{
		PartyUpdate party = new PartyUpdate();
		party.SequenceNum = this.GetSession().GameState.GroupUpdateCounter++;
		byte groupType = packet.ReadUInt8(); // group type flags
		bool isRaid = (groupType & 0x01) != 0;
		bool isBattleground = (groupType & 0x04) != 0;
		bool isLfg = (groupType & 0x08) != 0;
		byte ownSubGroup = packet.ReadUInt8();
		byte ownGroupFlags = packet.ReadUInt8();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			packet.ReadUInt8(); // LFG roles
		}
		if (isLfg)
		{
			packet.ReadUInt8(); // LFG dungeon status
			packet.ReadUInt32(); // LFG dungeon ID
		}
		party.PartyIndex = (byte)(isBattleground ? 1u : 0u);
		party.PartyGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			packet.ReadUInt32(); // group counter
		}
		if (party.PartyIndex != 0)
		{
			party.PartyFlags |= GroupFlags.FakeRaid;
		}
		HashSet<WowGuid128> uniqueMembers = new HashSet<WowGuid128>();
		uint membersCount = packet.ReadUInt32();
		if (membersCount != 0)
		{
			if (isRaid)
			{
				party.PartyFlags |= GroupFlags.Raid;
			}
			if (party.PartyIndex != 0)
			{
				party.PartyType = GroupType.PvP;
			}
			else
			{
				party.PartyType = GroupType.Normal;
			}
			PartyPlayerInfo player = default(PartyPlayerInfo);
			player.GUID = this.GetSession().GameState.CurrentPlayerGuid;
			player.Name = this.GetSession().GameState.GetPlayerName(player.GUID);
			player.Subgroup = ownSubGroup;
			player.Flags = (GroupMemberFlags)ownGroupFlags;
			player.Status = GroupMemberOnlineStatus.Online;
			party.PlayerList.Add(player);
			bool allAssist = true;
			for (uint i = 0u; i < membersCount; i++)
			{
				PartyPlayerInfo member = default(PartyPlayerInfo);
				member.Name = packet.ReadCString();
				member.GUID = packet.ReadGuid().To128(this.GetSession().GameState);
				member.Status = (GroupMemberOnlineStatus)packet.ReadUInt8();
				member.Subgroup = packet.ReadUInt8();
				member.Flags = (GroupMemberFlags)packet.ReadUInt8();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
				{
					packet.ReadUInt8(); // LFG roles
				}
				member.ClassId = this.GetSession().GameState.GetUnitClass(member.GUID);
				if (!member.Flags.HasAnyFlag(GroupMemberFlags.Assistant))
				{
					allAssist = false;
				}
				if (!uniqueMembers.Contains(member.GUID))
				{
					party.PlayerList.Add(member);
					uniqueMembers.Add(member.GUID);
				}
				this.Session.GameState.UpdatePlayerCache(member.GUID, new PlayerCache
				{
					Name = member.Name,
					ClassId = member.ClassId
				});
			}
			if (allAssist)
			{
				party.PartyFlags |= GroupFlags.EveryoneAssistant;
			}
			party.LeaderGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			party.LootSettings = new PartyLootSettings();
			party.LootSettings.Method = (LootMethod)packet.ReadUInt8();
			party.LootSettings.LootMaster = packet.ReadGuid().To128(this.GetSession().GameState);
			party.LootSettings.Threshold = packet.ReadUInt8();
			party.DifficultySettings = new PartyDifficultySettings();
			int difficultyId = packet.ReadUInt8();
			party.DifficultySettings.DungeonDifficultyID = (DifficultyModern)Enum.Parse(typeof(DifficultyModern), ((DifficultyLegacy)difficultyId/*cast due to .constrained prefix*/).ToString());
			if (ModernVersion.ExpansionVersion > 1)
			{
				party.DifficultySettings.RaidDifficultyID = DifficultyModern.Raid25N;
			}
			else
			{
				party.DifficultySettings.RaidDifficultyID = DifficultyModern.Raid40;
			}
			this.GetSession().GameState.WeWantToLeaveGroup = false;
			this.GetSession().GameState.CurrentGroups[party.PartyIndex] = party;
		}
		else
		{
			party.PartyFlags |= GroupFlags.Destroyed;
			if (party.PartyIndex == 0)
			{
				party.PartyGUID = WowGuid128.Empty;
			}
			party.LeaderGUID = WowGuid128.Empty;
			party.MyIndex = -1;
			this.GetSession().GameState.CurrentGroups[party.PartyIndex] = null;
			if (!this.GetSession().GameState.WeWantToLeaveGroup)
			{
				this.SendPacketToClient(new GroupUninvite());
			}
		}
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_GROUP_UNINVITE)]
	private void HandleGroupUninvite(WorldPacket packet)
	{
		GroupUninvite party = new GroupUninvite();
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.SMSG_GROUP_NEW_LEADER)]
	private void HandleGroupNewLeader(WorldPacket packet)
	{
		GroupNewLeader party = new GroupNewLeader();
		party.Name = packet.ReadCString();
		party.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
		this.SendPacketToClient(party);
	}

	[PacketHandler(Opcode.MSG_RAID_READY_CHECK, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleRaidReadyCheckVanilla(WorldPacket packet)
	{
		if (!packet.CanRead())
		{
			ReadyCheckStarted ready = new ReadyCheckStarted();
			ready.InitiatorGUID = this.GetSession().GameState.GetCurrentGroupLeader();
			ready.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
			ready.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
			this.SendPacketToClient(ready);
			return;
		}
		ReadyCheckResponse ready2 = new ReadyCheckResponse();
		ready2.Player = packet.ReadGuid().To128(this.GetSession().GameState);
		ready2.IsReady = packet.ReadBool();
		ready2.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
		this.SendPacketToClient(ready2);
		this.GetSession().GameState.GroupReadyCheckResponses++;
		if (this.GetSession().GameState.GroupReadyCheckResponses >= this.GetSession().GameState.GetCurrentGroupSize())
		{
			this.GetSession().GameState.GroupReadyCheckResponses = 0u;
			ReadyCheckCompleted completed = new ReadyCheckCompleted();
			completed.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
			completed.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
			this.SendPacketToClient(completed);
		}
	}

	[PacketHandler(Opcode.MSG_RAID_READY_CHECK, ClientVersionBuild.V2_0_1_6180)]
	private void HandleRaidReadyCheck(WorldPacket packet)
	{
		ReadyCheckStarted ready = new ReadyCheckStarted();
		ready.InitiatorGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		ready.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
		ready.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
		this.SendPacketToClient(ready);
	}

	[PacketHandler(Opcode.MSG_RAID_READY_CHECK_CONFIRM, ClientVersionBuild.V2_0_1_6180)]
	private void HandleRaidReadyCheckConfirm(WorldPacket packet)
	{
		ReadyCheckResponse ready = new ReadyCheckResponse();
		ready.Player = packet.ReadGuid().To128(this.GetSession().GameState);
		ready.IsReady = packet.ReadBool();
		ready.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
		this.SendPacketToClient(ready);
		this.GetSession().GameState.GroupReadyCheckResponses++;
		if (this.GetSession().GameState.GroupReadyCheckResponses >= this.GetSession().GameState.GetCurrentGroupSize())
		{
			this.GetSession().GameState.GroupReadyCheckResponses = 0u;
			ReadyCheckCompleted completed = new ReadyCheckCompleted();
			completed.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
			completed.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
			this.SendPacketToClient(completed);
		}
	}

	[PacketHandler(Opcode.MSG_RAID_READY_CHECK_FINISHED, ClientVersionBuild.V2_0_1_6180)]
	private void HandleRaidReadyCheckFinished(WorldPacket packet)
	{
		ReadyCheckCompleted ready = new ReadyCheckCompleted();
		ready.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
		ready.PartyGUID = this.GetSession().GameState.GetCurrentGroupGuid();
		this.SendPacketToClient(ready);
	}

	[PacketHandler(Opcode.MSG_RAID_TARGET_UPDATE)]
	private void HandleRaidTargetUpdate(WorldPacket packet)
	{
		if (packet.ReadBool())
		{
			SendRaidTargetUpdateAll update = new SendRaidTargetUpdateAll();
			update.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
			while (packet.CanRead())
			{
				sbyte symbol = packet.ReadInt8();
				WowGuid128 guid = packet.ReadGuid().To128(this.GetSession().GameState);
				update.TargetIcons.Add(new Tuple<sbyte, WowGuid128>(symbol, guid));
			}
			this.SendPacketToClient(update);
			return;
		}
		SendRaidTargetUpdateSingle update2 = new SendRaidTargetUpdateSingle();
		update2.PartyIndex = this.GetSession().GameState.GetCurrentPartyIndex();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			update2.ChangedBy = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		else
		{
			update2.ChangedBy = this.GetSession().GameState.CurrentPlayerGuid;
		}
		update2.Symbol = packet.ReadInt8();
		update2.Target = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(update2);
	}

	[PacketHandler(Opcode.SMSG_SUMMON_REQUEST)]
	private void HandleSummonRequest(WorldPacket packet)
	{
		SummonRequest summon = new SummonRequest();
		summon.SummonerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		summon.SummonerVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		summon.AreaID = packet.ReadInt32();
		packet.ReadUInt32();
		this.SendPacketToClient(summon);
	}

	[PacketHandler(Opcode.SMSG_PARTY_MEMBER_PARTIAL_STATE, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePartyMemberStats(WorldPacket packet)
	{
		if (this.GetSession().GameState.CurrentMapId == 489 && (this.GetSession().GameState.HasWsgAllyFlagCarrier || this.GetSession().GameState.HasWsgHordeFlagCarrier) && this._requestBgPlayerPosCounter++ > 10)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
			this._requestBgPlayerPosCounter = 0u;
		}
		PartyMemberPartialState state = new PartyMemberPartialState();
		state.AffectedGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		GroupUpdateFlagVanilla updateFlags = (GroupUpdateFlagVanilla)packet.ReadUInt32();
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Status))
		{
			state.StatusFlags = packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.CurrentHealth))
		{
			state.CurrentHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.MaxHealth))
		{
			state.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PowerType))
		{
			state.PowerType = packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.CurrentPower))
		{
			state.CurrentPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.MaxPower))
		{
			state.MaxPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Level))
		{
			state.Level = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Zone))
		{
			state.ZoneID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Position))
		{
			state.Position = new PartyMemberPartialState.Vector3_UInt16();
			state.Position.X = packet.ReadInt16();
			state.Position.Y = packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Auras))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			uint auraMask = packet.ReadUInt32();
			byte maxAura = 32;
			for (byte i = 0; i < maxAura; i++)
			{
				if ((auraMask & (1L << (int)i)) != 0)
				{
					PartyMemberAuraStates aura = new PartyMemberAuraStates();
					aura.SpellId = packet.ReadUInt16();
					if (aura.SpellId != 0)
					{
						aura.ActiveFlags = 1u;
						aura.AuraFlags = 256;
					}
					state.Auras.Add(aura);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.AurasNegative))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			ushort auraMask2 = packet.ReadUInt16();
			byte maxAura2 = 48;
			for (byte i2 = 0; i2 < maxAura2; i2++)
			{
				if ((auraMask2 & (1L << (int)i2)) != 0)
				{
					PartyMemberAuraStates aura2 = new PartyMemberAuraStates();
					aura2.SpellId = packet.ReadUInt16();
					if (aura2.SpellId != 0)
					{
						aura2.ActiveFlags = 1u;
						aura2.AuraFlags = 16;
					}
					state.Auras.Add(aura2);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetGuid))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetName))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetName = packet.ReadCString();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetModelId))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.DisplayID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetCurrentHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.Health = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetMaxHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetPowerType))
		{
			packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetCurrentPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetMaxPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetAuras))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			uint auraMask3 = packet.ReadUInt32();
			byte maxAura3 = 32;
			for (byte i3 = 0; i3 < maxAura3; i3++)
			{
				if ((auraMask3 & (1L << (int)i3)) != 0)
				{
					PartyMemberAuraStates aura3 = new PartyMemberAuraStates();
					aura3.SpellId = packet.ReadUInt16();
					if (aura3.SpellId != 0)
					{
						aura3.ActiveFlags = 1u;
						aura3.AuraFlags = 256;
					}
					state.Pet.Auras.Add(aura3);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetAurasNegative))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			ushort auraMask4 = packet.ReadUInt16();
			byte maxAura4 = 48;
			for (byte i4 = 0; i4 < maxAura4; i4++)
			{
				if ((auraMask4 & (1L << (int)i4)) != 0)
				{
					PartyMemberAuraStates aura4 = new PartyMemberAuraStates();
					aura4.SpellId = packet.ReadUInt16();
					if (aura4.SpellId != 0)
					{
						aura4.ActiveFlags = 1u;
						aura4.AuraFlags = 16;
					}
					state.Pet.Auras.Add(aura4);
				}
			}
		}
		this.SendPacketToClient(state);
	}

	[PacketHandler(Opcode.SMSG_PARTY_MEMBER_PARTIAL_STATE, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePartyMemberStatsTbc(WorldPacket packet)
	{
		if (this.GetSession().GameState.CurrentMapId == 489 && (this.GetSession().GameState.HasWsgAllyFlagCarrier || this.GetSession().GameState.HasWsgHordeFlagCarrier) && this._requestBgPlayerPosCounter++ > 10)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
			this._requestBgPlayerPosCounter = 0u;
		}
		PartyMemberPartialState state = new PartyMemberPartialState();
		state.AffectedGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		GroupUpdateFlagTBC updateFlags = (GroupUpdateFlagTBC)packet.ReadUInt32();
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Status))
		{
			state.StatusFlags = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.CurrentHealth))
		{
			state.CurrentHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.MaxHealth))
		{
			state.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PowerType))
		{
			state.PowerType = packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.CurrentPower))
		{
			state.CurrentPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.MaxPower))
		{
			state.MaxPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Level))
		{
			state.Level = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Zone))
		{
			state.ZoneID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Position))
		{
			state.Position = new PartyMemberPartialState.Vector3_UInt16();
			state.Position.X = packet.ReadInt16();
			state.Position.Y = packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Auras))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			ulong auraMask = packet.ReadUInt64();
			for (byte i = 0; i < LegacyVersion.GetAuraSlotsCount(); i++)
			{
				if ((auraMask & (ulong)(1L << (int)i)) != 0)
				{
					PartyMemberAuraStates aura = new PartyMemberAuraStates();
					aura.SpellId = packet.ReadUInt16();
					packet.ReadUInt8();
					if (aura.SpellId != 0)
					{
						aura.ActiveFlags = 1u;
						aura.AuraFlags = 256;
					}
					state.Auras.Add(aura);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetGuid))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetName))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetName = packet.ReadCString();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetModelId))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.DisplayID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetCurrentHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.Health = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetMaxHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetPowerType))
		{
			packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetCurrentPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetMaxPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetAuras))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			ulong auraMask2 = packet.ReadUInt64();
			for (byte i2 = 0; i2 < LegacyVersion.GetAuraSlotsCount(); i2++)
			{
				if ((auraMask2 & (ulong)(1L << (int)i2)) != 0)
				{
					PartyMemberAuraStates aura2 = new PartyMemberAuraStates();
					aura2.SpellId = packet.ReadUInt16();
					packet.ReadUInt8();
					if (aura2.SpellId != 0)
					{
						aura2.ActiveFlags = 1u;
						aura2.AuraFlags = 256;
					}
					state.Pet.Auras.Add(aura2);
				}
			}
		}
		this.SendPacketToClient(state);
	}

	[PacketHandler(Opcode.SMSG_PARTY_MEMBER_FULL_STATE, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePartyMemberStatsFull(WorldPacket packet)
	{
		if (this.GetSession().GameState.CurrentMapId == 489 && (this.GetSession().GameState.HasWsgAllyFlagCarrier || this.GetSession().GameState.HasWsgHordeFlagCarrier) && this._requestBgPlayerPosCounter++ > 10)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
			this._requestBgPlayerPosCounter = 0u;
		}
		PartyMemberFullState state = new PartyMemberFullState();
		if (this.GetSession().GameState.IsInBattleground())
		{
			state.PartyType[0] = 0;
			state.PartyType[1] = 2;
		}
		else
		{
			state.PartyType[0] = 1;
			state.PartyType[1] = 0;
		}
		state.MemberGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		GroupUpdateFlagVanilla updateFlags = (GroupUpdateFlagVanilla)packet.ReadUInt32();
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Status))
		{
			state.StatusFlags = (GroupMemberOnlineStatus)packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.CurrentHealth))
		{
			state.CurrentHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.MaxHealth))
		{
			state.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PowerType))
		{
			state.PowerType = packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.CurrentPower))
		{
			state.CurrentPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.MaxPower))
		{
			state.MaxPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Level))
		{
			state.Level = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Zone))
		{
			state.ZoneID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Position))
		{
			state.PositionX = packet.ReadInt16();
			state.PositionY = packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.Auras))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			uint auraMask = packet.ReadUInt32();
			byte maxAura = 32;
			for (byte i = 0; i < maxAura; i++)
			{
				if ((auraMask & (1L << (int)i)) != 0)
				{
					PartyMemberAuraStates aura = new PartyMemberAuraStates();
					aura.SpellId = packet.ReadUInt16();
					if (aura.SpellId != 0)
					{
						aura.ActiveFlags = 1u;
						aura.AuraFlags = 256;
					}
					state.Auras.Add(aura);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.AurasNegative))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			ushort auraMask2 = packet.ReadUInt16();
			byte maxAura2 = 48;
			for (byte i2 = 0; i2 < maxAura2; i2++)
			{
				if ((auraMask2 & (1L << (int)i2)) != 0)
				{
					PartyMemberAuraStates aura2 = new PartyMemberAuraStates();
					aura2.SpellId = packet.ReadUInt16();
					if (aura2.SpellId != 0)
					{
						aura2.ActiveFlags = 1u;
						aura2.AuraFlags = 16;
					}
					state.Auras.Add(aura2);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetGuid))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetName))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetName = packet.ReadCString();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetModelId))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.DisplayID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetCurrentHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.Health = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetMaxHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.MaxHealth = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetPowerType))
		{
			packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetCurrentPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetMaxPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetAuras))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			uint auraMask3 = packet.ReadUInt32();
			byte maxAura3 = 32;
			for (byte i3 = 0; i3 < maxAura3; i3++)
			{
				if ((auraMask3 & (1L << (int)i3)) != 0)
				{
					PartyMemberAuraStates aura3 = new PartyMemberAuraStates();
					aura3.SpellId = packet.ReadUInt16();
					if (aura3.SpellId != 0)
					{
						aura3.ActiveFlags = 1u;
						aura3.AuraFlags = 256;
					}
					state.Pet.Auras.Add(aura3);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagVanilla.PetAurasNegative))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			ushort auraMask4 = packet.ReadUInt16();
			byte maxAura4 = 48;
			for (byte i4 = 0; i4 < maxAura4; i4++)
			{
				if ((auraMask4 & (1L << (int)i4)) != 0)
				{
					PartyMemberAuraStates aura4 = new PartyMemberAuraStates();
					aura4.SpellId = packet.ReadUInt16();
					if (aura4.SpellId != 0)
					{
						aura4.ActiveFlags = 1u;
						aura4.AuraFlags = 16;
					}
					state.Pet.Auras.Add(aura4);
				}
			}
		}
		this.SendPacketToClient(state);
	}

	[PacketHandler(Opcode.SMSG_PARTY_MEMBER_FULL_STATE, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePartyMemberStatsFullTBC(WorldPacket packet)
	{
		if (this.GetSession().GameState.CurrentMapId == 489 && (this.GetSession().GameState.HasWsgAllyFlagCarrier || this.GetSession().GameState.HasWsgHordeFlagCarrier) && this._requestBgPlayerPosCounter++ > 10)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
			this._requestBgPlayerPosCounter = 0u;
		}
		PartyMemberFullState state = new PartyMemberFullState();
		if (this.GetSession().GameState.IsInBattleground())
		{
			state.PartyType[0] = 0;
			state.PartyType[1] = 2;
		}
		else
		{
			state.PartyType[0] = 1;
			state.PartyType[1] = 0;
		}

        state.ForEnemy = packet.ReadUInt8() != 0;

        state.MemberGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		GroupUpdateFlagTBC updateFlags = (GroupUpdateFlagTBC)packet.ReadUInt32();
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Status))
		{
			state.StatusFlags = (GroupMemberOnlineStatus)packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.CurrentHealth))
		{
			
			if (ModernVersion.ExpansionVersion == 3) // Health is int32 in 3.3.5, source: TC 3.3.5 - GroupHandler.cpp
                state.CurrentHealth = (int)packet.ReadUInt32();
			else
                state.CurrentHealth = packet.ReadUInt16();
        }
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.MaxHealth))
		{
            if (ModernVersion.ExpansionVersion == 3)
                state.MaxHealth = (int)packet.ReadUInt32();
			else
                state.MaxHealth = packet.ReadUInt16();

        }
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PowerType))
		{
			state.PowerType = packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.CurrentPower))
		{
			state.CurrentPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.MaxPower))
		{
			state.MaxPower = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Level))
		{
			state.Level = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Zone))
		{
			state.ZoneID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Position))
		{
			state.PositionX = packet.ReadInt16();
			state.PositionY = packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.Auras))
		{
			if (state.Auras == null)
			{
				state.Auras = new List<PartyMemberAuraStates>();
			}
			ulong auraMask = packet.ReadUInt64();
			for (byte i = 0; i < LegacyVersion.GetAuraSlotsCount(); i++)
			{
				if ((auraMask & (ulong)(1L << (int)i)) != 0)
				{
					PartyMemberAuraStates aura = new PartyMemberAuraStates();
                    if (ModernVersion.ExpansionVersion == 3)
                        aura.SpellId = packet.ReadUInt32();
					else
                        aura.SpellId = packet.ReadUInt16();
                    packet.ReadUInt8();
					if (aura.SpellId != 0)
					{
						aura.ActiveFlags = 1u;
						aura.AuraFlags = 256;
					}
					state.Auras.Add(aura);
				}
			}
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetGuid))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetName))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.NewPetName = packet.ReadCString();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetModelId))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			state.Pet.DisplayID = packet.ReadUInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetCurrentHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
            if (ModernVersion.ExpansionVersion == 3)
				state.Pet.Health = (uint)packet.ReadUInt32();
			else
                state.Pet.Health = packet.ReadUInt16();
        }
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetMaxHealth))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
            if (ModernVersion.ExpansionVersion == 3)
                state.Pet.MaxHealth = (uint)packet.ReadUInt32();
			else
                state.Pet.MaxHealth = packet.ReadUInt16();

        }
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetPowerType))
		{
			packet.ReadUInt8();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetCurrentPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetMaxPower))
		{
			packet.ReadInt16();
		}
		if (updateFlags.HasFlag(GroupUpdateFlagTBC.PetAuras))
		{
			if (state.Pet == null)
			{
				state.Pet = new PartyMemberPetStats();
			}
			if (state.Pet.Auras == null)
			{
				state.Pet.Auras = new List<PartyMemberAuraStates>();
			}
			ulong auraMask2 = packet.ReadUInt64();
			for (byte i2 = 0; i2 < LegacyVersion.GetAuraSlotsCount(); i2++)
			{
				if ((auraMask2 & (ulong)(1L << (int)i2)) != 0)
				{
					PartyMemberAuraStates aura2 = new PartyMemberAuraStates();
                    if (ModernVersion.ExpansionVersion == 3)
                        aura2.SpellId = packet.ReadUInt32();
                    else
                        aura2.SpellId = packet.ReadUInt16();
                    packet.ReadUInt8();
					if (aura2.SpellId != 0)
					{
						aura2.ActiveFlags = 1u;
						aura2.AuraFlags = 256;
					}
					state.Pet.Auras.Add(aura2);
				}
			}
		}
		this.SendPacketToClient(state);
	}

	[PacketHandler(Opcode.MSG_MINIMAP_PING)]
	private void HandleMinimapPing(WorldPacket packet)
	{
		MinimapPing ping = new MinimapPing();
		ping.SenderGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		ping.Position = packet.ReadVector2();
		this.SendPacketToClient(ping);
	}

	[PacketHandler(Opcode.MSG_RANDOM_ROLL)]
	private void HandleRandomRoll(WorldPacket packet)
	{
		RandomRoll roll = new RandomRoll();
		roll.Min = packet.ReadInt32();
		roll.Max = packet.ReadInt32();
		roll.Result = packet.ReadInt32();
		roll.Roller = packet.ReadGuid().To128(this.GetSession().GameState);
		roll.RollerWowAccount = this.GetSession().GetGameAccountGuidForPlayer(roll.Roller);
		this.SendPacketToClient(roll);
	}

	[PacketHandler(Opcode.SMSG_GUILD_COMMAND_RESULT)]
	private void HandleGuildCommandResult(WorldPacket packet)
	{
		GuildCommandResult result = new GuildCommandResult();
		result.Command = (GuildCommandType)packet.ReadUInt32();
		result.Name = packet.ReadCString();
		result.Result = (GuildCommandError)packet.ReadUInt32();
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.SMSG_GUILD_EVENT)]
	private void HandleGuildEvent(WorldPacket packet)
	{
		GuildEventType eventType = (GuildEventType)packet.ReadUInt8();
		byte size = packet.ReadUInt8();
		string[] strings = new string[size];
		for (int i = 0; i < size; i++)
		{
			strings[i] = packet.ReadCString();
		}
		WowGuid128 guid = WowGuid128.Empty;
		if (packet.CanRead())
		{
			guid = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		switch (eventType)
		{
		case GuildEventType.Promotion:
		case GuildEventType.Demotion:
		{
			WowGuid128 officer = this.GetSession().GameState.GetPlayerGuidByName(strings[0]);
			WowGuid128 player = this.GetSession().GameState.GetPlayerGuidByName(strings[1]);
			uint rankId = this.GetSession().GetGuildRankIdByName(this.GetSession().GameState.GetPlayerGuildId(this.GetSession().GameState.CurrentPlayerGuid), strings[2]);
			if (officer != null && player != null)
			{
				GuildSendRankChange promote = new GuildSendRankChange();
				promote.Officer = officer;
				promote.Other = player;
				promote.Promote = eventType == GuildEventType.Promotion;
				promote.RankID = rankId;
				this.SendPacketToClient(promote);
			}
			break;
		}
		case GuildEventType.MOTD:
		{
			GuildEventMotd motd = new GuildEventMotd();
			motd.MotdText = strings[0];
			this.SendPacketToClient(motd);
			break;
		}
		case GuildEventType.PlayerJoined:
		{
			GuildEventPlayerJoined joined = new GuildEventPlayerJoined();
			joined.Guid = guid;
			joined.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			joined.Name = strings[0];
			this.SendPacketToClient(joined);
			break;
		}
		case GuildEventType.PlayerLeft:
		{
			GuildEventPlayerLeft left = new GuildEventPlayerLeft();
			left.Removed = false;
			left.LeaverGUID = guid;
			left.LeaverVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			left.LeaverName = strings[0];
			this.SendPacketToClient(left);
			break;
		}
		case GuildEventType.PlayerRemoved:
		{
			GuildEventPlayerLeft removed = new GuildEventPlayerLeft();
			removed.Removed = true;
			removed.LeaverGUID = guid;
			removed.LeaverVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			removed.LeaverName = strings[0];
			removed.RemoverGUID = this.GetSession().GameState.GetPlayerGuidByName(strings[1]);
			removed.RemoverVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			removed.RemoverName = strings[1];
			this.SendPacketToClient(removed);
			break;
		}
		case GuildEventType.LeaderIs:
			break;
		case GuildEventType.LeaderChanged:
		{
			WowGuid128 oldLeader = this.GetSession().GameState.GetPlayerGuidByName(strings[0]);
			WowGuid128 newLeader = this.GetSession().GameState.GetPlayerGuidByName(strings[1]);
			if (oldLeader != null && newLeader != null)
			{
				GuildEventNewLeader leader = new GuildEventNewLeader();
				leader.OldLeaderGUID = oldLeader;
				leader.OldLeaderVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
				leader.OldLeaderName = strings[0];
				leader.NewLeaderGUID = newLeader;
				leader.NewLeaderVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
				leader.NewLeaderName = strings[1];
				this.SendPacketToClient(leader);
			}
			break;
		}
		case GuildEventType.Disbanded:
		{
			GuildEventDisbanded disband = new GuildEventDisbanded();
			this.SendPacketToClient(disband);
			break;
		}
		case GuildEventType.TabardChange:
			break;
		case GuildEventType.RankUpdated:
		{
			GuildEventRanksUpdated ranks = new GuildEventRanksUpdated();
			this.SendPacketToClient(ranks);
			break;
		}
		case GuildEventType.Unk11:
			break;
		case GuildEventType.PlayerSignedOn:
		case GuildEventType.PlayerSignedOff:
		{
			GuildEventPresenceChange presence = new GuildEventPresenceChange();
			presence.Guid = guid;
			presence.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			presence.LoggedOn = eventType == GuildEventType.PlayerSignedOn;
			presence.Name = strings[0];
			this.SendPacketToClient(presence);
			break;
		}
		case GuildEventType.BankBagSlotsChanged:
			break;
		case GuildEventType.BankTabPurchased:
		{
			GuildEventTabAdded tab3 = new GuildEventTabAdded();
			this.SendPacketToClient(tab3);
			break;
		}
		case GuildEventType.BankTabUpdated:
		{
			GuildEventTabModified tab2 = new GuildEventTabModified();
			tab2.Name = strings[0];
			tab2.Icon = strings[1];
			this.SendPacketToClient(tab2);
			break;
		}
		case GuildEventType.BankMoneyUpdate:
		{
			GuildEventBankMoneyChanged money = new GuildEventBankMoneyChanged();
			money.Money = (ulong)int.Parse(strings[0], NumberStyles.HexNumber);
			this.SendPacketToClient(money);
			break;
		}
		case GuildEventType.BankMoneyWithdraw:
			break;
		case GuildEventType.BankTextChanged:
		{
			GuildEventTabTextChanged tab = new GuildEventTabTextChanged();
			this.SendPacketToClient(tab);
			break;
		}
		}
	}

	[PacketHandler(Opcode.SMSG_QUERY_GUILD_INFO_RESPONSE)]
	private void HandleQueryGuildInfoResponse(WorldPacket packet)
	{
		QueryGuildInfoResponse guild = new QueryGuildInfoResponse();
		uint guildId = packet.ReadUInt32();
		guild.GuildGUID = WowGuid128.Create(HighGuidType703.Guild, guildId);
		guild.PlayerGuid = this.GetSession().GameState.CurrentPlayerGuid;
		guild.HasGuildInfo = true;
		guild.Info = new QueryGuildInfoResponse.GuildInfo();
		guild.Info.GuildGuid = guild.GuildGUID;
		guild.Info.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		guild.Info.GuildName = packet.ReadCString();
		this.GetSession().StoreGuildGuidAndName(guild.GuildGUID, guild.Info.GuildName);
		List<string> ranks = new List<string>();
		for (uint i = 0u; i < 10; i++)
		{
			string rankName = packet.ReadCString();
			if (!string.IsNullOrEmpty(rankName))
			{
				QueryGuildInfoResponse.GuildInfo.RankInfo rank = new QueryGuildInfoResponse.GuildInfo.RankInfo
				{
					RankID = i,
					RankOrder = i,
					RankName = rankName
				};
				ranks.Add(rankName);
				guild.Info.Ranks.Add(rank);
			}
		}
		this.GetSession().StoreGuildRankNames(guildId, ranks);
		guild.Info.EmblemStyle = packet.ReadUInt32();
		guild.Info.EmblemColor = packet.ReadUInt32();
		guild.Info.BorderStyle = packet.ReadUInt32();
		guild.Info.BorderColor = packet.ReadUInt32();
		guild.Info.BackgroundColor = packet.ReadUInt32();
		this.SendPacketToClient(guild);
	}

	[PacketHandler(Opcode.SMSG_GUILD_INFO)]
	private void HandleGuildInfo(WorldPacket packet)
	{
		packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			this.GetSession().GameState.CurrentGuildCreateTime = packet.ReadPackedTime();
		}
		else
		{
			int day = packet.ReadInt32();
			int month = packet.ReadInt32();
			int year = packet.ReadInt32();
			try
			{
				DateTime date = new DateTime(year, month, day);
				this.GetSession().GameState.CurrentGuildCreateTime = (uint)Time.DateTimeToUnixTime(date);
			}
			catch
			{
				Log.Print(LogType.Error, $"Invalid guild create date: {day}-{month}-{year}", "HandleGuildInfo", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\GuildHandler.cs");
			}
		}
		packet.ReadUInt32();
		this.GetSession().GameState.CurrentGuildNumAccounts = packet.ReadUInt32();
	}

	[PacketHandler(Opcode.SMSG_GUILD_ROSTER)]
	private void HandleGuildRoster(WorldPacket packet)
	{
		GuildRoster guild = new GuildRoster();
		uint membersCount = packet.ReadUInt32();
		if (this.GetSession().GameState.CurrentGuildNumAccounts != 0)
		{
			guild.NumAccounts = this.GetSession().GameState.CurrentGuildNumAccounts;
		}
		else
		{
			guild.NumAccounts = membersCount;
		}
		guild.WelcomeText = packet.ReadCString();
		guild.InfoText = packet.ReadCString();
		if (this.GetSession().GameState.CurrentGuildCreateTime != 0)
		{
			guild.CreateDate = this.GetSession().GameState.CurrentGuildCreateTime;
		}
		else
		{
			guild.CreateDate = (uint)Time.UnixTime;
		}
		int ranksCount = packet.ReadInt32();
		if (ranksCount > 0)
		{
			GuildRanks ranks = new GuildRanks();
			for (byte i = 0; i < ranksCount; i++)
			{
				GuildRankData rank = new GuildRankData();
				rank.RankID = i;
				rank.RankOrder = i;
				rank.RankName = this.GetSession().GetGuildRankNameById(this.GetSession().GameState.GetPlayerGuildId(this.GetSession().GameState.CurrentPlayerGuid), i);
				rank.Flags = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					rank.WithdrawGoldLimit = packet.ReadInt32();
					for (int j = 0; j < 6; j++)
					{
						rank.TabFlags[j] = packet.ReadUInt32();
						rank.TabWithdrawItemLimit[j] = packet.ReadUInt32();
					}
				}
				ranks.Ranks.Add(rank);
			}
			this.SendPacketToClient(ranks);
		}
		for (int k = 0; k < membersCount; k++)
		{
			GuildRosterMemberData member = new GuildRosterMemberData();
			PlayerCache cache = new PlayerCache();
			member.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			member.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			member.Status = packet.ReadUInt8();
			member.Name = (cache.Name = packet.ReadCString());
			member.RankID = packet.ReadInt32();
			member.Level = (cache.Level = packet.ReadUInt8());
			member.ClassID = (cache.ClassId = (Class)packet.ReadUInt8());
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
			{
				member.SexID = (cache.SexId = (Gender)packet.ReadUInt8());
			}
			this.GetSession().GameState.UpdatePlayerCache(member.Guid, cache);
			member.AreaID = packet.ReadInt32();
			if (member.Status == 0)
			{
				member.LastSave = packet.ReadFloat();
			}
			else
			{
				member.Authenticated = true;
			}
			member.Note = packet.ReadCString();
			member.OfficerNote = packet.ReadCString();
			guild.MemberData.Add(member);
		}
		this.SendPacketToClient(guild);
	}

	[PacketHandler(Opcode.SMSG_GUILD_INVITE)]
	private void HandleGuildInvite(WorldPacket packet)
	{
		GuildInvite invite = new GuildInvite();
		invite.InviterName = packet.ReadCString();
		invite.InviterVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		invite.GuildName = packet.ReadCString();
		invite.GuildVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		invite.GuildGUID = this.GetSession().GetGuildGuid(invite.GuildName);
		this.SendPacketToClient(invite);
	}

	[PacketHandler(Opcode.MSG_GUILD_PERMISSIONS)]
	private void HandleGuildPermissions(WorldPacket packet)
	{
		GuildPermissionsQueryResults results = new GuildPermissionsQueryResults();
		results.GuildID = packet.ReadUInt32();
		results.RankID = packet.ReadUInt32();
		results.Flags = packet.ReadUInt32();
		results.WithdrawGoldLimit = packet.ReadUInt32();
		results.RemainingWithdrawGoldLimit = packet.ReadUInt32();
		for (int i = 0; i < 6; i++)
		{
			results.TabPermissions[i] = packet.ReadUInt32();
		}
		this.SendPacketToClient(results);
	}

	[PacketHandler(Opcode.MSG_TABARDVENDOR_ACTIVATE)]
	private void HandleTabardVendorActivate(WorldPacket packet)
	{
		PlayerTabardVendorActivate activate = new PlayerTabardVendorActivate();
		activate.DesignerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(activate);
	}

	[PacketHandler(Opcode.MSG_SAVE_GUILD_EMBLEM)]
	private void HandleSaveGuildEmblem(WorldPacket packet)
	{
		PlayerSaveGuildEmblem emblem = new PlayerSaveGuildEmblem();
		emblem.Error = (GuildEmblemError)packet.ReadUInt32();
		this.SendPacketToClient(emblem);
	}

	[PacketHandler(Opcode.SMSG_GUILD_INVITE_DECLINED)]
	private void HandleGuildInviteDeclined(WorldPacket packet)
	{
		GuildInviteDeclined invite = new GuildInviteDeclined();
		invite.InviterName = packet.ReadCString();
		invite.InviterVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		this.SendPacketToClient(invite);
	}

	[PacketHandler(Opcode.SMSG_GUILD_BANK_QUERY_RESULTS)]
	private void HandleGuildBankQueryResults(WorldPacket packet)
	{
		GuildBankQueryResults result = new GuildBankQueryResults();
		result.Money = packet.ReadUInt64();
		result.Tab = packet.ReadUInt8();
		result.WithdrawalsRemaining = packet.ReadInt32();
		bool hasTabs = false;
		if (packet.ReadBool() && result.Tab == 0)
		{
			hasTabs = true;
			byte size = packet.ReadUInt8();
			for (int i = 0; i < size; i++)
			{
				GuildBankTabInfo tabInfo = new GuildBankTabInfo
				{
					TabIndex = i,
					Name = packet.ReadCString(),
					Icon = packet.ReadCString()
				};
				result.TabInfo.Add(tabInfo);
			}
		}
		byte slots = packet.ReadUInt8();
		for (int j = 0; j < slots; j++)
		{
			GuildBankItemInfo itemInfo = new GuildBankItemInfo();
			itemInfo.Slot = packet.ReadUInt8();
			int entry = packet.ReadInt32();
			if (entry > 0)
			{
				itemInfo.Item.ItemID = (uint)entry;
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
				{
					itemInfo.Flags = packet.ReadUInt32();
				}
				itemInfo.Item.RandomPropertiesID = packet.ReadUInt32();
				if (itemInfo.Item.RandomPropertiesID != 0)
				{
					itemInfo.Item.RandomPropertiesSeed = packet.ReadUInt32();
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					itemInfo.Count = packet.ReadInt32();
				}
				else
				{
					itemInfo.Count = packet.ReadUInt8();
				}
				itemInfo.EnchantmentID = packet.ReadInt32();
				itemInfo.Charges = packet.ReadUInt8();
				byte enchantments = packet.ReadUInt8();
				for (int k = 0; k < enchantments; k++)
				{
					byte slot = packet.ReadUInt8();
					uint enchantId = packet.ReadUInt32();
					if (enchantId != 0)
					{
						uint itemId = GameData.GetGemFromEnchantId(enchantId);
						if (itemId != 0)
						{
							ItemGemData gem = new ItemGemData();
							gem.Slot = slot;
							gem.Item.ItemID = itemId;
							itemInfo.SocketEnchant.Add(gem);
						}
					}
				}
			}
			result.ItemInfo.Add(itemInfo);
		}
		result.FullUpdate = hasTabs && slots > 0;
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.MSG_QUERY_GUILD_BANK_TEXT)]
	private void HandleQueryGuildBankText(WorldPacket packet)
	{
		GuildBankTextQueryResult result = new GuildBankTextQueryResult();
		result.Tab = packet.ReadUInt8();
		result.Text = packet.ReadCString();
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.MSG_GUILD_BANK_LOG_QUERY)]
	private void HandleGuildBankLongQuery(WorldPacket packet)
	{
		GuildBankLogQueryResults result = new GuildBankLogQueryResults();
		result.Tab = packet.ReadUInt8();
		byte logSize = packet.ReadUInt8();
		for (byte i = 0; i < logSize; i++)
		{
			GuildBankLogEntry logEntry = new GuildBankLogEntry();
			logEntry.EntryType = packet.ReadInt8();
			logEntry.PlayerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			if (result.Tab != 6)
			{
				logEntry.ItemID = packet.ReadInt32();
				logEntry.Count = packet.ReadUInt8();
				if (logEntry.EntryType == 3 || logEntry.EntryType == 7)
				{
					logEntry.OtherTab = packet.ReadInt8();
				}
			}
			else
			{
				logEntry.Money = packet.ReadUInt32();
			}
			logEntry.TimeOffset = packet.ReadUInt32();
			result.Entry.Add(logEntry);
		}
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.MSG_GUILD_BANK_MONEY_WITHDRAWN)]
	private void HandleGuildBankMoneyWithdrawn(WorldPacket packet)
	{
		GuildBankRemainingWithdrawMoney result = new GuildBankRemainingWithdrawMoney();
		result.RemainingWithdrawMoney = packet.ReadUInt32();
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_INSTANCE_OWNERSHIP)]
	private void HandleUpdateInstanceOwnership(WorldPacket packet)
	{
		UpdateInstanceOwnership instance = new UpdateInstanceOwnership();
		instance.IOwnInstance = packet.ReadUInt32();
		this.SendPacketToClient(instance);
	}

	[PacketHandler(Opcode.SMSG_INSTANCE_RESET)]
	private void HandleInstanceReset(WorldPacket packet)
	{
		InstanceReset reset = new InstanceReset();
		reset.MapID = packet.ReadUInt32();
		this.SendPacketToClient(reset);
	}

	[PacketHandler(Opcode.SMSG_INSTANCE_RESET_FAILED)]
	private void HandleInstanceResetFailed(WorldPacket packet)
	{
		InstanceResetFailed reset = new InstanceResetFailed();
		reset.ResetFailedReason = (ResetFailedReason)packet.ReadUInt32();
		reset.MapID = packet.ReadUInt32();
		this.SendPacketToClient(reset);
	}

	[PacketHandler(Opcode.SMSG_RESET_FAILED_NOTIFY)]
	private void HandleResetFailedNotify(WorldPacket packet)
	{
		ResetFailedNotify reset = new ResetFailedNotify();
		packet.ReadUInt32();
		this.SendPacketToClient(reset);
	}

	[PacketHandler(Opcode.SMSG_RAID_INSTANCE_INFO)]
	private void HandleRaidInstanceInfo(WorldPacket packet)
	{
		RaidInstanceInfo infos = new RaidInstanceInfo();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			InstanceLock instance = new InstanceLock();
			instance.MapID = packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				instance.DifficultyID = (DifficultyModern)packet.ReadUInt32();
			}
			else if (ModernVersion.ExpansionVersion == 1)
			{
				instance.DifficultyID = DifficultyModern.Raid40;
			}
			else
			{
				instance.DifficultyID = DifficultyModern.Raid25N;
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				instance.InstanceID = packet.ReadUInt64();
				instance.Locked = packet.ReadBool();
				instance.Extended = packet.ReadBool();
				instance.TimeRemaining = packet.ReadInt32();
			}
			else
			{
				instance.TimeRemaining = packet.ReadInt32();
				instance.InstanceID = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					packet.ReadUInt32();
				}
			}
			infos.LockList.Add(instance);
		}
		this.SendPacketToClient(infos);
	}

	[PacketHandler(Opcode.SMSG_INSTANCE_SAVE_CREATED)]
	private void HandleInstanceSaveCreated(WorldPacket packet)
	{
		InstanceSaveCreated save = new InstanceSaveCreated();
		save.Gm = packet.ReadUInt32() != 0;
		this.SendPacketToClient(save);
	}

	[PacketHandler(Opcode.SMSG_RAID_GROUP_ONLY)]
	private void HandleRaidGroupOnly(WorldPacket packet)
	{
		RaidGroupOnly save = new RaidGroupOnly();
		save.Delay = packet.ReadInt32();
		save.Reason = (RaidGroupReason)packet.ReadUInt32();
		this.SendPacketToClient(save);
	}

	[PacketHandler(Opcode.SMSG_RAID_INSTANCE_MESSAGE)]
	private void HandleRaidInstanceMessage(WorldPacket packet)
	{
		RaidInstanceMessage instance = new RaidInstanceMessage();
		instance.Type = (InstanceResetWarningType)packet.ReadUInt32();
		instance.MapID = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			instance.DifficultyID = (DifficultyModern)packet.ReadUInt32();
		}
		else if (ModernVersion.ExpansionVersion == 1)
		{
			instance.DifficultyID = DifficultyModern.Raid40;
		}
		else
		{
			instance.DifficultyID = DifficultyModern.Raid25N;
		}
		packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) && instance.Type == InstanceResetWarningType.Welcome)
		{
			instance.Locked = packet.ReadBool();
			instance.Extended = packet.ReadBool();
		}
		this.SendPacketToClient(instance);
	}

	[PacketHandler(Opcode.SMSG_SET_PROFICIENCY)]
	private void HandleSetProficiency(WorldPacket packet)
	{
		SetProficiency proficiency = new SetProficiency();
		proficiency.ProficiencyClass = packet.ReadUInt8();
		proficiency.ProficiencyMask = packet.ReadUInt32();
		this.SendPacketToClient(proficiency);
	}

	[PacketHandler(Opcode.SMSG_BUY_SUCCEEDED)]
	private void HandleBuySucceeded(WorldPacket packet)
	{
		BuySucceeded buy = new BuySucceeded();
		buy.VendorGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		buy.Muid = packet.ReadUInt32();
		buy.NewQuantity = packet.ReadInt32();
		buy.QuantityBought = packet.ReadUInt32();
		this.SendPacketToClient(buy);
	}

	[PacketHandler(Opcode.SMSG_ITEM_PUSH_RESULT)]
	private void HandleItemPushResult(WorldPacket packet)
	{
		ItemPushResult item = new ItemPushResult();
		item.PlayerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		bool fromNPC = packet.ReadUInt32() == 1;
		item.Created = packet.ReadUInt32() == 1;
		bool showInChat = packet.ReadUInt32() == 1;
		if (fromNPC && !item.Created)
		{
			item.DisplayText = ItemPushResult.DisplayType.Received;
			item.Pushed = true;
		}
		else if (!showInChat)
		{
			item.DisplayText = ItemPushResult.DisplayType.Hidden;
		}
		else
		{
			item.DisplayText = ItemPushResult.DisplayType.Loot;
		}
		item.Slot = packet.ReadUInt8();
		item.SlotInBag = packet.ReadInt32();
		item.Item.ItemID = packet.ReadUInt32();
		// Pre-query this item's template if not cached, so hotfix is ready before client requests it
		if (!GameData.ItemTemplates.ContainsKey(item.Item.ItemID))
		{
			WorldPacket queryPacket = new WorldPacket(Opcode.CMSG_ITEM_QUERY_SINGLE);
			queryPacket.WriteUInt32(item.Item.ItemID);
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				queryPacket.WriteGuid(WowGuid64.Empty);
			}
			this.SendPacketToServer(queryPacket);
		}
		item.Item.RandomPropertiesSeed = packet.ReadUInt32();
		item.Item.RandomPropertiesID = packet.ReadUInt32();
		item.Quantity = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			item.QuantityInInventory = packet.ReadUInt32();
		}
		else
		{
			uint currentCount = 0u;
			QuestObjective objective = GameData.GetQuestObjectiveForItem(item.Item.ItemID);
			if (objective != null)
			{
				Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(this.GetSession().GameState.CurrentPlayerGuid);
				int questsCount = LegacyVersion.GetQuestLogSize();
				for (int i = 0; i < questsCount; i++)
				{
					QuestLog logEntry = this.ReadQuestLogEntry(i, null, updateFields);
					if (logEntry != null && logEntry.QuestID.HasValue && logEntry.QuestID == objective.QuestID && logEntry.ObjectiveProgress[objective.StorageIndex].HasValue)
					{
						currentCount = (uint)logEntry.ObjectiveProgress[objective.StorageIndex].Value;
						break;
					}
				}
			}
			item.QuantityInInventory = item.Quantity + currentCount;
		}
		if (item.Slot == byte.MaxValue && item.SlotInBag >= 0 && item.PlayerGUID == this.GetSession().GameState.CurrentPlayerGuid)
		{
			item.ItemGUID = this.GetSession().GameState.GetInventorySlotItem(item.SlotInBag).To128(this.GetSession().GameState);
		}
		else
		{
			item.ItemGUID = WowGuid128.Empty;
		}
		this.SendPacketToClient(item);
	}

	[PacketHandler(Opcode.SMSG_READ_ITEM_RESULT_OK)]
	private void HandleReadItemResultOk(WorldPacket packet)
	{
		ReadItemResultOK read = new ReadItemResultOK();
		read.ItemGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(read);
	}

	[PacketHandler(Opcode.SMSG_READ_ITEM_RESULT_FAILED)]
	private void HandleReadItemResultFailed(WorldPacket packet)
	{
		ReadItemResultFailed read = new ReadItemResultFailed();
		read.ItemGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		read.Subcode = 2;
		this.SendPacketToClient(read);
	}

	[PacketHandler(Opcode.SMSG_BUY_FAILED)]
	private void HandleBuyFailed(WorldPacket packet)
	{
		BuyFailed fail = new BuyFailed();
		fail.VendorGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		fail.Muid = packet.ReadUInt32();
		fail.Reason = (BuyResult)packet.ReadUInt8();
		this.SendPacketToClient(fail);
	}

	[PacketHandler(Opcode.SMSG_INVENTORY_CHANGE_FAILURE, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandleInventoryChangeFailureVanilla(WorldPacket packet)
	{
		InventoryChangeFailure failure = new InventoryChangeFailure();
		failure.BagResult = LegacyVersion.ConvertInventoryResult(packet.ReadUInt8());
		if (failure.BagResult != InventoryResult.Ok)
		{
			InventoryResult bagResult = failure.BagResult;
			InventoryResult inventoryResult = bagResult;
			if (inventoryResult == InventoryResult.CantEquipLevel)
			{
				failure.Level = packet.ReadInt32();
			}
			failure.Item[0] = packet.ReadGuid().To128(this.GetSession().GameState);
			failure.Item[1] = packet.ReadGuid().To128(this.GetSession().GameState);
			failure.ContainerBSlot = packet.ReadUInt8();
			this.SendPacketToClient(failure);
			if (this.GetSession().GameState.CurrentClientNormalCast != null && !this.GetSession().GameState.CurrentClientNormalCast.HasStarted && this.GetSession().GameState.CurrentClientNormalCast.ItemGUID == failure.Item[0])
			{
				this.GetSession().InstanceSocket.SendCastRequestFailed(this.GetSession().GameState.CurrentClientNormalCast, isPet: false);
				this.GetSession().GameState.CurrentClientNormalCast = null;
			}
		}
	}

	[PacketHandler(Opcode.SMSG_INVENTORY_CHANGE_FAILURE, ClientVersionBuild.V2_0_1_6180)]
	private void HandleInventoryChangeFailure(WorldPacket packet)
	{
		InventoryChangeFailure failure = new InventoryChangeFailure();
		failure.BagResult = LegacyVersion.ConvertInventoryResult(packet.ReadUInt8());
		if (failure.BagResult != InventoryResult.Ok)
		{
			failure.Item[0] = packet.ReadGuid().To128(this.GetSession().GameState);
			failure.Item[1] = packet.ReadGuid().To128(this.GetSession().GameState);
			failure.ContainerBSlot = packet.ReadUInt8();
			switch (failure.BagResult)
			{
			case InventoryResult.CantEquipLevel:
			case InventoryResult.PurchaseLevelTooLow:
				failure.Level = packet.ReadInt32();
				break;
			case InventoryResult.EventAutoEquipBindConfirm:
				failure.SrcContainer = packet.ReadGuid().To128(this.GetSession().GameState);
				failure.SrcSlot = packet.ReadInt32();
				failure.DstContainer = packet.ReadGuid().To128(this.GetSession().GameState);
				break;
			case InventoryResult.ItemMaxLimitCategoryCountExceeded:
			case InventoryResult.ItemMaxLimitCategorySocketedExceeded:
			case InventoryResult.ItemMaxLimitCategoryEquippedExceeded:
				failure.LimitCategory = packet.ReadInt32();
				break;
			}
			this.SendPacketToClient(failure);
			if (this.GetSession().GameState.CurrentClientNormalCast != null && !this.GetSession().GameState.CurrentClientNormalCast.HasStarted && this.GetSession().GameState.CurrentClientNormalCast.ItemGUID == failure.Item[0])
			{
				this.GetSession().InstanceSocket.SendCastRequestFailed(this.GetSession().GameState.CurrentClientNormalCast, isPet: false);
				this.GetSession().GameState.CurrentClientNormalCast = null;
			}
		}
	}

	[PacketHandler(Opcode.SMSG_DURABILITY_DAMAGE_DEATH)]
	private void HandleDurabilityDamageDeath(WorldPacket packet)
	{
		DurabilityDamageDeath death = new DurabilityDamageDeath();
		death.Percent = 10u;
		this.SendPacketToClient(death);
	}

	[PacketHandler(Opcode.SMSG_ITEM_COOLDOWN)]
	private void HandleItemCooldown(WorldPacket packet)
	{
		ItemCooldown item = new ItemCooldown();
		item.ItemGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		item.SpellID = packet.ReadUInt32();
		item.Cooldown = 30000u;
		this.SendPacketToClient(item);
	}

	[PacketHandler(Opcode.SMSG_SELL_RESPONSE)]
	private void HandleSellResponse(WorldPacket packet)
	{
		SellResponse sell = new SellResponse();
		sell.VendorGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		sell.ItemGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		sell.Reason = packet.ReadUInt8();
		Log.Print(LogType.Debug, $"[SellResponse] Item={sell.ItemGUID} Vendor={sell.VendorGUID} Reason={sell.Reason}", "HandleSellResponse", "");
		this.SendPacketToClient(sell);
	}

	[PacketHandler(Opcode.SMSG_ITEM_ENCHANT_TIME_UPDATE)]
	private void HandleItemEnchantTimeUpdate(WorldPacket packet)
	{
		ItemEnchantTimeUpdate enchant = new ItemEnchantTimeUpdate();
		enchant.ItemGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		enchant.Slot = packet.ReadUInt32();
		enchant.DurationLeft = packet.ReadUInt32();
		enchant.OwnerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(enchant);
	}

	[PacketHandler(Opcode.SMSG_ENCHANTMENT_LOG)]
	private void HandleEnchantmentLog(WorldPacket packet)
	{
		EnchantmentLog enchantment = new EnchantmentLog();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			enchantment.Owner = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			enchantment.Caster = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			enchantment.Owner = packet.ReadGuid().To128(this.GetSession().GameState);
			enchantment.Caster = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		enchantment.ItemID = packet.ReadInt32();
		GameSessionData session = this.GetSession().GameState;
		for (int i = 0; i < 23; i++)
		{
			if (session.GetItemId(session.GetInventorySlotItem(i).To128(session)).Equals((uint)enchantment.ItemID))
			{
				enchantment.ItemGUID = session.GetInventorySlotItem(i).To128(session);
				break;
			}
		}
		if (!(enchantment.ItemGUID == null))
		{
			enchantment.Enchantment = packet.ReadInt32();
			this.SendPacketToClient(enchantment);
		}
	}

	[PacketHandler(Opcode.SMSG_LOOT_LIST)]
	private void HandleLootList(WorldPacket packet)
	{
		LootList list = new LootList();
		WowGuid64 creatureGuid = packet.ReadGuid();
		list.Owner = creatureGuid.To128(this.GetSession().GameState);
		list.LootObj = creatureGuid.ToLootGuid();

		WowGuid64 masterLooter = packet.ReadPackedGuid();
		if (!masterLooter.IsEmpty())
			list.Master = masterLooter.To128(this.GetSession().GameState);

		WowGuid64 roundRobinWinner = packet.ReadPackedGuid();
		if (!roundRobinWinner.IsEmpty())
			list.RoundRobinWinner = roundRobinWinner.To128(this.GetSession().GameState);

		this.SendPacketToClient(list);
	}

	[PacketHandler(Opcode.SMSG_LOOT_RESPONSE)]
	private void HandleLootResponse(WorldPacket packet)
	{
		LootResponse loot = new LootResponse();
		this.GetSession().GameState.LastLootTargetGuid = packet.ReadGuid();
		loot.Owner = this.GetSession().GameState.LastLootTargetGuid.To128(this.GetSession().GameState);
		loot.LootObj = this.GetSession().GameState.LastLootTargetGuid.ToLootGuid();
		loot.AcquireReason = (LootType)packet.ReadUInt8();
		if (loot.AcquireReason == LootType.None)
		{
			loot.FailureReason = (LootError)packet.ReadUInt8();
			return;
		}
		loot.LootMethod = this.GetSession().GameState.GetCurrentLootMethod();
		loot.Coins = packet.ReadUInt32();
		byte itemsCount = packet.ReadUInt8();
		for (int i = 0; i < itemsCount; i++)
		{
			LootItemData lootItem = new LootItemData();
			lootItem.LootListID = packet.ReadUInt8();
			lootItem.Loot.ItemID = packet.ReadUInt32();
			lootItem.Quantity = packet.ReadUInt32();
			packet.ReadUInt32();
			lootItem.Loot.RandomPropertiesSeed = packet.ReadUInt32();
			lootItem.Loot.RandomPropertiesID = packet.ReadUInt32();
			LootSlotTypeLegacy uiType = (LootSlotTypeLegacy)packet.ReadUInt8();
			lootItem.UIType = (LootSlotTypeModern)Enum.Parse(typeof(LootSlotTypeModern), uiType.ToString());
			loot.Items.Add(lootItem);
		}
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_RELEASE)]
	private void HandleLootRelease(WorldPacket packet)
	{
		LootReleaseResponse loot = new LootReleaseResponse();
		WowGuid64 owner = packet.ReadGuid();
		loot.Owner = owner.To128(this.GetSession().GameState);
		loot.LootObj = owner.ToLootGuid();
		packet.ReadBool();
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_REMOVED)]
	private void HandleLootRemoved(WorldPacket packet)
	{
		LootRemoved loot = new LootRemoved();
		loot.Owner = this.GetSession().GameState.LastLootTargetGuid.To128(this.GetSession().GameState);
		loot.LootObj = this.GetSession().GameState.LastLootTargetGuid.ToLootGuid();
		loot.LootListID = packet.ReadUInt8();
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_MONEY_NOTIFY)]
	private void HandleLootMoneyNotify(WorldPacket packet)
	{
		LootMoneyNotify loot = new LootMoneyNotify();
		loot.Money = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			loot.SoleLooter = packet.ReadBool();
		}
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_CLEAR_MONEY)]
	private void HandleLootCelarMoney(WorldPacket packet)
	{
		CoinRemoved loot = new CoinRemoved();
		loot.LootObj = this.GetSession().GameState.LastLootTargetGuid.ToLootGuid();
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_START_ROLL)]
	private void HandleLootStartRoll(WorldPacket packet)
	{
		StartLootRoll loot = new StartLootRoll();
		WowGuid64 owner = packet.ReadGuid();
		loot.LootObj = owner.ToLootGuid();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			loot.MapID = packet.ReadUInt32();
		}
		else
		{
			loot.MapID = this.GetSession().GameState.CurrentMapId.Value;
		}
		loot.Item.LootListID = (byte)packet.ReadUInt32();
		loot.Item.Loot.ItemID = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesSeed = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesID = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			loot.Item.Quantity = packet.ReadUInt32();
		}
		else
		{
			loot.Item.Quantity = 1u;
		}
		loot.RollTime = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			loot.ValidRolls = (RollMask)packet.ReadUInt8();
		}
		else
		{
			loot.ValidRolls = RollMask.AllNoDisenchant;
		}
		this.SendPacketToClient(loot);
		if (this.GetSession().GameState.IsPassingOnLoot)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_LOOT_ROLL);
			packet2.WriteGuid(owner);
			packet2.WriteUInt32(loot.Item.LootListID);
			packet2.WriteUInt8(0);
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.SMSG_LOOT_ROLL)]
	private void HandleLootRoll(WorldPacket packet)
	{
		LootRollBroadcast loot = new LootRollBroadcast();
		WowGuid64 owner = packet.ReadGuid();
		loot.LootObj = owner.ToLootGuid();
		loot.Item.LootListID = (byte)packet.ReadUInt32();
		loot.Player = packet.ReadGuid().To128(this.GetSession().GameState);
		loot.Item.Loot.ItemID = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesSeed = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesID = packet.ReadUInt32();
		loot.Item.Quantity = 1u;
		loot.Roll = packet.ReadUInt8();
		byte rollType = packet.ReadUInt8();
		if (loot.Roll == 128 && rollType == 128)
		{
			loot.RollType = RollType.Pass;
		}
		else if (loot.Roll == 0 && rollType == 0)
		{
			loot.RollType = RollType.Need;
		}
		else
		{
			loot.RollType = (RollType)rollType;
		}
		if (loot.Roll == 128)
		{
			loot.Roll = 0;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			loot.Autopassed = packet.ReadBool();
		}
		this.SendPacketToClient(loot);
	}

	[PacketHandler(Opcode.SMSG_LOOT_ROLL_WON)]
	private void HandleLootRollWon(WorldPacket packet)
	{
		LootRollWon loot = new LootRollWon();
		loot.LootObj = packet.ReadGuid().ToLootGuid();
		loot.Item.LootListID = (byte)packet.ReadUInt32();
		loot.Item.Loot.ItemID = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesSeed = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesID = packet.ReadUInt32();
		loot.Item.Quantity = 1u;
		loot.Winner = packet.ReadGuid().To128(this.GetSession().GameState);
		loot.Roll = packet.ReadUInt8();
		loot.RollType = (RollType)packet.ReadUInt8();
		if (loot.RollType == RollType.Need)
		{
			loot.MainSpec = 128;
		}
		this.SendPacketToClient(loot);
		LootRollsComplete complete = new LootRollsComplete();
		complete.LootObj = loot.LootObj;
		complete.LootListID = loot.Item.LootListID;
		this.SendPacketToClient(complete);
	}

	[PacketHandler(Opcode.SMSG_LOOT_ALL_PASSED)]
	private void HandleLootAllPassed(WorldPacket packet)
	{
		LootAllPassed loot = new LootAllPassed();
		loot.LootObj = packet.ReadGuid().ToLootGuid();
		loot.Item.LootListID = (byte)packet.ReadUInt32();
		loot.Item.Loot.ItemID = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesSeed = packet.ReadUInt32();
		loot.Item.Loot.RandomPropertiesID = packet.ReadUInt32();
		loot.Item.Quantity = 1u;
		this.SendPacketToClient(loot);
		LootRollsComplete complete = new LootRollsComplete();
		complete.LootObj = loot.LootObj;
		complete.LootListID = loot.Item.LootListID;
		this.SendPacketToClient(complete);
	}

	[PacketHandler(Opcode.SMSG_LOOT_MASTER_LIST)]
	private void HandleLootMasterList(WorldPacket packet)
	{
		if (!(this.GetSession().GameState.LastLootTargetGuid == null))
		{
			LootList list = new LootList();
			list.Owner = this.GetSession().GameState.LastLootTargetGuid.To128(this.GetSession().GameState);
			list.LootObj = this.GetSession().GameState.LastLootTargetGuid.ToLootGuid();
			list.Master = this.GetSession().GameState.CurrentPlayerGuid;
			this.SendPacketToClient(list);
			MasterLootCandidateList loot = new MasterLootCandidateList();
			loot.LootObj = this.GetSession().GameState.LastLootTargetGuid.ToLootGuid();
			byte count = packet.ReadUInt8();
			for (byte i = 0; i < count; i++)
			{
				WowGuid128 guid = packet.ReadGuid().To128(this.GetSession().GameState);
				loot.Players.Add(guid);
			}
			this.SendPacketToClient(loot);
		}
	}

	[PacketHandler(Opcode.SMSG_NOTIFY_RECEIVED_MAIL)]
	private void HandleNotifyReceivedMail(WorldPacket packet)
	{
		NotifyReceivedMail mail = new NotifyReceivedMail();
		mail.Delay = packet.ReadFloat();
		this.SendPacketToClient(mail);
	}

	[PacketHandler(Opcode.MSG_QUERY_NEXT_MAIL_TIME)]
	private void HandleQueryNextMailTime(WorldPacket packet)
	{
		MailQueryNextTimeResult result = new MailQueryNextTimeResult();
		result.NextMailTime = packet.ReadFloat();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_3_0_7561))
		{
			if (result.NextMailTime == 0f)
			{
				MailQueryNextTimeResult.MailNextTimeEntry mail = new MailQueryNextTimeResult.MailNextTimeEntry();
				mail.SenderGuid = this.GetSession().GameState.CurrentPlayerGuid;
				mail.AltSenderID = 0;
				mail.AltSenderType = 0;
				mail.StationeryID = 41;
				mail.TimeLeft = 3600f;
				result.Mails.Add(mail);
			}
		}
		else
		{
			uint count = packet.ReadUInt32();
			for (int i = 0; i < count; i++)
			{
				MailQueryNextTimeResult.MailNextTimeEntry mail2 = new MailQueryNextTimeResult.MailNextTimeEntry();
				mail2.SenderGuid = packet.ReadGuid().To128(this.GetSession().GameState);
				mail2.AltSenderID = packet.ReadInt32();
				mail2.AltSenderType = (sbyte)packet.ReadInt32();
				mail2.StationeryID = packet.ReadInt32();
				mail2.TimeLeft = packet.ReadFloat();
				result.Mails.Add(mail2);
			}
		}
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.SMSG_MAIL_LIST_RESULT)]
	private void HandleMailListResult(WorldPacket packet)
	{
		MailListResult result = new MailListResult();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			result.TotalNumRecords = packet.ReadInt32();
		}
		byte count = packet.ReadUInt8();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			result.TotalNumRecords = count;
		}
		for (int i = 0; i < count; i++)
		{
			MailListEntry mail = new MailListEntry();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				packet.ReadUInt16();
			}
			mail.MailID = packet.ReadInt32();
			mail.SenderType = (MailType)packet.ReadUInt8();
			switch (mail.SenderType)
			{
			case MailType.Normal:
				mail.SenderCharacter = packet.ReadGuid().To128(this.GetSession().GameState);
				break;
			case MailType.Item:
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					mail.AltSenderID = packet.ReadUInt32();
				}
				break;
			default:
				mail.AltSenderID = packet.ReadUInt32();
				break;
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				mail.Cod = packet.ReadUInt32();
			}
			else
			{
				mail.Subject = packet.ReadCString();
			}
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_3_0_10958))
			{
				mail.ItemTextId = packet.ReadUInt32();
				if (mail.ItemTextId != 0 && !this.GetSession().GameState.ItemTexts.ContainsKey(mail.ItemTextId))
				{
					this.GetSession().GameState.RequestedItemTextIds.Add(mail.ItemTextId);
					WorldPacket query = new WorldPacket(Opcode.CMSG_ITEM_TEXT_QUERY);
					query.WriteUInt32(mail.ItemTextId);
					query.WriteInt32(mail.MailID);
					query.WriteUInt32(0u);
					this.SendPacket(query);
				}
			}
			packet.ReadUInt32();
			mail.StationeryID = packet.ReadInt32();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				MailAttachedItem mailItem = this.ReadMailItem(packet);
				if (mailItem.Item.ItemID != 0)
				{
					mailItem.AttachID = 1;
					mail.Attachments.Add(mailItem);
				}
			}
			mail.SentMoney = packet.ReadUInt32();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				mail.Cod = packet.ReadUInt32();
			}
			mail.Flags = packet.ReadUInt32();
			mail.DaysLeft = packet.ReadFloat();
			mail.MailTemplateID = packet.ReadInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				mail.Subject = packet.ReadCString();
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
			{
				mail.Body = packet.ReadCString();
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				byte itemsCount = packet.ReadUInt8();
				for (int j = 0; j < itemsCount; j++)
				{
					MailAttachedItem mailItem2 = this.ReadMailItem(packet);
					mail.Attachments.Add(mailItem2);
				}
			}
			result.Mails.Add(mail);
		}
		if (this.GetSession().GameState.RequestedItemTextIds.Count == 0)
		{
			foreach (MailListEntry mail2 in result.Mails)
			{
				if (mail2.ItemTextId != 0)
				{
					mail2.Body = this.GetSession().GameState.ItemTexts[mail2.ItemTextId];
				}
			}
			this.SendPacketToClient(result);
		}
		else
		{
			this.GetSession().GameState.PendingMailListPacket = result;
		}
	}

	[PacketHandler(Opcode.SMSG_QUERY_ITEM_TEXT_RESPONSE)]
	private void HandleQueryItemTextResponse(WorldPacket packet)
	{
		uint itemTextId = packet.ReadUInt32();
		string text = packet.ReadCString();
		if (this.GetSession().GameState.ItemTexts.ContainsKey(itemTextId))
		{
			this.GetSession().GameState.ItemTexts[itemTextId] = text;
		}
		else
		{
			this.GetSession().GameState.ItemTexts.Add(itemTextId, text);
		}
		if (this.GetSession().GameState.RequestedItemTextIds.Contains(itemTextId))
		{
			this.GetSession().GameState.RequestedItemTextIds.Remove(itemTextId);
		}
		if (this.GetSession().GameState.PendingMailListPacket == null || this.GetSession().GameState.RequestedItemTextIds.Count != 0)
		{
			return;
		}
		MailListResult result = this.GetSession().GameState.PendingMailListPacket;
		foreach (MailListEntry mail in result.Mails)
		{
			if (mail.ItemTextId != 0)
			{
				mail.Body = this.GetSession().GameState.ItemTexts[mail.ItemTextId];
			}
		}
		this.SendPacketToClient(result);
	}

	private MailAttachedItem ReadMailItem(WorldPacket packet)
	{
		MailAttachedItem mailItem = new MailAttachedItem();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			mailItem.Position = packet.ReadUInt8();
			mailItem.AttachID = packet.ReadInt32();
		}
		mailItem.Item.ItemID = packet.ReadUInt32();
		byte enchantmentCount = (byte)(LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? 7 : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? 1 : 6));
		for (byte k = 0; k < enchantmentCount; k++)
		{
			ItemEnchantData enchant = new ItemEnchantData();
			enchant.Slot = k;
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				enchant.Charges = packet.ReadInt32();
				enchant.Expiration = packet.ReadUInt32();
			}
			enchant.ID = packet.ReadUInt32();
			if (enchant.ID != 0)
			{
				mailItem.Enchants.Add(enchant);
			}
		}
		mailItem.Item.RandomPropertiesID = packet.ReadUInt32();
		mailItem.Item.RandomPropertiesSeed = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			mailItem.Count = (byte)packet.ReadUInt32();
		}
		else
		{
			mailItem.Count = packet.ReadUInt8();
		}
		mailItem.Charges = packet.ReadInt32();
		mailItem.MaxDurability = packet.ReadUInt32();
		mailItem.Durability = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			mailItem.Unlocked = packet.ReadBool();
		}
		return mailItem;
	}

	[PacketHandler(Opcode.SMSG_MAIL_COMMAND_RESULT)]
	private void HandleMailCommandResult(WorldPacket packet)
	{
		MailCommandResult mail = new MailCommandResult();
		mail.MailID = packet.ReadUInt32();
		mail.Command = (MailActionType)packet.ReadUInt32();
		mail.ErrorCode = (MailErrorType)packet.ReadUInt32();
		if (mail.ErrorCode == MailErrorType.Equip)
		{
			mail.BagResult = LegacyVersion.ConvertInventoryResult(packet.ReadUInt32());
		}
		else if (mail.Command == MailActionType.AttachmentExpired)
		{
			mail.AttachID = packet.ReadUInt32();
			mail.QtyInInventory = packet.ReadUInt32();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				mail.AttachID = 1u;
			}
		}
		this.SendPacketToClient(mail);
	}

	[PacketHandler(Opcode.SMSG_PONG)]
	private void HandlePingResponse(WorldPacket packet)
	{
		uint serial = packet.ReadUInt32();
		this.SendPacketToClient(new Pong(serial));
	}

	[PacketHandler(Opcode.SMSG_TUTORIAL_FLAGS)]
	private void HandleTutorialFlags(WorldPacket packet)
	{
		TutorialFlags tutorials = new TutorialFlags();
		for (byte i = 0; i < 8; i++)
		{
			tutorials.TutorialData[i] = packet.ReadUInt32();
		}
		this.SendPacketToClient(tutorials);
	}

	[PacketHandler(Opcode.SMSG_ACCOUNT_DATA_TIMES)]
	private void HandleAccountDataTimes(WorldPacket packet)
	{
		this.GetSession().RealmSocket.SendAccountDataTimes();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			this.GetSession().RealmSocket.SendFeatureSystemStatus();
			this.GetSession().RealmSocket.SendMotd();
			this.GetSession().RealmSocket.SendSetTimeZoneInformation();
			this.GetSession().RealmSocket.SendSeasonInfo();
		}
	}

	[PacketHandler(Opcode.SMSG_BIND_POINT_UPDATE)]
	private void HandleBindPointUpdate(WorldPacket packet)
	{
		BindPointUpdate point = new BindPointUpdate();
		point.BindPosition = packet.ReadVector3();
		point.BindMapID = packet.ReadUInt32();
		point.BindAreaID = packet.ReadUInt32();
		this.SendPacketToClient(point);
	}

	[PacketHandler(Opcode.SMSG_PLAYER_BOUND)]
	private void HandlePlayerBound(WorldPacket packet)
	{
		PlayerBound bound = new PlayerBound();
		bound.BinderGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		bound.AreaID = packet.ReadUInt32();
		this.SendPacketToClient(bound);
	}

	[PacketHandler(Opcode.SMSG_DEATH_RELEASE_LOC)]
	private void HandleDeathReleaseLoc(WorldPacket packet)
	{
		DeathReleaseLoc death = new DeathReleaseLoc();
		death.MapID = packet.ReadInt32();
		death.Location = packet.ReadVector3();
		Log.Print(LogType.Debug, $"[DeathReleaseLoc] MapID={death.MapID} Pos={death.Location}", "HandleDeathReleaseLoc", "");
		this.SendPacketToClient(death);
	}

	[PacketHandler(Opcode.SMSG_PRE_RESSURECT)]
	private void HandlePreResurrect(WorldPacket packet)
	{
		WowGuid128 playerGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		PreRessurect preRes = new PreRessurect();
		preRes.PlayerGUID = playerGuid;
		this.SendPacketToClient(preRes);
	}

	[PacketHandler(Opcode.SMSG_CORPSE_RECLAIM_DELAY)]
	private void HandleCorpseReclaimDelay(WorldPacket packet)
	{
		CorpseReclaimDelay delay = new CorpseReclaimDelay();
		delay.Remaining = packet.ReadUInt32();
		this.SendPacketToClient(delay);
	}

	[PacketHandler(Opcode.SMSG_TIME_SYNC_REQUEST)]
	private void HandleTimeSyncRequest(WorldPacket packet)
	{
		TimeSyncRequest sync = new TimeSyncRequest();
		sync.SequenceIndex = packet.ReadUInt32();
		this.SendPacketToClient(sync);
	}

	[PacketHandler(Opcode.SMSG_WEATHER)]
	private void HandleWeather(WorldPacket packet)
	{
		WeatherPkt weather = new WeatherPkt();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WeatherType type = (WeatherType)packet.ReadUInt32();
			weather.Intensity = packet.ReadFloat();
			weather.WeatherID = Weather.ConvertWeatherTypeToWeatherState(type, weather.Intensity);
			packet.ReadUInt32();
			if (packet.CanRead())
			{
				weather.Abrupt = packet.ReadBool();
			}
		}
		else
		{
			weather.WeatherID = (WeatherState)packet.ReadUInt32();
			weather.Intensity = packet.ReadFloat();
			weather.Abrupt = packet.ReadBool();
		}
		this.SendPacketToClient(weather);
		this.SendPacketToClient(new StartLightningStorm());
	}

	[PacketHandler(Opcode.SMSG_LOGIN_SET_TIME_SPEED)]
	private void HandleLoginSetTimeSpeed(WorldPacket packet)
	{
		if (this.GetSession().GameState.IsFirstEnterWorld)
		{
			LoginSetTimeSpeed login = new LoginSetTimeSpeed();
			login.ServerTime = packet.ReadUInt32();
			login.GameTime = login.ServerTime;
			login.NewSpeed = packet.ReadFloat();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901))
			{
				login.ServerTimeHolidayOffset = packet.ReadInt32();
				login.GameTimeHolidayOffset = login.ServerTimeHolidayOffset;
			}
			this.SendPacketToClient(login);
		}
	}

	[PacketHandler(Opcode.SMSG_AREA_TRIGGER_MESSAGE)]
	private void HandleAreaTriggerMessage(WorldPacket packet)
	{
		uint length = packet.ReadUInt32();
		string message = packet.ReadString(length);
		if (this.GetSession().GameState.LastEnteredAreaTrigger != 0)
		{
			AreaTriggerMessage denied = new AreaTriggerMessage();
			denied.AreaTriggerID = this.GetSession().GameState.LastEnteredAreaTrigger;
			this.SendPacketToClient(denied);
		}
		else
		{
			ChatPkt chat = new ChatPkt(this.GetSession(), ChatMessageTypeModern.System, message);
			this.SendPacketToClient(chat);
		}
	}

	[PacketHandler(Opcode.MSG_CORPSE_QUERY)]
	private void HandleCorpseQuery(WorldPacket packet)
	{
		CorpseLocation corpse = new CorpseLocation
		{
			Player = this.GetSession().GameState.CurrentPlayerGuid,
			Transport = WowGuid128.Empty
		};
		corpse.Valid = packet.ReadBool();
		if (corpse.Valid)
		{
			corpse.ActualMapID = packet.ReadInt32();
			corpse.Position = packet.ReadVector3();
			corpse.MapID = packet.ReadInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_2_10482))
			{
				packet.ReadInt32();
			}
		}
		else
		{
			corpse.MapID = (corpse.ActualMapID = (int)this.GetSession().GameState.CurrentMapId.Value);
		}
		this.SendPacketToClient(corpse);
	}

	[PacketHandler(Opcode.SMSG_STAND_STATE_UPDATE)]
	private void HandleStandStateUpdate(WorldPacket packet)
	{
		StandStateUpdate state = new StandStateUpdate();
		state.StandState = packet.ReadUInt8();
		this.SendPacketToClient(state);
	}

	[PacketHandler(Opcode.SMSG_EXPLORATION_EXPERIENCE)]
	private void HandleExplorationExperience(WorldPacket packet)
	{
		ExplorationExperience explore = new ExplorationExperience();
		explore.AreaID = packet.ReadUInt32();
		explore.Experience = packet.ReadUInt32();
		this.SendPacketToClient(explore);
	}

	[PacketHandler(Opcode.SMSG_PLAY_MUSIC)]
	private void HandlePlayMusic(WorldPacket packet)
	{
		PlayMusic music = new PlayMusic();
		music.SoundEntryID = packet.ReadUInt32();
		this.SendPacketToClient(music);
	}

	[PacketHandler(Opcode.SMSG_PLAY_SOUND)]
	private void HandlePlaySound(WorldPacket packet)
	{
		PlaySound sound = new PlaySound();
		sound.SoundEntryID = packet.ReadUInt32();
		sound.SourceObjectGuid = this.GetSession().GameState.CurrentPlayerGuid;
		this.SendPacketToClient(sound);
	}

	[PacketHandler(Opcode.SMSG_PLAY_OBJECT_SOUND)]
	private void HandlePlayObjectSound(WorldPacket packet)
	{
		PlayObjectSound sound = new PlayObjectSound();
		sound.SoundEntryID = packet.ReadUInt32();
		sound.SourceObjectGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		sound.TargetObjectGUID = sound.SourceObjectGUID;
		this.SendPacketToClient(sound);
	}

	[PacketHandler(Opcode.SMSG_TRIGGER_CINEMATIC)]
	private void HandleTriggerCinematic(WorldPacket packet)
	{
		TriggerCinematic cinematic = new TriggerCinematic();
		cinematic.CinematicID = packet.ReadUInt32();
		this.SendPacketToClient(cinematic);
	}

	[PacketHandler(Opcode.SMSG_SPECIAL_MOUNT_ANIM)]
	private void HandleSpecialMountAnim(WorldPacket packet)
	{
		SpecialMountAnim mount = new SpecialMountAnim();
		mount.UnitGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(mount);
	}

	[PacketHandler(Opcode.SMSG_START_MIRROR_TIMER)]
	private void HandleStartMirrorTimer(WorldPacket packet)
	{
		StartMirrorTimer timer = new StartMirrorTimer();
		timer.Timer = (MirrorTimerType)packet.ReadUInt32();
		timer.Value = packet.ReadInt32();
		timer.MaxValue = packet.ReadInt32();
		timer.Scale = packet.ReadInt32();
		timer.Paused = packet.ReadBool();
		timer.SpellID = packet.ReadInt32();
		this.SendPacketToClient(timer);
	}

	[PacketHandler(Opcode.SMSG_PAUSE_MIRROR_TIMER)]
	private void HandlePauseMirrorTimer(WorldPacket packet)
	{
		PauseMirrorTimer timer = new PauseMirrorTimer();
		timer.Timer = (MirrorTimerType)packet.ReadUInt32();
		timer.Paused = packet.ReadBool();
		this.SendPacketToClient(timer);
	}

	[PacketHandler(Opcode.SMSG_STOP_MIRROR_TIMER)]
	private void HandleStopMirrorTimer(WorldPacket packet)
	{
		StopMirrorTimer timer = new StopMirrorTimer();
		timer.Timer = (MirrorTimerType)packet.ReadUInt32();
		this.SendPacketToClient(timer);
	}

	[PacketHandler(Opcode.SMSG_INVALIDATE_PLAYER)]
	private void HandleInvalidatePlayer(WorldPacket packet)
	{
		InvalidatePlayer invalidate = new InvalidatePlayer();
		invalidate.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(invalidate);
		if (this.GetSession().GameState.CachedPlayers.ContainsKey(invalidate.Guid))
		{
			this.GetSession().GameState.CachedPlayers.Remove(invalidate.Guid);
		}
	}

	[PacketHandler(Opcode.SMSG_ZONE_UNDER_ATTACK)]
	private void HandleZoneUnderAttack(WorldPacket packet)
	{
		ZoneUnderAttack zone = new ZoneUnderAttack();
		zone.AreaID = packet.ReadInt32();
		this.SendPacketToClient(zone);
	}

	[PacketHandler(Opcode.MSG_SET_DUNGEON_DIFFICULTY)]
	private void HandleSetDungeonDifficulty(WorldPacket packet)
	{
		DungeonDifficultySet difficulty = new DungeonDifficultySet();
		int difficultyId = packet.ReadInt32();
		difficulty.DifficultyID = (byte)Enum.Parse(typeof(DifficultyModern), ((DifficultyLegacy)difficultyId/*cast due to .constrained prefix*/).ToString());
		packet.ReadInt32();
		packet.ReadInt32();
		this.SendPacketToClient(difficulty);
	}

	[PacketHandler(Opcode.SMSG_POWER_UPDATE)]
	private void HandlePowerUpdate(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			byte powerType = packet.ReadUInt8();
			int powerValue = packet.ReadInt32();
			PowerUpdate update = new PowerUpdate(guid);
			update.Powers.Add(new PowerUpdatePower(powerValue, powerType));
			this.SendPacketToClient(update);
		}
	}

	[PacketHandler(Opcode.SMSG_UPDATE_TALENT_DATA)]
	private void HandleUpdateTalentData(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			byte isPet = packet.ReadUInt8();
			if (isPet != 0)
			{
				// Pet talents - skip for now
				return;
			}

			UpdateTalentData talentData = new UpdateTalentData();
			talentData.IsPetTalents = false;
			talentData.UnspentTalentPoints = packet.ReadUInt32();
			byte specsCount = packet.ReadUInt8();
			talentData.ActiveGroup = packet.ReadUInt8();

			for (byte spec = 0; spec < specsCount; spec++)
			{
				TalentGroupInfoData group = new TalentGroupInfoData();
				group.SpecID = 4; // MAX_SPECIALIZATIONS - sentinel for "no spec" in WotLK

				byte talentCount = packet.ReadUInt8();
				for (byte t = 0; t < talentCount; t++)
				{
					TalentInfoData talent = new TalentInfoData();
					talent.TalentID = packet.ReadUInt32();
					talent.Rank = packet.ReadUInt8();
					group.Talents.Add(talent);
				}

				byte glyphCount = packet.ReadUInt8();
				for (byte g = 0; g < glyphCount; g++)
				{
					group.GlyphIDs.Add(packet.ReadUInt16());
				}

				talentData.TalentGroups.Add(group);
			}

			// Compute total talent points (unspent + spent) and store for update fields
			int spentPoints = 0;
			foreach (var group2 in talentData.TalentGroups)
				foreach (var talent in group2.Talents)
					spentPoints += talent.Rank + 1; // rank is 0-based
			int totalPoints = (int)talentData.UnspentTalentPoints + spentPoints;
			this.GetSession().GameState.TotalTalentPoints = totalPoints;

			// Compute GlyphsEnabled from level (level = totalPoints + 9)
			int level = totalPoints + 9;
			byte glyphsEnabled = 0;
			if (level >= 15) glyphsEnabled |= 0x01 | 0x02; // Major slot 0 + Minor slot 1
			if (level >= 30) glyphsEnabled |= 0x08;         // Major slot 3
			if (level >= 50) glyphsEnabled |= 0x04;         // Major slot 2
			if (level >= 70) glyphsEnabled |= 0x10;         // Minor slot 4
			if (level >= 80) glyphsEnabled |= 0x20;         // Minor slot 5
			this.GetSession().GameState.GlyphsEnabled = glyphsEnabled;

			// Store active glyphs from active spec
			if (talentData.TalentGroups.Count > talentData.ActiveGroup)
			{
				var activeGroup = talentData.TalentGroups[talentData.ActiveGroup];
				for (int g = 0; g < activeGroup.GlyphIDs.Count && g < 6; g++)
					this.GetSession().GameState.ActiveGlyphs[g] = activeGroup.GlyphIDs[g];
			}

			this.SendPacketToClient(talentData);
		}
	}

	[PacketHandler(Opcode.SMSG_ALL_ACHIEVEMENT_DATA)]
	private void HandleAllAchievementData(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			AllAchievementData data = new AllAchievementData();
			uint realmAddress = this.GetSession().RealmId.GetAddress();
			WowGuid128 playerGuid = this.GetSession().GameState.CurrentPlayerGuid;

			// 3.3.5a format: earned achievements (terminated by -1), then criteria progress (terminated by -1)
			// Earned achievements
			while (true)
			{
				uint achievementId = packet.ReadUInt32();
				if (achievementId == 0xFFFFFFFF) // -1 terminator
					break;
				uint datePackedTime = packet.ReadUInt32();
				long dateUnix = (long)Time.GetUnixTimeFromPackedTime(datePackedTime);

				EarnedAchievement earned = new EarnedAchievement();
				earned.Id = achievementId;
				earned.Date = dateUnix;
				earned.Owner = playerGuid;
				earned.VirtualRealmAddress = realmAddress;
				earned.NativeRealmAddress = realmAddress;
				data.Earned.Add(earned);
			}

			// Criteria progress
			while (true)
			{
				uint criteriaId = packet.ReadUInt32();
				if (criteriaId == 0xFFFFFFFF) // -1 terminator
					break;
				ulong counter = packet.ReadPackedGuid().Low; // counter packed as guid
				WowGuid64 playerGuid64 = packet.ReadPackedGuid();
				uint timedFlag = packet.ReadUInt32();
				uint datePackedTime = packet.ReadUInt32();
				long dateUnix = (long)Time.GetUnixTimeFromPackedTime(datePackedTime);
				uint timeFromStart = packet.ReadUInt32();
				uint timeFromCreate = packet.ReadUInt32();

				CriteriaProgressPkt progress = new CriteriaProgressPkt();
				progress.Id = criteriaId;
				progress.Quantity = counter;
				progress.Player = playerGuid;
				progress.Flags = timedFlag;
				progress.Date = dateUnix;
				progress.TimeFromStart = timeFromStart;
				progress.TimeFromCreate = timeFromCreate;
				data.Progress.Add(progress);
			}

			this.SendPacketToClient(data);
		}
	}

	[PacketHandler(Opcode.SMSG_CRITERIA_UPDATE)]
	private void HandleCriteriaUpdate(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			CriteriaUpdatePkt update = new CriteriaUpdatePkt();
			update.CriteriaID = packet.ReadUInt32();
			update.Quantity = packet.ReadPackedGuid().Low; // counter packed as guid
			WowGuid64 playerGuid64 = packet.ReadPackedGuid();
			update.Flags = packet.ReadUInt32(); // timed flag
			uint datePackedTime = packet.ReadUInt32();
			update.CurrentTime = (long)Time.GetUnixTimeFromPackedTime(datePackedTime);
			update.ElapsedTime = packet.ReadUInt32();
			update.CreationTime = packet.ReadUInt32();
			update.PlayerGUID = this.GetSession().GameState.CurrentPlayerGuid ?? WowGuid128.Empty;
			this.SendPacketToClient(update);
		}
	}

	[PacketHandler(Opcode.SMSG_ACHIEVEMENT_EARNED)]
	private void HandleAchievementEarned(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			AchievementEarnedPkt earned = new AchievementEarnedPkt();
			WowGuid64 playerGuid64 = packet.ReadPackedGuid();
			earned.AchievementID = packet.ReadUInt32();
			uint datePackedTime = packet.ReadUInt32();
			earned.Time = (long)Time.GetUnixTimeFromPackedTime(datePackedTime);
			packet.ReadUInt32(); // unknown/reserved (0)

			uint realmAddress = this.GetSession().RealmId.GetAddress();
			earned.Sender = this.GetSession().GameState.CurrentPlayerGuid;
			earned.Earner = playerGuid64.To128(this.GetSession().GameState);
			earned.EarnerNativeRealm = realmAddress;
			earned.EarnerVirtualRealm = realmAddress;
			earned.Initial = false;
			this.SendPacketToClient(earned);
		}
	}

	[PacketHandler(Opcode.SMSG_LOAD_EQUIPMENT_SET)]
	private void HandleLoadEquipmentSet(WorldPacket packet)
	{
	}

	[PacketHandler(Opcode.SMSG_INSTANCE_DIFFICULTY)]
	private void HandleInstanceDifficulty(WorldPacket packet)
	{
	}

	[PacketHandler(Opcode.SMSG_LFG_PLAYER_INFO)]
	private void HandleLfgPlayerInfo(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			LfgPlayerInfoPkt info = new LfgPlayerInfoPkt();

			// 3.3.5a format: random dungeons, then locked dungeons
			// Random dungeons (available)
			byte randomCount = packet.ReadUInt8();
			for (int i = 0; i < randomCount; i++)
			{
				LfgPlayerDungeonInfo dungeon = new LfgPlayerDungeonInfo();
				dungeon.Slot = packet.ReadUInt32(); // dungeon entry (id + type)
				bool isDone = packet.ReadUInt8() != 0;
				dungeon.FirstReward = !isDone;
				dungeon.CompletionQuantity = isDone ? 1 : 0;
				dungeon.CompletionLimit = 1;

				LfgPlayerQuestReward rewards = new LfgPlayerQuestReward();
				rewards.Items = new List<LfgPlayerQuestRewardItem>();
				rewards.Currency = new List<LfgPlayerQuestRewardCurrency>();
				rewards.BonusCurrency = new List<LfgPlayerQuestRewardCurrency>();
				rewards.RewardMoney = (int)packet.ReadUInt32();
				rewards.RewardXP = (int)packet.ReadUInt32();
				packet.ReadUInt32(); // unknown
				packet.ReadUInt32(); // unknown
				byte itemCount = packet.ReadUInt8();
				for (int j = 0; j < itemCount; j++)
				{
					LfgPlayerQuestRewardItem item = new LfgPlayerQuestRewardItem();
					item.ItemID = (int)packet.ReadUInt32();
					packet.ReadUInt32(); // displayInfo
					item.Quantity = (int)packet.ReadUInt32();
					rewards.Items.Add(item);
				}
				dungeon.Rewards = rewards;
				info.Dungeons.Add(dungeon);
			}

			// Locked dungeons (blacklist)
			LfgBlackList blackList = new LfgBlackList();
			blackList.Slots = new List<LfgBlackListSlot>();
			uint lockCount = packet.ReadUInt32();
			for (uint i = 0; i < lockCount; i++)
			{
				LfgBlackListSlot slot = new LfgBlackListSlot();
				slot.Slot = packet.ReadUInt32(); // dungeon entry
				slot.Reason = packet.ReadUInt32(); // lock status
				blackList.Slots.Add(slot);
			}
			info.BlackList = blackList;

			this.SendPacketToClient(info);
		}
	}

	[PacketHandler(Opcode.SMSG_LFG_JOIN_RESULT)]
	private void HandleLfgJoinResult(WorldPacket packet)
	{
		DfJoinResult result = new DfJoinResult();
		result.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		result.Ticket.Id = 1;
		result.Ticket.Type = RideType.Lfg;
		result.Ticket.Time = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		result.Result = (byte)packet.ReadUInt32(); // joinData.result
		result.ResultDetail = (byte)packet.ReadUInt32(); // joinData.state
		if (packet.CanRead())
		{
			byte partySize = packet.ReadUInt8();
			for (int p = 0; p < partySize; p++)
			{
				DfJoinBlackList blackList = new DfJoinBlackList();
				blackList.PlayerGuid = packet.ReadGuid().To128(this.GetSession().GameState);
				uint dungeonCount = packet.ReadUInt32();
				for (uint d = 0; d < dungeonCount; d++)
				{
					DfJoinBlackListSlot slot = new DfJoinBlackListSlot();
					slot.Slot = packet.ReadUInt32();
					slot.Reason = packet.ReadUInt32();
					blackList.Slots.Add(slot);
				}
				result.BlackList.Add(blackList);
			}
		}
		this.SendPacketToClient(result);
	}

	[PacketHandler(Opcode.SMSG_LFG_UPDATE_PLAYER)]
	private void HandleLfgUpdatePlayer(WorldPacket packet)
	{
		DfUpdateStatus status = new DfUpdateStatus();
		status.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		status.Ticket.Id = 1;
		status.Ticket.Type = RideType.Lfg;
		status.Ticket.Time = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		byte updateType = packet.ReadUInt8();
		status.SubType = updateType;
		bool hasExtraInfo = packet.ReadUInt8() != 0;
		if (hasExtraInfo)
		{
			status.Queued = packet.ReadUInt8() != 0;
			packet.ReadUInt8(); // unk
			packet.ReadUInt8(); // unk
			byte dungeonCount = packet.ReadUInt8();
			for (int i = 0; i < dungeonCount; i++)
			{
				status.Slots.Add(packet.ReadUInt32());
			}
			status.Joined = true;
			status.LfgJoined = true;
			status.NotifyUI = true;
			packet.ReadCString(); // comment - not used in modern
		}
		this.SendPacketToClient(status);
	}

	[PacketHandler(Opcode.SMSG_LFG_QUEUE_STATUS)]
	private void HandleLfgQueueStatus(WorldPacket packet)
	{
		DfQueueStatus status = new DfQueueStatus();
		status.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		status.Ticket.Id = 1;
		status.Ticket.Type = RideType.Lfg;
		status.Ticket.Time = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		status.Slot = packet.ReadUInt32();
		status.AvgWaitTime = (uint)packet.ReadInt32();
		status.AvgWaitTimeMe = (uint)packet.ReadInt32();
		status.AvgWaitTimeByRole[0] = (uint)packet.ReadInt32(); // Tank
		status.AvgWaitTimeByRole[1] = (uint)packet.ReadInt32(); // Healer
		status.AvgWaitTimeByRole[2] = (uint)packet.ReadInt32(); // DPS
		status.LastNeeded[0] = packet.ReadUInt8(); // Tanks needed
		status.LastNeeded[1] = packet.ReadUInt8(); // Healers needed
		status.LastNeeded[2] = packet.ReadUInt8(); // DPS needed
		status.QueuedTime = packet.ReadUInt32();
		this.SendPacketToClient(status);
	}

	[PacketHandler(Opcode.SMSG_LFG_PROPOSAL_UPDATE)]
	private void HandleLfgProposalUpdate(WorldPacket packet)
	{
		DfProposalUpdate proposal = new DfProposalUpdate();
		proposal.Ticket.RequesterGuid = this.GetSession().GameState.CurrentPlayerGuid;
		proposal.Ticket.Id = 1;
		proposal.Ticket.Type = RideType.Lfg;
		proposal.Ticket.Time = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		uint dungeonEntry = packet.ReadUInt32();
		proposal.Slot = dungeonEntry;
		proposal.State = (sbyte)packet.ReadUInt8();
		proposal.ProposalID = packet.ReadUInt32();
		proposal.CompletedMask = packet.ReadUInt32();
		bool silent = packet.ReadUInt8() != 0;
		proposal.ProposalSilent = silent;
		byte playerCount = packet.ReadUInt8();
		for (int i = 0; i < playerCount; i++)
		{
			DfProposalPlayer player = new DfProposalPlayer();
			player.Roles = (byte)packet.ReadUInt32();
			player.Me = packet.ReadUInt8() != 0;
			bool inDungeon = packet.ReadUInt8() != 0;
			bool sameGroup = packet.ReadUInt8() != 0;
			player.SameParty = sameGroup;
			player.MyParty = inDungeon;
			player.Responded = packet.ReadUInt8() != 0;
			player.Accepted = packet.ReadUInt8() != 0;
			proposal.Players.Add(player);
		}
		this.SendPacketToClient(proposal);
	}

	[PacketHandler(Opcode.SMSG_LEARNED_DANCE_MOVES)]
	private void HandleLearnedDanceMoves(WorldPacket packet)
	{
	}

	[PacketHandler(Opcode.SMSG_CACHE_VERSION)]
	private void HandleCacheVersion(WorldPacket packet)
	{
	}

	[PacketHandler(Opcode.MSG_MOVE_START_FORWARD)]
	[PacketHandler(Opcode.MSG_MOVE_START_BACKWARD)]
	[PacketHandler(Opcode.MSG_MOVE_STOP)]
	[PacketHandler(Opcode.MSG_MOVE_START_STRAFE_LEFT)]
	[PacketHandler(Opcode.MSG_MOVE_START_STRAFE_RIGHT)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_STRAFE)]
	[PacketHandler(Opcode.MSG_MOVE_START_ASCEND)]
	[PacketHandler(Opcode.MSG_MOVE_START_DESCEND)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_ASCEND)]
	[PacketHandler(Opcode.MSG_MOVE_JUMP)]
	[PacketHandler(Opcode.MSG_MOVE_START_TURN_LEFT)]
	[PacketHandler(Opcode.MSG_MOVE_START_TURN_RIGHT)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_TURN)]
	[PacketHandler(Opcode.MSG_MOVE_START_PITCH_UP)]
	[PacketHandler(Opcode.MSG_MOVE_START_PITCH_DOWN)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_PITCH)]
	[PacketHandler(Opcode.MSG_MOVE_SET_RUN_MODE)]
	[PacketHandler(Opcode.MSG_MOVE_SET_WALK_MODE)]
	[PacketHandler(Opcode.MSG_MOVE_TELEPORT)]
	[PacketHandler(Opcode.MSG_MOVE_SET_FACING)]
	[PacketHandler(Opcode.MSG_MOVE_SET_PITCH)]
	[PacketHandler(Opcode.MSG_MOVE_TOGGLE_COLLISION_CHEAT)]
	[PacketHandler(Opcode.MSG_MOVE_GRAVITY_CHNG)]
	[PacketHandler(Opcode.MSG_MOVE_ROOT)]
	[PacketHandler(Opcode.MSG_MOVE_UNROOT)]
	[PacketHandler(Opcode.MSG_MOVE_START_SWIM)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_SWIM)]
	[PacketHandler(Opcode.MSG_MOVE_START_SWIM_CHEAT)]
	[PacketHandler(Opcode.MSG_MOVE_STOP_SWIM_CHEAT)]
	[PacketHandler(Opcode.MSG_MOVE_HEARTBEAT)]
	[PacketHandler(Opcode.MSG_MOVE_FALL_LAND)]
	[PacketHandler(Opcode.MSG_MOVE_UPDATE_CAN_FLY)]
	[PacketHandler(Opcode.MSG_MOVE_UPDATE_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY)]
	[PacketHandler(Opcode.MSG_MOVE_HOVER)]
	[PacketHandler(Opcode.MSG_MOVE_FEATHER_FALL)]
	[PacketHandler(Opcode.MSG_MOVE_WATER_WALK)]
	private void HandleMovementMessages(WorldPacket packet)
	{
		MoveUpdate moveUpdate = new MoveUpdate();
		moveUpdate.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		moveUpdate.MoveInfo = new MovementInfo();
		moveUpdate.MoveInfo.ReadMovementInfoLegacy(packet, this.GetSession().GameState);
		moveUpdate.MoveInfo.Flags = (uint)((MovementFlagWotLK)moveUpdate.MoveInfo.Flags).CastFlags<MovementFlagModern>();
		moveUpdate.MoveInfo.ValidateMovementInfo();
		this.SendPacketToClient(moveUpdate);
	}

	[PacketHandler(Opcode.MSG_MOVE_KNOCK_BACK)]
	private void HandleMoveKnockBack(WorldPacket packet)
	{
		MoveUpdateKnockBack knockback = new MoveUpdateKnockBack();
		knockback.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		knockback.MoveInfo = new MovementInfo();
		knockback.MoveInfo.ReadMovementInfoLegacy(packet, this.GetSession().GameState);
		knockback.MoveInfo.Flags = (uint)((MovementFlagWotLK)knockback.MoveInfo.Flags).CastFlags<MovementFlagModern>();
		knockback.MoveInfo.JumpSinAngle = packet.ReadFloat();
		knockback.MoveInfo.JumpCosAngle = packet.ReadFloat();
		knockback.MoveInfo.JumpHorizontalSpeed = packet.ReadFloat();
		knockback.MoveInfo.JumpVerticalSpeed = packet.ReadFloat();
		knockback.MoveInfo.ValidateMovementInfo();
		this.SendPacketToClient(knockback);
	}

	[PacketHandler(Opcode.SMSG_MOVE_KNOCK_BACK)]
	private void HandleMoveForceKnockBack(WorldPacket packet)
	{
		MoveKnockBack knockback = new MoveKnockBack();
		knockback.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		knockback.MoveCounter = packet.ReadUInt32();
		knockback.Direction = packet.ReadVector2();
		knockback.HorizontalSpeed = packet.ReadFloat();
		knockback.VerticalSpeed = packet.ReadFloat();
		this.SendPacketToClient(knockback);
	}

	[PacketHandler(Opcode.SMSG_CONTROL_UPDATE)]
	private void HandleControlUpdate(WorldPacket packet)
	{
		ControlUpdate control = new ControlUpdate();
		control.Guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		control.HasControl = packet.ReadBool();
		this.SendPacketToClient(control);
	}

	[PacketHandler(Opcode.MSG_MOVE_TELEPORT_ACK)]
	private void HandleMoveTeleportAck(WorldPacket packet)
	{
		WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		if (this.GetSession().GameState.IsInTaxiFlight && this.GetSession().GameState.CurrentPlayerGuid == guid)
		{
			ControlUpdate control = new ControlUpdate();
			control.Guid = guid;
			control.HasControl = true;
			this.SendPacketToClient(control);
			this.GetSession().GameState.IsInTaxiFlight = false;
		}
		MoveTeleport teleport = new MoveTeleport();
		teleport.MoverGUID = guid;
		teleport.MoveCounter = packet.ReadUInt32();
		MovementInfo moveInfo = new MovementInfo();
		moveInfo.ReadMovementInfoLegacy(packet, this.GetSession().GameState);
		moveInfo.Flags = (uint)((MovementFlagWotLK)moveInfo.Flags).CastFlags<MovementFlagModern>();
		moveInfo.ValidateMovementInfo();
		teleport.Position = moveInfo.Position;
		teleport.Orientation = moveInfo.Orientation;
		teleport.TransportGUID = moveInfo.TransportGuid;
		if (moveInfo.TransportSeat > 0)
		{
			teleport.Vehicle = new VehicleTeleport();
			teleport.Vehicle.VehicleSeatIndex = moveInfo.TransportSeat;
		}
		this.SendPacketToClient(teleport);
	}

	[PacketHandler(Opcode.SMSG_TRANSFER_PENDING)]
	private void HandleTransferPending(WorldPacket packet)
	{
		if (this.GetSession().GameState.IsWaitingForWorldPortAck)
		{
			Log.Print(LogType.Error, "Skipping SMSG_TRANSFER_PENDING, client is already being teleported.", "HandleTransferPending", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\MovementHandler.cs");
			return;
		}
		TransferPending transfer = new TransferPending();
		transfer.MapID = (this.GetSession().GameState.PendingTransferMapId = packet.ReadUInt32());
		transfer.OldMapPosition = Framework.GameMath.Vector3.Zero;
		this.SendPacketToClient(transfer);
		this.GetSession().GameState.IsFirstEnterWorld = false;
		this.GetSession().GameState.IsWaitingForNewWorld = true;
		SuspendToken suspend = new SuspendToken();
		suspend.SequenceIndex = 3u;
		suspend.Reason = 1u;
		this.SendPacketToClient(suspend);
	}

	[PacketHandler(Opcode.SMSG_TRANSFER_ABORTED)]
	private void HandleTransferAborted(WorldPacket packet)
	{
		TransferAborted transfer = new TransferAborted();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			transfer.MapID = packet.ReadUInt32();
		}
		else
		{
			transfer.MapID = this.GetSession().GameState.PendingTransferMapId;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			transfer.Reason = (TransferAbortReasonModern)packet.ReadUInt8();
		}
		else
		{
			TransferAbortReasonLegacy legacyReason = (TransferAbortReasonLegacy)packet.ReadUInt8();
			transfer.Reason = (TransferAbortReasonModern)Enum.Parse(typeof(TransferAbortReasonModern), legacyReason.ToString());
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			transfer.Arg = packet.ReadUInt8();
		}
		this.SendPacketToClient(transfer);
		this.GetSession().GameState.IsWaitingForNewWorld = false;
	}

	[PacketHandler(Opcode.SMSG_NEW_WORLD)]
	private void HandleNewWorld(WorldPacket packet)
	{
		NewWorld teleport = new NewWorld();
		this.GetSession().GameState.CurrentMapId = (teleport.MapID = packet.ReadUInt32());
		teleport.Position = packet.ReadVector3();
		teleport.Orientation = packet.ReadFloat();
		teleport.Reason = 4u;
		this.GetSession().GameState.IsFirstEnterWorld = false;
		if (!this.GetSession().GameState.IsWaitingForNewWorld)
		{
			return;
		}
		this.GetSession().GameState.IsWaitingForNewWorld = false;
		this.GetSession().GameState.IsWaitingForWorldPortAck = true;
		this.SendPacketToClient(teleport);
		if (teleport.MapID > 1)
		{
			UpdateLastInstance instance = new UpdateLastInstance();
			instance.MapID = teleport.MapID;
			this.SendPacketToClient(instance);
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.SendPacketToClient(new TimeSyncRequest());
			}
			ResumeToken resume = new ResumeToken();
			resume.SequenceIndex = 3u;
			resume.Reason = 1u;
			this.SendPacketToClient(resume);
		}
		WorldServerInfo info = new WorldServerInfo();
		if (teleport.MapID > 1)
		{
			info.DifficultyID = 1u;
			info.InstanceGroupSize = 5u;
		}
		this.SendPacketToClient(info);
	}

	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_WALK_BACK_SPEED)]
	private void HandleMoveSplineSetWalkBackSpeed(WorldPacket packet)
	{
		// Walk back speed does not exist in WotLK Classic 3.4.3 - silently drop
	}

		[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_FLIGHT_BACK_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_FLIGHT_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_PITCH_RATE)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_RUN_BACK_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_RUN_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_SWIM_BACK_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_SWIM_SPEED)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_TURN_RATE)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_WALK_SPEED)]
	private void HandleMoveSplineSetSpeed(WorldPacket packet)
	{
		MoveSplineSetSpeed speed = new MoveSplineSetSpeed(packet.GetUniversalOpcode(isModern: false));
		speed.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		speed.Speed = packet.ReadFloat();
		this.SendPacketToClient(speed);
	}

	[PacketHandler(Opcode.SMSG_FORCE_WALK_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_RUN_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_RUN_BACK_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_SWIM_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_SWIM_BACK_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_TURN_RATE_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_FLIGHT_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE)]
	[PacketHandler(Opcode.SMSG_FORCE_PITCH_RATE_CHANGE)]
	private void HandleMoveForceSpeedChange(WorldPacket packet)
	{
		string opcodeName = packet.GetUniversalOpcode(isModern: false).ToString().Replace("SMSG_FORCE_", "SMSG_MOVE_SET_")
			.Replace("_CHANGE", "");
		Opcode universalOpcode = Opcodes.GetUniversalOpcode(opcodeName);
		MoveSetSpeed speed = new MoveSetSpeed(universalOpcode);
		speed.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		speed.MoveCounter = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_FORCE_RUN_SPEED_CHANGE)
		{
			packet.ReadUInt8();
		}
		speed.Speed = packet.ReadFloat();
		this.SendPacketToClient(speed);
		bool flag = universalOpcode - 2420 <= Opcode.CMSG_ABANDON_NPE_RESPONSE;
		if (flag && LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			Opcode flyOpcode = (Opcode)Enum.Parse(typeof(Opcode), universalOpcode.ToString().Replace("SWIM", "FLIGHT"));
			MoveSetSpeed flySpeed = new MoveSetSpeed(flyOpcode);
			flySpeed.MoverGUID = speed.MoverGUID;
			flySpeed.MoveCounter = speed.MoveCounter;
			flySpeed.Speed = speed.Speed;
			this.SendPacketToClient(flySpeed);
		}
	}

	[PacketHandler(Opcode.MSG_MOVE_SET_FLIGHT_BACK_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_FLIGHT_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_PITCH_RATE)]
	[PacketHandler(Opcode.MSG_MOVE_SET_RUN_BACK_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_RUN_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_SWIM_BACK_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_SWIM_SPEED)]
	[PacketHandler(Opcode.MSG_MOVE_SET_TURN_RATE)]
	[PacketHandler(Opcode.MSG_MOVE_SET_WALK_SPEED)]
	private void HandleMoveUpdateSpeed(WorldPacket packet)
	{
		string opcodeName = packet.GetUniversalOpcode(isModern: false).ToString().Replace("MSG_MOVE_SET", "SMSG_MOVE_UPDATE");
		Opcode universalOpcode = Opcodes.GetUniversalOpcode(opcodeName);
		MoveUpdateSpeed speed = new MoveUpdateSpeed(universalOpcode);
		speed.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		speed.MoveInfo = new MovementInfo();
		speed.MoveInfo.ReadMovementInfoLegacy(packet, this.GetSession().GameState);
		MovementFlagModern newFlags = ((MovementFlagWotLK)speed.MoveInfo.Flags).CastFlags<MovementFlagModern>();
		speed.MoveInfo.Flags = (uint)newFlags;
		speed.MoveInfo.ValidateMovementInfo();
		speed.Speed = packet.ReadFloat();
		this.SendPacketToClient(speed);
		bool flag = universalOpcode - 2477 <= Opcode.CMSG_ABANDON_NPE_RESPONSE;
		if (flag && LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			Opcode flyOpcode = (Opcode)Enum.Parse(typeof(Opcode), universalOpcode.ToString().Replace("SWIM", "FLIGHT"));
			MoveUpdateSpeed flySpeed = new MoveUpdateSpeed(flyOpcode);
			flySpeed.MoverGUID = speed.MoverGUID;
			flySpeed.MoveInfo = speed.MoveInfo;
			flySpeed.Speed = speed.Speed;
			this.SendPacketToClient(flySpeed);
		}
	}

	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_ROOT)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_UNROOT)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_ENABLE_GRAVITY)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_DISABLE_GRAVITY)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_FEATHER_FALL)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_NORMAL_FALL)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_HOVER)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_UNSET_HOVER)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_WATER_WALK)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_LAND_WALK)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_START_SWIM)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_STOP_SWIM)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_RUN_MODE)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_WALK_MODE)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_SET_FLYING)]
	[PacketHandler(Opcode.SMSG_MOVE_SPLINE_UNSET_FLYING)]
	private void HandleSplineMovementMessages(WorldPacket packet)
	{
		MoveSplineSetFlag spline = new MoveSplineSetFlag(packet.GetUniversalOpcode(isModern: false));
		spline.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(spline);
	}

	[PacketHandler(Opcode.SMSG_MOVE_ROOT)]
	[PacketHandler(Opcode.SMSG_MOVE_UNROOT)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_WATER_WALK)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_LAND_WALK)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_HOVERING)]
	[PacketHandler(Opcode.SMSG_MOVE_UNSET_HOVERING)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_CAN_FLY)]
	[PacketHandler(Opcode.SMSG_MOVE_UNSET_CAN_FLY)]
	[PacketHandler(Opcode.SMSG_MOVE_ENABLE_TRANSITION_BETWEEN_SWIM_AND_FLY)]
	[PacketHandler(Opcode.SMSG_MOVE_DISABLE_TRANSITION_BETWEEN_SWIM_AND_FLY)]
	[PacketHandler(Opcode.SMSG_MOVE_DISABLE_GRAVITY)]
	[PacketHandler(Opcode.SMSG_MOVE_ENABLE_GRAVITY)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_FEATHER_FALL)]
	[PacketHandler(Opcode.SMSG_MOVE_SET_NORMAL_FALL)]
	private void HandleMoveForceFlagChange(WorldPacket packet)
	{
		MoveSetFlag flag = new MoveSetFlag(packet.GetUniversalOpcode(isModern: false));
		flag.MoverGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		flag.MoveCounter = packet.ReadUInt32();
		this.SendPacketToClient(flag);
	}

	[PacketHandler(Opcode.SMSG_COMPRESSED_MOVES)]
	private void HandleCompressedMoves(WorldPacket packet)
	{
		int uncompressedSize = packet.ReadInt32();
		WorldPacket pkt = packet.Inflate(uncompressedSize);
		while (pkt.CanRead())
		{
			byte size = pkt.ReadUInt8();
			ushort opc = pkt.ReadUInt16();
			byte[] data = pkt.ReadBytes((uint)(size - 2));
			WorldPacket pkt2 = new WorldPacket(opc, data);
			pkt2.SetReceiveTime(pkt.GetReceivedTime());
			this.HandlePacket(pkt2);
		}
	}

	[PacketHandler(Opcode.SMSG_ON_MONSTER_MOVE)]
	[PacketHandler(Opcode.SMSG_MONSTER_MOVE_TRANSPORT)]
	private void HandleMonsterMove(WorldPacket packet)
	{
		WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		ServerSideMovement moveSpline = new ServerSideMovement();
		if (packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_MONSTER_MOVE_TRANSPORT)
		{
			moveSpline.TransportGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
			{
				moveSpline.TransportSeat = packet.ReadInt8();
			}
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			packet.ReadBool();
		}
		moveSpline.StartPosition = packet.ReadVector3();
		moveSpline.SplineId = packet.ReadUInt32();
		SplineTypeLegacy type = (SplineTypeLegacy)packet.ReadUInt8();
		switch (type)
		{
		case SplineTypeLegacy.FacingSpot:
			moveSpline.SplineType = SplineTypeModern.FacingSpot;
			moveSpline.FinalFacingSpot = packet.ReadVector3();
			break;
		case SplineTypeLegacy.FacingTarget:
			moveSpline.SplineType = SplineTypeModern.FacingTarget;
			moveSpline.FinalFacingGuid = packet.ReadGuid().To128(this.GetSession().GameState);
			break;
		case SplineTypeLegacy.FacingAngle:
			moveSpline.SplineType = SplineTypeModern.FacingAngle;
			moveSpline.FinalOrientation = packet.ReadFloat();
			MovementInfo.ClampOrientation(ref moveSpline.FinalOrientation);
			break;
		case SplineTypeLegacy.Stop:
		{
			moveSpline.SplineType = SplineTypeModern.None;
			MonsterMove moveStop = new MonsterMove(guid, moveSpline);
			this.SendPacketToClient(moveStop);
			return;
		}
		}
		bool hasAnimTier;
		bool hasTrajectory;
		bool hasCatmullRom;
		bool hasTaxiFlightFlags;
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			SplineFlagVanilla splineFlags = (SplineFlagVanilla)packet.ReadUInt32();
			hasAnimTier = false;
			hasTrajectory = false;
			hasCatmullRom = splineFlags.HasAnyFlag(SplineFlagVanilla.Flying);
			hasTaxiFlightFlags = splineFlags == (SplineFlagVanilla.Runmode | SplineFlagVanilla.Flying);
			if (splineFlags == SplineFlagVanilla.Runmode)
			{
				moveSpline.SplineFlags = SplineFlagModern.Unknown5;
				UnitFlagsVanilla unitFlags = (UnitFlagsVanilla)this.GetSession().GameState.GetLegacyFieldValueUInt32(guid, UnitField.UNIT_FIELD_FLAGS);
				if (unitFlags.HasFlag(UnitFlagsVanilla.CanSwim))
				{
					moveSpline.SplineFlags |= SplineFlagModern.CanSwim;
				}
				if (type == SplineTypeLegacy.Normal && !unitFlags.HasFlag(UnitFlagsVanilla.InCombat))
				{
					moveSpline.SplineFlags |= SplineFlagModern.Steering | SplineFlagModern.Unknown10;
				}
			}
			else
			{
				moveSpline.SplineFlags = splineFlags.CastFlags<SplineFlagModern>();
			}
		}
		else if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			SplineFlagTBC splineFlags2 = (SplineFlagTBC)packet.ReadUInt32();
			hasAnimTier = false;
			hasTrajectory = false;
			hasCatmullRom = splineFlags2.HasAnyFlag(SplineFlagTBC.Flying);
			hasTaxiFlightFlags = splineFlags2 == (SplineFlagTBC.Runmode | SplineFlagTBC.Flying);
			if (splineFlags2 == SplineFlagTBC.Runmode)
			{
				moveSpline.SplineFlags = SplineFlagModern.Unknown5;
				UnitFlags unitFlags2 = (UnitFlags)this.GetSession().GameState.GetLegacyFieldValueUInt32(guid, UnitField.UNIT_FIELD_FLAGS);
				if (unitFlags2.HasFlag(UnitFlags.CanSwim))
				{
					moveSpline.SplineFlags |= SplineFlagModern.CanSwim;
				}
				if (type == SplineTypeLegacy.Normal && !unitFlags2.HasFlag(UnitFlags.InCombat))
				{
					moveSpline.SplineFlags |= SplineFlagModern.Steering | SplineFlagModern.Unknown10;
				}
			}
			else
			{
				moveSpline.SplineFlags = splineFlags2.CastFlags<SplineFlagModern>();
			}
		}
		else
		{
			SplineFlagWotLK splineFlags3 = (SplineFlagWotLK)packet.ReadUInt32();
			hasAnimTier = splineFlags3.HasAnyFlag(SplineFlagWotLK.AnimationTier);
			hasTrajectory = splineFlags3.HasAnyFlag(SplineFlagWotLK.Trajectory);
			hasCatmullRom = splineFlags3.HasAnyFlag(SplineFlagWotLK.Flying | SplineFlagWotLK.CatmullRom);
			hasTaxiFlightFlags = splineFlags3 == (SplineFlagWotLK.WalkMode | SplineFlagWotLK.Flying);
			moveSpline.SplineFlags = splineFlags3.CastFlags<SplineFlagModern>();
		}
		if (hasAnimTier)
		{
			packet.ReadUInt8();
			packet.ReadInt32();
		}
		moveSpline.SplineTimeFull = packet.ReadUInt32();
		if (hasTrajectory)
		{
			packet.ReadFloat();
			packet.ReadInt32();
		}
		moveSpline.SplineCount = packet.ReadUInt32();
		if (hasCatmullRom)
		{
			for (int i = 0; i < moveSpline.SplineCount; i++)
			{
				Framework.GameMath.Vector3 vec = packet.ReadVector3();
				moveSpline?.SplinePoints.Add(vec);
			}
			moveSpline.SplineFlags |= SplineFlagModern.UncompressedPath;
		}
		else
		{
			moveSpline.EndPosition = packet.ReadVector3();
			Framework.GameMath.Vector3 mid = (moveSpline.StartPosition + moveSpline.EndPosition) * 0.5f;
			for (int j = 1; j < moveSpline.SplineCount; j++)
			{
				Framework.GameMath.Vector3 vec2 = packet.ReadPackedVector3();
				vec2 = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? (moveSpline.EndPosition - vec2) : (mid - vec2));
				moveSpline.SplinePoints.Add(vec2);
			}
		}
		bool isTaxiFlight = hasTaxiFlightFlags && (this.GetSession().GameState.IsWaitingForTaxiStart || Math.Abs(packet.GetReceivedTime() - this.GetSession().GameState.CurrentPlayerCreateTime) <= 1000) && this.GetSession().GameState.CurrentPlayerGuid == guid;
		if (isTaxiFlight)
		{
			ServerSideMovement stopSpline = new ServerSideMovement();
			stopSpline.StartPosition = moveSpline.StartPosition;
			stopSpline.SplineId = moveSpline.SplineId - 2;
			MonsterMove moveStop2 = new MonsterMove(guid, stopSpline);
			this.SendPacketToClient(moveStop2);
			ControlUpdate update = new ControlUpdate();
			update.Guid = guid;
			update.HasControl = false;
			this.SendPacketToClient(update);
			stopSpline.SplineId = moveSpline.SplineId - 1;
			moveStop2 = new MonsterMove(guid, stopSpline);
			this.SendPacketToClient(moveStop2);
			update = new ControlUpdate();
			update.Guid = guid;
			update.HasControl = false;
			this.SendPacketToClient(update);
			moveSpline.SplineFlags = SplineFlagModern.Flying | SplineFlagModern.CatmullRom | SplineFlagModern.CanSwim | SplineFlagModern.UncompressedPath | SplineFlagModern.Unknown5 | SplineFlagModern.Steering | SplineFlagModern.Unknown10;
			if (!hasCatmullRom && moveSpline.EndPosition != Framework.GameMath.Vector3.Zero)
			{
				moveSpline.SplinePoints.Add(moveSpline.EndPosition);
			}
		}
		MonsterMove monsterMove = new MonsterMove(guid, moveSpline);
		this.SendPacketToClient(monsterMove);
		if (isTaxiFlight)
		{
			if (this.GetSession().GameState.IsWaitingForTaxiStart)
			{
				ActivateTaxiReplyPkt taxi = new ActivateTaxiReplyPkt();
				taxi.Reply = ActivateTaxiReply.Ok;
				this.SendPacketToClient(taxi);
				this.GetSession().GameState.IsWaitingForTaxiStart = false;
			}
			this.GetSession().GameState.IsInTaxiFlight = true;
		}
	}

	[PacketHandler(Opcode.SMSG_GOSSIP_MESSAGE)]
	private void HandleGossipmessage(WorldPacket packet)
	{
		GossipMessagePkt gossip = new GossipMessagePkt();
		gossip.GossipGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = gossip.GossipGUID;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
		{
			gossip.GossipID = packet.ReadInt32();
		}
		else
		{
			gossip.GossipID = (int)gossip.GossipGUID.GetEntry();
		}
		gossip.TextID = packet.ReadInt32();
		uint optionsCount = packet.ReadUInt32();
		for (uint i = 0u; i < optionsCount; i++)
		{
			ClientGossipOption option = new ClientGossipOption();
			option.OptionIndex = packet.ReadInt32();
			option.OptionIcon = packet.ReadUInt8();
			option.OptionFlags = (byte)(packet.ReadBool() ? 1u : 0u);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				option.OptionCost = packet.ReadInt32();
			}
			option.Text = packet.ReadCString();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				option.Confirm = packet.ReadCString();
			}
			gossip.GossipOptions.Add(option);
		}
		uint questsCount = packet.ReadUInt32();
		for (uint i2 = 0u; i2 < questsCount; i2++)
		{
			ClientGossipQuest quest = this.ReadGossipQuestOption(packet);
			gossip.GossipQuests.Add(quest);
		}
		this.SendPacketToClient(gossip);
	}

	[PacketHandler(Opcode.SMSG_GOSSIP_COMPLETE)]
	private void HandleGossipComplete(WorldPacket packet)
	{
		GossipComplete gossip = new GossipComplete();
		this.SendPacketToClient(gossip);
	}

	[PacketHandler(Opcode.SMSG_GOSSIP_POI)]
	private void HandleGossipPoi(WorldPacket packet)
	{
		GossipPOI poi = new GossipPOI();
		poi.Flags = packet.ReadUInt32();
		poi.Pos = new Framework.GameMath.Vector3(packet.ReadVector2());
		poi.Icon = packet.ReadUInt32();
		poi.Importance = packet.ReadUInt32();
		poi.Name = packet.ReadCString();
		this.SendPacketToClient(poi);
	}

	[PacketHandler(Opcode.SMSG_BINDER_CONFIRM)]
	private void HandleBinderConfirm(WorldPacket packet)
	{
		BinderConfirm confirm = new BinderConfirm();
		confirm.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = confirm.Guid;
		this.SendPacketToClient(confirm);
	}

	[PacketHandler(Opcode.SMSG_VENDOR_INVENTORY)]
	private void HandleVendorInventory(WorldPacket packet)
	{
		VendorInventory vendor = new VendorInventory();
		vendor.VendorGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = vendor.VendorGUID;
		byte itemsCount = packet.ReadUInt8();
		if (itemsCount == 0)
		{
			vendor.Reason = packet.ReadUInt8();
			this.SendPacketToClient(vendor);
			return;
		}
		for (byte i = 0; i < itemsCount; i++)
		{
			VendorItem vendorItem = new VendorItem();
			vendorItem.Slot = packet.ReadInt32();
			vendorItem.MuID = (uint)(i + 1);
			vendorItem.Item.ItemID = packet.ReadUInt32();
			packet.ReadUInt32();
			vendorItem.Quantity = packet.ReadInt32();
			vendorItem.Price = packet.ReadUInt32();
			vendorItem.Durability = packet.ReadInt32();
			vendorItem.StackCount = packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				vendorItem.ExtendedCostID = packet.ReadInt32();
			}
			this.GetSession().GameState.SetItemBuyCount(vendorItem.Item.ItemID, vendorItem.StackCount);
			vendor.Items.Add(vendorItem);
		}
		this.SendPacketToClient(vendor);
	}

	[PacketHandler(Opcode.SMSG_SHOW_BANK)]
	private void HandleShowBank(WorldPacket packet)
	{
		ShowBank bank = new ShowBank();
		bank.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = bank.Guid;
		this.SendPacketToClient(bank);
	}

	[PacketHandler(Opcode.SMSG_TRAINER_LIST)]
	private void HandleTrainerList(WorldPacket packet)
	{
		TrainerList trainer = new TrainerList();
		trainer.TrainerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = trainer.TrainerGUID;
		trainer.TrainerID = trainer.TrainerGUID.GetEntry();
		trainer.TrainerType = packet.ReadInt32();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			TrainerListSpell spell = new TrainerListSpell();
			uint spellId = packet.ReadUInt32();
			if (ModernVersion.ExpansionVersion > 1 && LegacyVersion.ExpansionVersion <= 1)
			{
				uint realSpellId = GameData.GetRealSpell(spellId);
				if (realSpellId != spellId)
				{
					this.GetSession().GameState.StoreRealSpell(realSpellId, spellId);
					spellId = realSpellId;
				}
			}
			spell.SpellID = spellId;
			TrainerSpellStateLegacy stateOld = (TrainerSpellStateLegacy)packet.ReadUInt8();
			TrainerSpellStateModern stateNew = (TrainerSpellStateModern)Enum.Parse(typeof(TrainerSpellStateModern), stateOld.ToString());
			spell.Usable = stateNew;
			spell.MoneyCost = packet.ReadUInt32();
			packet.ReadInt32();
			packet.ReadInt32();
			spell.ReqLevel = packet.ReadUInt8();
			spell.ReqSkillLine = packet.ReadUInt32();
			spell.ReqSkillRank = packet.ReadUInt32();
			spell.ReqAbility[0] = packet.ReadUInt32();
			spell.ReqAbility[1] = packet.ReadUInt32();
			spell.ReqAbility[2] = packet.ReadUInt32();
			trainer.Spells.Add(spell);
		}
		trainer.Greeting = packet.ReadCString();
		this.SendPacketToClient(trainer);
	}

	[PacketHandler(Opcode.SMSG_TRAINER_BUY_FAILED)]
	private void HandleTrainerBuyFailed(WorldPacket packet)
	{
		TrainerBuyFailed buy = new TrainerBuyFailed();
		buy.TrainerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		buy.SpellID = packet.ReadUInt32();
		buy.TrainerFailedReason = packet.ReadUInt32();
		this.SendPacketToClient(buy);
		ChatPkt chat = new ChatPkt(this.GetSession(), ChatMessageTypeModern.System, $"Failed to learn Spell {buy.SpellID} (Reason {buy.TrainerFailedReason}).");
		this.SendPacketToClient(chat);
	}

	[PacketHandler(Opcode.MSG_TALENT_WIPE_CONFIRM)]
	private void HandleTalentWipeConfirm(WorldPacket packet)
	{
		RespecWipeConfirm respec = new RespecWipeConfirm();
		respec.TrainerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		respec.Cost = packet.ReadUInt32();
		this.SendPacketToClient(respec);
	}

	[PacketHandler(Opcode.SMSG_SPIRIT_HEALER_CONFIRM)]
	private void HandleSpiritHealerConfirm(WorldPacket packet)
	{
		// 3.4.3 client has no SMSG_SPIRIT_HEALER_CONFIRM opcode — spirit healer works directly.
		// Auto-accept by sending CMSG_SPIRIT_HEALER_ACTIVATE back to the legacy server.
		WowGuid64 guid = packet.ReadGuid();
		WorldPacket activate = new WorldPacket(Opcode.CMSG_SPIRIT_HEALER_ACTIVATE);
		activate.WriteGuid(guid);
		this.SendPacket(activate);
	}

	[PacketHandler(Opcode.SMSG_PET_SPELLS_MESSAGE)]
	private void HandlePetSpellsMessage(WorldPacket packet)
	{
		WowGuid guid = packet.ReadGuid();
		this.GetSession().GameState.CurrentPetGuid = guid.To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentClientPetCast = null;
		if (guid.IsEmpty())
		{
			PetClearSpells clear = new PetClearSpells();
			this.SendPacketToClient(clear);
			return;
		}
		PetSpells spells = new PetSpells();
		spells.PetGUID = guid.To128(this.GetSession().GameState);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			spells.CreatureFamily = packet.ReadUInt16();
		}
		spells.TimeLimit = packet.ReadUInt32();
		spells.ReactState = (ReactStates)packet.ReadUInt8();
		spells.CommandState = (CommandStates)packet.ReadUInt8();
		packet.ReadUInt8();
		spells.Flag = packet.ReadUInt8();
		for (int i = 0; i < 10; i++)
		{
			spells.ActionButtons[i] = packet.ReadUInt32();
		}
		byte spellCount = packet.ReadUInt8();
		for (int j = 0; j < spellCount; j++)
		{
			spells.Actions.Add(packet.ReadUInt32());
		}
		byte cdCount = packet.ReadUInt8();
		for (int k = 0; k < cdCount; k++)
		{
			PetSpellCooldown cooldown = new PetSpellCooldown();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
			{
				cooldown.SpellID = packet.ReadUInt32();
			}
			else
			{
				cooldown.SpellID = packet.ReadUInt16();
			}
			cooldown.Category = packet.ReadUInt16();
			cooldown.Duration = packet.ReadUInt32();
			cooldown.CategoryDuration = packet.ReadUInt32();
			spells.Cooldowns.Add(cooldown);
		}
		this.SendPacketToClient(spells);
	}

	[PacketHandler(Opcode.SMSG_PET_ACTION_SOUND)]
	private void HandlePetActionSound(WorldPacket packet)
	{
		PetActionSound sound = new PetActionSound();
		sound.UnitGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		sound.Action = packet.ReadUInt32();
		this.SendPacketToClient(sound);
	}

	[PacketHandler(Opcode.SMSG_PET_BROKEN)]
	private void HandlePetBroken(WorldPacket packet)
	{
		PrintNotification notify = new PrintNotification();
		notify.NotifyText = "Your pet has run away";
		this.SendPacketToClient(notify);
	}

	[PacketHandler(Opcode.SMSG_PET_UNLEARN_CONFIRM)]
	private void HandlePetUnlearnConfirm(WorldPacket packet)
	{
		RespecWipeConfirm respec = new RespecWipeConfirm();
		respec.TrainerGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		respec.Cost = packet.ReadUInt32();
		respec.RespecType = SpecResetType.PetTalents;
		this.SendPacketToClient(respec);
	}

	[PacketHandler(Opcode.MSG_LIST_STABLED_PETS)]
	private void HandleListStabledPets(WorldPacket packet)
	{
		PetGuids pets = new PetGuids();
		Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(this.GetSession().GameState.CurrentPlayerGuid);
		int UNIT_FIELD_SUMMON = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_SUMMON);
		if (UNIT_FIELD_SUMMON >= 0 && updateFields.ContainsKey(UNIT_FIELD_SUMMON))
		{
			WowGuid128 guid = WorldClient.GetGuidValue(updateFields, UnitField.UNIT_FIELD_SUMMON).To128(this.GetSession().GameState);
			if (!guid.IsEmpty())
			{
				pets.Guids.Add(guid);
			}
		}
		this.SendPacketToClient(pets);
		PetStableList stable = new PetStableList();
		stable.StableMaster = packet.ReadGuid().To128(this.GetSession().GameState);
		byte count = packet.ReadUInt8();
		stable.NumStableSlots = packet.ReadUInt8();
		for (byte i = 0; i < count; i++)
		{
			PetStableInfo pet = new PetStableInfo();
			pet.PetNumber = packet.ReadUInt32();
			pet.CreatureID = packet.ReadUInt32();
			pet.ExperienceLevel = packet.ReadUInt32();
			pet.PetName = packet.ReadCString();
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				pet.LoyaltyLevel = (byte)packet.ReadUInt32();
			}
			pet.PetFlags = packet.ReadUInt8();
			if (pet.PetFlags != 1)
			{
				pet.PetFlags = 3;
			}
			CreatureTemplate template = GameData.GetCreatureTemplate(pet.CreatureID);
			if (template != null)
			{
				pet.DisplayID = template.Display.CreatureDisplay[0].CreatureDisplayID;
			}
			else
			{
				WorldPacket query = new WorldPacket(Opcode.CMSG_QUERY_CREATURE);
				query.WriteUInt32(pet.CreatureID);
				query.WriteGuid(WowGuid64.Empty);
				this.SendPacket(query);
			}
			stable.Pets.Add(pet);
		}
		this.SendPacketToClient(stable);
	}

	[PacketHandler(Opcode.SMSG_PET_STABLE_RESULT)]
	private void HandlePetStableResult(WorldPacket packet)
	{
		PetStableResult stable = new PetStableResult();
		stable.Result = packet.ReadUInt8();
		this.SendPacketToClient(stable);
	}

	[PacketHandler(Opcode.SMSG_PETITION_SHOW_LIST)]
	private void HandlePetitionShowList(WorldPacket packet)
	{
		ServerPetitionShowList petitions = new ServerPetitionShowList();
		petitions.Unit = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = petitions.Unit;
		byte count = packet.ReadUInt8();
		for (int i = 0; i < count; i++)
		{
			PetitionEntry petition = default(PetitionEntry);
			petition.Index = packet.ReadUInt32();
			petition.CharterEntry = packet.ReadUInt32();
			petition.IsArena = ((petition.CharterEntry != 5863) ? 1u : 0u);
			packet.ReadUInt32();
			petition.CharterCost = packet.ReadUInt32();
			packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				petition.RequiredSignatures = packet.ReadUInt32();
			}
			else
			{
				petition.RequiredSignatures = 9u;
			}
			petitions.Petitions.Add(petition);
		}
		this.SendPacketToClient(petitions);
	}

	[PacketHandler(Opcode.SMSG_PETITION_SHOW_SIGNATURES)]
	private void HandlePetitionShowSignatures(WorldPacket packet)
	{
		ServerPetitionShowSignatures petition = new ServerPetitionShowSignatures();
		petition.Item = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.Owner = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.OwnerAccountID = this.GetSession().GetGameAccountGuidForPlayer(petition.Owner);
		petition.PetitionID = packet.ReadInt32();
		byte counter = packet.ReadUInt8();
		for (int i = 0; i < counter; i++)
		{
			ServerPetitionShowSignatures.PetitionSignature signature = new ServerPetitionShowSignatures.PetitionSignature
			{
				Signer = packet.ReadGuid().To128(this.GetSession().GameState),
				Choice = packet.ReadInt32()
			};
			petition.Signatures.Add(signature);
		}
		this.SendPacketToClient(petition);
	}

	[PacketHandler(Opcode.SMSG_QUERY_PETITION_RESPONSE)]
	private void HandlePetitionQueryResponse(WorldPacket packet)
	{
		QueryPetitionResponse petition = new QueryPetitionResponse();
		petition.PetitionID = packet.ReadUInt32();
		petition.Allow = true;
		petition.Info = new PetitionInfo();
		petition.Info.PetitionID = petition.PetitionID;
		petition.Info.Petitioner = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.Info.Title = packet.ReadCString();
		petition.Info.BodyText = packet.ReadCString();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.ReadUInt32();
		}
		petition.Info.MinSignatures = packet.ReadUInt32();
		petition.Info.MaxSignatures = packet.ReadUInt32();
		petition.Info.DeadLine = packet.ReadInt32();
		petition.Info.IssueDate = packet.ReadInt32();
		petition.Info.AllowedGuildID = packet.ReadInt32();
		petition.Info.AllowedClasses = packet.ReadInt32();
		petition.Info.AllowedRaces = packet.ReadInt32();
		petition.Info.AllowedGender = packet.ReadInt16();
		petition.Info.AllowedMinLevel = packet.ReadInt32();
		petition.Info.AllowedMaxLevel = packet.ReadInt32();
		petition.Info.NumChoices = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			for (int i = 0; i < 10; i++)
			{
				petition.Info.Choicetext[i] = packet.ReadCString();
			}
		}
		petition.Info.Muid = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			petition.Info.StaticType = packet.ReadInt32();
		}
		this.SendPacketToClient(petition);
	}

	[PacketHandler(Opcode.MSG_PETITION_RENAME)]
	private void HandlePetitionRename(WorldPacket packet)
	{
		PetitionRenameGuildResponse petition = new PetitionRenameGuildResponse();
		petition.PetitionGuid = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.NewGuildName = packet.ReadCString();
		this.SendPacketToClient(petition);
	}

	[PacketHandler(Opcode.MSG_PETITION_DECLINE)]
	private void HandlePetitionDecline(WorldPacket packet)
	{
		WowGuid128 guid = packet.ReadGuid().To128(this.GetSession().GameState);
		string name = this.GetSession().GameState.GetPlayerName(guid);
		if (!string.IsNullOrEmpty(name))
		{
			ChatPkt chat = new ChatPkt(this.GetSession(), ChatMessageTypeModern.System, name + " has declined your guild invitation.");
			this.SendPacketToClient(chat);
		}
	}

	[PacketHandler(Opcode.SMSG_PETITION_SIGN_RESULTS)]
	private void HandlePetitionSignResults(WorldPacket packet)
	{
		PetitionSignResults petition = new PetitionSignResults();
		petition.Item = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.Player = packet.ReadGuid().To128(this.GetSession().GameState);
		petition.Error = (PetitionSignResult)packet.ReadUInt32();
		this.SendPacketToClient(petition);
	}

	[PacketHandler(Opcode.SMSG_TURN_IN_PETITION_RESULT)]
	private void HandleTurnInPetitionResult(WorldPacket packet)
	{
		TurnInPetitionResult petition = new TurnInPetitionResult();
		petition.Result = (PetitionTurnResult)packet.ReadUInt32();
		this.SendPacketToClient(petition);
	}

	[PacketHandler(Opcode.SMSG_QUERY_TIME_RESPONSE)]
	private void HandleQueryTimeResponse(WorldPacket packet)
	{
		QueryTimeResponse response = new QueryTimeResponse();
		response.CurrentTime = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.CanRead())
		{
			packet.ReadInt32();
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUERY_QUEST_INFO_RESPONSE)]
	private void HandleQueryQuestInfoResponse(WorldPacket packet)
	{
		QueryQuestInfoResponse response = new QueryQuestInfoResponse();
		KeyValuePair<int, bool> id = packet.ReadEntry();
		response.QuestID = (uint)id.Key;
		if (id.Value)
		{
			response.Allow = false;
			this.SendPacketToClient(response);
			return;
		}
		response.Allow = true;
		response.Info = new QuestTemplate();
		QuestTemplate quest = response.Info;
		quest.QuestID = response.QuestID;
		quest.QuestType = packet.ReadInt32();
		quest.QuestLevel = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.MinLevel = packet.ReadInt32();
		}
		else
		{
			quest.MinLevel = 1;
		}
		quest.QuestSortID = packet.ReadInt32();
		quest.QuestInfoID = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			quest.SuggestedGroupNum = packet.ReadUInt32();
		}
		sbyte objectiveCounter = 0;
		for (int i = 0; i < 2; i++)
		{
			int factionId = packet.ReadInt32();
			int factionValue = packet.ReadInt32();
			if (factionId != 0 && factionValue != 0)
			{
				QuestObjective objective = new QuestObjective();
				objective.QuestID = response.QuestID;
				objective.Id = QuestObjective.QuestObjectiveCounter++;
				objective.StorageIndex = objectiveCounter++;
				objective.Type = QuestObjectiveType.MinReputation;
				objective.ObjectID = factionId;
				objective.Amount = factionValue;
				quest.Objectives.Add(objective);
			}
		}
		quest.RewardNextQuest = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.RewardXPDifficulty = packet.ReadUInt32();
		}
		int rewOrReqMoney = packet.ReadInt32();
		if (rewOrReqMoney >= 0)
		{
			quest.RewardMoney = rewOrReqMoney;
		}
		else
		{
			QuestObjective objective2 = new QuestObjective();
			objective2.QuestID = response.QuestID;
			objective2.Id = QuestObjective.QuestObjectiveCounter++;
			objective2.StorageIndex = objectiveCounter++;
			objective2.Type = QuestObjectiveType.Money;
			objective2.ObjectID = 0;
			objective2.Amount = -rewOrReqMoney;
			quest.Objectives.Add(objective2);
		}
		quest.RewardBonusMoney = packet.ReadUInt32();
		quest.RewardDisplaySpell[0] = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			quest.RewardSpell = packet.ReadUInt32();
			quest.RewardHonor = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.RewardKillHonor = packet.ReadFloat();
		}
		quest.StartItem = packet.ReadUInt32();
		quest.Flags = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
		{
			quest.RewardTitle = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			int requiredPlayerKills = packet.ReadInt32();
			if (requiredPlayerKills != 0)
			{
				QuestObjective objective3 = new QuestObjective();
				objective3.QuestID = response.QuestID;
				objective3.Id = QuestObjective.QuestObjectiveCounter++;
				objective3.StorageIndex = objectiveCounter++;
				objective3.Type = QuestObjectiveType.PlayerKills;
				objective3.ObjectID = 0;
				objective3.Amount = requiredPlayerKills;
				quest.Objectives.Add(objective3);
			}
			packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.RewardArenaPoints = packet.ReadInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			packet.ReadInt32();
		}
		for (int j = 0; j < 4; j++)
		{
			quest.RewardItems[j] = packet.ReadUInt32();
			quest.RewardAmount[j] = packet.ReadUInt32();
		}
		for (int k = 0; k < 6; k++)
		{
			QuestInfoChoiceItem choiceItem = new QuestInfoChoiceItem
			{
				ItemID = packet.ReadUInt32(),
				Quantity = packet.ReadUInt32()
			};
			uint displayId = GameData.GetItemDisplayId(choiceItem.ItemID);
			if (displayId != 0)
			{
				choiceItem.DisplayID = displayId;
			}
			quest.UnfilteredChoiceItems[k] = choiceItem;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			for (int l = 0; l < 5; l++)
			{
				quest.RewardFactionID[l] = packet.ReadUInt32();
			}
			for (int m = 0; m < 5; m++)
			{
				quest.RewardFactionValue[m] = packet.ReadInt32();
			}
			for (int n = 0; n < 5; n++)
			{
				quest.RewardFactionOverride[n] = (int)packet.ReadUInt32();
			}
		}
		quest.POIContinent = packet.ReadUInt32();
		quest.POIx = packet.ReadFloat();
		quest.POIy = packet.ReadFloat();
		quest.POIPriority = packet.ReadUInt32();
		quest.LogTitle = packet.ReadCString();
		quest.LogDescription = packet.ReadCString();
		quest.QuestDescription = packet.ReadCString();
		quest.AreaDescription = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.QuestCompletionLog = packet.ReadCString();
		}
		KeyValuePair<int, bool>[] reqId = new KeyValuePair<int, bool>[4];
		int reqItemFieldCount = 4;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
		{
			reqItemFieldCount = 5;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			reqItemFieldCount = 6;
		}
		int[] requiredItemID = new int[reqItemFieldCount];
		int[] requiredItemCount = new int[reqItemFieldCount];
		for (int num = 0; num < 4; num++)
		{
			reqId[num] = packet.ReadEntry();
			bool isGo = reqId[num].Value;
			int creatureOrGoId = reqId[num].Key;
			int creatureOrGoAmount = packet.ReadInt32();
			if (creatureOrGoId != 0 && creatureOrGoAmount != 0)
			{
				QuestObjective objective4 = new QuestObjective();
				objective4.QuestID = response.QuestID;
				objective4.Id = QuestObjective.QuestObjectiveCounter++;
				objective4.StorageIndex = objectiveCounter++;
				objective4.Type = (isGo ? QuestObjectiveType.GameObject : QuestObjectiveType.Monster);
				objective4.ObjectID = creatureOrGoId;
				objective4.Amount = creatureOrGoAmount;
				quest.Objectives.Add(objective4);
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				requiredItemID[num] = packet.ReadInt32();
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
			{
				requiredItemCount[num] = packet.ReadInt32();
			}
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_8_9464))
			{
				requiredItemID[num] = packet.ReadInt32();
				requiredItemCount[num] = packet.ReadInt32();
			}
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
		{
			for (int num2 = 0; num2 < reqItemFieldCount; num2++)
			{
				requiredItemID[num2] = packet.ReadInt32();
				requiredItemCount[num2] = packet.ReadInt32();
			}
		}
		for (int num3 = 0; num3 < reqItemFieldCount; num3++)
		{
			if (requiredItemID[num3] != 0 && requiredItemCount[num3] != 0)
			{
				QuestObjective objective5 = new QuestObjective();
				objective5.QuestID = response.QuestID;
				objective5.Id = QuestObjective.QuestObjectiveCounter++;
				objective5.StorageIndex = objectiveCounter++;
				objective5.Type = QuestObjectiveType.Item;
				objective5.ObjectID = requiredItemID[num3];
				objective5.Amount = requiredItemCount[num3];
				quest.Objectives.Add(objective5);
			}
		}
		for (int num4 = 0; num4 < 4; num4++)
		{
			string objectiveText = packet.ReadCString();
			if (quest.Objectives.Count > num4)
			{
				quest.Objectives[num4].Description = objectiveText;
			}
		}
		quest.QuestMaxScalingLevel = 255;
		quest.RewardXPMultiplier = 1f;
		quest.RewardMoneyMultiplier = 1f;
		quest.RewardArtifactXPMultiplier = 1f;
		for (int num5 = 0; num5 < 5; num5++)
		{
			quest.RewardFactionCapIn[num5] = 7;
		}
		quest.AllowableRaces = 511L;
		quest.AcceptedSoundKitID = 890u;
		quest.CompleteSoundKitID = 878u;
		GameData.StoreQuestTemplate(response.QuestID, quest);
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUERY_CREATURE_RESPONSE)]
	private void HandleQueryCreatureResponse(WorldPacket packet)
	{
		QueryCreatureResponse response = new QueryCreatureResponse();
		KeyValuePair<int, bool> id = packet.ReadEntry();
		response.CreatureID = (uint)id.Key;
		if (id.Value)
		{
			response.Allow = false;
			this.SendPacketToClient(response);
			return;
		}
		response.Allow = true;
		response.Stats = new CreatureTemplate();
		CreatureTemplate creature = response.Stats;
		for (int i = 0; i < 4; i++)
		{
			creature.Name[i] = packet.ReadCString();
		}
		creature.Title = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			creature.CursorName = packet.ReadCString();
		}
		creature.Flags[0] = packet.ReadUInt32();
		creature.Type = packet.ReadInt32();
		creature.Family = packet.ReadInt32();
		creature.Classification = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			for (int j = 0; j < 2; j++)
			{
				creature.ProxyCreatureID[j] = packet.ReadUInt32();
			}
		}
		else
		{
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				packet.ReadInt32();
			}
			creature.PetSpellDataId = packet.ReadUInt32();
		}
		int displayIdCount = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? 1 : 4);
		for (int k = 0; k < displayIdCount; k++)
		{
			uint displayId = packet.ReadUInt32();
			if (displayId != 0)
			{
				creature.Display.CreatureDisplay.Add(new CreatureXDisplay(displayId, 1f, 0f));
			}
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			creature.HpMulti = packet.ReadFloat();
			creature.EnergyMulti = packet.ReadFloat();
		}
		else
		{
			creature.HpMulti = 1f;
			creature.EnergyMulti = 1f;
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			creature.Civilian = packet.ReadBool();
		}
		creature.Leader = packet.ReadBool();
		int questItems = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192) ? 6 : 4);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			for (uint i2 = 0u; i2 < questItems; i2++)
			{
				uint itemId = packet.ReadUInt32();
				if (itemId != 0)
				{
					creature.QuestItems.Add(itemId);
				}
			}
			packet.ReadUInt32();
		}
		creature.Flags[0] |= 134217728u;
		creature.MovementInfoID = 1693u;
		creature.Class = 1;
		GameData.StoreCreatureTemplate(response.CreatureID, creature);
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUERY_GAME_OBJECT_RESPONSE)]
	private void HandleQueryGameObjectResponse(WorldPacket packet)
	{
		QueryGameObjectResponse response = new QueryGameObjectResponse();
		KeyValuePair<int, bool> id = packet.ReadEntry();
		response.GameObjectID = (uint)id.Key;
		response.Guid = WowGuid128.Empty;
		if (id.Value)
		{
			response.Allow = false;
			this.SendPacketToClient(response);
			return;
		}
		response.Allow = true;
		response.Stats = new GameObjectStats();
		GameObjectStats gameObject = response.Stats;
		gameObject.Type = packet.ReadUInt32();
		gameObject.DisplayID = packet.ReadUInt32();
		for (int i = 0; i < 4; i++)
		{
			gameObject.Name[i] = packet.ReadCString();
		}
		gameObject.IconName = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			gameObject.CastBarCaption = packet.ReadCString();
			gameObject.UnkString = packet.ReadCString();
		}
		for (int j = 0; j < 24; j++)
		{
			gameObject.Data[j] = packet.ReadInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			gameObject.Size = packet.ReadFloat();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			uint count = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192) ? 6u : 4u);
			for (uint i2 = 0u; i2 < count; i2++)
			{
				uint itemId = packet.ReadUInt32();
				if (itemId != 0)
				{
					gameObject.QuestItems.Add(itemId);
				}
			}
		}
		// Cache for pre-sending before transport CreateObjects
		GetSession().GameState.GameObjectQueryCache[response.GameObjectID] = response;
		if (gameObject.Type == 15) // MO_TRANSPORT — log template data for debugging
			Log.Print(LogType.Debug, $"[GOQuery] Entry={response.GameObjectID} Type={gameObject.Type} DisplayID={gameObject.DisplayID} Name={gameObject.Name[0]} data0(pathID)={gameObject.Data[0]} data1(speed)={gameObject.Data[1]} data2(accel)={gameObject.Data[2]} Size={gameObject.Size}");
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUERY_PAGE_TEXT_RESPONSE)]
	private void HandleQueryPageTextResponse(WorldPacket packet)
	{
		QueryPageTextResponse response = new QueryPageTextResponse();
		response.PageTextID = packet.ReadUInt32();
		response.Allow = true;
		QueryPageTextResponse.PageTextInfo page = new QueryPageTextResponse.PageTextInfo
		{
			Id = response.PageTextID,
			Text = packet.ReadCString(),
			NextPageID = packet.ReadUInt32()
		};
		response.Pages.Add(page);
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUERY_NPC_TEXT_RESPONSE)]
	private void HandleQueryNpcTextResponse(WorldPacket packet)
	{
		QueryNPCTextResponse response = new QueryNPCTextResponse();
		KeyValuePair<int, bool> id = packet.ReadEntry();
		response.TextID = (uint)id.Key;
		if (id.Value)
		{
			response.Allow = false;
			this.SendPacketToClient(response);
			return;
		}
		response.Allow = true;
		for (int i = 0; i < 8; i++)
		{
			response.Probabilities[i] = packet.ReadFloat();
			string maleText = packet.ReadCString().TrimEnd().Replace("\0", "");
			string femaleText = packet.ReadCString().TrimEnd().Replace("\0", "");
			uint language = packet.ReadUInt32();
			ushort[] emoteDelays = new ushort[3];
			ushort[] emotes = new ushort[3];
			for (int j = 0; j < 3; j++)
			{
				emoteDelays[j] = (ushort)packet.ReadUInt32();
				emotes[j] = (ushort)packet.ReadUInt32();
			}
			if ((string.IsNullOrEmpty(maleText) && string.IsNullOrEmpty(femaleText)) || (maleText.Equals("Greetings $N") && femaleText.Equals("Greetings $N") && i != 0))
			{
				response.BroadcastTextID[i] = 0u;
			}
			else
			{
				response.BroadcastTextID[i] = GameData.GetBroadcastTextId(maleText, femaleText, language, emoteDelays, emotes);
			}
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_ITEM_QUERY_SINGLE_RESPONSE)]
	private void HandleItemQueryResponse(WorldPacket packet)
	{
		KeyValuePair<int, bool> entry = packet.ReadEntry();
		if (entry.Value)
		{
			// Server doesn't have this item - remove from requested sets but do NOT send
			// Invalid DBReply, as that would poison the client's hotfix cache before a
			// valid hotfix can arrive from a different query
			this.GetSession().GameState.RequestedItemHotfixes.Remove((uint)entry.Key);
			this.GetSession().GameState.RequestedItemSparseHotfixes.Remove((uint)entry.Key);
			Log.Print(LogType.Debug, $"Item #{entry.Key} not found on legacy server, skipping Invalid DBReply.", "HandleItemQueryResponse", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\QueryHandler.cs");
		}
		else
		{
			ItemTemplate item = new ItemTemplate();
			item.ReadFromLegacyPacket((uint)entry.Key, packet);
			this.SendItemUpdatesIfNeeded(item);
			GameData.StoreItemTemplate((uint)entry.Key, item);

			// Flush any buffered item CreateObjects that were waiting for this template
			uint itemEntryKey = (uint)entry.Key;
			if (this.GetSession().GameState.PendingItemCreates.TryGetValue(itemEntryKey, out var pendingUpdates))
			{
				this.GetSession().GameState.PendingItemCreates.Remove(itemEntryKey);
				UpdateObject updateObject = new UpdateObject(this.GetSession().GameState);
				foreach (var pending in pendingUpdates)
				{
					updateObject.ObjectUpdates.Add(pending);
					Log.Print(LogType.Debug, $"Flushing buffered item CreateObject {pending.Guid} entry={itemEntryKey} after template arrived.", "HandleItemQueryResponse", "");
				}
				if (updateObject.ObjectUpdates.Count > 0)
				{
					this.SendPacketToClient(updateObject);
				}
			}
		}
	}

	private void SendItemUpdatesIfNeeded(ItemTemplate item)
	{
		// Skip hotfix for glyph items (class=16) - the 3.4.3 client already has correct
		// data for these in CASC. Our hotfix would override it with incomplete data,
		// breaking the icon and preventing the item from being used.
		if (item.Class == 16)
			return;

		HotFixMessage reply = GameData.GenerateItemUpdateIfNeeded(item);
		if (reply != null)
		{
			this.SendPacketToClient(reply);
		}
		reply = GameData.GenerateItemSparseUpdateIfNeeded(item);
		if (reply != null)
		{
			this.SendPacketToClient(reply);
			DBReply replyA = new DBReply();
			replyA.Status = HotfixStatus.Valid;
			replyA.Timestamp = (uint)Time.UnixTime;
			replyA.RecordID = reply.Hotfixes[0].RecordId;
			replyA.TableHash = reply.Hotfixes[0].TableHash;
			replyA.Data = reply.Hotfixes[0].HotfixContent;
			this.SendPacketToClient(replyA);
		}
		// Skip ItemEffect hotfix for mount items (class 15, subclass 5) — the DB2 already has
		// the correct mount summon spell; overwriting with the learn spell (55884) breaks the
		// 3.4.3 client's mount item recognition.
		if (item.Class != 15 || item.SubClass != 5)
		{
			for (byte i = 0; i < 5; i++)
			{
				reply = GameData.GenerateItemEffectUpdateIfNeeded(item, i);
				if (reply != null)
				{
					this.SendPacketToClient(reply);
				}
			}
		}
		if (GameData.ItemCanHaveModel(item))
		{
			reply = GameData.GenerateItemAppearanceUpdateIfNeeded(item);
			if (reply != null)
			{
				this.SendPacketToClient(reply);
			}
			reply = GameData.GenerateItemModifiedAppearanceUpdateIfNeeded(item);
			if (reply != null)
			{
				this.SendPacketToClient(reply);
			}
		}
	}

	[PacketHandler(Opcode.SMSG_QUERY_PET_NAME_RESPONSE)]
	private void HandleQueryPetNameResponse(WorldPacket packet)
	{
		uint petNumber = packet.ReadUInt32();
		WowGuid128 guid = this.GetSession().GameState.GetPetGuidByNumber(petNumber);
		if (guid == null)
		{
			Log.Print(LogType.Error, $"Pet name query response for unknown pet {petNumber}!", "HandleQueryPetNameResponse", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\QueryHandler.cs");
			return;
		}
		QueryPetNameResponse response = new QueryPetNameResponse();
		response.UnitGUID = guid;
		response.Name = packet.ReadCString();
		if (response.Name.Length == 0)
		{
			response.Allow = false;
			packet.ReadBytes(7u);
			return;
		}
		response.Allow = true;
		response.Timestamp = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.ReadBool())
		{
			for (int i = 0; i < 5; i++)
			{
				response.DeclinedNames.name[i] = packet.ReadCString();
			}
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_ITEM_NAME_QUERY_RESPONSE)]
	private void HandleItemNameQueryResponse(WorldPacket packet)
	{
		uint entry = packet.ReadUInt32();
		string name = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.ReadUInt32();
		}
		GameData.StoreItemName(entry, name);
	}

	[PacketHandler(Opcode.SMSG_WHO)]
	private void HandleWhoResponse(WorldPacket packet)
	{
		WhoResponsePkt response = new WhoResponsePkt();
		response.RequestID = this.GetSession().GameState.LastWhoRequestId;
		uint count = packet.ReadUInt32();
		packet.ReadUInt32();
		for (int i = 0; i < count; i++)
		{
			WhoEntry player = new WhoEntry();
			player.PlayerData.Name = packet.ReadCString();
			player.GuildName = packet.ReadCString();
			player.PlayerData.Level = (byte)packet.ReadUInt32();
			player.PlayerData.ClassID = (Class)packet.ReadUInt32();
			player.PlayerData.RaceID = (Race)packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				player.PlayerData.Sex = (Gender)packet.ReadUInt8();
			}
			player.AreaID = packet.ReadInt32();
			player.PlayerData.GuidActual = this.GetSession().GameState.GetPlayerGuidByName(player.PlayerData.Name);
			if (player.PlayerData.GuidActual == null)
			{
				player.PlayerData.GuidActual = WowGuid128.CreateUnknownPlayerGuid();
			}
			player.PlayerData.AccountID = this.GetSession().GetGameAccountGuidForPlayer(player.PlayerData.GuidActual);
			player.PlayerData.BnetAccountID = this.GetSession().GetBnetAccountGuidForPlayer(player.PlayerData.GuidActual);
			player.PlayerData.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
			if (!string.IsNullOrEmpty(player.GuildName))
			{
				player.GuildGUID = this.GetSession().GetGuildGuid(player.GuildName);
				player.GuildVirtualRealmAddress = player.PlayerData.VirtualRealmAddress;
			}
			response.Players.Add(player);
			this.Session.GameState.UpdatePlayerCache(player.PlayerData.GuidActual, new PlayerCache
			{
				Name = player.PlayerData.Name,
				RaceId = player.PlayerData.RaceID,
				ClassId = player.PlayerData.ClassID,
				SexId = player.PlayerData.Sex,
				Level = player.PlayerData.Level
			});
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_QUEST_DETAILS)]
	private void HandleQuestGiverQuestDetails(WorldPacket packet)
	{
		QuestGiverQuestDetails quest = new QuestGiverQuestDetails();
		quest.QuestGiverGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = quest.QuestGiverGUID;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			quest.InformUnit = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		else
		{
			quest.InformUnit = quest.QuestGiverGUID;
		}
		quest.QuestID = packet.ReadUInt32();
		quest.QuestTitle = packet.ReadCString();
		quest.DescriptionText = packet.ReadCString();
		quest.LogDescription = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.AutoLaunched = packet.ReadBool();
		}
		else
		{
			quest.AutoLaunched = packet.ReadUInt32() != 0;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			quest.QuestFlags[0] = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			quest.SuggestedPartyMembers = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		if (LegacyVersion.InVersion(ClientVersionBuild.V3_1_0_9767, ClientVersionBuild.V3_3_3a_11723))
		{
			quest.StartCheat = packet.ReadBool();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_2_11403))
			{
				quest.DisplayPopup = packet.ReadBool();
			}
		}
		if (quest.QuestFlags[0].HasAnyFlag(QuestFlags.HiddenRewards) && LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_3_5a_12340))
		{
			packet.ReadUInt32();
			packet.ReadUInt32();
			quest.Rewards.Money = packet.ReadUInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_2_10482))
			{
				quest.Rewards.XP = packet.ReadUInt32();
			}
		}
		this.ReadExtraQuestInfo(packet, quest.Rewards, readFlags: false);
		uint emoteCount = packet.ReadUInt32();
		for (int i = 0; i < emoteCount; i++)
		{
			quest.DescEmotes[i].Type = packet.ReadUInt32();
			quest.DescEmotes[i].Delay = packet.ReadUInt32();
		}
		this.SendPacketToClient(quest);
	}

	private void ReadExtraQuestInfo(WorldPacket packet, QuestRewards rewards, bool readFlags)
	{
		rewards.ChoiceItemCount = packet.ReadUInt32();
		for (int i = 0; i < rewards.ChoiceItemCount; i++)
		{
			rewards.ChoiceItems[i].Item.ItemID = packet.ReadUInt32();
			rewards.ChoiceItems[i].Quantity = packet.ReadUInt32();
			packet.ReadUInt32();
		}
		uint rewardCount = packet.ReadUInt32();
		for (int j = 0; j < rewardCount; j++)
		{
			rewards.ItemID[j] = packet.ReadUInt32();
			rewards.ItemQty[j] = packet.ReadUInt32();
			packet.ReadUInt32();
		}
		rewards.Money = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_2_10482))
		{
			rewards.XP = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_3_0_7561))
		{
			rewards.Honor = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			packet.ReadFloat();
		}
		if (readFlags)
		{
			packet.ReadUInt32();
		}
		rewards.SpellCompletionID = packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
		{
			rewards.Title = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			rewards.NumSkillUps = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			packet.ReadUInt32();
			packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			for (int k = 0; k < 5; k++)
			{
				rewards.FactionID[k] = packet.ReadUInt32();
			}
			for (int l = 0; l < 5; l++)
			{
				rewards.FactionValue[l] = packet.ReadInt32();
			}
			for (int m = 0; m < 5; m++)
			{
				packet.ReadInt32();
			}
		}
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_STATUS)]
	private void HandleQuestGiverStatus(WorldPacket packet)
	{
		QuestGiverStatusPkt response = new QuestGiverStatusPkt();
		response.QuestGiver.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		response.QuestGiver.Status = LegacyVersion.ConvertQuestGiverStatus(packet.ReadUInt8());
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_STATUS_MULTIPLE)]
	private void HandleQuestGiverStatusMultple(WorldPacket packet)
	{
		QuestGiverStatusMultiple response = new QuestGiverStatusMultiple();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			QuestGiverInfo info = new QuestGiverInfo();
			info.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			info.Status = LegacyVersion.ConvertQuestGiverStatus(packet.ReadUInt8());
			response.QuestGivers.Add(info);
		}
		this.SendPacketToClient(response);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_QUEST_LIST_MESSAGE)]
	private void HandleQuestGiverQuestListMessage(WorldPacket packet)
	{
		QuestGiverQuestListMessage quests = new QuestGiverQuestListMessage();
		quests.QuestGiverGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = quests.QuestGiverGUID;
		quests.Greeting = packet.ReadCString();
		quests.GreetEmoteDelay = packet.ReadUInt32();
		quests.GreetEmoteType = packet.ReadUInt32();
		byte count = packet.ReadUInt8();
		for (int i = 0; i < count; i++)
		{
			ClientGossipQuest quest = this.ReadGossipQuestOption(packet);
			quests.QuestOptions.Add(quest);
		}
		this.SendPacketToClient(quests);
	}

	private ClientGossipQuest ReadGossipQuestOption(WorldPacket packet)
	{
		ClientGossipQuest quest = new ClientGossipQuest();
		quest.QuestID = packet.ReadUInt32();
		// Icon value from server: 0=autocomplete, 2=available, 4=completable
		// Use directly as QuestType - do NOT convert through QuestGiverStatus enum
		int questIcon = packet.ReadInt32();
		quest.QuestType = questIcon;
		quest.QuestLevel = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			quest.QuestFlags = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			quest.Repeatable = packet.ReadBool();
		}
		quest.QuestTitle = packet.ReadCString();
		return quest;
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_REQUEST_ITEMS)]
	private void HandleQuestGiverRequestItems(WorldPacket packet)
	{
		QuestGiverRequestItems quest = new QuestGiverRequestItems();
		quest.QuestGiverGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = quest.QuestGiverGUID;
		quest.QuestGiverCreatureID = quest.QuestGiverGUID.GetEntry();
		quest.QuestID = packet.ReadUInt32();
		quest.QuestTitle = packet.ReadCString();
		quest.CompletionText = packet.ReadCString();
		quest.CompEmoteDelay = packet.ReadUInt32();
		quest.CompEmoteType = packet.ReadUInt32();
		quest.AutoLaunched = packet.ReadUInt32() != 0;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			quest.QuestFlags[0] = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			quest.SuggestPartyMembers = packet.ReadUInt32();
		}
		quest.MoneyToGet = packet.ReadInt32();
		uint itemsCount = packet.ReadUInt32();
		for (int i = 0; i < itemsCount; i++)
		{
			QuestObjectiveCollect item = new QuestObjectiveCollect
			{
				ObjectID = packet.ReadUInt32(),
				Amount = packet.ReadUInt32()
			};
			packet.ReadUInt32();
			quest.Collect.Add(item);
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.ReadUInt32();
		}
		uint statusFlags = packet.ReadUInt32();
		if ((statusFlags & 3) != 0)
		{
			quest.StatusFlags = 223u;
		}
		else
		{
			quest.StatusFlags = 219u;
		}
		packet.ReadUInt32();
		packet.ReadUInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.ReadUInt32();
		}
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_OFFER_REWARD_MESSAGE)]
	private void HandleQuestGiverOfferRewardMessage(WorldPacket packet)
	{
		QuestGiverOfferRewardMessage quest = new QuestGiverOfferRewardMessage();
		quest.QuestData.QuestGiverGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.GetSession().GameState.CurrentInteractedWithNPC = quest.QuestData.QuestGiverGUID;
		quest.QuestData.QuestGiverCreatureID = quest.QuestData.QuestGiverGUID.GetEntry();
		quest.QuestGiverCreatureID = (int)quest.QuestData.QuestGiverGUID.GetEntry();
		quest.QuestData.QuestID = packet.ReadUInt32();
		quest.QuestTitle = packet.ReadCString();
		quest.RewardText = packet.ReadCString();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
		{
			quest.QuestData.AutoLaunched = packet.ReadBool();
		}
		else
		{
			quest.QuestData.AutoLaunched = packet.ReadUInt32() != 0;
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_3_11685))
		{
			quest.QuestData.QuestFlags[0] = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			quest.QuestData.SuggestedPartyMembers = packet.ReadUInt32();
		}
		uint emotesCount = packet.ReadUInt32();
		for (int i = 0; i < emotesCount; i++)
		{
			QuestDescEmote emote = new QuestDescEmote
			{
				Delay = packet.ReadUInt32(),
				Type = packet.ReadUInt32()
			};
		}
		this.ReadExtraQuestInfo(packet, quest.QuestData.Rewards, readFlags: true);
		// Cache quest template for reward selection (HandleQuestGiverChooseReward needs it)
		if (GameData.GetQuestTemplate(quest.QuestData.QuestID) == null)
		{
			QuestTemplate cached = new QuestTemplate();
			for (int ci = 0; ci < quest.QuestData.Rewards.ChoiceItemCount && ci < 6; ci++)
			{
				cached.UnfilteredChoiceItems[ci].ItemID = quest.QuestData.Rewards.ChoiceItems[ci].Item.ItemID;
				cached.UnfilteredChoiceItems[ci].Quantity = quest.QuestData.Rewards.ChoiceItems[ci].Quantity;
			}
			GameData.StoreQuestTemplate(quest.QuestData.QuestID, cached);
		}
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_QUEST_COMPLETE)]
	private void HandleQuestGiverQuestComplete(WorldPacket packet)
	{
		QuestGiverQuestComplete quest = new QuestGiverQuestComplete();
		quest.QuestID = packet.ReadUInt32();
		this.GetSession().GameState.CurrentPlayerStorage.CompletedQuests.MarkQuestAsCompleted(quest.QuestID);
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt32();
		}
		quest.XPReward = packet.ReadUInt32();
		quest.MoneyReward = packet.ReadInt32();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_3_0_7561))
		{
			packet.ReadInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadInt32();
			packet.ReadInt32();
		}
		uint itemId = 0u;
		uint itemCount = 0u;
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			uint itemsCount = packet.ReadUInt32();
			for (uint i = 0u; i < itemsCount; i++)
			{
				uint itemId2 = packet.ReadUInt32();
				uint itemCount2 = packet.ReadUInt32();
				if (itemId2 != 0 && itemCount2 != 0)
				{
					itemId = itemId2;
					itemCount = itemCount2;
				}
			}
		}
		quest.ItemReward.ItemID = itemId;
		QuestTemplate questTemplate = GameData.GetQuestTemplate(quest.QuestID);
		if (questTemplate != null && questTemplate.RewardNextQuest == 0)
		{
			quest.LaunchQuest = false;
			if (this.GetSession().GameState.CurrentInteractedWithNPC != null)
			{
				uint npcFlags = this.GetSession().GameState.GetLegacyFieldValueUInt32(this.GetSession().GameState.CurrentInteractedWithNPC, UnitField.UNIT_NPC_FLAGS);
				if (npcFlags.HasAnyFlag(NPCFlags.Gossip))
				{
					quest.LaunchGossip = true;
				}
			}
		}
		this.SendPacketToClient(quest);
		DisplayToast toast = new DisplayToast();
		toast.QuestID = quest.QuestID;
		if (itemId != 0 && itemCount != 0)
		{
			toast.Quantity = 1uL;
			toast.Type = 0;
			toast.ItemReward.ItemID = itemId;
		}
		else
		{
			toast.Quantity = 60uL;
			toast.Type = 2;
		}
		this.SendPacketToClient(toast);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_QUEST_FAILED)]
	private void HandleQuestGiverQuestFailed(WorldPacket packet)
	{
		QuestGiverQuestFailed quest = new QuestGiverQuestFailed();
		quest.QuestID = packet.ReadUInt32();
		quest.Reason = LegacyVersion.ConvertInventoryResult(packet.ReadUInt32());
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_QUEST_GIVER_INVALID_QUEST)]
	private void HandleQuestGiverInvalidQuest(WorldPacket packet)
	{
		QuestGiverInvalidQuest quest = new QuestGiverInvalidQuest();
		quest.Reason = (QuestFailedReasons)packet.ReadUInt32();
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_QUEST_UPDATE_COMPLETE)]
	[PacketHandler(Opcode.SMSG_QUEST_UPDATE_FAILED)]
	[PacketHandler(Opcode.SMSG_QUEST_UPDATE_FAILED_TIMER)]
	private void HandleQuestUpdateStatus(WorldPacket packet)
	{
		QuestUpdateStatus quest = new QuestUpdateStatus(packet.GetUniversalOpcode(isModern: false));
		quest.QuestID = packet.ReadUInt32();
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_QUEST_UPDATE_ADD_ITEM)]
	private void HandleQuestUpdateAddItem(WorldPacket packet)
	{
		uint itemId = packet.ReadUInt32();
		uint count = packet.ReadUInt32();
		QuestObjective objective = GameData.GetQuestObjectiveForItem(itemId);
		if (objective != null)
		{
			return;
		}
		Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(this.GetSession().GameState.CurrentPlayerGuid);
		int questsCount = LegacyVersion.GetQuestLogSize();
		for (int i = 0; i < questsCount; i++)
		{
			QuestLog logEntry = this.ReadQuestLogEntry(i, null, updateFields);
			if (logEntry != null && logEntry.QuestID.HasValue && GameData.GetQuestTemplate((uint)logEntry.QuestID.Value) == null)
			{
				WorldPacket packet2 = new WorldPacket(Opcode.CMSG_QUERY_QUEST_INFO);
				packet2.WriteUInt32((uint)logEntry.QuestID.Value);
				this.SendPacketToServer(packet2);
			}
		}
	}

	[PacketHandler(Opcode.SMSG_QUEST_UPDATE_ADD_KILL)]
	private void HandleQuestUpdateAddKill(WorldPacket packet)
	{
		QuestUpdateAddCredit credit = new QuestUpdateAddCredit();
		credit.QuestID = packet.ReadUInt32();
		KeyValuePair<int, bool> entry = packet.ReadEntry();
		credit.ObjectID = entry.Key;
		credit.ObjectiveType = (entry.Value ? QuestObjectiveType.GameObject : QuestObjectiveType.Monster);
		credit.Count = (ushort)packet.ReadUInt32();
		credit.Required = (ushort)packet.ReadUInt32();
		credit.VictimGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(credit);
	}

	[PacketHandler(Opcode.SMSG_QUEST_CONFIRM_ACCEPT)]
	private void HandleQuestConfirmAccept(WorldPacket packet)
	{
		QuestConfirmAccept quest = new QuestConfirmAccept();
		quest.QuestID = packet.ReadUInt32();
		quest.QuestTitle = packet.ReadCString();
		quest.InitiatedBy = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.MSG_QUEST_PUSH_RESULT)]
	private void HandleQuestPushResult(WorldPacket packet)
	{
		QuestPushResult quest = new QuestPushResult();
		quest.SenderGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		quest.Result = (QuestPushReason)packet.ReadUInt8();
		this.SendPacketToClient(quest);
	}

	[PacketHandler(Opcode.SMSG_INITIALIZE_FACTIONS)]
	private void HandleInitializeFactions(WorldPacket packet)
	{
		if (this.GetSession().GameState.IsFirstEnterWorld)
		{
			InitializeFactions factions = new InitializeFactions();
			uint count = packet.ReadUInt32();
			for (uint i = 0u; i < count; i++)
			{
				factions.FactionFlags[i] = (ReputationFlags)packet.ReadUInt8();
				factions.FactionStandings[i] = packet.ReadInt32();
			}
			this.SendPacketToClient(factions);
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.SendPacketToClient(new TimeSyncRequest());
			}
		}
	}

	[PacketHandler(Opcode.SMSG_SET_FACTION_STANDING)]
	private void HandleSetFactionStanding(WorldPacket packet)
	{
		SetFactionStanding standing = new SetFactionStanding();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
		{
			packet.ReadFloat();
		}
		bool showVisual = true;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			showVisual = packet.ReadBool();
		}
		standing.ShowVisual = showVisual;
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			FactionStandingData faction = new FactionStandingData
			{
				Index = packet.ReadInt32(),
				Standing = packet.ReadInt32()
			};
			standing.Factions.Add(faction);
		}
		this.SendPacketToClient(standing);
	}

	[PacketHandler(Opcode.SMSG_SET_FORCED_REACTIONS)]
	private void HandleSetForcedReaction(WorldPacket packet)
	{
		SetForcedReactions reactions = new SetForcedReactions();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			ForcedReaction reaction = new ForcedReaction
			{
				Faction = packet.ReadInt32(),
				Reaction = packet.ReadInt32()
			};
			reactions.Reactions.Add(reaction);
		}
		this.SendPacketToClient(reactions);
	}

	[PacketHandler(Opcode.SMSG_SET_FACTION_VISIBLE)]
	private void HandleSetFactionVisible(WorldPacket packet)
	{
		SetFactionVisible faction = new SetFactionVisible(visible: true);
		faction.FactionIndex = packet.ReadUInt32();
		this.SendPacketToClient(faction);
	}

	[PacketHandler(Opcode.SMSG_FRIEND_LIST)]
	private void HandleFriendList(WorldPacket packet)
	{
		ContactList contacts = new ContactList();
		contacts.Flags = SocialFlag.Friend;
		byte count = packet.ReadUInt8();
		for (int i = 0; i < count; i++)
		{
			ContactInfo contact = new ContactInfo();
			contact.TypeFlags = SocialFlag.Friend;
			contact.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			contact.WowAccountGuid = this.GetSession().GetGameAccountGuidForPlayer(contact.Guid);
			contact.NativeRealmAddr = this.GetSession().RealmId.GetAddress();
			contact.VirtualRealmAddr = this.GetSession().RealmId.GetAddress();
			contact.Status = (FriendStatus)packet.ReadUInt8();
			if (contact.Status != FriendStatus.Offline)
			{
				contact.AreaID = packet.ReadUInt32();
				contact.Level = packet.ReadUInt32();
				contact.ClassID = (Class)packet.ReadUInt32();
			}
			contacts.Contacts.Add(contact);
		}
		this.SendPacketToClient(contacts);
	}

	[PacketHandler(Opcode.SMSG_IGNORE_LIST)]
	private void HandleIgnoreList(WorldPacket packet)
	{
		ContactList contacts = new ContactList();
		contacts.Flags = SocialFlag.Ignored;
		byte count = packet.ReadUInt8();
		HashSet<WowGuid128> ignoredPlayers = new HashSet<WowGuid128>();
		for (int i = 0; i < count; i++)
		{
			ContactInfo contact = new ContactInfo();
			contact.TypeFlags = SocialFlag.Ignored;
			contact.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			contact.WowAccountGuid = this.GetSession().GetGameAccountGuidForPlayer(contact.Guid);
			contact.NativeRealmAddr = this.GetSession().RealmId.GetAddress();
			contact.VirtualRealmAddr = this.GetSession().RealmId.GetAddress();
			contacts.Contacts.Add(contact);
			ignoredPlayers.Add(contact.Guid);
		}
		this.Session.GameState.IgnoredPlayers = ignoredPlayers;
		this.SendPacketToClient(contacts);
	}

	[PacketHandler(Opcode.SMSG_CONTACT_LIST)]
	private void HandleContactList(WorldPacket packet)
	{
		ContactList contacts = new ContactList();
		contacts.Flags = (SocialFlag)packet.ReadUInt32();
		uint count = packet.ReadUInt32();
		for (int i = 0; i < count; i++)
		{
			ContactInfo contact = new ContactInfo();
			contact.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
			contact.WowAccountGuid = this.GetSession().GetGameAccountGuidForPlayer(contact.Guid);
			contact.NativeRealmAddr = this.GetSession().RealmId.GetAddress();
			contact.VirtualRealmAddr = this.GetSession().RealmId.GetAddress();
			contact.TypeFlags = (SocialFlag)packet.ReadUInt32();
			contact.Note = packet.ReadCString();
			if (contact.TypeFlags.HasAnyFlag(SocialFlag.Friend))
			{
				contact.Status = (FriendStatus)packet.ReadUInt8();
				if (contact.Status != FriendStatus.Offline)
				{
					contact.AreaID = packet.ReadUInt32();
					contact.Level = packet.ReadUInt32();
					contact.ClassID = (Class)packet.ReadUInt32();
				}
			}
			contacts.Contacts.Add(contact);
		}
		this.SendPacketToClient(contacts);
	}

	[PacketHandler(Opcode.SMSG_FRIEND_STATUS)]
	private void HandleFriendStatus(WorldPacket packet)
	{
		FriendStatusPkt friend = new FriendStatusPkt();
		friend.FriendResult = (FriendsResult)packet.ReadUInt8();
		friend.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		friend.WowAccountGuid = this.GetSession().GetGameAccountGuidForPlayer(friend.Guid);
		friend.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		switch (friend.FriendResult)
		{
		case FriendsResult.AddedOffline:
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				friend.Notes = packet.ReadCString();
			}
			break;
		case FriendsResult.AddedOnline:
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				friend.Notes = packet.ReadCString();
			}
			friend.Status = (FriendStatus)packet.ReadUInt8();
			friend.AreaID = packet.ReadUInt32();
			friend.Level = packet.ReadUInt32();
			friend.ClassID = (Class)packet.ReadUInt32();
			break;
		case FriendsResult.Online:
			friend.Status = (FriendStatus)packet.ReadUInt8();
			friend.AreaID = packet.ReadUInt32();
			friend.Level = packet.ReadUInt32();
			friend.ClassID = (Class)packet.ReadUInt32();
			break;
		}
		this.SendPacketToClient(friend);
		if (friend.FriendResult == FriendsResult.IgnoreAdded)
		{
			this.Session.GameState.IgnoredPlayers.Add(friend.Guid);
		}
		else if (friend.FriendResult == FriendsResult.IgnoreRemoved)
		{
			this.Session.GameState.IgnoredPlayers.Remove(friend.Guid);
		}
	}

	[PacketHandler(Opcode.SMSG_SEND_KNOWN_SPELLS)]
	private void HandleSendKnownSpells(WorldPacket packet)
	{
		SendKnownSpells spells = new SendKnownSpells();
		spells.InitialLogin = packet.ReadBool();
		ushort spellCount = packet.ReadUInt16();
		for (ushort i = 0; i < spellCount; i++)
		{
			if (!packet.CanRead()) break;
			uint spellId = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) ? packet.ReadUInt16() : packet.ReadUInt32());
			spells.KnownSpells.Add(spellId);
			this.GetSession().GameState.KnownSpells.Add(spellId);
			if (!packet.CanRead()) break;
			packet.ReadInt16();
		}
		this.SendPacketToClient(spells);
		// Send mount collection based on known mount spells
		this.SendAccountMountUpdate();
		if (!packet.CanRead())
			return;
		ushort cooldownCount = packet.ReadUInt16();
		if (cooldownCount != 0)
		{
			SendSpellHistory histories = new SendSpellHistory();
			for (ushort i2 = 0; i2 < cooldownCount; i2++)
			{
				if (!packet.CanRead()) break;
				SpellHistoryEntry history = new SpellHistoryEntry();
				uint spellId2 = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) ? packet.ReadUInt16() : packet.ReadUInt32());
				history.SpellID = spellId2;
				uint itemId = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V4_2_2_14545)) ? packet.ReadUInt16() : packet.ReadUInt32());
				history.ItemID = itemId;
				history.Category = packet.ReadUInt16();
				history.RecoveryTime = packet.ReadInt32();
				history.CategoryRecoveryTime = packet.ReadInt32();
				histories.Entries.Add(history);
			}
			this.SendPacketToClient(histories, Opcode.SMSG_SEND_UNLEARN_SPELLS);
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			this.SendPacketToClient(new SendUnlearnSpells());
			this.SendPacketToClient(new SendSpellCharges());
		}
	}

	[PacketHandler(Opcode.SMSG_SUPERCEDED_SPELLS)]
	private void HandleSupercededSpells(WorldPacket packet)
	{
		SupercededSpells spells = new SupercededSpells();
		uint supercededId;
		uint spellId;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			supercededId = packet.ReadUInt32();
			spellId = packet.ReadUInt32();
		}
		else
		{
			supercededId = packet.ReadUInt16();
			spellId = packet.ReadUInt16();
		}
		spells.SpellID.Add(spellId);
		spells.Superceded.Add(supercededId);
		this.SendPacketToClient(spells);
	}

	[PacketHandler(Opcode.SMSG_LEARNED_SPELL)]
	private void HandleLearnedSpell(WorldPacket packet)
	{
		LearnedSpells spells = new LearnedSpells();
		uint spellId = packet.ReadUInt32();
		spells.ClientLearnedSpellData.Add(new LearnedSpellInfo
		{
			SpellID = (int)spellId,
			IsFavorite = false,
			Superceded = null
		});
		this.GetSession().GameState.KnownSpells.Add(spellId);
		this.SendPacketToClient(spells);
		// If this is a mount spell, update the mount collection
		if (GameData.MountSpells.Contains(spellId))
			this.SendAccountMountUpdate();
	}

	/// <summary>
	/// Sends SMSG_ACCOUNT_MOUNT_UPDATE with all known mount spells.
	/// TC343 format: WriteBit IsFullUpdate, uint32 count, then (int32 spellId + 4-bit flags) per mount.
	/// </summary>
	private void SendAccountMountUpdate()
	{
		AccountMountUpdate update = new AccountMountUpdate();
		foreach (uint spellId in this.GetSession().GameState.KnownSpells)
		{
			if (GameData.MountSpells.Contains(spellId))
				update.MountSpellIDs.Add(spellId);
		}
		this.SendPacketToClient(update);
		Log.Print(LogType.Debug, $"[MountUpdate] Sent {update.MountSpellIDs.Count} mounts to client", "SendAccountMountUpdate", "");
	}

	[PacketHandler(Opcode.SMSG_SEND_UNLEARN_SPELLS)]
	private void HandleSendUnlearnSpells(WorldPacket packet)
	{
		SendUnlearnSpells spells = new SendUnlearnSpells();
		uint spellCount = packet.ReadUInt32();
		for (uint i = 0u; i < spellCount; i++)
		{
			uint spellId = packet.ReadUInt32();
			spells.Spells.Add(spellId);
		}
		this.SendPacketToClient(spells);
	}

	[PacketHandler(Opcode.SMSG_UNLEARNED_SPELLS)]
	private void HandleUnlearnedSpells(WorldPacket packet)
	{
		UnlearnedSpells spells = new UnlearnedSpells();
		uint spellId = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) ? packet.ReadUInt16() : packet.ReadUInt32());
		spells.Spells.Add(spellId);
		this.SendPacketToClient(spells);
	}

	[PacketHandler(Opcode.SMSG_CAST_FAILED)]
	private void HandleCastFailed(WorldPacket packet)
	{
		if (Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		uint spellId = packet.ReadUInt32();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			byte status = packet.ReadUInt8();
			if (status != 2)
			{
				return;
			}
		}
		uint reason = packet.ReadUInt8();
		Log.Print(LogType.Debug, $"[CastFailed] SpellID={spellId} Reason={reason}", "HandleCastFailed", "");
		if (LegacyVersion.InVersion(ClientVersionBuild.V2_0_1_6180, ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		int arg1 = 0;
		int arg2 = 0;
		if (packet.CanRead())
		{
			arg1 = packet.ReadInt32();
		}
		if (packet.CanRead())
		{
			arg2 = packet.ReadInt32();
		}
		if (this.GetSession().GameState.CurrentClientSpecialCast != null && this.GetSession().GameState.CurrentClientSpecialCast.SpellId == spellId)
		{
			CastFailed failed = new CastFailed();
			failed.SpellID = this.GetSession().GameState.CurrentClientSpecialCast.SpellId;
			failed.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientSpecialCast.SpellXSpellVisualId;
			failed.Reason = LegacyVersion.ConvertSpellCastResult(reason);
			failed.CastID = this.GetSession().GameState.CurrentClientSpecialCast.ServerGUID;
			failed.FailedArg1 = arg1;
			failed.FailedArg2 = arg2;
			this.SendPacketToClient(failed);
			this.GetSession().GameState.CurrentClientSpecialCast = null;
		}
		else
		{
			if (this.GetSession().GameState.CurrentClientNormalCast == null || this.GetSession().GameState.CurrentClientNormalCast.SpellId != spellId)
			{
				return;
			}
			if (!this.GetSession().GameState.CurrentClientNormalCast.HasStarted)
			{
				SpellPrepare prepare2 = new SpellPrepare();
				prepare2.ClientCastID = this.GetSession().GameState.CurrentClientNormalCast.ClientGUID;
				prepare2.ServerCastID = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
				this.SendPacketToClient(prepare2);
			}
			CastFailed failed2 = new CastFailed();
			failed2.SpellID = this.GetSession().GameState.CurrentClientNormalCast.SpellId;
			failed2.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientNormalCast.SpellXSpellVisualId;
			failed2.Reason = LegacyVersion.ConvertSpellCastResult(reason);
			failed2.CastID = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
			failed2.FailedArg1 = arg1;
			failed2.FailedArg2 = arg2;
			this.SendPacketToClient(failed2);
			this.GetSession().GameState.CurrentClientNormalCast = null;
			foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientCasts)
			{
				this.GetSession().InstanceSocket.SendCastRequestFailed(pending, isPet: false);
			}
			this.GetSession().GameState.PendingClientCasts.Clear();
		}
	}

	[PacketHandler(Opcode.SMSG_PET_CAST_FAILED, ClientVersionBuild.Zero, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePetCastFailed(WorldPacket packet)
	{
		if (Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		uint spellId = packet.ReadUInt32();
		byte status = packet.ReadUInt8();
		if (status != 2 || this.GetSession().GameState.CurrentClientPetCast == null || this.GetSession().GameState.CurrentClientPetCast.SpellId != spellId)
		{
			return;
		}
		if (!this.GetSession().GameState.CurrentClientPetCast.HasStarted)
		{
			SpellPrepare prepare2 = new SpellPrepare();
			prepare2.ClientCastID = this.GetSession().GameState.CurrentClientPetCast.ClientGUID;
			prepare2.ServerCastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
			this.SendPacketToClient(prepare2);
		}
		PetCastFailed spell = new PetCastFailed();
		spell.SpellID = spellId;
		uint reason = packet.ReadUInt8();
		spell.Reason = LegacyVersion.ConvertSpellCastResult(reason);
		spell.CastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
		this.SendPacketToClient(spell);
		foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientPetCasts)
		{
			this.GetSession().InstanceSocket.SendCastRequestFailed(pending, isPet: true);
		}
		this.GetSession().GameState.PendingClientPetCasts.Clear();
	}

	[PacketHandler(Opcode.SMSG_PET_CAST_FAILED, ClientVersionBuild.V2_0_1_6180)]
	private void HandlePetCastFailedTBC(WorldPacket packet)
	{
		if (Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		uint spellId = packet.ReadUInt32();
		if (this.GetSession().GameState.CurrentClientPetCast == null || this.GetSession().GameState.CurrentClientPetCast.SpellId != spellId)
		{
			return;
		}
		if (!this.GetSession().GameState.CurrentClientPetCast.HasStarted)
		{
			SpellPrepare prepare2 = new SpellPrepare();
			prepare2.ClientCastID = this.GetSession().GameState.CurrentClientPetCast.ClientGUID;
			prepare2.ServerCastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
			this.SendPacketToClient(prepare2);
		}
		PetCastFailed failed = new PetCastFailed();
		failed.SpellID = spellId;
		uint reason = packet.ReadUInt8();
		failed.Reason = LegacyVersion.ConvertSpellCastResult(reason);
		failed.CastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
		if (packet.CanRead())
		{
			failed.FailedArg1 = packet.ReadInt32();
		}
		if (packet.CanRead())
		{
			failed.FailedArg2 = packet.ReadInt32();
		}
		this.SendPacketToClient(failed);
		foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientPetCasts)
		{
			this.GetSession().InstanceSocket.SendCastRequestFailed(pending, isPet: true);
		}
		this.GetSession().GameState.PendingClientPetCasts.Clear();
	}

	[PacketHandler(Opcode.SMSG_SPELL_FAILURE)]
	private void HandleSpellFailure(WorldPacket packet)
	{
		// Consumed — SpellFailure is generated from SMSG_SPELL_FAILED_OTHER handler
	}

	[PacketHandler(Opcode.SMSG_SPELL_FAILED_OTHER)]
	private void HandleSpellFailedOther(WorldPacket packet)
	{
		WowGuid128 casterUnit = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? packet.ReadGuid().To128(this.GetSession().GameState) : packet.ReadPackedGuid().To128(this.GetSession().GameState));
		if (casterUnit == this.GetSession().GameState.CurrentPlayerGuid && Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		uint spellId = packet.ReadUInt32();
		byte reason = 61;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			reason = (byte)LegacyVersion.ConvertSpellCastResult(packet.ReadUInt8());
		}
		WowGuid128 castId;
		uint spellVisual;
		if (this.GetSession().GameState.CurrentPlayerGuid == casterUnit && this.GetSession().GameState.CurrentClientNormalCast != null && this.GetSession().GameState.CurrentClientNormalCast.SpellId == spellId)
		{
			castId = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
			spellVisual = this.GetSession().GameState.CurrentClientNormalCast.SpellXSpellVisualId;
		}
		else if (this.GetSession().GameState.CurrentPetGuid == casterUnit && this.GetSession().GameState.CurrentClientPetCast != null && this.GetSession().GameState.CurrentClientPetCast.SpellId == spellId)
		{
			castId = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
			spellVisual = this.GetSession().GameState.CurrentClientPetCast.SpellXSpellVisualId;
		}
		else if (casterUnit == this.GetSession().GameState.CurrentPlayerGuid && this.GetSession().GameState.CurrentChanneledSpellId == spellId && this.GetSession().GameState.CurrentChanneledCastId != null)
		{
			// Channeled spell failure (e.g. fishing cancel) — use stored cast info
			castId = this.GetSession().GameState.CurrentChanneledCastId;
			spellVisual = this.GetSession().GameState.CurrentChanneledSpellVisualId;
			this.GetSession().GameState.CurrentChanneledSpellId = 0;
			this.GetSession().GameState.CurrentChanneledCastId = null;
		}
		else
		{
			castId = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, spellId, spellId + casterUnit.GetCounter());
			spellVisual = GameData.GetSpellVisual(spellId);
		}
		SpellFailure spell = new SpellFailure();
		spell.CasterUnit = casterUnit;
		spell.CastID = castId;
		spell.SpellID = spellId;
		spell.SpellXSpellVisualID = spellVisual;
		spell.Reason = reason;
		this.SendPacketToClient(spell);
		SpellFailedOther spell2 = new SpellFailedOther();
		spell2.CasterUnit = casterUnit;
		spell2.CastID = castId;
		spell2.SpellID = spellId;
		spell2.SpellXSpellVisualID = spellVisual;
		spell2.Reason = reason;
		this.SendPacketToClient(spell2);
	}

	[PacketHandler(Opcode.SMSG_SPELL_START)]
	private void HandleSpellStart(WorldPacket packet)
	{
		if (!this.GetSession().GameState.CurrentMapId.HasValue)
		{
			return;
		}
		SpellStart spell = new SpellStart();
		spell.Cast = this.HandleSpellStartOrGo(packet, isSpellGo: false);
		byte failPending = 0;
		if (this.GetSession().GameState.CurrentPlayerGuid == spell.Cast.CasterUnit && this.GetSession().GameState.CurrentClientNormalCast != null && this.GetSession().GameState.CurrentClientNormalCast.SpellId == spell.Cast.SpellID)
		{
			spell.Cast.CastID = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
			spell.Cast.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientNormalCast.SpellXSpellVisualId;
			this.GetSession().GameState.CurrentClientNormalCast.HasStarted = true;
			SpellPrepare prepare = new SpellPrepare();
			prepare.ClientCastID = this.GetSession().GameState.CurrentClientNormalCast.ClientGUID;
			prepare.ServerCastID = spell.Cast.CastID;
			this.SendPacketToClient(prepare);
			failPending = 1;
		}
		else if (this.GetSession().GameState.CurrentPetGuid == spell.Cast.CasterUnit && this.GetSession().GameState.CurrentClientPetCast != null && this.GetSession().GameState.CurrentClientPetCast.SpellId == spell.Cast.SpellID)
		{
			spell.Cast.CastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
			spell.Cast.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientPetCast.SpellXSpellVisualId;
			this.GetSession().GameState.CurrentClientPetCast.HasStarted = true;
			SpellPrepare prepare2 = new SpellPrepare();
			prepare2.ClientCastID = this.GetSession().GameState.CurrentClientPetCast.ClientGUID;
			prepare2.ServerCastID = spell.Cast.CastID;
			this.SendPacketToClient(prepare2);
			failPending = 2;
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) && GameData.DispellSpells.Contains((uint)spell.Cast.SpellID))
		{
			this.GetSession().GameState.LastDispellSpellId = (uint)spell.Cast.SpellID;
		}
		this.SendPacketToClient(spell);
		switch (failPending)
		{
		case 1:
			foreach (ClientCastRequest pending2 in this.GetSession().GameState.PendingClientCasts)
			{
				this.GetSession().InstanceSocket.SendCastRequestFailed(pending2, isPet: false);
			}
			this.GetSession().GameState.PendingClientCasts.Clear();
			break;
		case 2:
			foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientPetCasts)
			{
				this.GetSession().InstanceSocket.SendCastRequestFailed(pending, isPet: true);
			}
			this.GetSession().GameState.PendingClientPetCasts.Clear();
			break;
		}
	}

	[PacketHandler(Opcode.SMSG_SPELL_GO)]
	private void HandleSpellGo(WorldPacket packet)
	{
		if (!this.GetSession().GameState.CurrentMapId.HasValue)
		{
			return;
		}
		SpellGo spell = new SpellGo();
		spell.Cast = this.HandleSpellStartOrGo(packet, isSpellGo: true);
		// 3.3.5a SpellGo doesn't set CAST_FLAG_HAS_TRAJECTORY but 3.4.3 always expects it
		spell.Cast.CastFlags |= (uint)CastFlag.HasTrajectory;
		if (this.GetSession().GameState.CurrentPlayerGuid == spell.Cast.CasterUnit && this.GetSession().GameState.CurrentClientNormalCast != null && this.GetSession().GameState.CurrentClientNormalCast.SpellId == spell.Cast.SpellID)
		{
			spell.Cast.CastID = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
			spell.Cast.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientNormalCast.SpellXSpellVisualId;
			// Save cast info for channeled spells — needed for cancel/failure after channel starts
			this.GetSession().GameState.CurrentChanneledCastId = this.GetSession().GameState.CurrentClientNormalCast.ServerGUID;
			this.GetSession().GameState.CurrentChanneledSpellVisualId = this.GetSession().GameState.CurrentClientNormalCast.SpellXSpellVisualId;
			this.GetSession().GameState.CurrentClientNormalCast = null;
		}
		else if (this.GetSession().GameState.CurrentPlayerGuid == spell.Cast.CasterUnit && this.GetSession().GameState.CurrentClientSpecialCast != null && this.GetSession().GameState.CurrentClientSpecialCast.SpellId == spell.Cast.SpellID)
		{
			spell.Cast.CastID = this.GetSession().GameState.CurrentClientSpecialCast.ServerGUID;
			spell.Cast.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientSpecialCast.SpellXSpellVisualId;
			this.GetSession().GameState.CurrentClientSpecialCast = null;
		}
		else if (this.GetSession().GameState.CurrentPetGuid == spell.Cast.CasterUnit && this.GetSession().GameState.CurrentClientPetCast != null && this.GetSession().GameState.CurrentClientPetCast.SpellId == spell.Cast.SpellID)
		{
			spell.Cast.CastID = this.GetSession().GameState.CurrentClientPetCast.ServerGUID;
			spell.Cast.SpellXSpellVisualID = this.GetSession().GameState.CurrentClientPetCast.SpellXSpellVisualId;
			this.GetSession().GameState.CurrentClientPetCast = null;
		}
		if (!spell.Cast.CasterUnit.IsEmpty() && GameData.AuraSpells.Contains((uint)spell.Cast.SpellID))
		{
			foreach (WowGuid128 target in spell.Cast.HitTargets)
			{
				this.GetSession().GameState.StoreLastAuraCasterOnTarget(target, (uint)spell.Cast.SpellID, spell.Cast.CasterUnit);
			}
		}
		this.SendPacketToClient(spell);
	}

	private SpellCastData HandleSpellStartOrGo(WorldPacket packet, bool isSpellGo)
	{
		SpellCastData dbdata = new SpellCastData();
		dbdata.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		dbdata.CasterUnit = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		if (dbdata.CasterUnit == this.GetSession().GameState.CurrentPlayerGuid && Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadUInt8();
		}
		dbdata.SpellID = packet.ReadInt32();
		dbdata.SpellXSpellVisualID = GameData.GetSpellVisual((uint)dbdata.SpellID);
		dbdata.CastID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, (uint)dbdata.SpellID, (ulong)dbdata.SpellID + dbdata.CasterUnit.GetCounter());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056) && !isSpellGo)
		{
			packet.ReadUInt8();
		}
		uint flags = (dbdata.CastFlags = ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056)) ? packet.ReadUInt16() : packet.ReadUInt32()));
		if (!isSpellGo || LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			dbdata.CastTime = packet.ReadUInt32();
		}
		if (isSpellGo)
		{
			byte hitCount = packet.ReadUInt8();
			for (int i = 0; i < hitCount; i++)
			{
				WowGuid128 hitTarget = packet.ReadGuid().To128(this.GetSession().GameState);
				dbdata.HitTargets.Add(hitTarget);
			}
			byte missCount = packet.ReadUInt8();
			for (int j = 0; j < missCount; j++)
			{
				WowGuid128 missTarget = packet.ReadGuid().To128(this.GetSession().GameState);
				SpellMissInfo missType = (SpellMissInfo)packet.ReadUInt8();
				SpellMissInfo reflectType = SpellMissInfo.None;
				if (missType == SpellMissInfo.Reflect)
				{
					reflectType = (SpellMissInfo)packet.ReadUInt8();
				}
				dbdata.MissTargets.Add(missTarget);
				dbdata.MissStatus.Add(new SpellMissStatus(missType, reflectType));
			}
		}
		SpellCastTargetFlags targetFlags = (SpellCastTargetFlags)(LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? packet.ReadUInt32() : packet.ReadUInt16());
		dbdata.Target.Flags = targetFlags;
		WowGuid128 unitTarget = WowGuid128.Empty;
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.CorpseMask | SpellCastTargetFlags.Unit | SpellCastTargetFlags.GameObject | SpellCastTargetFlags.UnitMinipet))
		{
			unitTarget = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		dbdata.Target.Unit = unitTarget;
		WowGuid128 itemTarget = WowGuid128.Empty;
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.Item | SpellCastTargetFlags.TradeItem))
		{
			itemTarget = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		dbdata.Target.Item = itemTarget;
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.SourceLocation))
		{
			dbdata.Target.SrcLocation = new TargetLocation();
			dbdata.Target.SrcLocation.Transport = WowGuid128.Empty;
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
			{
				dbdata.Target.SrcLocation.Transport = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			}
			dbdata.Target.SrcLocation.Location = packet.ReadVector3();
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.DestLocation))
		{
			dbdata.Target.DstLocation = new TargetLocation();
			dbdata.Target.DstLocation.Transport = WowGuid128.Empty;
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
			{
				dbdata.Target.DstLocation.Transport = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			}
			dbdata.Target.DstLocation.Location = packet.ReadVector3();
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.String))
		{
			dbdata.Target.Name = packet.ReadCString();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			if (flags.HasAnyFlag(CastFlag.PredictedPower))
			{
				packet.ReadInt32();
			}
			if (flags.HasAnyFlag(CastFlag.RuneInfo))
			{
				byte spellRuneState = packet.ReadUInt8();
				byte playerRuneState = packet.ReadUInt8();
				for (int k = 0; k < 6; k++)
				{
					int mask = 1 << k;
					if ((mask & spellRuneState) != 0 && (mask & playerRuneState) == 0)
					{
						packet.ReadUInt8();
					}
				}
			}
			if (isSpellGo && flags.HasAnyFlag(CastFlag.AdjustMissile))
			{
				dbdata.MissileTrajectory.Pitch = packet.ReadFloat();
				dbdata.MissileTrajectory.TravelTime = packet.ReadUInt32();
			}
		}
		if (flags.HasAnyFlag(CastFlag.Projectile))
		{
			dbdata.AmmoDisplayId = packet.ReadInt32();
			dbdata.AmmoInventoryType = packet.ReadInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			if (isSpellGo)
			{
				if (flags.HasAnyFlag(CastFlag.VisualChain))
				{
					packet.ReadInt32();
					packet.ReadInt32();
				}
				if (targetFlags.HasAnyFlag(SpellCastTargetFlags.DestLocation))
				{
					packet.ReadInt8();
				}
				if (targetFlags.HasAnyFlag(SpellCastTargetFlags.ExtraTargets))
				{
					int targetCount = packet.ReadInt32();
					if (targetCount > 0)
					{
						TargetLocation location = new TargetLocation();
						for (int l = 0; l < targetCount; l++)
						{
							location.Location = packet.ReadVector3();
							location.Transport = packet.ReadGuid().To128(this.GetSession().GameState);
						}
						dbdata.TargetPoints.Add(location);
					}
				}
			}
			else
			{
				if (flags.HasAnyFlag(CastFlag.Immunity))
				{
					dbdata.Immunities.School = packet.ReadUInt32();
					dbdata.Immunities.Value = packet.ReadUInt32();
				}
				if (flags.HasAnyFlag(CastFlag.HealPrediction))
				{
					packet.ReadInt32();
					if (packet.ReadUInt8() == 2)
					{
						packet.ReadPackedGuid();
					}
				}
			}
		}
		return dbdata;
	}

	[PacketHandler(Opcode.SMSG_CANCEL_AUTO_REPEAT)]
	private void HandleCancelAutoRepeat(WorldPacket packet)
	{
		if (Settings.ClientSpellDelay > 0)
		{
			Thread.Sleep(Settings.ClientSpellDelay);
		}
		if (this.GetSession().GameState.CurrentClientSpecialCast != null && GameData.AutoRepeatSpells.Contains(this.GetSession().GameState.CurrentClientSpecialCast.SpellId))
		{
			this.GetSession().GameState.CurrentClientSpecialCast = null;
		}
		CancelAutoRepeat cancel = new CancelAutoRepeat();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			cancel.Guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			cancel.Guid = this.GetSession().GameState.CurrentPlayerGuid;
		}
		this.SendPacketToClient(cancel);
	}

	[PacketHandler(Opcode.SMSG_SPELL_COOLDOWN)]
	private void HandleSpellCooldown(WorldPacket packet)
	{
		SpellCooldownPkt cooldown = new SpellCooldownPkt();
		try
		{
			cooldown.Caster = packet.ReadGuid().To128(this.GetSession().GameState);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				cooldown.Flags = packet.ReadUInt8();
			}
			while (packet.CanRead())
			{
				SpellCooldownStruct cd = new SpellCooldownStruct();
				cd.SpellID = packet.ReadUInt32();
				cd.ForcedCooldown = packet.ReadUInt32();
				cooldown.SpellCooldowns.Add(cd);
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			packet.ResetReadPos();
			SpellCooldownStruct cd2 = new SpellCooldownStruct();
			cd2.SpellID = packet.ReadUInt32();
			cooldown.Caster = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			cd2.ForcedCooldown = packet.ReadUInt32();
			cooldown.SpellCooldowns.Add(cd2);
		}
		this.SendPacketToClient(cooldown);
	}

	[PacketHandler(Opcode.SMSG_COOLDOWN_EVENT)]
	private void HandleCooldownEvent(WorldPacket packet)
	{
		CooldownEvent cooldown = new CooldownEvent();
		cooldown.SpellID = packet.ReadUInt32();
		WowGuid guid = packet.ReadGuid();
		cooldown.IsPet = guid.GetHighType() == HighGuidType.Pet;
		this.SendPacketToClient(cooldown);
	}

	[PacketHandler(Opcode.SMSG_CLEAR_COOLDOWN)]
	private void HandleClearCooldown(WorldPacket packet)
	{
		ClearCooldown cooldown = new ClearCooldown();
		cooldown.SpellID = packet.ReadUInt32();
		WowGuid guid = packet.ReadGuid();
		cooldown.IsPet = guid.GetHighType() == HighGuidType.Pet;
		this.SendPacketToClient(cooldown);
	}

	[PacketHandler(Opcode.SMSG_COOLDOWN_CHEAT)]
	private void HandleCooldownCheat(WorldPacket packet)
	{
		CooldownCheat cooldown = new CooldownCheat();
		cooldown.Guid = packet.ReadGuid().To128(this.GetSession().GameState);
		this.SendPacketToClient(cooldown);
	}

	[PacketHandler(Opcode.SMSG_SPELL_NON_MELEE_DAMAGE_LOG)]
	private void HandleSpellNonMeleeDamageLog(WorldPacket packet)
	{
		SpellNonMeleeDamageLog spell = new SpellNonMeleeDamageLog();
		spell.TargetGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.SpellID = packet.ReadUInt32();
		spell.SpellXSpellVisualID = GameData.GetSpellVisual(spell.SpellID);
		spell.CastID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, spell.SpellID, spell.SpellID + spell.CasterGUID.GetCounter());
		spell.Damage = packet.ReadInt32();
		spell.OriginalDamage = spell.Damage;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_3_9183))
		{
			spell.Overkill = packet.ReadInt32();
		}
		else
		{
			spell.Overkill = -1;
		}
		byte school = packet.ReadUInt8();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			school = (byte)(1 << (int)school);
		}
		spell.SchoolMask = school;
		spell.Absorbed = packet.ReadInt32();
		spell.Resisted = packet.ReadInt32();
		spell.Periodic = packet.ReadBool();
		packet.ReadUInt8();
		spell.ShieldBlock = packet.ReadInt32();
		spell.Flags = (SpellHitType)packet.ReadUInt32();
		if (packet.ReadBool() && !spell.Flags.HasAnyFlag(SpellHitType.Split))
		{
			if (spell.Flags.HasAnyFlag(SpellHitType.CritDebug))
			{
				packet.ReadFloat();
				packet.ReadFloat();
			}
			if (spell.Flags.HasAnyFlag(SpellHitType.HitDebug))
			{
				packet.ReadFloat();
				packet.ReadFloat();
			}
			if (spell.Flags.HasAnyFlag(SpellHitType.AttackTableDebug))
			{
				packet.ReadFloat();
				packet.ReadFloat();
				packet.ReadFloat();
				packet.ReadFloat();
				packet.ReadFloat();
				packet.ReadFloat();
			}
		}
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_SPELL_HEAL_LOG)]
	private void HandleSpellHealLog(WorldPacket packet)
	{
		SpellHealLog spell = new SpellHealLog();
		spell.TargetGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.SpellID = packet.ReadUInt32();
		spell.HealAmount = packet.ReadInt32();
		spell.OriginalHealAmount = spell.HealAmount;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_3_9183))
		{
			spell.OverHeal = packet.ReadUInt32();
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			spell.Absorbed = packet.ReadUInt32();
		}
		spell.Crit = packet.ReadBool();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.ReadBool())
		{
			spell.CritRollMade = packet.ReadFloat();
			spell.CritRollNeeded = packet.ReadFloat();
		}
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_SPELL_PERIODIC_AURA_LOG)]
	private void HandleSpellPeriodicAuraLog(WorldPacket packet)
	{
		SpellPeriodicAuraLog spell = new SpellPeriodicAuraLog();
		spell.TargetGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.SpellID = packet.ReadUInt32();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			AuraType aura = (AuraType)packet.ReadUInt32();
			switch (aura)
			{
			case AuraType.PeriodicDamage:
			case AuraType.PeriodicDamagePercent:
			{
				SpellPeriodicAuraLog.SpellLogEffect effect4 = new SpellPeriodicAuraLog.SpellLogEffect();
				effect4.Effect = (uint)aura;
				effect4.Amount = packet.ReadInt32();
				effect4.OriginalDamage = effect4.Amount;
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					effect4.OverHealOrKill = packet.ReadUInt32();
				}
				uint school = packet.ReadUInt32();
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					school = (uint)(1 << (int)(byte)school);
				}
				effect4.SchoolMaskOrPower = school;
				effect4.AbsorbedOrAmplitude = packet.ReadUInt32();
				effect4.Resisted = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901))
				{
					effect4.Crit = packet.ReadBool();
				}
				spell.Effects.Add(effect4);
				break;
			}
			case AuraType.PeriodicHeal:
			case AuraType.ObsModHealth:
			{
				SpellPeriodicAuraLog.SpellLogEffect effect3 = new SpellPeriodicAuraLog.SpellLogEffect();
				effect3.Effect = (uint)aura;
				effect3.Amount = packet.ReadInt32();
				effect3.OriginalDamage = effect3.Amount;
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					effect3.OverHealOrKill = packet.ReadUInt32();
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_3_0_10958))
				{
					effect3.AbsorbedOrAmplitude = packet.ReadUInt32();
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901))
				{
					effect3.Crit = packet.ReadBool();
				}
				spell.Effects.Add(effect3);
				break;
			}
			case AuraType.ObsModPower:
			case AuraType.PeriodicEnergize:
			{
				SpellPeriodicAuraLog.SpellLogEffect effect2 = new SpellPeriodicAuraLog.SpellLogEffect();
				effect2.Effect = (uint)aura;
				effect2.SchoolMaskOrPower = packet.ReadUInt32();
				effect2.Amount = packet.ReadInt32();
				spell.Effects.Add(effect2);
				break;
			}
			case AuraType.PeriodicManaLeech:
			{
				SpellPeriodicAuraLog.SpellLogEffect effect = new SpellPeriodicAuraLog.SpellLogEffect();
				effect.Effect = (uint)aura;
				effect.SchoolMaskOrPower = packet.ReadUInt32();
				effect.Amount = packet.ReadInt32();
				packet.ReadFloat();
				spell.Effects.Add(effect);
				break;
			}
			}
		}
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_SPELL_ENERGIZE_LOG)]
	private void HandleSpellEnergizeLog(WorldPacket packet)
	{
		SpellEnergizeLog spell = new SpellEnergizeLog();
		spell.TargetGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.SpellID = packet.ReadUInt32();
		spell.Type = (PowerType)packet.ReadUInt32();
		spell.Amount = packet.ReadInt32();
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_SPELL_DELAYED)]
	private void HandleSpellDelayed(WorldPacket packet)
	{
		SpellDelayed delay = new SpellDelayed();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			delay.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			delay.CasterGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		delay.Delay = packet.ReadInt32();
		this.SendPacketToClient(delay);
	}

	[PacketHandler(Opcode.MSG_CHANNEL_START)]
	private void HandleSpellChannelStart(WorldPacket packet)
	{
		SpellChannelStart channel = new SpellChannelStart();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			channel.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			channel.CasterGUID = this.GetSession().GameState.CurrentPlayerGuid;
		}
		channel.SpellID = packet.ReadUInt32();
		channel.SpellXSpellVisualID = GameData.GetSpellVisual(channel.SpellID);
		channel.Duration = packet.ReadUInt32();
		// Store channeled spell ID so cancel can use the real ID
		if (channel.CasterGUID == this.GetSession().GameState.CurrentPlayerGuid)
		{
			this.GetSession().GameState.CurrentChanneledSpellId = (uint)channel.SpellID;
		}
		this.SendPacketToClient(channel);
	}

	[PacketHandler(Opcode.MSG_CHANNEL_UPDATE)]
	private void HandleSpellChannelUpdate(WorldPacket packet)
	{
		SpellChannelUpdate channel = new SpellChannelUpdate();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			channel.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			channel.CasterGUID = this.GetSession().GameState.CurrentPlayerGuid;
		}
		channel.TimeRemaining = packet.ReadInt32();
		if (channel.TimeRemaining == 0 && channel.CasterGUID == this.GetSession().GameState.CurrentPlayerGuid)
		{
			this.GetSession().GameState.CurrentChanneledSpellId = 0;
			Log.Print(LogType.Debug, $"[ChannelUpdate] Channel ended (TimeRemaining=0)", "HandleSpellChannelUpdate", "");
		}
		this.SendPacketToClient(channel);
	}

	[PacketHandler(Opcode.SMSG_SPELL_DAMAGE_SHIELD)]
	private void HandleSpellDamageShield(WorldPacket packet)
	{
		SpellDamageShield spell = new SpellDamageShield();
		spell.VictimGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			spell.SpellID = packet.ReadUInt32();
		}
		else
		{
			spell.SpellID = 7294u;
		}
		spell.Damage = packet.ReadInt32();
		spell.OriginalDamage = spell.Damage;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			spell.OverKill = packet.ReadUInt32();
		}
		uint school = packet.ReadUInt32();
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			school = (uint)(1 << (int)(byte)school);
		}
		spell.SchoolMask = school;
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_ENVIRONMENTAL_DAMAGE_LOG)]
	private void HandleEnvironmentalDamageLog(WorldPacket packet)
	{
		EnvironmentalDamageLog damage = new EnvironmentalDamageLog();
		damage.Victim = packet.ReadGuid().To128(this.GetSession().GameState);
		damage.Type = (EnvironmentalDamage)packet.ReadUInt8();
		damage.Amount = packet.ReadInt32();
		damage.Absorbed = packet.ReadInt32();
		damage.Resisted = packet.ReadInt32();
		this.SendPacketToClient(damage);
	}

	[PacketHandler(Opcode.SMSG_SPELL_INSTAKILL_LOG)]
	private void HandleSpellInstakillLog(WorldPacket packet)
	{
		SpellInstakillLog spell = new SpellInstakillLog();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			spell.CasterGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			spell.TargetGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		}
		else
		{
			spell.CasterGUID = (spell.TargetGUID = packet.ReadGuid().To128(this.GetSession().GameState));
		}
		spell.SpellID = packet.ReadUInt32();
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_SPELL_DISPELL_LOG)]
	private void HandleSpellDispellLog(WorldPacket packet)
	{
		SpellDispellLog spell = new SpellDispellLog();
		spell.TargetGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		spell.CasterGUID = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			spell.DispelledBySpellID = packet.ReadUInt32();
		}
		else
		{
			spell.DispelledBySpellID = this.GetSession().GameState.LastDispellSpellId;
		}
		bool hasDebug = LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) && packet.ReadBool();
		int count = packet.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			SpellDispellData dispel = new SpellDispellData
			{
				SpellID = packet.ReadUInt32()
			};
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				dispel.Harmful = packet.ReadBool();
			}
			spell.DispellData.Add(dispel);
		}
		if (hasDebug)
		{
			packet.ReadInt32();
			packet.ReadInt32();
		}
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_PLAY_SPELL_VISUAL)]
	private void HandlePlaySpellVisualKit(WorldPacket packet)
	{
		PlaySpellVisualKit spell = new PlaySpellVisualKit();
		spell.Unit = packet.ReadGuid().To128(this.GetSession().GameState);
		spell.KitRecID = packet.ReadUInt32();
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_PLAY_SPELL_IMPACT)]
	private void HandlePlaySpellImpact(WorldPacket packet)
	{
		PlaySpellVisualKit spell = new PlaySpellVisualKit();
		spell.Unit = packet.ReadGuid().To128(this.GetSession().GameState);
		spell.KitRecID = packet.ReadUInt32();
		this.SendPacketToClient(spell);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_AURA_DURATION)]
	private void HandleUpdateAuraDuration(WorldPacket packet)
	{
		byte slot = packet.ReadUInt8();
		int duration = packet.ReadInt32();
		WowGuid128 guid = this.GetSession().GameState.CurrentPlayerGuid;
		if (guid == null)
		{
			return;
		}
		this.GetSession().GameState.StoreAuraDurationLeft(guid, slot, duration, (int)packet.GetReceivedTime());
		if (duration <= 0)
		{
			return;
		}
		Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(guid);
		if (updateFields != null)
		{
			AuraInfo aura = new AuraInfo
			{
				Slot = slot,
				AuraData = this.ReadAuraSlot(slot, guid, updateFields)
			};
			if (aura.AuraData != null)
			{
				aura.AuraData.Flags |= AuraFlagsModern.Duration;
				aura.AuraData.Duration = duration;
				aura.AuraData.Remaining = duration;
				AuraUpdate update = new AuraUpdate(guid, all: false);
				update.Auras.Add(aura);
				this.SendPacketToClient(update);
			}
		}
	}

	[PacketHandler(Opcode.SMSG_SET_EXTRA_AURA_INFO)]
	[PacketHandler(Opcode.SMSG_SET_EXTRA_AURA_INFO_NEED_UPDATE)]
	private void HandleSetExtraAuraInfo(WorldPacket packet)
	{
		WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		if (!packet.CanRead())
		{
			return;
		}
		byte slot = packet.ReadUInt8();
		uint spellId = packet.ReadUInt32();
		int durationFull = packet.ReadInt32();
		int durationLeft = packet.ReadInt32();
		this.GetSession().GameState.StoreAuraDurationFull(guid, slot, durationFull);
		this.GetSession().GameState.StoreAuraDurationLeft(guid, slot, durationLeft, (int)packet.GetReceivedTime());
		if (packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_SET_EXTRA_AURA_INFO_NEED_UPDATE)
		{
			this.GetSession().GameState.StoreAuraCaster(guid, slot, this.GetSession().GameState.CurrentPlayerGuid);
		}
		if (durationFull <= 0 && durationLeft <= 0)
		{
			return;
		}
		Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(guid);
		if (updateFields != null)
		{
			AuraInfo aura = new AuraInfo
			{
				Slot = slot,
				AuraData = this.ReadAuraSlot(slot, guid, updateFields)
			};
			if (aura.AuraData != null && aura.AuraData.SpellID == spellId)
			{
				aura.AuraData.CastUnit = this.GetSession().GameState.GetAuraCaster(guid, slot, spellId);
				aura.AuraData.Flags |= AuraFlagsModern.Duration;
				aura.AuraData.Duration = durationFull;
				aura.AuraData.Remaining = durationLeft;
				AuraUpdate update = new AuraUpdate(guid, all: false);
				update.Auras.Add(aura);
				this.SendPacketToClient(update);
			}
		}
	}

	[PacketHandler(Opcode.SMSG_AURA_UPDATE)]
	private void HandleAuraUpdate(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			AuraUpdate update = new AuraUpdate(guid, all: false);
			this.ReadSingleAura(packet, guid, update);
			if (update.Auras.Count > 0)
			{
					this.SendPacketToClient(update);
			}
		}
	}

	[PacketHandler(Opcode.SMSG_AURA_UPDATE_ALL)]
	private void HandleAuraUpdateAll(WorldPacket packet)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			AuraUpdate update = new AuraUpdate(guid, all: true);
			while (packet.CanRead())
			{
				this.ReadSingleAura(packet, guid, update);
			}
			if (update.Auras.Count > 0)
			{
				this.SendPacketToClient(update);
			}
		}
	}

	private void ReadSingleAura(WorldPacket packet, WowGuid128 guid, AuraUpdate update)
	{
		byte slot = packet.ReadUInt8();
		uint spellId = packet.ReadUInt32();
		AuraInfo aura = new AuraInfo
		{
			Slot = slot
		};
		if (spellId == 0)
		{
			aura.AuraData = null;
			update.Auras.Add(aura);
			if (guid == this.GetSession().GameState.CurrentPlayerGuid)
				Log.Print(LogType.Debug, $"[AuraUpdate] REMOVE slot={slot} for player", "ReadSingleAura", "");
			return;
		}
		if (guid == this.GetSession().GameState.CurrentPlayerGuid)
			Log.Print(LogType.Debug, $"[AuraUpdate] SET slot={slot} spellId={spellId} for player", "ReadSingleAura", "");
		AuraDataInfo data = new AuraDataInfo();
		data.SpellID = spellId;
		data.CastID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Aura, this.GetSession().GameState.CurrentMapId.Value, spellId, guid.GetCounter());
		data.SpellXSpellVisualID = GameData.GetSpellVisual(spellId);
		byte flags = packet.ReadUInt8();
		data.CastLevel = packet.ReadUInt8();
		data.Applications = packet.ReadUInt8();
		data.Flags = AuraFlagsModern.None;
		data.ActiveFlags = 0u;
		if ((flags & 0x10) != 0)
		{
			data.Flags |= AuraFlagsModern.Positive;
		}
		if ((flags & 0x20) != 0)
		{
			data.Flags |= AuraFlagsModern.Duration;
		}
		if ((flags & 1) != 0)
		{
			data.ActiveFlags |= 1u;
		}
		if ((flags & 2) != 0)
		{
			data.ActiveFlags |= 2u;
		}
		if ((flags & 4) != 0)
		{
			data.ActiveFlags |= 4u;
		}
		if ((flags & 8) == 0)
		{
			data.CastUnit = packet.ReadPackedGuid().To128(this.GetSession().GameState);
		}
		else
		{
			data.CastUnit = guid;
		}
		if ((flags & 0x20) != 0)
		{
			data.Duration = packet.ReadInt32();
			data.Remaining = packet.ReadInt32();
		}
		if ((flags & 0x40) != 0)
		{
			if ((flags & 1) != 0)
			{
				data.Points.Add(packet.ReadFloat());
			}
			if ((flags & 2) != 0)
			{
				data.Points.Add(packet.ReadFloat());
			}
			if ((flags & 4) != 0)
			{
				data.Points.Add(packet.ReadFloat());
			}
		}
		aura.AuraData = data;
		update.Auras.Add(aura);
	}

	[PacketHandler(Opcode.SMSG_RESURRECT_REQUEST)]
	private void HandleResurrectRequest(WorldPacket packet)
	{
		ResurrectRequest revive = new ResurrectRequest();
		revive.CasterGUID = packet.ReadGuid().To128(this.GetSession().GameState);
		revive.CasterVirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		packet.ReadUInt32();
		revive.Name = packet.ReadCString();
		revive.Sickness = packet.ReadBool();
		revive.UseTimer = packet.ReadBool();
		this.SendPacketToClient(revive);
	}

	[PacketHandler(Opcode.SMSG_TOTEM_CREATED)]
	private void HandleTotemCreated(WorldPacket packet)
	{
		TotemCreated totem = new TotemCreated();
		totem.Slot = packet.ReadUInt8();
		totem.Totem = packet.ReadGuid().To128(this.GetSession().GameState);
		totem.Duration = packet.ReadUInt32();
		totem.SpellId = packet.ReadUInt32();
		this.SendPacketToClient(totem);
	}

	[PacketHandler(Opcode.SMSG_SET_FLAT_SPELL_MODIFIER)]
	[PacketHandler(Opcode.SMSG_SET_PCT_SPELL_MODIFIER)]
	private void HandleSetSpellModifier(WorldPacket packet)
	{
		byte classIndex = packet.ReadUInt8();
		byte modIndex = packet.ReadUInt8();
		int modValue = packet.ReadInt32();
		if (this.GetSession().GameState.CurrentPlayerCreateTime != 0)
		{
			SetSpellModifier spell = new SetSpellModifier(packet.GetUniversalOpcode(isModern: false));
			SpellModifierInfo mod = new SpellModifierInfo();
			SpellModifierData data = new SpellModifierData
			{
				ClassIndex = classIndex
			};
			mod.ModIndex = modIndex;
			data.ModifierValue = modValue;
			mod.ModifierData.Add(data);
			spell.Modifiers.Add(mod);
			this.SendPacketToClient(spell);
		}
		if (packet.GetUniversalOpcode(isModern: false) == Opcode.SMSG_SET_FLAT_SPELL_MODIFIER)
		{
			this.GetSession().GameState.SetFlatSpellMod(modIndex, classIndex, modValue);
		}
		else
		{
			this.GetSession().GameState.SetPctSpellMod(modIndex, classIndex, modValue);
		}
	}

	[PacketHandler(Opcode.SMSG_GM_TICKET_CREATE)]
	private void HandleGmTicketCreate(WorldPacket packet)
	{
		LegacyGmTicketResponse response = (LegacyGmTicketResponse)packet.ReadUInt32();
		bool flag = ((response == LegacyGmTicketResponse.CreateSuccess || response == LegacyGmTicketResponse.UpdateSuccess) ? true : false);
		bool isError = !flag;
		this.Session.SendHermesTextMessage($"GM Ticket Status: {response}", isError);
	}

	[PacketHandler(Opcode.SMSG_FEATURE_SYSTEM_STATUS)]
	private void HandleFeatureSystemStatus(WorldPacket packet)
	{
		this.GetSession().RealmSocket.SendFeatureSystemStatus();
	}

	[PacketHandler(Opcode.SMSG_MOTD)]
	private void HandleMotd(WorldPacket packet)
	{
		MOTD motd = new MOTD();
		uint count = packet.ReadUInt32();
		for (uint i = 0u; i < count; i++)
		{
			motd.Text.Add(packet.ReadCString());
		}
		this.SendPacketToClient(motd);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			this.GetSession().RealmSocket.SendSetTimeZoneInformation();
			this.GetSession().RealmSocket.SendSeasonInfo();
		}
	}

	[PacketHandler(Opcode.SMSG_TAXI_NODE_STATUS)]
	private void HandleTaxiNodeStatus(WorldPacket packet)
	{
		TaxiNodeStatusPkt taxi = new TaxiNodeStatusPkt();
		taxi.FlightMaster = packet.ReadGuid().To128(this.GetSession().GameState);
		bool learned = packet.ReadBool();
		taxi.Status = (learned ? TaxiNodeStatus.Learned : TaxiNodeStatus.Unlearned);
		this.SendPacketToClient(taxi);
	}

	[PacketHandler(Opcode.SMSG_SHOW_TAXI_NODES)]
	private void HandleShowTaxiNodes(WorldPacket packet)
	{
		uint playerFlags = this.GetSession().GameState.GetLegacyFieldValueUInt32(this.GetSession().GameState.CurrentPlayerGuid, PlayerField.PLAYER_FLAGS);
		if (playerFlags.HasAnyFlag(PlayerFlags.GM))
		{
			ChatPkt chat = new ChatPkt(this.GetSession(), ChatMessageTypeModern.System, "Disable GM mode before talking to taxi master or your game will freeze.");
			this.SendPacketToClient(chat);
			return;
		}
		ShowTaxiNodes taxi = new ShowTaxiNodes();
		if (packet.ReadUInt32() != 0)
		{
			taxi.WindowInfo = new ShowTaxiNodesWindowInfo();
			taxi.WindowInfo.UnitGUID = packet.ReadGuid().To128(this.GetSession().GameState);
			taxi.WindowInfo.CurrentNode = (this.GetSession().GameState.CurrentTaxiNode = packet.ReadUInt32());
		}
		while (packet.CanRead())
		{
			byte nodesMask = packet.ReadUInt8();
			taxi.CanLandNodes.Add(nodesMask);
			taxi.CanUseNodes.Add(nodesMask);
		}
		this.GetSession().GameState.UsableTaxiNodes = taxi.CanUseNodes;
		this.SendPacketToClient(taxi);
	}

	[PacketHandler(Opcode.SMSG_NEW_TAXI_PATH)]
	private void HandleNewTaxiPath(WorldPacket packet)
	{
		NewTaxiPath taxi = new NewTaxiPath();
		this.SendPacketToClient(taxi);
	}

	[PacketHandler(Opcode.SMSG_ACTIVATE_TAXI_REPLY)]
	private void HandleActivateTaxiReply(WorldPacket packet)
	{
		ActivateTaxiReply reply = (ActivateTaxiReply)packet.ReadUInt32();
		if (reply != ActivateTaxiReply.Ok)
		{
			ActivateTaxiReplyPkt taxi = new ActivateTaxiReplyPkt();
			taxi.Reply = reply;
			this.SendPacketToClient(taxi);
			this.GetSession().GameState.IsWaitingForTaxiStart = false;
		}
	}

	[PacketHandler(Opcode.SMSG_TRADE_STATUS)]
	private void HandleTradeStatus(WorldPacket packet)
	{
		TradeStatusPkt trade = new TradeStatusPkt();
		trade.Status = (TradeStatus)packet.ReadUInt32();
		TradeSession tradeSession = this.GetSession().GameState.CurrentTrade;
		if (tradeSession == null)
		{
			TradeStatus status = trade.Status;
			TradeStatus tradeStatus = status;
			if ((uint)(tradeStatus - 1) > 1u)
			{
				Log.Print(LogType.Error, $"Got SMSG_TRADE_STATUS without trade session (status: {trade.Status})", "HandleTradeStatus", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\TradeHandler.cs");
				this.SendPacketToClient(new TradeStatusPkt
				{
					Status = TradeStatus.Cancelled
				});
				return;
			}
			tradeSession = new TradeSession();
			this.GetSession().GameState.CurrentTrade = tradeSession;
		}
		switch (trade.Status)
		{
		case TradeStatus.Proposed:
			trade.Partner = (tradeSession.Partner = packet.ReadGuid().To128(this.GetSession().GameState));
			trade.PartnerAccount = (tradeSession.PartnerAccount = this.GetSession().GetGameAccountGuidForPlayer(trade.Partner));
			break;
		case TradeStatus.Initiated:
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				trade.Id = packet.ReadUInt32();
			}
			else
			{
				trade.Id = TradeSession.GlobalTradeIdCounter++;
			}
			tradeSession.TradeId = trade.Id;
			break;
		case TradeStatus.Failed:
			trade.BagResult = LegacyVersion.ConvertInventoryResult(packet.ReadUInt32());
			trade.FailureForYou = packet.ReadBool();
			trade.ItemID = packet.ReadUInt32();
			break;
		case TradeStatus.WrongRealm:
		case TradeStatus.NotOnTaplist:
			trade.TradeSlot = packet.ReadUInt8();
			break;
		}
		bool flag;
		switch (trade.Status)
		{
		case TradeStatus.Proposed:
		case TradeStatus.Initiated:
		case TradeStatus.Accepted:
		case TradeStatus.Unaccepted:
		case TradeStatus.StateChanged:
		case TradeStatus.WrongRealm:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			this.GetSession().GameState.CurrentTrade = null;
		}
		this.SendPacketToClient(trade);
	}

	[PacketHandler(Opcode.SMSG_TRADE_STATUS_EXTENDED)]
	private void HandleTradeStatusExtended(WorldPacket packet)
	{
		TradeSession tradeSession = this.GetSession().GameState.CurrentTrade;
		if (tradeSession == null)
		{
			Log.Print(LogType.Error, "Got SMSG_TRADE_STATUS_EXTENDED without trade session", "HandleTradeStatusExtended", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\TradeHandler.cs");
			return;
		}
		tradeSession.ServerStateIndex++;
		TradeUpdated trade = new TradeUpdated();
		trade.WhichPlayer = packet.ReadUInt8();
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			uint actualTradeId = packet.ReadUInt32();
			if (actualTradeId != trade.Id)
			{
				Log.Print(LogType.Error, $"Got SMSG_TRADE_STATUS_EXTENDED with wrong tradeId (expected {trade.Id} but got {actualTradeId})", "HandleTradeStatusExtended", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\TradeHandler.cs");
				return;
			}
		}
		trade.Id = tradeSession.TradeId;
		packet.ReadUInt32();
		packet.ReadUInt32();
		trade.ClientStateIndex = tradeSession.ClientStateIndex;
		trade.CurrentStateIndex = tradeSession.ServerStateIndex;
		trade.Gold = packet.ReadUInt32();
		trade.ProposedEnchantment = packet.ReadInt32();
		while (packet.CanRead())
		{
			TradeUpdated.TradeItem item = new TradeUpdated.TradeItem();
			item.Unwrapped = new TradeUpdated.UnwrappedTradeItem();
			item.Slot = packet.ReadUInt8();
			item.Item.ItemID = packet.ReadUInt32();
			packet.ReadUInt32();
			item.StackCount = packet.ReadInt32();
			packet.ReadUInt32();
			item.GiftCreator = packet.ReadGuid().To128(this.GetSession().GameState);
			item.Unwrapped.EnchantID = packet.ReadInt32();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				for (int i = 0; i < 3; i++)
				{
					packet.ReadUInt32();
				}
			}
			item.Unwrapped.Creator = packet.ReadGuid().To128(this.GetSession().GameState);
			item.Unwrapped.Charges = packet.ReadInt32();
			item.Item.RandomPropertiesSeed = packet.ReadUInt32();
			item.Item.RandomPropertiesID = packet.ReadUInt32();
			item.Unwrapped.Lock = packet.ReadUInt32() != 0;
			item.Unwrapped.MaxDurability = packet.ReadUInt32();
			item.Unwrapped.Durability = packet.ReadUInt32();
			trade.Items.Add(item);
		}
		this.SendPacketToClient(trade);
	}

	[PacketHandler(Opcode.SMSG_DESTROY_OBJECT)]
	private void HandleDestroyObject(WorldPacket packet)
	{
		WowGuid128 guid = packet.ReadGuid().To128(this.GetSession().GameState);
		Log.Print(LogType.Debug, $"[DestroyObject] Destroying {guid} type={guid.GetHighType()}", "HandleDestroyObject", "");
		this.GetSession().GameState.ObjectCacheMutex.WaitOne();
		this.GetSession().GameState.ObjectCacheLegacy.Remove(guid);
		this.GetSession().GameState.ObjectCacheModern.Remove(guid);
		this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();
		this.GetSession().GameState.LastAuraCasterOnTarget.Remove(guid);
		// Send both DestroyObject (for 3.4.3 GO cleanup) and UpdateObject (for compatibility)
		if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_DESTROY_OBJECT) != 0)
		{
			this.SendPacketToClient(new DestroyObject(guid));
		}
		UpdateObject updateObject = new UpdateObject(this.GetSession().GameState);
		updateObject.DestroyedGuids.Add(guid);
		this.SendPacketToClient(updateObject);
	}

	[PacketHandler(Opcode.SMSG_COMPRESSED_UPDATE_OBJECT)]
	private void HandleCompressedUpdateObject(WorldPacket packet)
	{
		using WorldPacket packet2 = packet.Inflate(packet.ReadInt32());
		this.HandleUpdateObject(packet2);
	}

	[PacketHandler(Opcode.SMSG_UPDATE_OBJECT)]
	private void HandleUpdateObject(WorldPacket packet)
	{
		uint count = packet.ReadUInt32();
		this.PrintString($"Updates Count = {count}");
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.ReadBool();
		}
		HashSet<uint> missingItemTemplates = new HashSet<uint>();
		List<AuraUpdate> auraUpdates = new List<AuraUpdate>();
		UpdateObject updateObject = new UpdateObject(this.GetSession().GameState);
		for (int i = 0; i < count; i++)
		{
			UpdateTypeLegacy type = (UpdateTypeLegacy)packet.ReadUInt8();
			this.PrintString($"Update Type = {type}", i);
			switch (type)
			{
			case UpdateTypeLegacy.Values:
			{
				WowGuid128 guid3 = packet.ReadPackedGuid().To128(this.GetSession().GameState);
				this.PrintString("Guid = " + guid3.ToString(), i);
				ObjectUpdate updateData2 = new ObjectUpdate(guid3, UpdateTypeModern.Values, this.GetSession());
				AuraUpdate auraUpdate2 = new AuraUpdate(guid3, all: false);
				PowerUpdate powerUpdate = new PowerUpdate(guid3);
				this.ReadValuesUpdateBlock(packet, guid3, updateData2, auraUpdate2, powerUpdate, i);
				if (powerUpdate.Powers.Count != 0)
				{
					this.SendPacketToClient(powerUpdate);
				}
				if (guid3 == this.GetSession().GameState.CurrentPlayerGuid)
				{
					// No stripping — packet splitting + ThreadStatic fix prevent corruption.
					// Let ALL data through including ObjectData DynamicFlags.
				}
				// DestroyObject + CreateObject2 on revive: ghost→alive transition
				if (guid3 == this.GetSession().GameState.CurrentPlayerGuid && this.GetSession().GameState.NeedPlayerRecreate)
				{
					this.GetSession().GameState.NeedPlayerRecreate = false;
					Log.Print(LogType.Debug, "[DeathRevive] Performing DestroyObject + CreateObject2 for revive", "HandleUpdateObject", "");

					// 1. Send DestroyObject to remove stale ghost-flagged player from client
					UpdateObject destroyPacket = new UpdateObject(this.GetSession().GameState);
					destroyPacket.DestroyedGuids.Add(guid3);
					this.SendPacketToClient(destroyPacket);

					// 2. Clear modern cache so CreateObject2 starts fresh
					this.GetSession().GameState.ObjectCacheMutex.WaitOne();
					this.GetSession().GameState.ObjectCacheModern.Remove(guid3);
					this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();

					// 3. Build full CreateObject2 from cached legacy fields
					Dictionary<int, UpdateField> cachedFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(guid3);
					if (cachedFields != null && this.GetSession().GameState.LastSelfPlayerMoveInfo != null)
					{
						ObjectUpdate createUpdate = new ObjectUpdate(guid3, UpdateTypeModern.CreateObject2, this.GetSession());
						createUpdate.CreateData.ObjectType = ObjectType.ActivePlayer;
						createUpdate.CreateData.ThisIsYou = true;
						createUpdate.CreateData.MoveInfo = this.GetSession().GameState.LastSelfPlayerMoveInfo.CopyFromMe();

						// Build full update mask from all cached fields
						int maxKey = 0;
						foreach (int key in cachedFields.Keys)
							if (key > maxKey) maxKey = key;
						BitArray fullMask = new BitArray(maxKey + 1, false);
						foreach (int key in cachedFields.Keys)
							fullMask.Set(key, true);

						// Populate all field values via StoreObjectUpdate
						AuraUpdate createAuraUpdate = new AuraUpdate(guid3, all: true);
						this.StoreObjectUpdate(guid3, ObjectType.ActivePlayer, fullMask, cachedFields, createAuraUpdate, null, true, createUpdate, fullMask);

						// Also apply completed quests
						this.GetSession().GameState.CurrentPlayerStorage.CompletedQuests.WriteAllCompletedIntoArray(createUpdate.ActivePlayerData.QuestCompleted);

						updateObject.ObjectUpdates.Add(createUpdate);
						if (createAuraUpdate.Auras.Count != 0)
						{
							auraUpdates.Add(createAuraUpdate);
						}
						Log.Print(LogType.Debug, $"[DeathRevive] Created full CreateObject2 with {cachedFields.Count} fields, pos={createUpdate.CreateData.MoveInfo.Position}", "HandleUpdateObject", "");
					}
					else
					{
						Log.Print(LogType.Error, "[DeathRevive] Cannot recreate player — missing cached fields or MoveInfo, sending Values update instead", "HandleUpdateObject", "");
						// Fall through to normal Values update as fallback
						goto normalValuesUpdate;
					}
					// Send any aura updates and skip normal Values path
					if (auraUpdate2.Auras.Count != 0)
					{
						auraUpdates.Add(auraUpdate2);
					}
					break;
				}
				normalValuesUpdate:
				// Check if the update has any actual data to send.
				// Empty Values updates (changedMask=0) crash the 3.4.3 client.
				bool hasAnythingToSend = false;
				if (updateData2.ObjectData != null && (updateData2.ObjectData.EntryID.HasValue || updateData2.ObjectData.DynamicFlags.HasValue || updateData2.ObjectData.Scale.HasValue))
					hasAnythingToSend = true;
				if (updateData2.UnitData != null && (updateData2.UnitData.Health.HasValue || updateData2.UnitData.MaxHealth.HasValue ||
					updateData2.UnitData.DisplayID.HasValue || updateData2.UnitData.Target != null ||
					updateData2.UnitData.Flags.HasValue || updateData2.UnitData.Flags2.HasValue ||
					updateData2.UnitData.Level.HasValue || updateData2.UnitData.FactionTemplate.HasValue ||
					updateData2.UnitData.AuraState.HasValue || updateData2.UnitData.NativeDisplayID.HasValue))
					hasAnythingToSend = true;
				if (updateData2.UnitData != null && updateData2.UnitData.Power != null)
					for (int p = 0; p < updateData2.UnitData.Power.Length; p++)
						if (updateData2.UnitData.Power[p].HasValue) { hasAnythingToSend = true; break; }
				if (updateData2.UnitData != null && updateData2.UnitData.MaxPower != null)
					for (int p = 0; p < updateData2.UnitData.MaxPower.Length; p++)
						if (updateData2.UnitData.MaxPower[p].HasValue) { hasAnythingToSend = true; break; }
				// Check stat/resistance/combat fields
				if (updateData2.UnitData != null)
				{
					var u = updateData2.UnitData;
					if (u.AttackPower.HasValue || u.RangedAttackPower.HasValue ||
						u.AttackPowerModPos.HasValue || u.AttackPowerModNeg.HasValue ||
						u.ShapeshiftForm.HasValue || u.BaseMana.HasValue || u.BaseHealth.HasValue ||
						u.EmoteState.HasValue || u.SheatheState.HasValue ||
						u.ModCastSpeed.HasValue || u.ModCastHaste.HasValue ||
						u.MinDamage.HasValue || u.MaxDamage.HasValue ||
						u.MountDisplayID.HasValue || u.GuildGUID != null)
						hasAnythingToSend = true;
					if (u.Stats != null)
						for (int s = 0; s < u.Stats.Length; s++)
							if (u.Stats[s].HasValue) { hasAnythingToSend = true; break; }
					if (u.Resistances != null)
						for (int r = 0; r < 7; r++)
							if (u.Resistances[r].HasValue) { hasAnythingToSend = true; break; }
					if (u.ResistanceBuffModsPositive != null)
						for (int r = 0; r < 7; r++)
							if (u.ResistanceBuffModsPositive[r].HasValue) { hasAnythingToSend = true; break; }
					if (u.ResistanceBuffModsNegative != null)
						for (int r = 0; r < 7; r++)
							if (u.ResistanceBuffModsNegative[r].HasValue) { hasAnythingToSend = true; break; }
				}
				// Skip Item-only Values updates - sends corrupt data that breaks client state
				if (guid3.IsItem())
					hasAnythingToSend = false;
				if (updateData2.ActivePlayerData != null)
				{
					ActivePlayerData a = updateData2.ActivePlayerData;
					if (a.Coinage.HasValue || a.XP.HasValue || a.NextLevelXP.HasValue)
						hasAnythingToSend = true;
					if (a.InvSlots != null)
						for (int s = 0; s < a.InvSlots.Length; s++)
							if (a.InvSlots[s] != null) { hasAnythingToSend = true; break; }
					if (a.PackSlots != null)
						for (int s = 0; s < a.PackSlots.Length; s++)
							if (a.PackSlots[s] != null) { hasAnythingToSend = true; break; }
				}
				if (updateData2.PlayerData != null)
				{
					PlayerData pd = updateData2.PlayerData;
					if (pd.PlayerFlags.HasValue || pd.PlayerFlagsEx.HasValue || pd.ChosenTitle.HasValue || pd.GuildTimeStamp.HasValue)
						hasAnythingToSend = true;
					if (pd.QuestLog != null)
						for (int q = 0; q < pd.QuestLog.Length; q++)
							if (pd.QuestLog[q] != null && pd.QuestLog[q].QuestID.HasValue) { hasAnythingToSend = true; break; }
					if (pd.VisibleItems != null)
						for (int v = 0; v < pd.VisibleItems.Length; v++)
							if (pd.VisibleItems[v] != null) { hasAnythingToSend = true; break; }
				}
				if (hasAnythingToSend)
				{
					// Debug: log stat/resistance updates for the player
					if (guid3 == this.GetSession().GameState.CurrentPlayerGuid && updateData2.UnitData != null)
					{
						var u = updateData2.UnitData;
						string statInfo = "";
						if (u.Stats != null)
							for (int si = 0; si < u.Stats.Length; si++)
								if (u.Stats[si].HasValue) statInfo += $" Stat{si}={u.Stats[si].Value}";
						if (u.Resistances != null)
							for (int ri = 0; ri < 7; ri++)
								if (u.Resistances[ri].HasValue) statInfo += $" Res{ri}={u.Resistances[ri].Value}";
						if (u.AttackPower.HasValue) statInfo += $" AP={u.AttackPower.Value}";
						if (u.BaseMana.HasValue) statInfo += $" BaseMana={u.BaseMana.Value}";
						if (u.BaseHealth.HasValue) statInfo += $" BaseHP={u.BaseHealth.Value}";
						if (statInfo.Length > 0)
							Log.Print(LogType.Debug, $"[PlayerUpdate] SENDING stats:{statInfo}", "HandleUpdateObject", "");
						if (updateData2.PlayerData?.VisibleItems != null)
						{
							string visInfo = "";
							for (int vi = 0; vi < updateData2.PlayerData.VisibleItems.Length; vi++)
								if (updateData2.PlayerData.VisibleItems[vi] != null)
									visInfo += $" Slot{vi}=ItemID:{updateData2.PlayerData.VisibleItems[vi].ItemID}";
							if (visInfo.Length > 0)
								Log.Print(LogType.Debug, $"[PlayerUpdate] SENDING visItems:{visInfo}", "HandleUpdateObject", "");
						}
					}
					updateObject.ObjectUpdates.Add(updateData2);
				}
				else if (guid3 == this.GetSession().GameState.CurrentPlayerGuid)
				{
					Log.Print(LogType.Debug, "[PlayerUpdate] DROPPED - no sendable data", "HandleUpdateObject", "");
				}
				if (auraUpdate2.Auras.Count != 0)
				{
					auraUpdates.Add(auraUpdate2);
				}
				break;
			}
			case UpdateTypeLegacy.Movement:
			{
				WowGuid64 guid2 = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901) ? packet.ReadPackedGuid() : packet.ReadGuid());
				this.PrintString("Guid = " + guid2.ToString(), i);
				this.ReadMovementUpdateBlock(packet, guid2, null, i);
				break;
			}
			case UpdateTypeLegacy.CreateObject1:
			{
				WowGuid64 oldGuid2 = packet.ReadPackedGuid();
				if (oldGuid2.GetHighType() == HighGuidType.Creature || oldGuid2.GetHighType() == HighGuidType.GameObject)
				{
					if (!this.GetSession().GameState.ObjectSpawnCount.ContainsKey(oldGuid2))
					{
						this.GetSession().GameState.ObjectSpawnCount.Add(oldGuid2, 0);
					}
					else if (oldGuid2.GetHighType() == HighGuidType.GameObject && this.GetSession().GameState.DespawnedGameObjects.Contains(oldGuid2))
					{
						this.GetSession().GameState.IncrementObjectSpawnCounter(oldGuid2);
					}
				}
				WowGuid128 guid4 = oldGuid2.To128(this.GetSession().GameState);
				this.PrintString("Guid = " + guid4.ToString(), i);
				if (guid4 == this.GetSession().GameState.CurrentPlayerGuid && this.GetSession().GameState.IsInFarSight)
				{
					UpdateObject updateObject2 = new UpdateObject(this.GetSession().GameState);
					ObjectUpdate updateData3 = new ObjectUpdate(guid4, UpdateTypeModern.Values, this.GetSession());
					updateData3.ActivePlayerData.FarsightObject = WowGuid128.Empty;
					updateObject2.ObjectUpdates.Add(updateData3);
					this.SendPacketToClient(updateObject2);
				}
				ObjectUpdate updateData4 = new ObjectUpdate(guid4, UpdateTypeModern.CreateObject1, this.GetSession());
				AuraUpdate auraUpdate3 = new AuraUpdate(guid4, all: true);
				this.ReadCreateObjectBlock(packet, guid4, updateData4, auraUpdate3, i);
				if (updateData4.Guid == this.GetSession().GameState.CurrentPlayerGuid)
				{
					this.GetSession().GameState.CurrentPlayerStorage.CompletedQuests.WriteAllCompletedIntoArray(updateData4.ActivePlayerData.QuestCompleted);
				}
				if (guid4.IsItem() && updateData4.ObjectData.EntryID.HasValue && !GameData.ItemTemplates.ContainsKey((uint)updateData4.ObjectData.EntryID.Value))
				{
					uint entryId4 = (uint)updateData4.ObjectData.EntryID.Value;
					missingItemTemplates.Add(entryId4);
					// Buffer this item create until its template arrives via hotfix
					if (!this.GetSession().GameState.PendingItemCreates.ContainsKey(entryId4))
						this.GetSession().GameState.PendingItemCreates[entryId4] = new List<ObjectUpdate>();
					this.GetSession().GameState.PendingItemCreates[entryId4].Add(updateData4);
					Log.Print(LogType.Debug, $"Buffering item CreateObject {guid4} entry={entryId4} until template arrives.", "HandleUpdateObject", "");
				}
				else if (updateData4.CreateData.MoveInfo != null || !guid4.IsWorldObject())
				{
					updateObject.ObjectUpdates.Add(updateData4);
					if (auraUpdate3.Auras.Count != 0)
					{
						auraUpdates.Add(auraUpdate3);
					}
				}
				else
				{
					Log.Print(LogType.Error, $"Broken create1 without position for {guid4}", "HandleUpdateObject", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\UpdateHandler.cs");
				}
				break;
			}
			case UpdateTypeLegacy.CreateObject2:
			{
				WowGuid64 oldGuid = packet.ReadPackedGuid();
				if (oldGuid.GetHighType() == HighGuidType.Creature || oldGuid.GetHighType() == HighGuidType.GameObject)
				{
					this.GetSession().GameState.IncrementObjectSpawnCounter(oldGuid);
				}
				WowGuid128 guid = oldGuid.To128(this.GetSession().GameState);
				this.PrintString("Guid = " + guid.ToString(), i);
				// In 3.4.3, CreateObject2 is ONLY for the self-player.
				// Legacy 3.3.5a uses CreateObject2 for all nearby objects, so downgrade non-self objects to CreateObject1.
				UpdateTypeModern createType = (guid == this.GetSession().GameState.CurrentPlayerGuid)
					? UpdateTypeModern.CreateObject2
					: UpdateTypeModern.CreateObject1;
				ObjectUpdate updateData = new ObjectUpdate(guid, createType, this.GetSession());
				AuraUpdate auraUpdate = new AuraUpdate(guid, all: true);
				this.ReadCreateObjectBlock(packet, guid, updateData, auraUpdate, i);
				// Cache MoveInfo for self player — needed for DestroyObject+CreateObject2 on revive
				if (guid == this.GetSession().GameState.CurrentPlayerGuid && updateData.CreateData?.MoveInfo != null)
				{
					this.GetSession().GameState.LastSelfPlayerMoveInfo = updateData.CreateData.MoveInfo.CopyFromMe();
					Log.Print(LogType.Debug, $"[DeathRevive] Cached self player MoveInfo pos={updateData.CreateData.MoveInfo.Position}", "HandleUpdateObject", "");
				}
				if (guid.IsItem() && updateData.ObjectData.EntryID.HasValue && !GameData.ItemTemplates.ContainsKey((uint)updateData.ObjectData.EntryID.Value))
				{
					uint entryId2 = (uint)updateData.ObjectData.EntryID.Value;
					missingItemTemplates.Add(entryId2);
					// Buffer this item create until its template arrives via hotfix
					if (!this.GetSession().GameState.PendingItemCreates.ContainsKey(entryId2))
						this.GetSession().GameState.PendingItemCreates[entryId2] = new List<ObjectUpdate>();
					this.GetSession().GameState.PendingItemCreates[entryId2].Add(updateData);
					Log.Print(LogType.Debug, $"Buffering item CreateObject {guid} entry={entryId2} until template arrives.", "HandleUpdateObject", "");
				}
				else if (updateData.CreateData.MoveInfo != null || !guid.IsWorldObject())
				{
					updateObject.ObjectUpdates.Add(updateData);
					if (auraUpdate.Auras.Count != 0)
					{
						auraUpdates.Add(auraUpdate);
					}
				}
				else
				{
					Log.Print(LogType.Error, $"Broken create2 without position for {guid}", "HandleUpdateObject", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\PacketHandlers\\UpdateHandler.cs");
				}
				break;
			}
			case UpdateTypeLegacy.NearObjects:
				this.ReadNearObjectsBlock(packet, i);
				break;
			case UpdateTypeLegacy.FarObjects:
				this.ReadFarObjectsBlock(packet, updateObject, i);
				break;
			}
		}
		if (updateObject.ObjectUpdates.Count == 0 && this.GetSession().GameState.IsWaitingForNewWorld)
		{
			return;
		}
		foreach (uint itemId in missingItemTemplates)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ITEM_QUERY_SINGLE);
			packet2.WriteUInt32(itemId);
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				packet2.WriteGuid(WowGuid64.Empty);
			}
			this.SendPacketToServer(packet2);
		}
		int activePlayerUpdateIndex = -1;
		for (int j = 0; j < updateObject.ObjectUpdates.Count; j++)
		{
			if (updateObject.ObjectUpdates[j].CreateData != null && updateObject.ObjectUpdates[j].CreateData.ThisIsYou)
			{
				activePlayerUpdateIndex = j;
				break;
			}
		}
		if (activePlayerUpdateIndex >= 0)
		{
			if (this.GetSession().GameState.FlatSpellMods.Count > 0)
			{
				SetSpellModifier spell = new SetSpellModifier(Opcode.SMSG_SET_FLAT_SPELL_MODIFIER);
				foreach (KeyValuePair<byte, Dictionary<byte, int>> modItr in this.GetSession().GameState.FlatSpellMods)
				{
					SpellModifierInfo mod = new SpellModifierInfo();
					mod.ModIndex = modItr.Key;
					foreach (KeyValuePair<byte, int> dataItr in modItr.Value)
					{
						SpellModifierData data = new SpellModifierData
						{
							ClassIndex = dataItr.Key,
							ModifierValue = dataItr.Value
						};
						mod.ModifierData.Add(data);
					}
					spell.Modifiers.Add(mod);
				}
				this.SendPacketToClient(spell);
			}
			if (this.GetSession().GameState.PctSpellMods.Count > 0)
			{
				SetSpellModifier spell2 = new SetSpellModifier(Opcode.SMSG_SET_PCT_SPELL_MODIFIER);
				foreach (KeyValuePair<byte, Dictionary<byte, int>> modItr2 in this.GetSession().GameState.PctSpellMods)
				{
					SpellModifierInfo mod2 = new SpellModifierInfo();
					mod2.ModIndex = modItr2.Key;
					foreach (KeyValuePair<byte, int> dataItr2 in modItr2.Value)
					{
						SpellModifierData data2 = new SpellModifierData
						{
							ClassIndex = dataItr2.Key,
							ModifierValue = dataItr2.Value
						};
						mod2.ModifierData.Add(data2);
					}
					spell2.Modifiers.Add(mod2);
				}
				this.SendPacketToClient(spell2);
			}
		}
		if (activePlayerUpdateIndex > 0)
		{
			ObjectUpdate tmp = updateObject.ObjectUpdates[0];
			updateObject.ObjectUpdates[0] = updateObject.ObjectUpdates[activePlayerUpdateIndex];
			updateObject.ObjectUpdates[activePlayerUpdateIndex] = tmp;
		}
		if (this.GetSession().GameState.CurrentMapId == 489)
		{
			bool resetBgPlayerPositions = false;
			foreach (WowGuid128 guid5 in updateObject.OutOfRangeGuids)
			{
				if (guid5.IsPlayer() && this.GetSession().GameState.FlagCarrierGuids.Contains(guid5))
				{
					resetBgPlayerPositions = true;
					break;
				}
			}
			if (resetBgPlayerPositions)
			{
				BattlegroundPlayerPositions bglist = new BattlegroundPlayerPositions();
				this.SendPacketToClient(bglist);
			}
		}
		// Split player Values updates into a separate packet from creature updates
		WowGuid128 playerGuid = this.GetSession().GameState.CurrentPlayerGuid;
		List<ObjectUpdate> playerValuesUpdates = new List<ObjectUpdate>();
		List<ObjectUpdate> otherUpdates = new List<ObjectUpdate>();
		foreach (var upd in updateObject.ObjectUpdates)
		{
			if (upd.Guid == playerGuid && upd.Type == UpdateTypeModern.Values)
				playerValuesUpdates.Add(upd);
			else
				otherUpdates.Add(upd);
		}
		if (otherUpdates.Count != 0 || updateObject.DestroyedGuids.Count != 0 || updateObject.OutOfRangeGuids.Count != 0)
		{
			updateObject.ObjectUpdates.Clear();
			updateObject.ObjectUpdates.AddRange(otherUpdates);
			this.SendPacketToClient(updateObject);
		}
		if (playerValuesUpdates.Count != 0)
		{
			UpdateObject playerUpdateObject = new UpdateObject(this.GetSession().GameState);
			playerUpdateObject.ObjectUpdates.AddRange(playerValuesUpdates);
			this.SendPacketToClient(playerUpdateObject);
		}
		foreach (AuraUpdate auraUpdate4 in auraUpdates)
		{
			this.SendPacketToClient(auraUpdate4);
		}
	}

	public void ReadNearObjectsBlock(WorldPacket packet, object index)
	{
		int objCount = packet.ReadInt32();
		this.PrintString($"NearObjectsCount = {objCount}", index);
		for (int j = 0; j < objCount; j++)
		{
			WowGuid64 guid = packet.ReadPackedGuid();
			this.PrintString($"Guid = {objCount}", index, j);
		}
	}

	public void ReadFarObjectsBlock(WorldPacket packet, UpdateObject updateObject, object index)
	{
		int objCount = packet.ReadInt32();
		this.PrintString($"FarObjectsCount = {objCount}", index);
		for (int j = 0; j < objCount; j++)
		{
			WowGuid128 guid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			if (!(guid == this.GetSession().GameState.CurrentPlayerGuid))
			{
				this.PrintString($"Guid = {objCount}", index, j);
				this.GetSession().GameState.ObjectCacheMutex.WaitOne();
				this.GetSession().GameState.ObjectCacheLegacy.Remove(guid);
				this.GetSession().GameState.ObjectCacheModern.Remove(guid);
				this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();
				this.GetSession().GameState.LastAuraCasterOnTarget.Remove(guid);
				if (this.GetSession().GameState.CurrentPetGuid == guid)
				{
					UpdateObject updateObject2 = new UpdateObject(this.GetSession().GameState);
					ObjectUpdate updateData2 = new ObjectUpdate(guid, UpdateTypeModern.Values, this.GetSession());
					updateObject2.ObjectUpdates.Add(updateData2);
					this.SendPacketToClient(updateObject2);
				}
				updateObject.OutOfRangeGuids.Add(guid);
			}
		}
	}

	private void ReadCreateObjectBlock(WorldPacket packet, WowGuid128 guid, ObjectUpdate updateData, AuraUpdate auraUpdate, object index)
	{
		updateData.CreateData.ObjectType = ObjectTypeConverter.Convert((ObjectTypeLegacy)packet.ReadUInt8());
		this.GetSession().GameState.StoreOriginalObjectType(guid, updateData.CreateData.ObjectType);
		this.ReadMovementUpdateBlock(packet, guid, updateData, index);
		this.ReadValuesUpdateBlockOnCreate(packet, guid, updateData.CreateData.ObjectType, updateData, auraUpdate, index);
	}

	public void ReadValuesUpdateBlockOnCreate(WorldPacket packet, WowGuid128 guid, ObjectType type, ObjectUpdate updateData, AuraUpdate auraUpdate, object index)
	{
		BitArray updateMaskArray = null;
		BitArray actuallyChangedValuesMaskArray;
		Dictionary<int, UpdateField> updates = this.ReadValuesUpdateBlock(packet, ref type, index, isCreating: true, null, out updateMaskArray, out actuallyChangedValuesMaskArray);
		this.StoreObjectUpdate(guid, type, updateMaskArray, updates, auraUpdate, null, isCreate: true, updateData, actuallyChangedValuesMaskArray);
		this.GetSession().GameState.ObjectCacheMutex.WaitOne();
		if (!this.GetSession().GameState.ObjectCacheLegacy.ContainsKey(guid))
		{
			this.GetSession().GameState.ObjectCacheLegacy.Add(guid, updates);
		}
		else
		{
			this.GetSession().GameState.ObjectCacheLegacy[guid] = updates;
		}
		this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();
	}

	public void ReadValuesUpdateBlock(WorldPacket packet, WowGuid128 guid, ObjectUpdate updateData, AuraUpdate auraUpdate, PowerUpdate powerUpdate, int index)
	{
		BitArray updateMaskArray = null;
		ObjectType type = this.GetSession().GameState.GetOriginalObjectType(guid);
		BitArray actuallyChangedValuesMaskArray;
		Dictionary<int, UpdateField> updates = this.ReadValuesUpdateBlock(packet, ref type, index, isCreating: false, this.GetSession().GameState.GetCachedObjectFieldsLegacy(guid), out updateMaskArray, out actuallyChangedValuesMaskArray);
		this.StoreObjectUpdate(guid, type, updateMaskArray, updates, auraUpdate, powerUpdate, isCreate: false, updateData, actuallyChangedValuesMaskArray);

		// Merge changed fields back into ObjectCacheLegacy so inventory slot
		// GUIDs and other values stay current for subsequent lookups
		// (e.g. HandleItemPushResult reading GetInventorySlotItem).
		this.GetSession().GameState.ObjectCacheMutex.WaitOne();
		if (this.GetSession().GameState.ObjectCacheLegacy.TryGetValue(guid, out var cached))
		{
			foreach (var kvp in updates)
				cached[kvp.Key] = kvp.Value;
		}
		else
		{
			this.GetSession().GameState.ObjectCacheLegacy[guid] = updates;
		}
		this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();
	}

	private string GetIndexString(params object[] values)
	{
		IEnumerable<object> list = values.Flatten();
		return list.Where((object value) => value != null).Aggregate(string.Empty, delegate(string current, object value)
		{
			string text = ((value is string) ? "()" : "[]");
			return current + text[0] + value.ToString() + text[1] + " ";
		});
	}

	private void PrintString(string txt, params object[] indexes)
	{
	}

	private T PrintValue<T>(string name, T obj, params object[] indexes)
	{
		return obj;
	}

	private Dictionary<int, UpdateField> ReadValuesUpdateBlock(WorldPacket packet, ref ObjectType type, object index, bool isCreating, Dictionary<int, UpdateField>? oldValues, out BitArray outUpdateMaskArray, out BitArray outActuallyChangedValuesMaskArray)
	{
		bool missingCreateObject = !isCreating && oldValues == null;
		byte maskSize = packet.ReadUInt8();
		int[] updateMask = new int[maskSize];
		for (int i = 0; i < maskSize; i++)
		{
			updateMask[i] = packet.ReadInt32();
		}
		BitArray mask = (outUpdateMaskArray = new BitArray(updateMask));
		outActuallyChangedValuesMaskArray = new BitArray(new int[maskSize]);
		Dictionary<int, UpdateField> dict = oldValues ?? new Dictionary<int, UpdateField>();
		if (missingCreateObject)
		{
			switch (type)
			{
			case ObjectType.Item:
				if (mask.Count >= LegacyVersion.GetUpdateField(ItemField.ITEM_END) && maskSize == Convert.ToInt32((LegacyVersion.GetUpdateField(ContainerField.CONTAINER_END) + 32) / 32))
				{
					type = ObjectType.Container;
				}
				break;
			case ObjectType.Player:
				if (mask.Count >= LegacyVersion.GetUpdateField(PlayerField.PLAYER_END) && maskSize == Convert.ToInt32((LegacyVersion.GetUpdateField(ActivePlayerField.ACTIVE_PLAYER_END) + 32) / 32))
				{
					type = ObjectType.ActivePlayer;
				}
				break;
			}
		}
		else
		{
			switch (type)
			{
			case ObjectType.Item:
			{
				int ITEM_END = LegacyVersion.GetUpdateField(ItemField.ITEM_END);
				if (mask.Length < ITEM_END)
				{
					mask.Length = ITEM_END;
				}
				break;
			}
			case ObjectType.Container:
			{
				int CONTAINER_END = LegacyVersion.GetUpdateField(ContainerField.CONTAINER_END);
				if (mask.Length < CONTAINER_END)
				{
					mask.Length = CONTAINER_END;
				}
				break;
			}
			case ObjectType.Unit:
			{
				int UNIT_END = LegacyVersion.GetUpdateField(UnitField.UNIT_END);
				if (mask.Length < UNIT_END)
				{
					mask.Length = UNIT_END;
				}
				break;
			}
			case ObjectType.Player:
			{
				int PLAYER_END = LegacyVersion.GetUpdateField(PlayerField.PLAYER_END);
				if (mask.Length < PLAYER_END)
				{
					mask.Length = PLAYER_END;
				}
				break;
			}
			case ObjectType.GameObject:
			{
				int GAMEOBJECT_END = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_END);
				if (mask.Length < GAMEOBJECT_END)
				{
					mask.Length = GAMEOBJECT_END;
				}
				break;
			}
			case ObjectType.DynamicObject:
			{
				int DYNAMICOBJECT_END = LegacyVersion.GetUpdateField(DynamicObjectField.DYNAMICOBJECT_END);
				if (mask.Length < DYNAMICOBJECT_END)
				{
					mask.Length = DYNAMICOBJECT_END;
				}
				break;
			}
			case ObjectType.Corpse:
			{
				int CORPSE_END = LegacyVersion.GetUpdateField(CorpseField.CORPSE_END);
				if (mask.Length < CORPSE_END)
				{
					mask.Length = CORPSE_END;
				}
				break;
			}
			}
		}
		int objectEnd = LegacyVersion.GetUpdateField(ObjectField.OBJECT_END);
		for (int j = 0; j < mask.Count; j++)
		{
			if (!mask[j])
			{
				continue;
			}
			UpdateField blockVal = packet.ReadUpdateField();
			string key = "Block Value " + j;
			string value = blockVal.UInt32Value + "/" + blockVal.FloatValue;
			UpdateFieldInfo fieldInfo = null;
			if (j < objectEnd)
			{
				fieldInfo = LegacyVersion.GetUpdateFieldInfo<ObjectField>(j);
			}
			else
			{
				switch (type)
				{
				case ObjectType.Container:
					if (j < LegacyVersion.GetUpdateField(ItemField.ITEM_END))
					{
						goto case ObjectType.Item;
					}
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<ContainerField>(j);
					break;
				case ObjectType.Item:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<ItemField>(j);
					break;
				case ObjectType.AzeriteEmpoweredItem:
					if (j < LegacyVersion.GetUpdateField(ItemField.ITEM_END))
					{
						goto case ObjectType.Item;
					}
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<AzeriteEmpoweredItemField>(j);
					break;
				case ObjectType.AzeriteItem:
					if (j < LegacyVersion.GetUpdateField(ItemField.ITEM_END))
					{
						goto case ObjectType.Item;
					}
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<AzeriteItemField>(j);
					break;
				case ObjectType.Player:
					if (j < LegacyVersion.GetUpdateField(UnitField.UNIT_END) || j < LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_END))
					{
						goto case ObjectType.Unit;
					}
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<PlayerField>(j);
					break;
				case ObjectType.ActivePlayer:
					if (j < LegacyVersion.GetUpdateField(PlayerField.PLAYER_END))
					{
						goto case ObjectType.Player;
					}
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<ActivePlayerField>(j);
					break;
				case ObjectType.Unit:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<UnitField>(j);
					break;
				case ObjectType.GameObject:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<GameObjectField>(j);
					break;
				case ObjectType.DynamicObject:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<DynamicObjectField>(j);
					break;
				case ObjectType.Corpse:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<CorpseField>(j);
					break;
				case ObjectType.AreaTrigger:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<AreaTriggerField>(j);
					break;
				case ObjectType.SceneObject:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<SceneObjectField>(j);
					break;
				case ObjectType.Conversation:
					fieldInfo = LegacyVersion.GetUpdateFieldInfo<ConversationField>(j);
					break;
				}
			}
			int start = j;
			int size = 1;
			UpdateFieldType updateFieldType = UpdateFieldType.Default;
			if (fieldInfo != null)
			{
				key = fieldInfo.Name;
				size = fieldInfo.Size;
				start = fieldInfo.Value;
				updateFieldType = fieldInfo.Format;
			}
			List<UpdateField> fieldData = new List<UpdateField>();
			for (int k = start; k < j; k++)
			{
				if (oldValues == null || !oldValues.TryGetValue(k, out var updateField))
				{
					updateField = new UpdateField(0);
				}
				fieldData.Add(updateField);
			}
			fieldData.Add(blockVal);
			for (int l = j - start + 1; l < size; l++)
			{
				int currentPosition = ++j;
				UpdateField updateField2;
				if (mask[currentPosition])
				{
					updateField2 = packet.ReadUpdateField();
				}
				else if (oldValues == null || !oldValues.TryGetValue(currentPosition, out updateField2))
				{
					updateField2 = new UpdateField(0);
				}
				fieldData.Add(updateField2);
			}
			switch (updateFieldType)
			{
			case UpdateFieldType.Guid:
			{
				int guidSize = (LegacyVersion.AddedInVersion(ClientVersionBuild.V6_0_2_19033) ? 4 : 2);
				int guidCount = size / guidSize;
				for (int guidI = 0; guidI < guidCount; guidI++)
				{
					bool hasGuidValue = false;
					for (int guidPart = 0; guidPart < guidSize; guidPart++)
					{
						if (mask[start + guidI * guidSize + guidPart])
						{
							hasGuidValue = true;
						}
					}
					if (!hasGuidValue)
					{
						continue;
					}
					if (!LegacyVersion.AddedInVersion(ClientVersionBuild.V6_0_2_19033))
					{
						ulong guid = fieldData[guidI * guidSize + 1].UInt32Value;
						guid <<= 32;
						guid |= fieldData[guidI * guidSize].UInt32Value;
						if (!isCreating || guid != 0)
						{
							this.PrintValue(key + ((guidCount > 1) ? (" + " + guidI) : ""), new WowGuid64(guid), index);
						}
						continue;
					}
					ulong low = fieldData[guidI * guidSize + 1].UInt32Value;
					low <<= 32;
					low |= fieldData[guidI * guidSize].UInt32Value;
					ulong high = fieldData[guidI * guidSize + 3].UInt32Value;
					high <<= 32;
					high |= fieldData[guidI * guidSize + 2].UInt32Value;
					if (!isCreating || high != 0L || low != 0)
					{
						this.PrintValue(key + ((guidCount > 1) ? (" + " + guidI) : ""), new WowGuid128(low, high), index);
					}
				}
				break;
			}
			case UpdateFieldType.Quaternion:
			{
				int quaternionCount = size / 4;
				for (int quatI = 0; quatI < quaternionCount; quatI++)
				{
					bool hasQuatValue = false;
					for (int num3 = 0; num3 < 4; num3++)
					{
						if (mask[start + quatI * 4 + num3])
						{
							hasQuatValue = true;
						}
					}
					if (hasQuatValue)
					{
						this.PrintValue(key + ((quaternionCount > 1) ? (" + " + quatI) : ""), new Framework.GameMath.Quaternion(fieldData[quatI * 4].FloatValue, fieldData[quatI * 4 + 1].FloatValue, fieldData[quatI * 4 + 2].FloatValue, fieldData[quatI * 4 + 3].FloatValue), index);
					}
				}
				break;
			}
			case UpdateFieldType.PackedQuaternion:
			{
				int quaternionCount2 = size / 2;
				for (int num5 = 0; num5 < quaternionCount2; num5++)
				{
					bool hasQuatValue2 = false;
					for (int num6 = 0; num6 < 2; num6++)
					{
						if (mask[start + num5 * 2 + num6])
						{
							hasQuatValue2 = true;
						}
					}
					if (hasQuatValue2)
					{
						long quat = fieldData[num5 * 2 + 1].UInt32Value;
						quat <<= 32;
						quat |= fieldData[num5 * 2].UInt32Value;
						this.PrintValue(key + ((quaternionCount2 > 1) ? (" + " + num5) : ""), new Framework.GameMath.Quaternion(quat), index);
					}
				}
				break;
			}
			case UpdateFieldType.Uint:
			{
				for (int num = 0; num < fieldData.Count; num++)
				{
					if (mask[start + num] && (!isCreating || fieldData[num].UInt32Value != 0))
					{
						this.PrintValue((num > 0) ? (key + " + " + num) : key, fieldData[num].UInt32Value, index);
					}
				}
				break;
			}
			case UpdateFieldType.Int:
			{
				for (int num7 = 0; num7 < fieldData.Count; num7++)
				{
					if (mask[start + num7] && (!isCreating || fieldData[num7].UInt32Value != 0))
					{
						this.PrintValue((num7 > 0) ? (key + " + " + num7) : key, fieldData[num7].Int32Value, index);
					}
				}
				break;
			}
			case UpdateFieldType.Float:
			{
				for (int num4 = 0; num4 < fieldData.Count; num4++)
				{
					if (mask[start + num4] && (!isCreating || fieldData[num4].UInt32Value != 0))
					{
						this.PrintValue((num4 > 0) ? (key + " + " + num4) : key, fieldData[num4].FloatValue, index);
					}
				}
				break;
			}
			case UpdateFieldType.Bytes:
			{
				for (int num2 = 0; num2 < fieldData.Count; num2++)
				{
					if (mask[start + num2] && (!isCreating || fieldData[num2].UInt32Value != 0))
					{
						byte[] intBytes = BitConverter.GetBytes(fieldData[num2].UInt32Value);
						this.PrintValue((num2 > 0) ? (key + " + " + num2) : key, intBytes[0] + "/" + intBytes[1] + "/" + intBytes[2] + "/" + intBytes[3], index);
					}
				}
				break;
			}
			case UpdateFieldType.Short:
			{
				for (int n = 0; n < fieldData.Count; n++)
				{
					if (mask[start + n] && (!isCreating || fieldData[n].UInt32Value != 0))
					{
						this.PrintValue((n > 0) ? (key + " + " + n) : key, (short)(fieldData[n].UInt32Value & 0xFFFF) + "/" + (short)(fieldData[n].UInt32Value >> 16), index);
					}
				}
				break;
			}
			default:
			{
				for (int m = 0; m < fieldData.Count; m++)
				{
					if (mask[start + m] && (!isCreating || fieldData[m].UInt32Value != 0))
					{
						this.PrintValue((m > 0) ? (key + " + " + m) : key, fieldData[m].UInt32Value + "/" + fieldData[m].FloatValue, index);
					}
				}
				break;
			}
			}
			for (int num8 = 0; num8 < fieldData.Count; num8++)
			{
				if (!dict.ContainsKey(start + num8))
				{
					outActuallyChangedValuesMaskArray.Set(start + num8, value: true);
					dict.Add(start + num8, fieldData[num8]);
					continue;
				}
				if (dict[start + num8] != fieldData[num8])
				{
					outActuallyChangedValuesMaskArray.Set(start + num8, value: true);
				}
				dict[start + num8] = fieldData[num8];
			}
		}
		return dict;
	}

	private void ReadMovementUpdateBlock(WorldPacket packet, WowGuid guid, ObjectUpdate updateData, object index)
	{
		MovementInfo moveInfo = null;
		UpdateFlag flags = (UpdateFlag)((!LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) ? packet.ReadUInt8() : packet.ReadUInt16());
		if (flags.HasAnyFlag(UpdateFlag.Self))
		{
			if (updateData != null)
			{
				updateData.CreateData.ThisIsYou = true;
			}
			this.GetSession().GameState.CurrentPlayerCreateTime = packet.GetReceivedTime();
		}
		if (flags.HasAnyFlag(UpdateFlag.Living))
		{
			moveInfo = new MovementInfo();
			moveInfo.ReadMovementInfoLegacy(packet, this.GetSession().GameState);
			uint moveFlags = moveInfo.Flags;
			moveInfo.WalkSpeed = packet.ReadFloat();
			moveInfo.RunSpeed = packet.ReadFloat();
			moveInfo.RunBackSpeed = packet.ReadFloat();
			moveInfo.SwimSpeed = packet.ReadFloat();
			moveInfo.SwimBackSpeed = packet.ReadFloat();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				moveInfo.FlightSpeed = packet.ReadFloat();
				moveInfo.FlightBackSpeed = packet.ReadFloat();
			}
			else
			{
				moveInfo.FlightSpeed = moveInfo.SwimSpeed;
				moveInfo.FlightBackSpeed = moveInfo.SwimBackSpeed;
			}
			moveInfo.TurnRate = packet.ReadFloat();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				moveInfo.PitchRate = packet.ReadFloat();
			}
			if (moveFlags.HasAnyFlag(MovementFlagWotLK.SplineEnabled))
			{
				moveInfo.HasSplineData = true;
				ServerSideMovement monsterMove = new ServerSideMovement();
				if (moveInfo.TransportGuid != null)
				{
					monsterMove.TransportGuid = moveInfo.TransportGuid;
				}
				monsterMove.TransportSeat = moveInfo.TransportSeat;
				monsterMove.SplineFlags = SplineFlagModern.None;
				monsterMove.SplineType = SplineTypeModern.None;
				bool hasTaxiFlightFlags;
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					SplineFlagWotLK splineFlags = (SplineFlagWotLK)packet.ReadUInt32();
					monsterMove.SplineFlags = splineFlags.CastFlags<SplineFlagModern>();
					hasTaxiFlightFlags = splineFlags == (SplineFlagWotLK.WalkMode | SplineFlagWotLK.Flying);
					if (splineFlags.HasAnyFlag(SplineFlagWotLK.FinalTarget))
					{
						monsterMove.FinalFacingGuid = packet.ReadGuid().To128(this.GetSession().GameState);
						monsterMove.SplineType = SplineTypeModern.FacingTarget;
					}
					else if (splineFlags.HasAnyFlag(SplineFlagWotLK.FinalOrientation))
					{
						monsterMove.FinalOrientation = packet.ReadFloat();
						MovementInfo.ClampOrientation(ref monsterMove.FinalOrientation);
						monsterMove.SplineType = SplineTypeModern.FacingAngle;
					}
					else if (splineFlags.HasAnyFlag(SplineFlagWotLK.FinalPoint))
					{
						monsterMove.FinalFacingSpot = packet.ReadVector3();
						monsterMove.SplineType = SplineTypeModern.FacingSpot;
					}
				}
				else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					SplineFlagTBC splineFlags2 = (SplineFlagTBC)packet.ReadUInt32();
					monsterMove.SplineFlags = splineFlags2.CastFlags<SplineFlagModern>();
					hasTaxiFlightFlags = splineFlags2 == (SplineFlagTBC.Runmode | SplineFlagTBC.Flying);
					if (splineFlags2.HasAnyFlag(SplineFlagTBC.FinalTarget))
					{
						monsterMove.FinalFacingGuid = packet.ReadGuid().To128(this.GetSession().GameState);
						monsterMove.SplineType = SplineTypeModern.FacingTarget;
					}
					else if (splineFlags2.HasAnyFlag(SplineFlagTBC.FinalOrientation))
					{
						monsterMove.FinalOrientation = packet.ReadFloat();
						MovementInfo.ClampOrientation(ref monsterMove.FinalOrientation);
						monsterMove.SplineType = SplineTypeModern.FacingAngle;
					}
					else if (splineFlags2.HasAnyFlag(SplineFlagTBC.FinalPoint))
					{
						monsterMove.FinalFacingSpot = packet.ReadVector3();
						monsterMove.SplineType = SplineTypeModern.FacingSpot;
					}
				}
				else
				{
					SplineFlagVanilla splineFlags3 = (SplineFlagVanilla)packet.ReadUInt32();
					monsterMove.SplineFlags = splineFlags3.CastFlags<SplineFlagModern>();
					hasTaxiFlightFlags = splineFlags3 == (SplineFlagVanilla.Runmode | SplineFlagVanilla.Flying);
					if (splineFlags3.HasAnyFlag(SplineFlagVanilla.FinalTarget))
					{
						monsterMove.FinalFacingGuid = packet.ReadGuid().To128(this.GetSession().GameState);
						monsterMove.SplineType = SplineTypeModern.FacingTarget;
					}
					else if (splineFlags3.HasAnyFlag(SplineFlagVanilla.FinalOrientation))
					{
						monsterMove.FinalOrientation = packet.ReadFloat();
						MovementInfo.ClampOrientation(ref monsterMove.FinalOrientation);
						monsterMove.SplineType = SplineTypeModern.FacingAngle;
					}
					else if (splineFlags3.HasAnyFlag(SplineFlagVanilla.FinalPoint))
					{
						monsterMove.FinalFacingSpot = packet.ReadVector3();
						monsterMove.SplineType = SplineTypeModern.FacingSpot;
					}
				}
				if (hasTaxiFlightFlags && guid.IsPlayer() && flags.HasAnyFlag(UpdateFlag.Self))
				{
					monsterMove.SplineFlags = SplineFlagModern.Flying | SplineFlagModern.CatmullRom | SplineFlagModern.CanSwim | SplineFlagModern.UncompressedPath | SplineFlagModern.Unknown5 | SplineFlagModern.Steering | SplineFlagModern.Unknown10;
				}
				monsterMove.SplineTime = packet.ReadUInt32();
				monsterMove.SplineTimeFull = packet.ReadUInt32();
				monsterMove.SplineId = packet.ReadUInt32();
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
				{
					packet.ReadFloat();
					packet.ReadFloat();
					packet.ReadInt32();
					packet.ReadInt32();
				}
				uint splineCount = (monsterMove.SplineCount = packet.ReadUInt32());
				monsterMove.SplinePoints = new List<Framework.GameMath.Vector3>();
				for (int i = 0; i < splineCount; i++)
				{
					Framework.GameMath.Vector3 vec = packet.ReadVector3();
					monsterMove.SplinePoints.Add(vec);
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
				{
					monsterMove.SplineMode = packet.ReadUInt8();
				}
				monsterMove.EndPosition = packet.ReadVector3();
				if (updateData != null)
				{
					updateData.CreateData.MoveSpline = monsterMove;
				}
			}
		}
		else if (flags.HasAnyFlag(UpdateFlag.GOPosition))
		{
			moveInfo = new MovementInfo();
			moveInfo.TransportGuid = packet.ReadPackedGuid().To128(this.GetSession().GameState);
			moveInfo.Position = packet.ReadVector3();
			moveInfo.TransportOffset = packet.ReadVector3();
			moveInfo.Orientation = packet.ReadFloat();
			moveInfo.TransportOrientation = moveInfo.Orientation;
			moveInfo.CorpseOrientation = packet.ReadFloat();
		}
		else if (flags.HasAnyFlag(UpdateFlag.StationaryObject))
		{
			moveInfo = new MovementInfo();
			moveInfo.Position = packet.ReadVector3();
			moveInfo.Orientation = packet.ReadFloat();
		}
		if (flags.HasAnyFlag(UpdateFlag.LowGuid))
		{
			packet.ReadUInt32();
		}
		if (flags.HasAnyFlag(UpdateFlag.HighGuid))
		{
			packet.ReadUInt32();
		}
		if (flags.HasAnyFlag(UpdateFlag.AttackingTarget))
		{
			WowGuid64 attackGuid = packet.ReadPackedGuid();
			if (updateData != null)
			{
				updateData.CreateData.AutoAttackVictim = attackGuid.To128(this.GetSession().GameState);
			}
		}
		if (flags.HasAnyFlag(UpdateFlag.Transport))
		{
			uint transportPathTimer = packet.ReadUInt32();
			if (moveInfo != null)
			{
				moveInfo.TransportPathTimer = transportPathTimer;
			}
		}
		if (flags.HasAnyFlag(UpdateFlag.Vehicle))
		{
			uint vehicleId = packet.ReadUInt32();
			float vehicleOrientation = packet.ReadFloat();
			if (moveInfo != null)
			{
				moveInfo.VehicleId = vehicleId;
				moveInfo.VehicleOrientation = vehicleOrientation;
			}
		}
		if (flags.HasAnyFlag(UpdateFlag.GORotation))
		{
			Framework.GameMath.Quaternion rotation = packet.ReadPackedQuaternion();
			if (moveInfo != null)
			{
				moveInfo.Rotation = rotation;
			}
		}
		if (updateData != null && moveInfo != null)
		{
			moveInfo.Flags = (uint)((MovementFlagWotLK)moveInfo.Flags).CastFlags<MovementFlagModern>();
			moveInfo.ValidateMovementInfo();
			updateData.CreateData.MoveInfo = moveInfo;
		}
	}

	private static WowGuid GetGuidValue<T>(Dictionary<int, UpdateField> UpdateFields, T field) where T : Enum
	{
		if (!LegacyVersion.AddedInVersion(ClientVersionBuild.V6_0_2_19033))
		{
			uint[] parts = UpdateFields.GetArray<T, uint>(field, 2);
			return new WowGuid64(MathFunctions.MakePair64(parts[0], parts[1]));
		}
		uint[] parts2 = UpdateFields.GetArray<T, uint>(field, 4);
		return new WowGuid128(MathFunctions.MakePair64(parts2[0], parts2[1]), MathFunctions.MakePair64(parts2[2], parts2[3]));
	}

	private static WowGuid GetGuidValue(Dictionary<int, UpdateField> UpdateFields, int field)
	{
		if (!LegacyVersion.AddedInVersion(ClientVersionBuild.V6_0_2_19033))
		{
			uint[] parts = UpdateFields.GetArray<uint>(field, 2);
			return new WowGuid64(MathFunctions.MakePair64(parts[0], parts[1]));
		}
		uint[] parts2 = UpdateFields.GetArray<uint>(field, 4);
		return new WowGuid128(MathFunctions.MakePair64(parts2[0], parts2[1]), MathFunctions.MakePair64(parts2[2], parts2[3]));
	}

	public QuestLog ReadQuestLogEntry(int i, BitArray updateMaskArray, Dictionary<int, UpdateField> updates)
	{
		int PLAYER_QUEST_LOG_1_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_QUEST_LOG_1_1);
		// 3.3.5a quest log: 5 fields per entry (QuestID, StateFlags, Progress, [gap], Timer)
		// Fields: _1=QuestID(+0), _2=StateFlags(+1), _3=Progress(+2), skip(+3), _4/_5=Timer(+4)
		int sizePerEntry = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089) ? 5 : 3);
		int stateOffset = 1;
		int progressOffset = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089) ? 2 : (-1));
		int timerOffset = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089) ? 4 : 2);
		QuestLog questLog = null;
		int index = PLAYER_QUEST_LOG_1_1 + i * sizePerEntry;
		if ((updateMaskArray != null && updateMaskArray[index]) || (updateMaskArray == null && updates.ContainsKey(index)))
		{
			if (questLog == null)
			{
				questLog = new QuestLog();
			}
			questLog.QuestID = updates[index].Int32Value;
			// Cache the QuestID for this slot
			this.GetSession().GameState.QuestLogQuestIDs[i] = questLog.QuestID.Value;
			Log.Print(LogType.Debug, $"[QuestLogRead] slot={i} QuestID={questLog.QuestID.Value} fieldIndex={index}", "ReadQuestLogEntry", "");
		}
		if ((updateMaskArray != null && updateMaskArray[index + stateOffset]) || (updateMaskArray == null && updates.ContainsKey(index + stateOffset)))
		{
			if (questLog == null)
			{
				questLog = new QuestLog();
			}
			if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_4_0_8089))
			{
				uint rawValue = updates[index + stateOffset].UInt32Value;
				questLog.ObjectiveProgress[0] = (byte)(rawValue & 0x3F);
				questLog.ObjectiveProgress[1] = (byte)((rawValue & 0xFC0) >> 6);
				questLog.ObjectiveProgress[2] = (byte)((rawValue & 0x3F000) >> 12);
				questLog.ObjectiveProgress[3] = (byte)((rawValue & 0xFC0000) >> 18);
				questLog.StateFlags = (rawValue >> 24) & 0xFF;
			}
			else
			{
				questLog.StateFlags = updates[index + stateOffset].UInt32Value;
			}
		}
		if (progressOffset != -1 && ((updateMaskArray != null && updateMaskArray[index + progressOffset]) || (updateMaskArray == null && updates.ContainsKey(index + progressOffset))))
		{
			if (questLog == null)
			{
				questLog = new QuestLog();
			}
			// In 3.3.5a, objective counts are 16-bit each, stored as uint64 across fields +2 and +3
			// Field +2: objective 0 (low 16 bits) | objective 1 (high 16 bits)
			// Field +3: objective 2 (low 16 bits) | objective 3 (high 16 bits)
			uint progressField0 = updates[index + progressOffset].UInt32Value;
			questLog.ObjectiveProgress[0] = (short)(progressField0 & 0xFFFF);
			questLog.ObjectiveProgress[1] = (short)((progressField0 >> 16) & 0xFFFF);
			int progressOffset2 = progressOffset + 1;
			if (updates.ContainsKey(index + progressOffset2))
			{
				uint progressField1 = updates[index + progressOffset2].UInt32Value;
				questLog.ObjectiveProgress[2] = (short)(progressField1 & 0xFFFF);
				questLog.ObjectiveProgress[3] = (short)((progressField1 >> 16) & 0xFFFF);
			}
		}
		// Also handle when only field +3 updates (objectives 2-3 change without 0-1 changing)
		if (progressOffset != -1)
		{
			int progressOffset2 = progressOffset + 1;
			bool field3Updated = (updateMaskArray != null && updateMaskArray[index + progressOffset2]) || (updateMaskArray == null && updates.ContainsKey(index + progressOffset2));
			bool field2Updated = (updateMaskArray != null && updateMaskArray[index + progressOffset]) || (updateMaskArray == null && updates.ContainsKey(index + progressOffset));
			if (field3Updated && !field2Updated)
			{
				if (questLog == null)
				{
					questLog = new QuestLog();
				}
				uint progressField1 = updates[index + progressOffset2].UInt32Value;
				questLog.ObjectiveProgress[2] = (short)(progressField1 & 0xFFFF);
				questLog.ObjectiveProgress[3] = (short)((progressField1 >> 16) & 0xFFFF);
			}
		}
		if ((updateMaskArray != null && updateMaskArray[index + timerOffset]) || (updateMaskArray == null && updates.ContainsKey(index + timerOffset)))
		{
			if (questLog == null)
			{
				questLog = new QuestLog();
			}
			questLog.EndTime = updates[index + timerOffset].UInt32Value;
		}
		// If we have quest data (StateFlags/Progress) but no QuestID in this update,
		// fill QuestID from the cache (set during CreateObject or earlier Values update)
		if (questLog != null && !questLog.QuestID.HasValue)
		{
			int cachedId = this.GetSession().GameState.QuestLogQuestIDs[i];
			if (cachedId != 0)
			{
				questLog.QuestID = cachedId;
			}
		}
		// If QuestID was explicitly set to 0, clear the cache (quest abandoned/completed)
		if (questLog != null && questLog.QuestID.HasValue && questLog.QuestID.Value == 0)
		{
			this.GetSession().GameState.QuestLogQuestIDs[i] = 0;
		}
		return questLog;
	}

	public AuraDataInfo ReadAuraSlot(byte i, WowGuid128 guid, Dictionary<int, UpdateField> updates)
	{
		int UNIT_FIELD_AURA = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURA);
		int UNIT_FIELD_AURAFLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURAFLAGS);
		int UNIT_FIELD_AURALEVELS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURALEVELS);
		int UNIT_FIELD_AURAAPPLICATIONS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURAAPPLICATIONS);
		if (!updates.ContainsKey(UNIT_FIELD_AURA + i))
		{
			return null;
		}
		uint spellId = updates[UNIT_FIELD_AURA + i].UInt32Value;
		if (spellId == 0)
		{
			return null;
		}
		AuraDataInfo data = new AuraDataInfo();
		data.CastID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Aura, this.GetSession().GameState.CurrentMapId.Value, spellId, guid.GetCounter());
		data.SpellID = spellId;
		data.SpellXSpellVisualID = GameData.GetSpellVisual(spellId);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			int flagsIndex = UNIT_FIELD_AURAFLAGS + i / 4;
			if (updates.ContainsKey(flagsIndex))
			{
				ushort flags = (ushort)((updates[flagsIndex].UInt32Value >> i % 4 * 8) & 0xFF);
				ModernVersion.ConvertAuraFlags(flags, i, out data.Flags, out data.ActiveFlags);
			}
		}
		else
		{
			int flagsIndex2 = UNIT_FIELD_AURAFLAGS + i / 8;
			if (updates.ContainsKey(flagsIndex2))
			{
				ushort flags2 = (ushort)((updates[flagsIndex2].UInt32Value >> i % 8 * 4) & 0xF);
				ModernVersion.ConvertAuraFlags(flags2, i, out data.Flags, out data.ActiveFlags);
			}
		}
		int levelsIndex = UNIT_FIELD_AURALEVELS + i / 4;
		if (updates.ContainsKey(levelsIndex))
		{
			data.CastLevel = (ushort)((updates[levelsIndex].UInt32Value >> i % 4 * 8) & 0xFF);
		}
		else
		{
			data.CastLevel = 0;
		}
		int stacksIndex = UNIT_FIELD_AURAAPPLICATIONS + i / 4;
		if (updates.ContainsKey(stacksIndex))
		{
			data.Applications = (byte)((updates[stacksIndex].UInt32Value >> i % 4 * 8) & 0xFF);
		}
		else
		{
			data.Applications = 0;
		}
		if (GameData.StackableAuras.Contains(spellId))
		{
			data.Applications++;
		}
		if (GameData.SpellEffectPoints.TryGetValue(spellId, out var basePoints))
		{
			data.Points = basePoints;
		}
		return data;
	}

	public byte ReadPvPFlags(Dictionary<int, UpdateField> updates)
	{
		byte flags = 0;
		int UNIT_FIELD_FLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_FLAGS);
		if (UNIT_FIELD_FLAGS >= 0 && updates.ContainsKey(UNIT_FIELD_FLAGS) && updates[UNIT_FIELD_FLAGS].UInt32Value.HasAnyFlag(UnitFlags.Pvp))
		{
			flags |= 1;
		}
		int PLAYER_FLAGS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FLAGS);
		if (PLAYER_FLAGS >= 0 && updates.ContainsKey(PLAYER_FLAGS))
		{
			if (updates[PLAYER_FLAGS].UInt32Value.HasAnyFlag(PlayerFlagsLegacy.FreeForAllPvP))
			{
				flags |= 4;
			}
			if (updates[PLAYER_FLAGS].UInt32Value.HasAnyFlag(PlayerFlagsLegacy.Sanctuary))
			{
				flags |= 8;
			}
		}
		return flags;
	}

	public void StoreObjectUpdate(WowGuid128 guid, ObjectType objectType, BitArray updateMaskArray, Dictionary<int, UpdateField> updates, AuraUpdate auraUpdate, PowerUpdate powerUpdate, bool isCreate, ObjectUpdate updateData, BitArray actuallyChangedValuesMaskArray)
	{
		this.StoreObjectUpdateInternal(guid, objectType, updateMaskArray, updates, auraUpdate, powerUpdate, isCreate, updateData);
		this.AfterStoreObjectUpdateHook(guid, objectType, updateMaskArray, updates, auraUpdate, powerUpdate, isCreate, updateData, actuallyChangedValuesMaskArray);
	}

	private void AfterStoreObjectUpdateHook(WowGuid128 guid, ObjectType objectType, BitArray updateMaskArray, Dictionary<int, UpdateField> updates, AuraUpdate auraUpdate, PowerUpdate powerUpdate, bool isCreate, ObjectUpdate updateData, BitArray changedValuesMask)
	{
		if (objectType != ObjectType.Player && objectType != ObjectType.ActivePlayer)
		{
			return;
		}
		int UNIT_FIELD_NATIVEDISPLAYID = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_NATIVEDISPLAYID);
		int UNIT_FIELD_MOUNTDISPLAYID = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MOUNTDISPLAYID);
		int OBJECT_FIELD_SCALE_X = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_SCALE_X);
		if (UNIT_FIELD_NATIVEDISPLAYID < 0 || UNIT_FIELD_MOUNTDISPLAYID < 0 || OBJECT_FIELD_SCALE_X < 0 || (!changedValuesMask.Get(UNIT_FIELD_NATIVEDISPLAYID) && !changedValuesMask.Get(UNIT_FIELD_MOUNTDISPLAYID) && !changedValuesMask.Get(OBJECT_FIELD_SCALE_X)))
		{
			return;
		}
		int nativeDisplayId = this.Session.GameState.GetLegacyFieldValueInt32(guid, UnitField.UNIT_FIELD_DISPLAYID);
		int mountDisplayId = this.Session.GameState.GetLegacyFieldValueInt32(guid, UnitField.UNIT_FIELD_MOUNTDISPLAYID);
		float rawScaleX = this.Session.GameState.GetLegacyFieldValueFloat(guid, ObjectField.OBJECT_FIELD_SCALE_X);
		if (rawScaleX != 0f)
		{
			float regularNativeDisplaySize = GameData.GetUnitCompleteDisplayScale((uint)nativeDisplayId);
			float scale = rawScaleX / regularNativeDisplaySize;
			CreatureDisplayInfo ourDisplayInfo = GameData.GetDisplayInfo((uint)nativeDisplayId);
			CreatureModelCollisionHeight ourModel = GameData.GetModelData(ourDisplayInfo.ModelId);
			float calculatedBaseHeight;
			if (mountDisplayId != 0 && LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				CreatureDisplayInfo mountDisplayInfo = GameData.GetDisplayInfo((uint)mountDisplayId);
				CreatureModelCollisionHeight mountModel = GameData.GetModelData(mountDisplayInfo.ModelId);
				calculatedBaseHeight = mountModel.MountHeight * mountDisplayInfo.DisplayScale + ourModel.Height * ourModel.ModelScale * ourDisplayInfo.DisplayScale * 0.5f;
			}
			else
			{
				calculatedBaseHeight = ourDisplayInfo.DisplayScale * ourModel.Height * ourModel.ModelScale;
			}
			if (calculatedBaseHeight == 0f)
			{
				calculatedBaseHeight = ((mountDisplayId != 0) ? 3.081099f : 2.438083f);
			}
			float heightScale = Math.Max(scale, regularNativeDisplaySize);
			float scaledHeight = heightScale * calculatedBaseHeight;
			float displayScale = regularNativeDisplaySize * scale;
			MoveSetCollisionHeight.UpdateCollisionHeightReason reason = (changedValuesMask.Get(UNIT_FIELD_MOUNTDISPLAYID) ? MoveSetCollisionHeight.UpdateCollisionHeightReason.Mount : MoveSetCollisionHeight.UpdateCollisionHeightReason.Force);
			MoveSetCollisionHeight height = new MoveSetCollisionHeight
			{
				MoverGUID = guid,
				Height = scaledHeight,
				Scale = displayScale,
				Reason = reason,
				MountDisplayID = (uint)mountDisplayId
			};
			this.SendPacketToClient(height, Opcode.SMSG_UPDATE_OBJECT);
		}
	}

	private void StoreObjectUpdateInternal(WowGuid128 guid, ObjectType objectType, BitArray updateMaskArray, Dictionary<int, UpdateField> updates, AuraUpdate auraUpdate, PowerUpdate powerUpdate, bool isCreate, ObjectUpdate updateData)
	{
		int OBJECT_FIELD_GUID = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_GUID);
		if (OBJECT_FIELD_GUID >= 0 && updateMaskArray[OBJECT_FIELD_GUID])
		{
			updateData.ObjectData.Guid = WorldClient.GetGuidValue(updates, ObjectField.OBJECT_FIELD_GUID).To128(this.GetSession().GameState);
		}
		int OBJECT_FIELD_ENTRY = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY);
		if (OBJECT_FIELD_ENTRY >= 0 && updateMaskArray[OBJECT_FIELD_ENTRY])
		{
			updateData.ObjectData.EntryID = updates[OBJECT_FIELD_ENTRY].Int32Value;
		}
		int OBJECT_FIELD_SCALE_X = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_SCALE_X);
		if (OBJECT_FIELD_SCALE_X >= 0 && updateMaskArray[OBJECT_FIELD_SCALE_X])
		{
			updateData.ObjectData.Scale = updates[OBJECT_FIELD_SCALE_X].FloatValue;
		}
		if (objectType == ObjectType.Item || objectType == ObjectType.Container)
		{
			int ITEM_FIELD_OWNER = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_OWNER);
			if (ITEM_FIELD_OWNER >= 0 && updateMaskArray[ITEM_FIELD_OWNER])
			{
				updateData.ItemData.Owner = WorldClient.GetGuidValue(updates, ItemField.ITEM_FIELD_OWNER).To128(this.GetSession().GameState);
			}
			int ITEM_FIELD_CONTAINED = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_CONTAINED);
			if (ITEM_FIELD_CONTAINED >= 0 && updateMaskArray[ITEM_FIELD_CONTAINED])
			{
				updateData.ItemData.ContainedIn = WorldClient.GetGuidValue(updates, ItemField.ITEM_FIELD_CONTAINED).To128(this.GetSession().GameState);
			}
			int ITEM_FIELD_CREATOR = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_CREATOR);
			if (ITEM_FIELD_CREATOR >= 0 && updateMaskArray[ITEM_FIELD_CREATOR])
			{
				updateData.ItemData.Creator = WorldClient.GetGuidValue(updates, ItemField.ITEM_FIELD_CREATOR).To128(this.GetSession().GameState);
			}
			int ITEM_FIELD_GIFTCREATOR = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_GIFTCREATOR);
			if (ITEM_FIELD_GIFTCREATOR >= 0 && updateMaskArray[ITEM_FIELD_GIFTCREATOR])
			{
				updateData.ItemData.GiftCreator = WorldClient.GetGuidValue(updates, ItemField.ITEM_FIELD_GIFTCREATOR).To128(this.GetSession().GameState);
			}
			int ITEM_FIELD_STACK_COUNT = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_STACK_COUNT);
			if (ITEM_FIELD_STACK_COUNT >= 0 && updateMaskArray[ITEM_FIELD_STACK_COUNT])
			{
				updateData.ItemData.StackCount = updates[ITEM_FIELD_STACK_COUNT].UInt32Value;
			}
			int ITEM_FIELD_DURATION = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_DURATION);
			if (ITEM_FIELD_DURATION >= 0 && updateMaskArray[ITEM_FIELD_DURATION])
			{
				updateData.ItemData.Duration = updates[ITEM_FIELD_DURATION].UInt32Value;
			}
			int ITEM_FIELD_SPELL_CHARGES = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_SPELL_CHARGES);
			if (ITEM_FIELD_SPELL_CHARGES >= 0)
			{
				for (int i = 0; i < 5; i++)
				{
					if (updateMaskArray[ITEM_FIELD_SPELL_CHARGES + i])
					{
						updateData.ItemData.SpellCharges[i] = updates[ITEM_FIELD_SPELL_CHARGES + i].Int32Value;
					}
				}
			}
			int ITEM_FIELD_FLAGS = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_FLAGS);
			if (ITEM_FIELD_FLAGS >= 0 && updateMaskArray[ITEM_FIELD_FLAGS])
			{
				updateData.ItemData.Flags = updates[ITEM_FIELD_FLAGS].UInt32Value;
			}
			int ITEM_FIELD_ENCHANTMENT = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_ENCHANTMENT);
			if (ITEM_FIELD_ENCHANTMENT >= 0)
			{
				int sizePerEntry = 3;
				Func<int, ItemEnchantment> ReadEnchantData = delegate(int num2)
				{
					ItemEnchantment itemEnchantment = null;
					int num = ITEM_FIELD_ENCHANTMENT + num2 * sizePerEntry;
					int num3 = num + 1;
					int num4 = num3 + 1;
					if (updateMaskArray[num])
					{
						if (itemEnchantment == null)
						{
							itemEnchantment = new ItemEnchantment();
						}
						itemEnchantment.ID = updates[num].Int32Value;
					}
					if (updateMaskArray[num3])
					{
						if (itemEnchantment == null)
						{
							itemEnchantment = new ItemEnchantment();
						}
						itemEnchantment.Duration = updates[num3].UInt32Value;
					}
					if (updateMaskArray[num4])
					{
						if (itemEnchantment == null)
						{
							itemEnchantment = new ItemEnchantment();
						}
						itemEnchantment.Charges = (ushort)updates[num4].UInt32Value;
					}
					return itemEnchantment;
				};
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Perm] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Perm);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Temp] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Temp);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop0] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Prop0);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop1] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Prop1);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop2] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Prop2);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop3] = ReadEnchantData(HermesProxy.World.Enums.Vanilla.EnchantmentSlot.Prop3);
				}
				else if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Perm] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Perm);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Temp] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Temp);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock1] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Sock1);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock2] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Sock2);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock3] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Sock3);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Bonus] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Bonus);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop0] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Prop0);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop1] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Prop1);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop2] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Prop2);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop3] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Prop3);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop4] = ReadEnchantData(HermesProxy.World.Enums.TBC.EnchantmentSlot.Prop4);
				}
				else
				{
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Perm] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Perm);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Temp] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Temp);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock1] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Sock1);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock2] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Sock2);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock3] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Sock3);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Bonus] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Bonus);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prismatic] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prismatic);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop0] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prop0);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop1] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prop1);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop2] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prop2);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop3] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prop3);
					updateData.ItemData.Enchantment[HermesProxy.World.Enums.Classic.EnchantmentSlot.Prop4] = ReadEnchantData(HermesProxy.World.Enums.WotLK.EnchantmentSlot.Prop4);
				}
				uint?[] gems = new uint?[3];
				for (int i2 = 0; i2 < 3; i2++)
				{
					int slot = HermesProxy.World.Enums.Classic.EnchantmentSlot.Sock1 + i2;
					if (updateData.ItemData.Enchantment[slot] != null && updateData.ItemData.Enchantment[slot].ID.HasValue)
					{
						uint itemId = GameData.GetGemFromEnchantId((uint)updateData.ItemData.Enchantment[slot].ID.Value);
						if (itemId != 0 || updateData.ItemData.Enchantment[slot].ID == 0)
						{
							gems[i2] = itemId;
							updateData.ItemData.HasGemsUpdate = true;
						}
					}
				}
				if (updateData.ItemData.HasGemsUpdate)
				{
					this.GetSession().GameState.SaveGemsForItem(guid, gems);
				}
			}
			int ITEM_FIELD_PROPERTY_SEED = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_PROPERTY_SEED);
			if (ITEM_FIELD_PROPERTY_SEED >= 0 && updateMaskArray[ITEM_FIELD_PROPERTY_SEED])
			{
				updateData.ItemData.PropertySeed = updates[ITEM_FIELD_PROPERTY_SEED].UInt32Value;
			}
			int ITEM_FIELD_RANDOM_PROPERTIES_ID = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_RANDOM_PROPERTIES_ID);
			if (ITEM_FIELD_RANDOM_PROPERTIES_ID >= 0 && updateMaskArray[ITEM_FIELD_RANDOM_PROPERTIES_ID])
			{
				updateData.ItemData.RandomProperty = updates[ITEM_FIELD_RANDOM_PROPERTIES_ID].UInt32Value;
			}
			int ITEM_FIELD_DURABILITY = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_DURABILITY);
			if (ITEM_FIELD_DURABILITY >= 0 && updateMaskArray[ITEM_FIELD_DURABILITY])
			{
				updateData.ItemData.Durability = updates[ITEM_FIELD_DURABILITY].UInt32Value;
			}
			int ITEM_FIELD_MAXDURABILITY = LegacyVersion.GetUpdateField(ItemField.ITEM_FIELD_MAXDURABILITY);
			if (ITEM_FIELD_MAXDURABILITY >= 0 && updateMaskArray[ITEM_FIELD_MAXDURABILITY])
			{
				updateData.ItemData.MaxDurability = updates[ITEM_FIELD_MAXDURABILITY].UInt32Value;
			}
		}
		if (objectType == ObjectType.Container)
		{
			int CONTAINER_FIELD_NUM_SLOTS = LegacyVersion.GetUpdateField(ContainerField.CONTAINER_FIELD_NUM_SLOTS);
			if (CONTAINER_FIELD_NUM_SLOTS >= 0 && updateMaskArray[CONTAINER_FIELD_NUM_SLOTS])
			{
				updateData.ContainerData.NumSlots = updates[CONTAINER_FIELD_NUM_SLOTS].UInt32Value;
			}
			int CONTAINER_FIELD_SLOT_1 = LegacyVersion.GetUpdateField(ContainerField.CONTAINER_FIELD_SLOT_1);
			if (CONTAINER_FIELD_SLOT_1 >= 0)
			{
				for (int i3 = 0; i3 < 36; i3++)
				{
					if (updateMaskArray[CONTAINER_FIELD_SLOT_1 + i3 * 2])
					{
						updateData.ContainerData.Slots[i3] = WorldClient.GetGuidValue(updates, CONTAINER_FIELD_SLOT_1 + i3 * 2).To128(this.GetSession().GameState);
					}
				}
			}
		}
		if (objectType == ObjectType.Unit || objectType == ObjectType.Player || objectType == ObjectType.ActivePlayer)
		{
			int UNIT_FIELD_CHARM = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_CHARM);
			if (UNIT_FIELD_CHARM >= 0 && updateMaskArray[UNIT_FIELD_CHARM])
			{
				updateData.UnitData.Charm = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_CHARM).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_SUMMON = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_SUMMON);
			if (UNIT_FIELD_SUMMON >= 0 && updateMaskArray[UNIT_FIELD_SUMMON])
			{
				updateData.UnitData.Summon = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_SUMMON).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_CHARMEDBY = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_CHARMEDBY);
			if (UNIT_FIELD_CHARMEDBY >= 0 && updateMaskArray[UNIT_FIELD_CHARMEDBY])
			{
				updateData.UnitData.CharmedBy = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_CHARMEDBY).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_SUMMONEDBY = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_SUMMONEDBY);
			if (UNIT_FIELD_SUMMONEDBY >= 0 && updateMaskArray[UNIT_FIELD_SUMMONEDBY])
			{
				updateData.UnitData.SummonedBy = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_SUMMONEDBY).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_CREATEDBY = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_CREATEDBY);
			if (UNIT_FIELD_CREATEDBY >= 0 && updateMaskArray[UNIT_FIELD_CREATEDBY])
			{
				updateData.UnitData.CreatedBy = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_CREATEDBY).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_TARGET = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_TARGET);
			if (UNIT_FIELD_TARGET >= 0 && updateMaskArray[UNIT_FIELD_TARGET])
			{
				updateData.UnitData.Target = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_TARGET).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_CHANNEL_OBJECT = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_CHANNEL_OBJECT);
			if (UNIT_FIELD_CHANNEL_OBJECT >= 0 && updateMaskArray[UNIT_FIELD_CHANNEL_OBJECT])
			{
				updateData.UnitData.ChannelObject = WorldClient.GetGuidValue(updates, UnitField.UNIT_FIELD_CHANNEL_OBJECT).To128(this.GetSession().GameState);
			}
			int UNIT_FIELD_HEALTH = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_HEALTH);
			if (UNIT_FIELD_HEALTH >= 0 && updateMaskArray[UNIT_FIELD_HEALTH])
			{
				updateData.UnitData.Health = updates[UNIT_FIELD_HEALTH].Int32Value;
			}
			int UNIT_FIELD_MAXHEALTH = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXHEALTH);
			if (UNIT_FIELD_MAXHEALTH >= 0 && updateMaskArray[UNIT_FIELD_MAXHEALTH])
			{
				updateData.UnitData.MaxHealth = updates[UNIT_FIELD_MAXHEALTH].Int32Value;
			}
			int UNIT_FIELD_LEVEL = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_LEVEL);
			if (UNIT_FIELD_LEVEL >= 0 && updateMaskArray[UNIT_FIELD_LEVEL])
			{
				updateData.UnitData.Level = updates[UNIT_FIELD_LEVEL].Int32Value;
				// Compute GlyphsEnabled for current player based on level
				if (guid == this.GetSession().GameState.CurrentPlayerGuid)
				{
					int lvl = updates[UNIT_FIELD_LEVEL].Int32Value;
					byte ge = 0;
					if (lvl >= 15) ge |= 0x01 | 0x02;
					if (lvl >= 30) ge |= 0x08;
					if (lvl >= 50) ge |= 0x04;
					if (lvl >= 70) ge |= 0x10;
					if (lvl >= 80) ge |= 0x20;
					this.GetSession().GameState.GlyphsEnabled = ge;
				}
			}
			int UNIT_FIELD_FACTIONTEMPLATE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_FACTIONTEMPLATE);
			if (UNIT_FIELD_FACTIONTEMPLATE >= 0 && updateMaskArray[UNIT_FIELD_FACTIONTEMPLATE])
			{
				updateData.UnitData.FactionTemplate = updates[UNIT_FIELD_FACTIONTEMPLATE].Int32Value;
			}
			int UNIT_FIELD_BYTES_0 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0);
			if (UNIT_FIELD_BYTES_0 >= 0 && updateMaskArray[UNIT_FIELD_BYTES_0])
			{
				updateData.UnitData.RaceId = (byte)(updates[UNIT_FIELD_BYTES_0].UInt32Value & 0xFF);
				updateData.UnitData.ClassId = (byte)((updates[UNIT_FIELD_BYTES_0].UInt32Value >> 8) & 0xFF);
				updateData.UnitData.SexId = (byte)((updates[UNIT_FIELD_BYTES_0].UInt32Value >> 16) & 0xFF);
				updateData.UnitData.DisplayPower = (byte)((updates[UNIT_FIELD_BYTES_0].UInt32Value >> 24) & 0xFF);
				if (guid.GetHighType() == HighGuidType.Pet && updateData.UnitData.DisplayPower == 2)
				{
					this.GetSession().GameState.HunterPetGuids.Add(guid);
				}
				if (objectType == ObjectType.Unit)
				{
					this.GetSession().GameState.StoreCreatureClass(guid.GetEntry(), (Class)updateData.UnitData.ClassId.Value);
				}
				else
				{
					updateData.PlayerData.ArenaFaction = (byte)(GameData.IsAllianceRace((Race)updateData.UnitData.RaceId.Value) ? 1u : 0u);
				}
			}
			int UNIT_FIELD_POWER1 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER1);
			if (UNIT_FIELD_POWER1 >= 0)
			{
				for (int i4 = 0; i4 < LegacyVersion.GetPowersCount(); i4++)
				{
					if (updateMaskArray[UNIT_FIELD_POWER1 + i4])
					{
						if (powerUpdate != null && (guid == this.GetSession().GameState.CurrentPlayerGuid || guid == this.GetSession().GameState.CurrentPetGuid))
						{
							powerUpdate.Powers.Add(new PowerUpdatePower(updates[UNIT_FIELD_POWER1 + i4].Int32Value, (byte)i4));
						}
						sbyte powerSlot;
						if (this.GetSession().GameState.HunterPetGuids.Contains(guid))
						{
							powerSlot = ClassPowerTypes.GetPowerSlotForPet((PowerType)i4);
						}
						else
						{
							Class classId = ((!updateData.UnitData.ClassId.HasValue) ? this.GetSession().GameState.GetUnitClass(guid.To128(this.GetSession().GameState)) : ((Class)updateData.UnitData.ClassId.Value));
							powerSlot = ClassPowerTypes.GetPowerSlotForClass(classId, (PowerType)i4);
						}
						if (powerSlot >= 0)
						{
							updateData.UnitData.Power[powerSlot] = updates[UNIT_FIELD_POWER1 + i4].Int32Value;
						}
					}
				}
			}
			int UNIT_FIELD_MAXPOWER1 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXPOWER1);
			if (UNIT_FIELD_MAXPOWER1 >= 0)
			{
				for (int i5 = 0; i5 < LegacyVersion.GetPowersCount(); i5++)
				{
					if (!updateMaskArray[UNIT_FIELD_MAXPOWER1 + i5])
					{
						continue;
					}
					Class classId2 = ((!updateData.UnitData.ClassId.HasValue) ? this.GetSession().GameState.GetUnitClass(guid.To128(this.GetSession().GameState)) : ((Class)updateData.UnitData.ClassId.Value));
					sbyte powerSlot2 = ((!this.GetSession().GameState.HunterPetGuids.Contains(guid)) ? ClassPowerTypes.GetPowerSlotForClass(classId2, (PowerType)i5) : ClassPowerTypes.GetPowerSlotForPet((PowerType)i5));
					if (powerSlot2 >= 0)
					{
						updateData.UnitData.MaxPower[powerSlot2] = updates[UNIT_FIELD_MAXPOWER1 + i5].Int32Value;
					}
					if (i5 == 3)
					{
						powerSlot2 = ClassPowerTypes.GetPowerSlotForClass(classId2, PowerType.ComboPoints);
						if (powerSlot2 >= 0)
						{
							updateData.UnitData.MaxPower[powerSlot2] = 5;
						}
					}
				}
			}
			int UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER);
		if (UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER >= 0)
		{
			for (int iPR = 0; iPR < 7; iPR++)
			{
				if (updateMaskArray[UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER + iPR])
				{
					updateData.UnitData.ModPowerRegen[iPR] = updates[UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER + iPR].FloatValue;
				}
			}
		}
		int UNIT_VIRTUAL_ITEM_SLOT_DISPLAY = LegacyVersion.GetUpdateField(UnitField.UNIT_VIRTUAL_ITEM_SLOT_DISPLAY);
			if (UNIT_VIRTUAL_ITEM_SLOT_DISPLAY >= 0)
			{
				for (int i6 = 0; i6 < 3; i6++)
				{
					if (updateMaskArray[UNIT_VIRTUAL_ITEM_SLOT_DISPLAY + i6])
					{
						uint itemDisplayId = updates[UNIT_VIRTUAL_ITEM_SLOT_DISPLAY + i6].UInt32Value;
						uint itemId2 = GameData.GetItemIdWithDisplayId(itemDisplayId);
						if (itemId2 != 0)
						{
							VisibleItem visibleItem = new VisibleItem();
							visibleItem.ItemID = (int)itemId2;
							updateData.UnitData.VirtualItems[i6] = visibleItem;
						}
					}
				}
			}
			int UNIT_VIRTUAL_ITEM_SLOT_ID = LegacyVersion.GetUpdateField(UnitField.UNIT_VIRTUAL_ITEM_SLOT_ID);
			if (UNIT_VIRTUAL_ITEM_SLOT_ID >= 0)
			{
				for (int i7 = 0; i7 < 3; i7++)
				{
					if (updateMaskArray[UNIT_VIRTUAL_ITEM_SLOT_ID + i7])
					{
						VisibleItem visibleItem2 = new VisibleItem();
						visibleItem2.ItemID = updates[UNIT_VIRTUAL_ITEM_SLOT_ID + i7].Int32Value;
						updateData.UnitData.VirtualItems[i7] = visibleItem2;
					}
				}
			}
			int UNIT_FIELD_FLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_FLAGS);
			if (UNIT_FIELD_FLAGS >= 0 && updateMaskArray[UNIT_FIELD_FLAGS])
			{
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					UnitFlagsVanilla vanillaFlags = (UnitFlagsVanilla)updates[UNIT_FIELD_FLAGS].UInt32Value;
					updateData.UnitData.Flags = (uint)vanillaFlags.CastFlags<UnitFlags>();
					if (vanillaFlags.HasAnyFlag(UnitFlagsVanilla.PetRename))
					{
						if (!updateData.UnitData.PetFlags.HasValue)
						{
							updateData.UnitData.PetFlags = 1;
						}
						else
						{
							UnitData unitData = updateData.UnitData;
							unitData.PetFlags |= 1;
						}
					}
					if (vanillaFlags.HasAnyFlag(UnitFlagsVanilla.PetAbandon))
					{
						if (!updateData.UnitData.PetFlags.HasValue)
						{
							updateData.UnitData.PetFlags = 2;
						}
						else
						{
							UnitData unitData = updateData.UnitData;
							unitData.PetFlags |= 2;
						}
					}
				}
				else
				{
					updateData.UnitData.Flags = updates[UNIT_FIELD_FLAGS].UInt32Value;
				}
				if (updateData.UnitData.Flags.HasAnyFlag(UnitFlags.ServerControlled) && isCreate && guid == this.GetSession().GameState.CurrentPlayerGuid && updateData.CreateData.MoveSpline == null)
				{
					UnitData unitData = updateData.UnitData;
					unitData.Flags &= 4294967294u;
				}
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056) && !updateData.UnitData.PvpFlags.HasValue)
				{
					updateData.UnitData.PvpFlags = this.ReadPvPFlags(updates);
				}
			}
			int UNIT_FIELD_FLAGS_2 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_FLAGS_2);
			if (UNIT_FIELD_FLAGS_2 >= 0 && updateMaskArray[UNIT_FIELD_FLAGS_2])
			{
				updateData.UnitData.Flags2 = updates[UNIT_FIELD_FLAGS_2].UInt32Value;
			}
			int UNIT_FIELD_AURASTATE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURASTATE);
			if (UNIT_FIELD_AURASTATE >= 0 && updateMaskArray[UNIT_FIELD_AURASTATE])
			{
				updateData.UnitData.AuraState = updates[UNIT_FIELD_AURASTATE].UInt32Value;
			}
			int UNIT_FIELD_BASEATTACKTIME = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BASEATTACKTIME);
			if (UNIT_FIELD_BASEATTACKTIME >= 0)
			{
				for (int i8 = 0; i8 < 2; i8++)
				{
					if (updateMaskArray[UNIT_FIELD_BASEATTACKTIME + i8])
					{
						updateData.UnitData.AttackRoundBaseTime[i8] = updates[UNIT_FIELD_BASEATTACKTIME + i8].UInt32Value;
					}
				}
			}
			int UNIT_FIELD_RANGEDATTACKTIME = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RANGEDATTACKTIME);
			if (UNIT_FIELD_RANGEDATTACKTIME >= 0 && updateMaskArray[UNIT_FIELD_RANGEDATTACKTIME])
			{
				updateData.UnitData.RangedAttackRoundBaseTime = updates[UNIT_FIELD_RANGEDATTACKTIME].UInt32Value;
				Log.Print(LogType.Debug, $"[UnitField] RangedAttackRoundBaseTime = {updates[UNIT_FIELD_RANGEDATTACKTIME].UInt32Value}", "HandleUpdateObject", "");
			}
			int UNIT_FIELD_BOUNDINGRADIUS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BOUNDINGRADIUS);
			if (UNIT_FIELD_BOUNDINGRADIUS >= 0 && updateMaskArray[UNIT_FIELD_BOUNDINGRADIUS])
			{
				updateData.UnitData.BoundingRadius = updates[UNIT_FIELD_BOUNDINGRADIUS].FloatValue;
			}
			int UNIT_FIELD_COMBATREACH = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_COMBATREACH);
			if (UNIT_FIELD_COMBATREACH >= 0 && updateMaskArray[UNIT_FIELD_COMBATREACH])
			{
				updateData.UnitData.CombatReach = updates[UNIT_FIELD_COMBATREACH].FloatValue;
			}
			int UNIT_FIELD_DISPLAYID = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_DISPLAYID);
			if (UNIT_FIELD_DISPLAYID >= 0 && updateMaskArray[UNIT_FIELD_DISPLAYID])
			{
				updateData.UnitData.DisplayID = updates[UNIT_FIELD_DISPLAYID].Int32Value;
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					updateData.UnitData.DisplayScale = 1f / GameData.GetUnitCompleteDisplayScale((uint)updateData.UnitData.DisplayID.Value);
				}
			}
			int UNIT_FIELD_NATIVEDISPLAYID = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_NATIVEDISPLAYID);
			if (UNIT_FIELD_NATIVEDISPLAYID >= 0 && updateMaskArray[UNIT_FIELD_NATIVEDISPLAYID])
			{
				updateData.UnitData.NativeDisplayID = updates[UNIT_FIELD_NATIVEDISPLAYID].Int32Value;
			}
			int UNIT_FIELD_MOUNTDISPLAYID = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MOUNTDISPLAYID);
			if (UNIT_FIELD_MOUNTDISPLAYID >= 0 && updateMaskArray[UNIT_FIELD_MOUNTDISPLAYID])
			{
				updateData.UnitData.MountDisplayID = updates[UNIT_FIELD_MOUNTDISPLAYID].Int32Value;
			}
			int UNIT_FIELD_MINDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MINDAMAGE);
			if (UNIT_FIELD_MINDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MINDAMAGE])
			{
				updateData.UnitData.MinDamage = updates[UNIT_FIELD_MINDAMAGE].FloatValue;
			}
			int UNIT_FIELD_MAXDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXDAMAGE);
			if (UNIT_FIELD_MAXDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MAXDAMAGE])
			{
				updateData.UnitData.MaxDamage = updates[UNIT_FIELD_MAXDAMAGE].FloatValue;
			}
			int UNIT_FIELD_MINOFFHANDDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MINOFFHANDDAMAGE);
			if (UNIT_FIELD_MINOFFHANDDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MINOFFHANDDAMAGE])
			{
				updateData.UnitData.MinOffHandDamage = updates[UNIT_FIELD_MINOFFHANDDAMAGE].FloatValue;
			}
			int UNIT_FIELD_MAXOFFHANDDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXOFFHANDDAMAGE);
			if (UNIT_FIELD_MAXOFFHANDDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MAXOFFHANDDAMAGE])
			{
				updateData.UnitData.MaxOffHandDamage = updates[UNIT_FIELD_MAXOFFHANDDAMAGE].FloatValue;
			}
			int UNIT_FIELD_BYTES_1 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_1);
			if (UNIT_FIELD_BYTES_1 >= 0 && updateMaskArray[UNIT_FIELD_BYTES_1])
			{
				updateData.UnitData.StandState = (byte)(updates[UNIT_FIELD_BYTES_1].UInt32Value & 0xFF);
				byte petLoyaltyIndex = (byte)((updates[UNIT_FIELD_BYTES_1].UInt32Value >> 8) & 0xFF);
				if (petLoyaltyIndex != 238)
				{
					updateData.UnitData.PetLoyaltyIndex = petLoyaltyIndex;
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
				{
					updateData.UnitData.VisFlags = (byte)((updates[UNIT_FIELD_BYTES_1].UInt32Value >> 16) & 0xFF);
					updateData.UnitData.AnimTier = (byte)((updates[UNIT_FIELD_BYTES_1].UInt32Value >> 24) & 0xFF);
				}
				else
				{
					updateData.UnitData.ShapeshiftForm = (byte)((updates[UNIT_FIELD_BYTES_1].UInt32Value >> 16) & 0xFF);
					updateData.UnitData.VisFlags = (byte)((updates[UNIT_FIELD_BYTES_1].UInt32Value >> 24) & 0xFF);
				}
			}
			int UNIT_FIELD_PETNUMBER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_PETNUMBER);
			if (UNIT_FIELD_PETNUMBER >= 0 && updateMaskArray[UNIT_FIELD_PETNUMBER])
			{
				updateData.UnitData.PetNumber = updates[UNIT_FIELD_PETNUMBER].UInt32Value;
			}
			int UNIT_FIELD_PET_NAME_TIMESTAMP = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_PET_NAME_TIMESTAMP);
			if (UNIT_FIELD_PET_NAME_TIMESTAMP >= 0 && updateMaskArray[UNIT_FIELD_PET_NAME_TIMESTAMP])
			{
				updateData.UnitData.PetNameTimestamp = updates[UNIT_FIELD_PET_NAME_TIMESTAMP].UInt32Value;
			}
			int UNIT_FIELD_PETEXPERIENCE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_PETEXPERIENCE);
			if (UNIT_FIELD_PETEXPERIENCE >= 0 && updateMaskArray[UNIT_FIELD_PETEXPERIENCE])
			{
				updateData.UnitData.PetExperience = updates[UNIT_FIELD_PETEXPERIENCE].UInt32Value;
			}
			int UNIT_FIELD_PETNEXTLEVELEXP = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_PETNEXTLEVELEXP);
			if (UNIT_FIELD_PETNEXTLEVELEXP >= 0 && updateMaskArray[UNIT_FIELD_PETNEXTLEVELEXP])
			{
				updateData.UnitData.PetNextLevelExperience = updates[UNIT_FIELD_PETNEXTLEVELEXP].UInt32Value;
			}
			int UNIT_DYNAMIC_FLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_DYNAMIC_FLAGS);
			if (UNIT_DYNAMIC_FLAGS >= 0 && updateMaskArray[UNIT_DYNAMIC_FLAGS])
			{
				UnitDynamicFlagsLegacy flags = (UnitDynamicFlagsLegacy)updates[UNIT_DYNAMIC_FLAGS].UInt32Value;
				if (flags.HasFlag(UnitDynamicFlagsLegacy.Tapped) && flags.HasFlag(UnitDynamicFlagsLegacy.TappedByPlayer))
				{
					flags = (UnitDynamicFlagsLegacy)((uint)flags & 0xFFFFFFF3u);
				}
				updateData.ObjectData.DynamicFlags = (uint)flags.CastFlags<UnitDynamicFlagsModern>();
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					if (!updateData.UnitData.Flags2.HasValue)
					{
						updateData.UnitData.Flags2 = 2048u;
					}
					if (flags.HasAnyFlag(UnitDynamicFlagsLegacy.AppearDead))
					{
						UnitData unitData = updateData.UnitData;
						unitData.Flags2 |= 1u;
					}
				}
			}
			int UNIT_CHANNEL_SPELL = LegacyVersion.GetUpdateField(UnitField.UNIT_CHANNEL_SPELL);
			if (UNIT_CHANNEL_SPELL >= 0 && updateMaskArray[UNIT_CHANNEL_SPELL])
			{
				int channelSpellId = updates[UNIT_CHANNEL_SPELL].Int32Value;
				if (channelSpellId == 0)
				{
					this.GetSession().GameState.CurrentChanneledSpellId = 0;
					// Don't write ChannelData with SpellID=0 — SMSG_SPELL_CHANNEL_UPDATE
					// handles channel end. Writing it causes the 3.4.3 client to get stuck.
				}
				else
				{
					// Write ChannelData for active channels — the client needs ChannelObject
					// (bobber GUID) to identify the fishing bobber for interaction.
					UnitChannel channel = new UnitChannel();
					channel.SpellID = channelSpellId;
					channel.SpellXSpellVisualID = (int)GameData.GetSpellVisual((uint)channelSpellId);
					updateData.UnitData.ChannelData = channel;
				}
			}
			int UNIT_MOD_CAST_SPEED = LegacyVersion.GetUpdateField(UnitField.UNIT_MOD_CAST_SPEED);
			if (UNIT_MOD_CAST_SPEED >= 0 && updateMaskArray[UNIT_MOD_CAST_SPEED])
			{
				updateData.UnitData.ModCastSpeed = updates[UNIT_MOD_CAST_SPEED].FloatValue;
			}
			int UNIT_CREATED_BY_SPELL = LegacyVersion.GetUpdateField(UnitField.UNIT_CREATED_BY_SPELL);
			if (UNIT_CREATED_BY_SPELL >= 0 && updateMaskArray[UNIT_CREATED_BY_SPELL])
			{
				updateData.UnitData.CreatedBySpell = updates[UNIT_CREATED_BY_SPELL].Int32Value;
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) && isCreate && updateData.UnitData.CreatedBy == this.GetSession().GameState.CurrentPlayerGuid)
				{
					int totemSlot = GameData.GetTotemSlotForSpell((uint)updateData.UnitData.CreatedBySpell.Value);
					if (totemSlot >= 0)
					{
						TotemCreated totem = new TotemCreated();
						totem.Slot = (byte)totemSlot;
						totem.Totem = guid;
						totem.Duration = 120000u;
						totem.SpellId = (uint)updateData.UnitData.CreatedBySpell.Value;
						totem.CannotDismiss = true;
						this.SendPacketToClient(totem);
					}
				}
			}
			int UNIT_NPC_FLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_NPC_FLAGS);
			if (UNIT_NPC_FLAGS >= 0 && updateMaskArray[UNIT_NPC_FLAGS])
			{
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					NPCFlagsVanilla vanillaFlags2 = (NPCFlagsVanilla)updates[UNIT_NPC_FLAGS].UInt32Value;
					updateData.UnitData.NpcFlags[0] = (uint)vanillaFlags2.CastFlags<NPCFlags>();
				}
				else
				{
					updateData.UnitData.NpcFlags[0] = updates[UNIT_NPC_FLAGS].UInt32Value;
				}
			}
			int UNIT_NPC_EMOTESTATE = LegacyVersion.GetUpdateField(UnitField.UNIT_NPC_EMOTESTATE);
			if (UNIT_NPC_EMOTESTATE >= 0 && updateMaskArray[UNIT_NPC_EMOTESTATE])
			{
				updateData.UnitData.EmoteState = updates[UNIT_NPC_EMOTESTATE].Int32Value;
			}
			int UNIT_TRAINING_POINTS = LegacyVersion.GetUpdateField(UnitField.UNIT_TRAINING_POINTS);
			if (UNIT_TRAINING_POINTS >= 0 && updateMaskArray[UNIT_TRAINING_POINTS])
			{
				updateData.UnitData.TrainingPointsUsed = (ushort)(updates[UNIT_TRAINING_POINTS].UInt32Value & 0xFFFF);
				updateData.UnitData.TrainingPointsTotal = (ushort)((updates[UNIT_TRAINING_POINTS].UInt32Value >> 16) & 0xFFFF);
			}
			int UNIT_FIELD_STAT0 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_STAT0);
			if (UNIT_FIELD_STAT0 >= 0)
			{
				for (int i9 = 0; i9 < 5; i9++)
				{
					if (updateMaskArray[UNIT_FIELD_STAT0 + i9])
					{
						updateData.UnitData.Stats[i9] = updates[UNIT_FIELD_STAT0 + i9].Int32Value;
					}
				}
			}
			int UNIT_FIELD_POSSTAT0 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POSSTAT0);
			if (UNIT_FIELD_POSSTAT0 >= 0)
			{
				for (int i10 = 0; i10 < 5; i10++)
				{
					if (updateMaskArray[UNIT_FIELD_POSSTAT0 + i10])
					{
						updateData.UnitData.StatPosBuff[i10] = updates[UNIT_FIELD_POSSTAT0 + i10].Int32Value;
					}
				}
			}
			int UNIT_FIELD_NEGSTAT0 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_NEGSTAT0);
			if (UNIT_FIELD_NEGSTAT0 >= 0)
			{
				for (int i11 = 0; i11 < 5; i11++)
				{
					if (updateMaskArray[UNIT_FIELD_NEGSTAT0 + i11])
					{
						updateData.UnitData.StatNegBuff[i11] = updates[UNIT_FIELD_NEGSTAT0 + i11].Int32Value;
					}
				}
			}
			// 3.3.5a uses individual names (RESISTANCES_ARMOR=99..RESISTANCES_ARCANE=105)
			// not the generic UNIT_FIELD_RESISTANCES. Fall back to _ARMOR as base.
			int UNIT_FIELD_RESISTANCES = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCES);
			if (UNIT_FIELD_RESISTANCES < 0)
				UNIT_FIELD_RESISTANCES = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCES_ARMOR);
			if (UNIT_FIELD_RESISTANCES >= 0)
			{
				for (int i12 = 0; i12 < 7; i12++)
				{
					if (updateMaskArray[UNIT_FIELD_RESISTANCES + i12])
					{
						updateData.UnitData.Resistances[i12] = updates[UNIT_FIELD_RESISTANCES + i12].Int32Value;
					}
				}
			}
			int UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE);
			if (UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE < 0)
				UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE_ARMOR);
			if (UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE >= 0)
			{
				for (int i13 = 0; i13 < 7; i13++)
				{
					if (updateMaskArray[UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE + i13])
					{
						updateData.UnitData.ResistanceBuffModsPositive[i13] = updates[UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE + i13].Int32Value;
					}
				}
			}
			int UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE);
			if (UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE < 0)
				UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE_ARMOR);
			if (UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE >= 0)
			{
				for (int i14 = 0; i14 < 7; i14++)
				{
					if (updateMaskArray[UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE + i14])
					{
						updateData.UnitData.ResistanceBuffModsNegative[i14] = updates[UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE + i14].Int32Value;
					}
				}
			}
			int UNIT_FIELD_BASE_MANA = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BASE_MANA);
			if (UNIT_FIELD_BASE_MANA >= 0 && updateMaskArray[UNIT_FIELD_BASE_MANA])
			{
				updateData.UnitData.BaseMana = updates[UNIT_FIELD_BASE_MANA].Int32Value;
			}
			int UNIT_FIELD_BASE_HEALTH = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BASE_HEALTH);
			if (UNIT_FIELD_BASE_HEALTH >= 0 && updateMaskArray[UNIT_FIELD_BASE_HEALTH])
			{
				updateData.UnitData.BaseHealth = updates[UNIT_FIELD_BASE_HEALTH].Int32Value;
			}
			int UNIT_FIELD_BYTES_2 = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_2);
			if (UNIT_FIELD_BYTES_2 >= 0 && updateMaskArray[UNIT_FIELD_BYTES_2])
			{
				updateData.UnitData.SheatheState = (byte)(updates[UNIT_FIELD_BYTES_2].UInt32Value & 0xFF);
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
				{
					updateData.UnitData.PvpFlags = (byte)((updates[UNIT_FIELD_BYTES_2].UInt32Value >> 8) & 0xFF);
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					updateData.UnitData.PetFlags = (byte)((updates[UNIT_FIELD_BYTES_2].UInt32Value >> 16) & 0xFF);
				}
				if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_4_0_8089))
				{
					updateData.UnitData.ShapeshiftForm = (byte)((updates[UNIT_FIELD_BYTES_2].UInt32Value >> 24) & 0xFF);
				}
			}
			int UNIT_FIELD_ATTACK_POWER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_ATTACK_POWER);
			if (UNIT_FIELD_ATTACK_POWER >= 0 && updateMaskArray[UNIT_FIELD_ATTACK_POWER])
			{
				updateData.UnitData.AttackPower = updates[UNIT_FIELD_ATTACK_POWER].Int32Value;
			}
			int UNIT_FIELD_ATTACK_POWER_MODS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_ATTACK_POWER_MODS);
			if (UNIT_FIELD_ATTACK_POWER_MODS >= 0 && updateMaskArray[UNIT_FIELD_ATTACK_POWER_MODS])
			{
				updateData.UnitData.AttackPowerModNeg = updates[UNIT_FIELD_ATTACK_POWER_MODS].Int32Value & 0xFFFF;
				updateData.UnitData.AttackPowerModPos = (updates[UNIT_FIELD_ATTACK_POWER_MODS].Int32Value >> 16) & 0xFFFF;
			}
			int UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER);
			if (UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER >= 0 && updateMaskArray[UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER])
			{
				updateData.UnitData.AttackPowerMultiplier = updates[UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER].FloatValue;
			}
			int UNIT_FIELD_MINRANGEDDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MINRANGEDDAMAGE);
			if (UNIT_FIELD_MINRANGEDDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MINRANGEDDAMAGE])
			{
				updateData.UnitData.MinRangedDamage = updates[UNIT_FIELD_MINRANGEDDAMAGE].FloatValue;
			}
			int UNIT_FIELD_MAXRANGEDDAMAGE = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXRANGEDDAMAGE);
			if (UNIT_FIELD_MAXRANGEDDAMAGE >= 0 && updateMaskArray[UNIT_FIELD_MAXRANGEDDAMAGE])
			{
				updateData.UnitData.MaxRangedDamage = updates[UNIT_FIELD_MAXRANGEDDAMAGE].FloatValue;
			}
			int UNIT_FIELD_POWER_COST_MODIFIER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER_COST_MODIFIER);
			if (UNIT_FIELD_POWER_COST_MODIFIER >= 0)
			{
				for (int i15 = 0; i15 < 7; i15++)
				{
					if (updateMaskArray[UNIT_FIELD_POWER_COST_MODIFIER + i15])
					{
						updateData.UnitData.PowerCostModifier[i15] = updates[UNIT_FIELD_POWER_COST_MODIFIER + i15].Int32Value;
					}
				}
			}
			int UNIT_FIELD_POWER_COST_MULTIPLIER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER_COST_MULTIPLIER);
			if (UNIT_FIELD_POWER_COST_MULTIPLIER >= 0)
			{
				for (int i16 = 0; i16 < 7; i16++)
				{
					if (updateMaskArray[UNIT_FIELD_POWER_COST_MULTIPLIER + i16])
					{
						updateData.UnitData.PowerCostMultiplier[i16] = updates[UNIT_FIELD_POWER_COST_MULTIPLIER + i16].FloatValue;
					}
				}
			}
			int UNIT_FIELD_MAXHEALTHMODIFIER = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_MAXHEALTHMODIFIER);
			if (UNIT_FIELD_MAXHEALTHMODIFIER >= 0 && updateMaskArray[UNIT_FIELD_MAXHEALTHMODIFIER])
			{
				updateData.UnitData.MaxHealthModifier = updates[UNIT_FIELD_MAXHEALTHMODIFIER].FloatValue;
			}
			int UNIT_FIELD_AURA = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURA);
			int UNIT_FIELD_AURAFLAGS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURAFLAGS);
			int UNIT_FIELD_AURALEVELS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURALEVELS);
			int UNIT_FIELD_AURAAPPLICATIONS = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURAAPPLICATIONS);
			if (UNIT_FIELD_AURA > 0 && UNIT_FIELD_AURAFLAGS > 0 && UNIT_FIELD_AURALEVELS > 0 && UNIT_FIELD_AURAAPPLICATIONS > 0)
			{
				int aurasCount = LegacyVersion.GetAuraSlotsCount();
				for (byte i17 = 0; i17 < aurasCount; i17++)
				{
					if (!updateMaskArray[UNIT_FIELD_AURA + i17] && !updateMaskArray[UNIT_FIELD_AURALEVELS + i17 / 4] && !updateMaskArray[UNIT_FIELD_AURAAPPLICATIONS + i17 / 4])
					{
						continue;
					}
					AuraInfo aura = new AuraInfo
					{
						Slot = i17,
						AuraData = this.ReadAuraSlot(i17, guid, updates)
					};
					if (aura.AuraData != null)
					{
						this.GetSession().GameState.GetAuraDuration(guid, i17, out var durationLeft, out var durationFull);
						if (durationLeft > 0 && durationFull > 0)
						{
							AuraDataInfo auraData = aura.AuraData;
							auraData.Flags |= AuraFlagsModern.Duration;
							aura.AuraData.Duration = durationFull;
							aura.AuraData.Remaining = durationLeft;
						}
						aura.AuraData.CastUnit = this.GetSession().GameState.GetAuraCaster(guid, i17, aura.AuraData.SpellID);
					}
					else if (updateMaskArray[UNIT_FIELD_AURA + i17])
					{
						this.GetSession().GameState.ClearAuraDuration(guid, i17);
						this.GetSession().GameState.ClearAuraCaster(guid, i17);
					}
					if (aura.AuraData != null || updateMaskArray[UNIT_FIELD_AURA + i17])
					{
						auraUpdate.Auras.Add(aura);
					}
				}
			}
		}
		if (objectType == ObjectType.Player || objectType == ObjectType.ActivePlayer)
		{
			int PLAYER_DUEL_ARBITER = LegacyVersion.GetUpdateField(PlayerField.PLAYER_DUEL_ARBITER);
			if (PLAYER_DUEL_ARBITER >= 0 && updateMaskArray[PLAYER_DUEL_ARBITER])
			{
				updateData.PlayerData.DuelArbiter = WorldClient.GetGuidValue(updates, PlayerField.PLAYER_DUEL_ARBITER).To128(this.GetSession().GameState);
			}
			int PLAYER_FLAGS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FLAGS);
			if (PLAYER_FLAGS >= 0 && updateMaskArray[PLAYER_FLAGS])
			{
				PlayerFlagsLegacy legacyFlags = (PlayerFlagsLegacy)updates[PLAYER_FLAGS].UInt32Value;
				PlayerFlags flags2 = legacyFlags.CastFlags<PlayerFlags>();
				if (updateData.Guid == this.GetSession().GameState.CurrentPlayerGuid)
				{
					this.GetSession().GameState.CurrentPlayerStorage.Settings.PatchFlags(ref flags2);
					// Detect ghost→alive transition for DestroyObject+CreateObject2 revive fix
					bool isGhostNow = legacyFlags.HasAnyFlag(PlayerFlagsLegacy.Ghost);
					if (this.GetSession().GameState.IsPlayerGhost && !isGhostNow)
					{
						Log.Print(LogType.Debug, "[DeathRevive] Ghost→Alive transition detected, will recreate player object", "StoreObjectUpdate", "");
						this.GetSession().GameState.NeedPlayerRecreate = true;
					}
					this.GetSession().GameState.IsPlayerGhost = isGhostNow;
				}
				// Ghost flag now sent through — DestroyObject+CreateObject2 on revive clears the grey overlay
				updateData.PlayerData.PlayerFlags = (uint)flags2;
				if (!updateData.PlayerData.PlayerFlagsEx.HasValue)
				{
					updateData.PlayerData.PlayerFlagsEx = 0u;
				}
				// 3.4.3 uses ActivePlayerData.LocalFlags for death UI (RELEASE_TIMER=0x08)
				// 3.3.5a doesn't have this — only inject when actually ghost
				// Setting LocalFlags=0 on every update floods ActivePlayerData and may break bag updates
				if (updateData.Guid == this.GetSession().GameState.CurrentPlayerGuid
					&& legacyFlags.HasAnyFlag(PlayerFlagsLegacy.Ghost))
				{
					updateData.ActivePlayerData.LocalFlags = 0x08u; // PLAYER_LOCAL_FLAG_RELEASE_TIMER
				}
				if (legacyFlags.HasAnyFlag(PlayerFlagsLegacy.HideHelm))
				{
					PlayerData playerData = updateData.PlayerData;
					playerData.PlayerFlagsEx |= 128u;
				}
				if (legacyFlags.HasAnyFlag(PlayerFlagsLegacy.HideCloak))
				{
					PlayerData playerData = updateData.PlayerData;
					playerData.PlayerFlagsEx |= 256u;
				}
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056) && !updateData.UnitData.PvpFlags.HasValue)
				{
					updateData.UnitData.PvpFlags = this.ReadPvPFlags(updates);
				}
			}
			else if (updateData.Guid == this.GetSession().GameState.CurrentPlayerGuid && this.GetSession().GameState.CurrentPlayerStorage.Settings.NeedToForcePatchFlags)
			{
				PlayerFlags flags3 = this.GetSession().GameState.CurrentPlayerStorage.Settings.CreateNewFlags();
				updateData.PlayerData.PlayerFlags = (uint)flags3;
			}
			int PLAYER_GUILDID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_GUILDID);
			if (PLAYER_GUILDID >= 0 && updateMaskArray[PLAYER_GUILDID])
			{
				this.GetSession().GameState.StorePlayerGuildId(guid, updates[PLAYER_GUILDID].UInt32Value);
				updateData.UnitData.GuildGUID = WowGuid128.Create(HighGuidType703.Guild, updates[PLAYER_GUILDID].UInt32Value);
			}
			int PLAYER_GUILDRANK = LegacyVersion.GetUpdateField(PlayerField.PLAYER_GUILDRANK);
			if (PLAYER_GUILDRANK >= 0 && updateMaskArray[PLAYER_GUILDRANK])
			{
				updateData.PlayerData.GuildLevel = 25;
				updateData.PlayerData.GuildRankID = updates[PLAYER_GUILDRANK].UInt32Value;
			}
			int PLAYER_GUILD_TIMESTAMP = LegacyVersion.GetUpdateField(PlayerField.PLAYER_GUILD_TIMESTAMP);
			if (PLAYER_GUILD_TIMESTAMP >= 0 && updateMaskArray[PLAYER_GUILD_TIMESTAMP])
			{
				updateData.PlayerData.GuildTimeStamp = updates[PLAYER_GUILD_TIMESTAMP].Int32Value;
			}
			int PLAYER_QUEST_LOG_1_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_QUEST_LOG_1_1);
			if (PLAYER_QUEST_LOG_1_1 >= 0)
			{
				int questsCount = LegacyVersion.GetQuestLogSize();
				for (int i18 = 0; i18 < questsCount; i18++)
				{
					updateData.PlayerData.QuestLog[i18] = this.ReadQuestLogEntry(i18, updateMaskArray, updates);
				}
			}
			int PLAYER_CHOSEN_TITLE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_CHOSEN_TITLE);
			if (PLAYER_CHOSEN_TITLE >= 0 && updateMaskArray[PLAYER_CHOSEN_TITLE])
			{
				updateData.PlayerData.ChosenTitle = updates[PLAYER_CHOSEN_TITLE].Int32Value;
			}
			int PLAYER_VISIBLE_ITEM_1_0 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_0);
			if (PLAYER_VISIBLE_ITEM_1_0 >= 0)
			{
				int offset = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 16 : 12);
				for (int i19 = 0; i19 < 19; i19++)
				{
					int itemIdIndex = PLAYER_VISIBLE_ITEM_1_0 + i19 * offset;
					int enchantIdIndex = PLAYER_VISIBLE_ITEM_1_0 + 1 + i19 * offset;
					if (updateMaskArray[itemIdIndex] || updateMaskArray[enchantIdIndex])
					{
						updateData.PlayerData.VisibleItems[i19] = new VisibleItem();
						if (updates.ContainsKey(itemIdIndex))
						{
							updateData.PlayerData.VisibleItems[i19].ItemID = updates[itemIdIndex].Int32Value;
						}
						if (updates.ContainsKey(enchantIdIndex))
						{
							updateData.PlayerData.VisibleItems[i19].ItemVisual = (ushort)GameData.GetItemEnchantVisual(updates[enchantIdIndex].UInt32Value);
						}
					}
				}
			}
			int PLAYER_VISIBLE_ITEM_1_ENTRYID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID);
			if (PLAYER_VISIBLE_ITEM_1_ENTRYID >= 0)
			{
				int offset2 = 2;
				for (int i20 = 0; i20 < 19; i20++)
				{
					if (updateMaskArray[PLAYER_VISIBLE_ITEM_1_ENTRYID + i20 * offset2])
					{
						updateData.PlayerData.VisibleItems[i20] = new VisibleItem();
						updateData.PlayerData.VisibleItems[i20].ItemID = updates[PLAYER_VISIBLE_ITEM_1_ENTRYID + i20 * offset2].Int32Value;
						if (i20 >= 15 && i20 <= 18)
							Log.Print(LogType.Debug, $"[VisibleItem] Slot {i20} ItemID={updateData.PlayerData.VisibleItems[i20].ItemID}", "HandleUpdateObject", "");
					}
				}
			}
			int PLAYER_FIELD_INV_SLOT_HEAD = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_INV_SLOT_HEAD);
			if (PLAYER_FIELD_INV_SLOT_HEAD >= 0)
			{
				for (int i21 = 0; i21 < 23; i21++)
				{
					if (updateMaskArray[PLAYER_FIELD_INV_SLOT_HEAD + i21 * 2])
					{
						updateData.ActivePlayerData.InvSlots[i21] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_INV_SLOT_HEAD + i21 * 2).To128(this.GetSession().GameState);
						if (i21 >= 15 && i21 <= 18)
							Log.Print(LogType.Debug, $"[InvSlot] Slot {i21} = {updateData.ActivePlayerData.InvSlots[i21]}", "HandleUpdateObject", "");
					}
				}
			}
			int PLAYER_FIELD_PACK_SLOT_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_PACK_SLOT_1);
			if (PLAYER_FIELD_PACK_SLOT_1 >= 0)
			{
				for (int i22 = 0; i22 < 16; i22++)
				{
					if (updateMaskArray[PLAYER_FIELD_PACK_SLOT_1 + i22 * 2])
					{
						updateData.ActivePlayerData.PackSlots[i22] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_PACK_SLOT_1 + i22 * 2).To128(this.GetSession().GameState);
						Log.Print(LogType.Debug, $"[InvUpdate] PackSlot[{i22}] = {updateData.ActivePlayerData.PackSlots[i22]} (modern idx {35 + i22})", "HandleUpdateObject", "");
					}
				}
			}
			int PLAYER_FIELD_BANK_SLOT_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BANK_SLOT_1);
			if (PLAYER_FIELD_BANK_SLOT_1 >= 0)
			{
				int bankSlots = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 28 : 24);
				for (int i23 = 0; i23 < bankSlots; i23++)
				{
					if (updateMaskArray[PLAYER_FIELD_BANK_SLOT_1 + i23 * 2])
					{
						updateData.ActivePlayerData.BankSlots[i23] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_BANK_SLOT_1 + i23 * 2).To128(this.GetSession().GameState);
						Log.Print(LogType.Debug, $"[InvUpdate] BankSlot[{i23}] = {updateData.ActivePlayerData.BankSlots[i23]} (modern idx {59 + i23})", "HandleUpdateObject", "");
					}
				}
			}
			int PLAYER_FIELD_BANKBAG_SLOT_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BANKBAG_SLOT_1);
			if (PLAYER_FIELD_BANKBAG_SLOT_1 >= 0)
			{
				int bankBagSlots = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 7 : 6);
				for (int i24 = 0; i24 < bankBagSlots; i24++)
				{
					if (updateMaskArray[PLAYER_FIELD_BANKBAG_SLOT_1 + i24 * 2])
					{
						updateData.ActivePlayerData.BankBagSlots[i24] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_BANKBAG_SLOT_1 + i24 * 2).To128(this.GetSession().GameState);
					}
				}
			}
			int PLAYER_FIELD_VENDORBUYBACK_SLOT_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_VENDORBUYBACK_SLOT_1);
			if (PLAYER_FIELD_VENDORBUYBACK_SLOT_1 >= 0)
			{
				for (int i25 = 0; i25 < 12; i25++)
				{
					if (updateMaskArray[PLAYER_FIELD_VENDORBUYBACK_SLOT_1 + i25 * 2])
					{
						updateData.ActivePlayerData.BuyBackSlots[i25] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_VENDORBUYBACK_SLOT_1 + i25 * 2).To128(this.GetSession().GameState);
					}
				}
			}
			int PLAYER_FIELD_KEYRING_SLOT_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_KEYRING_SLOT_1);
			if (PLAYER_FIELD_KEYRING_SLOT_1 >= 0)
			{
				for (int i26 = 0; i26 < 32; i26++)
				{
					if (updateMaskArray[PLAYER_FIELD_KEYRING_SLOT_1 + i26 * 2])
					{
						updateData.ActivePlayerData.KeyringSlots[i26] = WorldClient.GetGuidValue(updates, PLAYER_FIELD_KEYRING_SLOT_1 + i26 * 2).To128(this.GetSession().GameState);
					}
				}
			}
			byte? skin = null;
			byte? face = null;
			byte? hairStyle = null;
			byte? hairColor = null;
			byte? facialHair = null;
			int PLAYER_BYTES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES);
			if (PLAYER_BYTES >= 0 && updateMaskArray[PLAYER_BYTES])
			{
				skin = (byte)(updates[PLAYER_BYTES].UInt32Value & 0xFF);
				face = (byte)((updates[PLAYER_BYTES].UInt32Value >> 8) & 0xFF);
				hairStyle = (byte)((updates[PLAYER_BYTES].UInt32Value >> 16) & 0xFF);
				hairColor = (byte)((updates[PLAYER_BYTES].UInt32Value >> 24) & 0xFF);
			}
			RestInfo restInfo = ((isCreate && guid == this.GetSession().GameState.CurrentPlayerGuid) ? new RestInfo() : null);
			if (restInfo != null)
			{
				restInfo.StateID = 2u;
			}
			int PLAYER_BYTES_2 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES_2);
			if (PLAYER_BYTES_2 >= 0 && updateMaskArray[PLAYER_BYTES_2])
			{
				facialHair = (byte)(updates[PLAYER_BYTES_2].UInt32Value & 0xFF);
				updateData.PlayerData.NumBankSlots = (byte)((updates[PLAYER_BYTES_2].UInt32Value >> 16) & 0xFF);
				if (restInfo == null && guid == this.GetSession().GameState.CurrentPlayerGuid)
				{
					restInfo = new RestInfo();
				}
				if (restInfo != null)
				{
					restInfo.StateID = (byte)((updates[PLAYER_BYTES_2].UInt32Value >> 24) & 0xFF);
				}
			}
			if (skin.HasValue && face.HasValue && hairStyle.HasValue && hairColor.HasValue && facialHair.HasValue)
			{
				Race raceId = Race.None;
				Gender sexId = Gender.None;
				if (updateData.UnitData.RaceId.HasValue)
				{
					raceId = (Race)updateData.UnitData.RaceId.Value;
				}
				if (updateData.UnitData.SexId.HasValue)
				{
					sexId = (Gender)updateData.UnitData.SexId.Value;
				}
				if ((raceId == Race.None || sexId == Gender.None) && this.GetSession().GameState.CachedPlayers.TryGetValue(guid.To128(this.GetSession().GameState), out var cache))
				{
					raceId = cache.RaceId;
					sexId = cache.SexId;
				}
				if (raceId != Race.None && sexId != Gender.None)
				{
					Array<ChrCustomizationChoice> customizations = CharacterCustomizations.ConvertLegacyCustomizationsToModern(raceId, sexId, skin.Value, face.Value, hairStyle.Value, hairColor.Value, facialHair.Value);
					for (int i27 = 0; i27 < 5; i27++)
					{
						updateData.PlayerData.Customizations[i27] = customizations[i27];
					}
				}
			}
			int PLAYER_REST_STATE_EXPERIENCE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_REST_STATE_EXPERIENCE);
			if (PLAYER_REST_STATE_EXPERIENCE >= 0 && updateMaskArray[PLAYER_REST_STATE_EXPERIENCE])
			{
				if (restInfo == null && guid == this.GetSession().GameState.CurrentPlayerGuid)
				{
					restInfo = new RestInfo();
				}
				if (restInfo != null)
				{
					restInfo.Threshold = updates[PLAYER_REST_STATE_EXPERIENCE].UInt32Value;
				}
			}
			if (restInfo != null)
			{
				updateData.ActivePlayerData.RestInfo[0] = restInfo;
			}
			int PLAYER_BYTES_3 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES_3);
			if (PLAYER_BYTES_3 >= 0 && updateMaskArray[PLAYER_BYTES_3])
			{
				ushort genderAndInebriation = (ushort)(updates[PLAYER_BYTES_3].UInt32Value & 0xFFFF);
				updateData.PlayerData.NativeSex = (byte)(genderAndInebriation & 1);
				updateData.PlayerData.Inebriation = (byte)(genderAndInebriation & 0xFFFE);
				updateData.PlayerData.PvpTitle = (byte)((updates[PLAYER_BYTES_3].UInt32Value >> 16) & 0xFF);
				updateData.PlayerData.PvPRank = (byte)((updates[PLAYER_BYTES_3].UInt32Value >> 24) & 0xFF);
			}
			int PLAYER_DUEL_TEAM = LegacyVersion.GetUpdateField(PlayerField.PLAYER_DUEL_TEAM);
			if (PLAYER_DUEL_TEAM >= 0 && updateMaskArray[PLAYER_DUEL_TEAM])
			{
				updateData.PlayerData.DuelTeam = updates[PLAYER_DUEL_TEAM].UInt32Value;
			}
			int PLAYER_FARSIGHT = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FARSIGHT);
			if (PLAYER_FARSIGHT >= 0 && updateMaskArray[PLAYER_FARSIGHT])
			{
				updateData.ActivePlayerData.FarsightObject = WorldClient.GetGuidValue(updates, PlayerField.PLAYER_FARSIGHT).To128(this.GetSession().GameState);
			}
			int PLAYER_FIELD_COMBO_TARGET = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_COMBO_TARGET);
			if (PLAYER_FIELD_COMBO_TARGET >= 0 && updateMaskArray[PLAYER_FIELD_COMBO_TARGET])
			{
				updateData.ActivePlayerData.ComboTarget = WorldClient.GetGuidValue(updates, PlayerField.PLAYER_FIELD_COMBO_TARGET).To128(this.GetSession().GameState);
			}
			int PLAYER_FIELD_KNOWN_TITLES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_KNOWN_TITLES);
			if (PLAYER_FIELD_KNOWN_TITLES >= 0)
			{
				int count = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? 3 : 2);
				for (int i28 = 0; i28 < count; i28++)
				{
					if (updateMaskArray[PLAYER_FIELD_KNOWN_TITLES + i28])
					{
						updateData.ActivePlayerData.KnownTitles[i28] = updates[PLAYER_FIELD_KNOWN_TITLES + i28].UInt32Value;
					}
				}
			}
			int PLAYER_XP = LegacyVersion.GetUpdateField(PlayerField.PLAYER_XP);
			if (PLAYER_XP >= 0 && updateMaskArray[PLAYER_XP])
			{
				updateData.ActivePlayerData.XP = updates[PLAYER_XP].Int32Value;
			}
			int PLAYER_NEXT_LEVEL_XP = LegacyVersion.GetUpdateField(PlayerField.PLAYER_NEXT_LEVEL_XP);
			if (PLAYER_NEXT_LEVEL_XP >= 0 && updateMaskArray[PLAYER_NEXT_LEVEL_XP])
			{
				updateData.ActivePlayerData.NextLevelXP = updates[PLAYER_NEXT_LEVEL_XP].Int32Value;
			}
			int PLAYER_SKILL_INFO_1_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_SKILL_INFO_1_1);
			if (PLAYER_SKILL_INFO_1_1 >= 0)
			{
				for (int i29 = 0; i29 < 128; i29++)
				{
					int idIndex = PLAYER_SKILL_INFO_1_1 + i29 * 3;
					if (updateMaskArray[idIndex])
					{
						updateData.ActivePlayerData.Skill.SkillLineID[i29] = (ushort)(updates[idIndex].UInt32Value & 0xFFFF);
						updateData.ActivePlayerData.Skill.SkillStep[i29] = (ushort)((updates[idIndex].UInt32Value >> 16) & 0xFFFF);
					}
					int valueIndex = idIndex + 1;
					if (updateMaskArray[valueIndex])
					{
						updateData.ActivePlayerData.Skill.SkillRank[i29] = (ushort)(updates[valueIndex].UInt32Value & 0xFFFF);
						updateData.ActivePlayerData.Skill.SkillMaxRank[i29] = (ushort)((updates[valueIndex].UInt32Value >> 16) & 0xFFFF);
					}
					int bonusIndex = valueIndex + 1;
					if (updateMaskArray[bonusIndex])
					{
						updateData.ActivePlayerData.Skill.SkillTempBonus[i29] = (short)(updates[bonusIndex].Int32Value & 0xFFFF);
						updateData.ActivePlayerData.Skill.SkillPermBonus[i29] = (ushort)((updates[bonusIndex].UInt32Value >> 16) & 0xFFFF);
					}
				}
			}
			int PLAYER_CHARACTER_POINTS1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_CHARACTER_POINTS1);
			if (PLAYER_CHARACTER_POINTS1 >= 0 && updateMaskArray[PLAYER_CHARACTER_POINTS1])
			{
				updateData.ActivePlayerData.CharacterPoints = updates[PLAYER_CHARACTER_POINTS1].Int32Value;
				// MaxTalentTiers = total talent points (from SMSG_UPDATE_TALENT_DATA)
				int totalTalentPoints = this.GetSession().GameState.TotalTalentPoints;
				if (totalTalentPoints > 0)
					updateData.ActivePlayerData.MaxTalentTiers = totalTalentPoints;
				else
					updateData.ActivePlayerData.MaxTalentTiers = updates[PLAYER_CHARACTER_POINTS1].Int32Value;
			}
			int PLAYER_TRACK_CREATURES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_TRACK_CREATURES);
			if (PLAYER_TRACK_CREATURES >= 0 && updateMaskArray[PLAYER_TRACK_CREATURES])
			{
				updateData.ActivePlayerData.TrackCreatureMask = updates[PLAYER_TRACK_CREATURES].UInt32Value;
			}
			int PLAYER_TRACK_RESOURCES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_TRACK_RESOURCES);
			if (PLAYER_TRACK_RESOURCES >= 0 && updateMaskArray[PLAYER_TRACK_RESOURCES])
			{
				updateData.ActivePlayerData.TrackResourceMask[0] = updates[PLAYER_TRACK_RESOURCES].UInt32Value;
			}
			int PLAYER_BLOCK_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_BLOCK_PERCENTAGE);
			if (PLAYER_BLOCK_PERCENTAGE >= 0 && updateMaskArray[PLAYER_BLOCK_PERCENTAGE])
			{
				updateData.ActivePlayerData.BlockPercentage = updates[PLAYER_BLOCK_PERCENTAGE].FloatValue;
			}
			int PLAYER_DODGE_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_DODGE_PERCENTAGE);
			if (PLAYER_DODGE_PERCENTAGE >= 0 && updateMaskArray[PLAYER_DODGE_PERCENTAGE])
			{
				updateData.ActivePlayerData.DodgePercentage = updates[PLAYER_DODGE_PERCENTAGE].FloatValue;
			}
			int PLAYER_PARRY_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_PARRY_PERCENTAGE);
			if (PLAYER_PARRY_PERCENTAGE >= 0 && updateMaskArray[PLAYER_PARRY_PERCENTAGE])
			{
				updateData.ActivePlayerData.ParryPercentage = updates[PLAYER_PARRY_PERCENTAGE].FloatValue;
			}
			int PLAYER_EXPERTISE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_EXPERTISE);
			if (PLAYER_EXPERTISE >= 0 && updateMaskArray[PLAYER_EXPERTISE])
			{
				updateData.ActivePlayerData.MainhandExpertise = updates[PLAYER_EXPERTISE].Int32Value;
			}
			int PLAYER_OFFHAND_EXPERTISE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_OFFHAND_EXPERTISE);
			if (PLAYER_OFFHAND_EXPERTISE >= 0 && updateMaskArray[PLAYER_OFFHAND_EXPERTISE])
			{
				updateData.ActivePlayerData.OffhandExpertise = updates[PLAYER_OFFHAND_EXPERTISE].Int32Value;
			}
			int PLAYER_CRIT_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_CRIT_PERCENTAGE);
			if (PLAYER_CRIT_PERCENTAGE >= 0 && updateMaskArray[PLAYER_CRIT_PERCENTAGE])
			{
				updateData.ActivePlayerData.CritPercentage = updates[PLAYER_CRIT_PERCENTAGE].FloatValue;
			}
			int PLAYER_RANGED_CRIT_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_RANGED_CRIT_PERCENTAGE);
			if (PLAYER_RANGED_CRIT_PERCENTAGE >= 0 && updateMaskArray[PLAYER_RANGED_CRIT_PERCENTAGE])
			{
				updateData.ActivePlayerData.RangedCritPercentage = updates[PLAYER_RANGED_CRIT_PERCENTAGE].FloatValue;
			}
			int PLAYER_OFFHAND_CRIT_PERCENTAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_OFFHAND_CRIT_PERCENTAGE);
			if (PLAYER_OFFHAND_CRIT_PERCENTAGE >= 0 && updateMaskArray[PLAYER_OFFHAND_CRIT_PERCENTAGE])
			{
				updateData.ActivePlayerData.OffhandCritPercentage = updates[PLAYER_OFFHAND_CRIT_PERCENTAGE].FloatValue;
			}
			int PLAYER_SPELL_CRIT_PERCENTAGE1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_SPELL_CRIT_PERCENTAGE1);
			if (PLAYER_SPELL_CRIT_PERCENTAGE1 >= 0)
			{
				for (int i30 = 0; i30 < 7; i30++)
				{
					if (updateMaskArray[PLAYER_SPELL_CRIT_PERCENTAGE1 + i30])
					{
						updateData.ActivePlayerData.SpellCritPercentage[i30] = updates[PLAYER_SPELL_CRIT_PERCENTAGE1 + i30].FloatValue;
					}
				}
			}
			int PLAYER_SHIELD_BLOCK = LegacyVersion.GetUpdateField(PlayerField.PLAYER_SHIELD_BLOCK);
			if (PLAYER_SHIELD_BLOCK >= 0 && updateMaskArray[PLAYER_SHIELD_BLOCK])
			{
				updateData.ActivePlayerData.ShieldBlock = updates[PLAYER_SHIELD_BLOCK].Int32Value;
			}
			int PLAYER_EXPLORED_ZONES_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_EXPLORED_ZONES_1);
			if (PLAYER_EXPLORED_ZONES_1 >= 0)
			{
				int maxZones = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 128 : 64);
				for (int i31 = 0; i31 < maxZones; i31++)
				{
					if (updateMaskArray[PLAYER_EXPLORED_ZONES_1 + i31])
					{
						if ((i31 & 1) != 0)
						{
							ulong oldValue = (updateData.ActivePlayerData.ExploredZones[i31 / 2].HasValue ? updateData.ActivePlayerData.ExploredZones[i31 / 2].Value : 0);
							updateData.ActivePlayerData.ExploredZones[i31 / 2] = oldValue | ((ulong)updates[PLAYER_EXPLORED_ZONES_1 + i31].UInt32Value << 32);
						}
						else
						{
							updateData.ActivePlayerData.ExploredZones[i31 / 2] = updates[PLAYER_EXPLORED_ZONES_1 + i31].UInt32Value;
						}
					}
				}
			}
			int PLAYER_FIELD_COINAGE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_COINAGE);
			if (PLAYER_FIELD_COINAGE >= 0 && updateMaskArray[PLAYER_FIELD_COINAGE])
			{
				updateData.ActivePlayerData.Coinage = updates[PLAYER_FIELD_COINAGE].UInt32Value;
			}
			int PLAYER_FIELD_POSSTAT0 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_POSSTAT0);
			if (PLAYER_FIELD_POSSTAT0 >= 0)
			{
				for (int i32 = 0; i32 < 5; i32++)
				{
					if (updateMaskArray[PLAYER_FIELD_POSSTAT0 + i32])
					{
						updateData.UnitData.StatPosBuff[i32] = updates[PLAYER_FIELD_POSSTAT0 + i32].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_NEGSTAT0 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_NEGSTAT0);
			if (PLAYER_FIELD_NEGSTAT0 >= 0)
			{
				for (int i33 = 0; i33 < 5; i33++)
				{
					if (updateMaskArray[PLAYER_FIELD_NEGSTAT0 + i33])
					{
						updateData.UnitData.StatNegBuff[i33] = updates[PLAYER_FIELD_NEGSTAT0 + i33].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_RESISTANCEBUFFMODSPOSITIVE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_RESISTANCEBUFFMODSPOSITIVE);
			if (PLAYER_FIELD_RESISTANCEBUFFMODSPOSITIVE >= 0)
			{
				for (int i34 = 0; i34 < 7; i34++)
				{
					if (updateMaskArray[PLAYER_FIELD_RESISTANCEBUFFMODSPOSITIVE + i34])
					{
						updateData.UnitData.ResistanceBuffModsPositive[i34] = updates[PLAYER_FIELD_RESISTANCEBUFFMODSPOSITIVE + i34].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_RESISTANCEBUFFMODSNEGATIVE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_RESISTANCEBUFFMODSNEGATIVE);
			if (PLAYER_FIELD_RESISTANCEBUFFMODSNEGATIVE >= 0)
			{
				for (int i35 = 0; i35 < 7; i35++)
				{
					if (updateMaskArray[PLAYER_FIELD_RESISTANCEBUFFMODSNEGATIVE + i35])
					{
						updateData.UnitData.ResistanceBuffModsNegative[i35] = updates[PLAYER_FIELD_RESISTANCEBUFFMODSNEGATIVE + i35].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_MOD_DAMAGE_DONE_POS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_DAMAGE_DONE_POS);
			if (PLAYER_FIELD_MOD_DAMAGE_DONE_POS >= 0)
			{
				for (int i36 = 0; i36 < 7; i36++)
				{
					if (updateMaskArray[PLAYER_FIELD_MOD_DAMAGE_DONE_POS + i36])
					{
						updateData.ActivePlayerData.ModDamageDonePos[i36] = updates[PLAYER_FIELD_MOD_DAMAGE_DONE_POS + i36].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_MOD_DAMAGE_DONE_NEG = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_DAMAGE_DONE_NEG);
			if (PLAYER_FIELD_MOD_DAMAGE_DONE_NEG >= 0)
			{
				for (int i37 = 0; i37 < 7; i37++)
				{
					if (updateMaskArray[PLAYER_FIELD_MOD_DAMAGE_DONE_NEG + i37])
					{
						updateData.ActivePlayerData.ModDamageDoneNeg[i37] = updates[PLAYER_FIELD_MOD_DAMAGE_DONE_NEG + i37].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_MOD_DAMAGE_DONE_PCT = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_DAMAGE_DONE_PCT);
			if (PLAYER_FIELD_MOD_DAMAGE_DONE_PCT >= 0)
			{
				for (int i38 = 0; i38 < 7; i38++)
				{
					if (updateMaskArray[PLAYER_FIELD_MOD_DAMAGE_DONE_PCT + i38])
					{
						updateData.ActivePlayerData.ModDamageDonePercent[i38] = updates[PLAYER_FIELD_MOD_DAMAGE_DONE_PCT + i38].FloatValue;
					}
				}
			}
			int PLAYER_FIELD_MOD_HEALING_DONE_POS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_HEALING_DONE_POS);
			if (PLAYER_FIELD_MOD_HEALING_DONE_POS >= 0 && updateMaskArray[PLAYER_FIELD_MOD_HEALING_DONE_POS])
			{
				updateData.ActivePlayerData.ModHealingDonePos = updates[PLAYER_FIELD_MOD_HEALING_DONE_POS].Int32Value;
			}
			int PLAYER_FIELD_MOD_TARGET_RESISTANCE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_TARGET_RESISTANCE);
			if (PLAYER_FIELD_MOD_TARGET_RESISTANCE >= 0 && updateMaskArray[PLAYER_FIELD_MOD_TARGET_RESISTANCE])
			{
				updateData.ActivePlayerData.ModTargetResistance = updates[PLAYER_FIELD_MOD_TARGET_RESISTANCE].Int32Value;
			}
			int PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE);
			if (PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE >= 0 && updateMaskArray[PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE])
			{
				updateData.ActivePlayerData.ModTargetPhysicalResistance = updates[PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE].Int32Value;
			}
			int PLAYER_FIELD_BYTES = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BYTES);
			if (PLAYER_FIELD_BYTES >= 0 && updateMaskArray[PLAYER_FIELD_BYTES])
			{
				updateData.ActivePlayerData.LocalFlags = (byte)(updates[PLAYER_FIELD_BYTES].UInt32Value & 0xFF);
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					byte comboPoints = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 8) & 0xFF);
					Class classId3 = Class.None;
					classId3 = ((!updateData.UnitData.ClassId.HasValue) ? this.GetSession().GameState.GetUnitClass(guid.To128(this.GetSession().GameState)) : ((Class)updateData.UnitData.ClassId.Value));
					sbyte powerSlot3 = ClassPowerTypes.GetPowerSlotForClass(classId3, PowerType.ComboPoints);
					if (powerSlot3 >= 0)
					{
						if (powerUpdate != null && guid == this.GetSession().GameState.CurrentPlayerGuid)
						{
							powerUpdate.Powers.Add(new PowerUpdatePower(comboPoints, 14));
						}
						updateData.UnitData.Power[powerSlot3] = comboPoints;
					}
				}
				else
				{
					updateData.ActivePlayerData.GrantableLevels = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 8) & 0xFF);
				}
				updateData.ActivePlayerData.MultiActionBars = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 16) & 0xFF);
				updateData.ActivePlayerData.LifetimeMaxRank = (byte)((updates[PLAYER_FIELD_BYTES].UInt32Value >> 24) & 0xFF);
			}
			int PLAYER_AMMO_ID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_AMMO_ID);
			if (PLAYER_AMMO_ID >= 0 && updateMaskArray[PLAYER_AMMO_ID])
			{
				updateData.ActivePlayerData.AmmoID = updates[PLAYER_AMMO_ID].UInt32Value;
			}
			int PLAYER_SELF_RES_SPELL = LegacyVersion.GetUpdateField(PlayerField.PLAYER_SELF_RES_SPELL);
			if (PLAYER_SELF_RES_SPELL >= 0 && updateMaskArray[PLAYER_SELF_RES_SPELL])
			{
				uint spellId = updates[PLAYER_SELF_RES_SPELL].UInt32Value;
				updateData.ActivePlayerData.SelfResSpells = new List<uint>();
				updateData.ActivePlayerData.SelfResSpells.Add(spellId);
			}
			int PLAYER_FIELD_PVP_MEDALS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_PVP_MEDALS);
			if (PLAYER_FIELD_PVP_MEDALS >= 0 && updateMaskArray[PLAYER_FIELD_PVP_MEDALS])
			{
				updateData.ActivePlayerData.PvpMedals = updates[PLAYER_FIELD_PVP_MEDALS].UInt32Value;
			}
			int PLAYER_FIELD_BUYBACK_PRICE_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BUYBACK_PRICE_1);
			if (PLAYER_FIELD_BUYBACK_PRICE_1 >= 0)
			{
				for (int i39 = 0; i39 < 12; i39++)
				{
					if (updateMaskArray[PLAYER_FIELD_BUYBACK_PRICE_1 + i39])
					{
						updateData.ActivePlayerData.BuybackPrice[i39] = updates[PLAYER_FIELD_BUYBACK_PRICE_1 + i39].UInt32Value;
					}
				}
			}
			int PLAYER_FIELD_BUYBACK_TIMESTAMP_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BUYBACK_TIMESTAMP_1);
			if (PLAYER_FIELD_BUYBACK_TIMESTAMP_1 >= 0)
			{
				for (int i40 = 0; i40 < 12; i40++)
				{
					if (updateMaskArray[PLAYER_FIELD_BUYBACK_TIMESTAMP_1 + i40])
					{
						updateData.ActivePlayerData.BuybackTimestamp[i40] = updates[PLAYER_FIELD_BUYBACK_TIMESTAMP_1 + i40].UInt32Value;
					}
				}
			}
			int PLAYER_FIELD_SESSION_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_SESSION_KILLS);
			if (PLAYER_FIELD_SESSION_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_SESSION_KILLS])
			{
				updateData.ActivePlayerData.TodayHonorableKills = (ushort)(updates[PLAYER_FIELD_SESSION_KILLS].UInt32Value & 0xFFFF);
				updateData.ActivePlayerData.TodayDishonorableKills = (ushort)((updates[PLAYER_FIELD_SESSION_KILLS].UInt32Value >> 16) & 0xFFFF);
			}
			int PLAYER_FIELD_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_KILLS);
			if (PLAYER_FIELD_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_KILLS])
			{
				updateData.ActivePlayerData.TodayHonorableKills = (ushort)(updates[PLAYER_FIELD_KILLS].UInt32Value & 0xFFFF);
				updateData.ActivePlayerData.YesterdayHonorableKills = (ushort)((updates[PLAYER_FIELD_KILLS].UInt32Value >> 16) & 0xFFFF);
			}
			int PLAYER_FIELD_YESTERDAY_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_YESTERDAY_KILLS);
			if (PLAYER_FIELD_YESTERDAY_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_YESTERDAY_KILLS])
			{
				updateData.ActivePlayerData.YesterdayHonorableKills = (ushort)(updates[PLAYER_FIELD_YESTERDAY_KILLS].UInt32Value & 0xFFFF);
				updateData.ActivePlayerData.YesterdayDishonorableKills = (ushort)((updates[PLAYER_FIELD_YESTERDAY_KILLS].UInt32Value >> 16) & 0xFFFF);
			}
			int PLAYER_FIELD_LAST_WEEK_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_LAST_WEEK_KILLS);
			if (PLAYER_FIELD_LAST_WEEK_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_LAST_WEEK_KILLS])
			{
				updateData.ActivePlayerData.LastWeekHonorableKills = (ushort)(updates[PLAYER_FIELD_LAST_WEEK_KILLS].UInt32Value & 0xFFFF);
				updateData.ActivePlayerData.LastWeekDishonorableKills = (ushort)((updates[PLAYER_FIELD_LAST_WEEK_KILLS].UInt32Value >> 16) & 0xFFFF);
			}
			int PLAYER_FIELD_THIS_WEEK_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_THIS_WEEK_KILLS);
			if (PLAYER_FIELD_THIS_WEEK_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_THIS_WEEK_KILLS])
			{
				updateData.ActivePlayerData.ThisWeekHonorableKills = (ushort)(updates[PLAYER_FIELD_THIS_WEEK_KILLS].UInt32Value & 0xFFFF);
				updateData.ActivePlayerData.ThisWeekDishonorableKills = (ushort)((updates[PLAYER_FIELD_THIS_WEEK_KILLS].UInt32Value >> 16) & 0xFFFF);
			}
			int PLAYER_FIELD_THIS_WEEK_CONTRIBUTION = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_THIS_WEEK_CONTRIBUTION);
			if (PLAYER_FIELD_THIS_WEEK_CONTRIBUTION < 0)
			{
				PLAYER_FIELD_THIS_WEEK_CONTRIBUTION = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_TODAY_CONTRIBUTION);
			}
			if (PLAYER_FIELD_THIS_WEEK_CONTRIBUTION >= 0 && updateMaskArray[PLAYER_FIELD_THIS_WEEK_CONTRIBUTION])
			{
				updateData.ActivePlayerData.ThisWeekContribution = updates[PLAYER_FIELD_THIS_WEEK_CONTRIBUTION].UInt32Value;
			}
			int PLAYER_FIELD_LIFETIME_HONORABLE_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_LIFETIME_HONORABLE_KILLS);
			if (PLAYER_FIELD_LIFETIME_HONORABLE_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_LIFETIME_HONORABLE_KILLS])
			{
				updateData.ActivePlayerData.LifetimeHonorableKills = updates[PLAYER_FIELD_LIFETIME_HONORABLE_KILLS].UInt32Value;
			}
			int PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS);
			if (PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS >= 0 && updateMaskArray[PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS])
			{
				updateData.ActivePlayerData.LifetimeDishonorableKills = updates[PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS].UInt32Value;
			}
			int PLAYER_FIELD_YESTERDAY_CONTRIBUTION = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_YESTERDAY_CONTRIBUTION);
			if (PLAYER_FIELD_YESTERDAY_CONTRIBUTION >= 0 && updateMaskArray[PLAYER_FIELD_YESTERDAY_CONTRIBUTION])
			{
				updateData.ActivePlayerData.YesterdayContribution = updates[PLAYER_FIELD_YESTERDAY_CONTRIBUTION].UInt32Value;
			}
			int PLAYER_FIELD_LAST_WEEK_CONTRIBUTION = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_LAST_WEEK_CONTRIBUTION);
			if (PLAYER_FIELD_LAST_WEEK_CONTRIBUTION >= 0 && updateMaskArray[PLAYER_FIELD_LAST_WEEK_CONTRIBUTION])
			{
				updateData.ActivePlayerData.LastWeekContribution = updates[PLAYER_FIELD_LAST_WEEK_CONTRIBUTION].UInt32Value;
			}
			int PLAYER_FIELD_LAST_WEEK_RANK = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_LAST_WEEK_RANK);
			if (PLAYER_FIELD_LAST_WEEK_RANK >= 0 && updateMaskArray[PLAYER_FIELD_LAST_WEEK_RANK])
			{
				updateData.ActivePlayerData.LastWeekRank = updates[PLAYER_FIELD_LAST_WEEK_RANK].UInt32Value;
			}
			int PLAYER_FIELD_BYTES2 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_BYTES2);
			if (PLAYER_FIELD_BYTES2 >= 0 && updateMaskArray[PLAYER_FIELD_BYTES2])
			{
				updateData.ActivePlayerData.PvPRankProgress = (byte)(updates[PLAYER_FIELD_BYTES2].UInt32Value & 0xFF);
				updateData.ActivePlayerData.AuraVision = (byte)((updates[PLAYER_FIELD_BYTES2].UInt32Value >> 8) & 0xFF);
			}
			int PLAYER_FIELD_WATCHED_FACTION_INDEX = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_WATCHED_FACTION_INDEX);
			if (PLAYER_FIELD_WATCHED_FACTION_INDEX >= 0 && updateMaskArray[PLAYER_FIELD_WATCHED_FACTION_INDEX])
			{
				updateData.ActivePlayerData.WatchedFactionIndex = updates[PLAYER_FIELD_WATCHED_FACTION_INDEX].Int32Value;
			}
			int PLAYER_FIELD_COMBAT_RATING_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_COMBAT_RATING_1);
			if (PLAYER_FIELD_COMBAT_RATING_1 >= 0)
			{
				for (int i41 = 0; i41 < 20; i41++)
				{
					if (updateMaskArray[PLAYER_FIELD_COMBAT_RATING_1 + i41])
					{
						updateData.ActivePlayerData.CombatRatings[i41] = updates[PLAYER_FIELD_COMBAT_RATING_1 + i41].Int32Value;
					}
				}
			}
			int PLAYER_FIELD_ARENA_TEAM_INFO_1_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_ARENA_TEAM_INFO_1_1);
			if (PLAYER_FIELD_ARENA_TEAM_INFO_1_1 >= 0)
			{
				int teamIdOffset = 0;
				int teamGamesWeekOffset = 2;
				int teamGamesSeasonOffset = 3;
				int teamWinsSeasonOffset = 4;
				int teamPersonalRatingOffset = 5;
				int sizePerEntry2 = 6;
				for (int i42 = 0; i42 < 3; i42++)
				{
					int startOffset = PLAYER_FIELD_ARENA_TEAM_INFO_1_1 + i42 * sizePerEntry2;
					if (updateMaskArray[startOffset + teamIdOffset] && guid == this.GetSession().GameState.CurrentPlayerGuid)
					{
						uint teamId = (this.GetSession().GameState.CurrentArenaTeamIds[i42] = updates[startOffset + teamIdOffset].UInt32Value);
						if (teamId != 0)
						{
							WorldPacket packet = new WorldPacket(Opcode.CMSG_ARENA_TEAM_QUERY);
							packet.WriteUInt32(teamId);
							this.SendPacketToServer(packet);
							WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ARENA_TEAM_ROSTER);
							packet2.WriteUInt32(teamId);
							this.SendPacketToServer(packet2);
						}
						else
						{
							ArenaTeamRosterResponse response = new ArenaTeamRosterResponse();
							response.TeamSize = ModernVersion.GetArenaTeamSizeFromIndex((uint)i42);
							this.SendPacketToClient(response);
						}
					}
					if (updateMaskArray[startOffset + teamGamesWeekOffset])
					{
						if (updateData.ActivePlayerData.PvpInfo[i42] == null)
						{
							updateData.ActivePlayerData.PvpInfo[i42] = new PVPInfo();
						}
						updateData.ActivePlayerData.PvpInfo[i42].WeeklyPlayed = updates[startOffset + teamGamesWeekOffset].UInt32Value;
					}
					if (updateMaskArray[startOffset + teamGamesSeasonOffset])
					{
						if (updateData.ActivePlayerData.PvpInfo[i42] == null)
						{
							updateData.ActivePlayerData.PvpInfo[i42] = new PVPInfo();
						}
						updateData.ActivePlayerData.PvpInfo[i42].SeasonPlayed = updates[startOffset + teamGamesSeasonOffset].UInt32Value;
					}
					if (updateMaskArray[startOffset + teamWinsSeasonOffset])
					{
						if (updateData.ActivePlayerData.PvpInfo[i42] == null)
						{
							updateData.ActivePlayerData.PvpInfo[i42] = new PVPInfo();
						}
						updateData.ActivePlayerData.PvpInfo[i42].SeasonWon = updates[startOffset + teamWinsSeasonOffset].UInt32Value;
					}
					if (updateMaskArray[startOffset + teamPersonalRatingOffset])
					{
						if (updateData.ActivePlayerData.PvpInfo[i42] == null)
						{
							updateData.ActivePlayerData.PvpInfo[i42] = new PVPInfo();
						}
						updateData.ActivePlayerData.PvpInfo[i42].Rating = updates[startOffset + teamPersonalRatingOffset].UInt32Value;
					}
				}
			}
			if (guid == this.GetSession().GameState.CurrentPlayerGuid && ModernVersion.ExpansionVersion > 1)
			{
				int PLAYER_FIELD_HONOR_CURRENCY = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_HONOR_CURRENCY);
				int PLAYER_FIELD_ARENA_CURRENCY = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_ARENA_CURRENCY);
				if (PLAYER_FIELD_HONOR_CURRENCY >= 0 && PLAYER_FIELD_ARENA_CURRENCY >= 0 && (updateMaskArray[PLAYER_FIELD_HONOR_CURRENCY] || updateMaskArray[PLAYER_FIELD_ARENA_CURRENCY]))
				{
					SetupCurrency currencies = new SetupCurrency();
					if (updates.ContainsKey(PLAYER_FIELD_ARENA_CURRENCY))
					{
						SetupCurrency.Record honor = new SetupCurrency.Record
						{
							Type = 1900u,
							Quantity = updates[PLAYER_FIELD_ARENA_CURRENCY].UInt32Value
						};
						currencies.Data.Add(honor);
					}
					if (updates.ContainsKey(PLAYER_FIELD_HONOR_CURRENCY))
					{
						SetupCurrency.Record honor2 = new SetupCurrency.Record
						{
							Type = 1901u,
							Quantity = updates[PLAYER_FIELD_HONOR_CURRENCY].UInt32Value
						};
						currencies.Data.Add(honor2);
					}
					this.SendPacketToClient(currencies);
				}
			}
			int PLAYER_FIELD_MOD_MANA_REGEN = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MOD_MANA_REGEN);
			if (PLAYER_FIELD_MOD_MANA_REGEN >= 0 && updateMaskArray[PLAYER_FIELD_MOD_MANA_REGEN])
			{
				updateData.UnitData.ModPowerRegen[0] = updates[PLAYER_FIELD_MOD_MANA_REGEN].FloatValue;
			}
			int PLAYER_FIELD_MAX_LEVEL = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_MAX_LEVEL);
			if (PLAYER_FIELD_MAX_LEVEL >= 0 && updateMaskArray[PLAYER_FIELD_MAX_LEVEL])
			{
				updateData.ActivePlayerData.MaxLevel = updates[PLAYER_FIELD_MAX_LEVEL].Int32Value;
			}
			int PLAYER_FIELD_DAILY_QUESTS_1 = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_DAILY_QUESTS_1);
			if (PLAYER_FIELD_DAILY_QUESTS_1 >= 0 && guid == this.GetSession().GameState.CurrentPlayerGuid)
			{
				for (int i43 = 0; i43 < 25; i43++)
				{
					if (updateMaskArray[PLAYER_FIELD_DAILY_QUESTS_1 + i43])
					{
						this.GetSession().GameState.SetDailyQuestSlot((uint)i43, updates[PLAYER_FIELD_DAILY_QUESTS_1 + i43].UInt32Value);
						updateData.ActivePlayerData.HasDailyQuestsUpdate = true;
					}
				}
			}
		}
		if (objectType == ObjectType.GameObject)
		{
			int GAMEOBJECT_FIELD_CREATED_BY = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_FIELD_CREATED_BY);
			if (GAMEOBJECT_FIELD_CREATED_BY >= 0 && updateMaskArray[GAMEOBJECT_FIELD_CREATED_BY])
			{
				updateData.GameObjectData.CreatedBy = WorldClient.GetGuidValue(updates, GameObjectField.GAMEOBJECT_FIELD_CREATED_BY).To128(this.GetSession().GameState);
			}
			int GAMEOBJECT_DISPLAYID = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_DISPLAYID);
			if (GAMEOBJECT_DISPLAYID >= 0 && updateMaskArray[GAMEOBJECT_DISPLAYID])
			{
				updateData.GameObjectData.DisplayID = updates[GAMEOBJECT_DISPLAYID].Int32Value;
			}
			int GAMEOBJECT_FLAGS = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_FLAGS);
			if (GAMEOBJECT_FLAGS >= 0 && updateMaskArray[GAMEOBJECT_FLAGS])
			{
				updateData.GameObjectData.Flags = updates[GAMEOBJECT_FLAGS].UInt32Value;
			}
			int GAMEOBJECT_ROTATION = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_ROTATION);
			if (GAMEOBJECT_ROTATION >= 0 && updateData.CreateData != null && updateData.CreateData.MoveInfo != null)
			{
				for (int i44 = 0; i44 < 4; i44++)
				{
					if (updateMaskArray[GAMEOBJECT_ROTATION + i44])
					{
						updateData.CreateData.MoveInfo.Rotation[i44] = updates[GAMEOBJECT_ROTATION + i44].FloatValue;
					}
				}
				switch (updateData.ObjectData.EntryID)
				{
				case 176080:
				case 176084:
				case 176085:
				{
					EulerAngles rot = updateData.CreateData.MoveInfo.Rotation.AsEulerAngles();
					rot.Yaw *= -1.0;
					updateData.CreateData.MoveInfo.Rotation = rot.AsQuaternion();
					break;
				}
				}
				switch (updateData.ObjectData.EntryID)
				{
				case 176081:
				case 176082:
				case 176083:
				case 176085:
					updateData.GameObjectData.ParentRotation = new float?[4] { -4.371139E-08f, 0f, 1f, 0f };
					break;
				case 183177:
					updateData.GameObjectData.ParentRotation = new float?[4] { 0f, 0f, -0.69465846f, 0.7193397f };
					break;
				}
			}
			int GAMEOBJECT_STATE = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_STATE);
			if (GAMEOBJECT_STATE >= 0 && updateMaskArray[GAMEOBJECT_STATE])
			{
				updateData.GameObjectData.State = (sbyte)updates[GAMEOBJECT_STATE].Int32Value;
			}
			// Handle GO dynamic flags - try GAMEOBJECT_DYN_FLAGS first (newer expansions),
			// then fall back to GAMEOBJECT_DYNAMIC (3.3.5a packs dyn flags in low 16 bits)
			int GAMEOBJECT_DYN_FLAGS = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_DYN_FLAGS);
			int GAMEOBJECT_DYNAMIC = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_DYNAMIC);
			uint legacyDynFlags = 0;
			bool hasDynFlags = false;
			if (GAMEOBJECT_DYN_FLAGS >= 0 && updateMaskArray[GAMEOBJECT_DYN_FLAGS])
			{
				legacyDynFlags = updates[GAMEOBJECT_DYN_FLAGS].UInt32Value;
				hasDynFlags = true;
			}
			else if (GAMEOBJECT_DYNAMIC >= 0 && updateMaskArray[GAMEOBJECT_DYNAMIC])
			{
				// In 3.3.5a, GAMEOBJECT_DYNAMIC low 16 bits = dynamic flags, high 16 bits = path progress
				legacyDynFlags = updates[GAMEOBJECT_DYNAMIC].UInt32Value & 0xFFFF;
				hasDynFlags = true;
			}
			if (hasDynFlags)
			{
				uint oldValue2 = 0u;
				if (updateData.ObjectData.DynamicFlags.HasValue)
				{
					oldValue2 = updateData.ObjectData.DynamicFlags.Value;
				}
				else if (!guid.IsTransport())
				{
					oldValue2 = 4294901760u;
				}
				GameObjectDynamicFlagsLegacy flags4 = (GameObjectDynamicFlagsLegacy)legacyDynFlags;
				updateData.ObjectData.DynamicFlags = oldValue2 | (uint)flags4.CastFlags<GameObjectDynamicFlagsModern>();
			}
			// Fishing bobbers need Activate flag to be clickable in 3.4.3
			if (updateData.ObjectData.EntryID == 35591)
			{
				uint dynVal = updateData.ObjectData.DynamicFlags.GetValueOrDefault(0xFFFF0000u);
				dynVal |= (uint)GameObjectDynamicFlagsModern.Activate;
				updateData.ObjectData.DynamicFlags = dynVal;
			}
			int GAMEOBJECT_FACTION = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_FACTION);
			if (GAMEOBJECT_FACTION >= 0 && updateMaskArray[GAMEOBJECT_FACTION])
			{
				updateData.GameObjectData.FactionTemplate = updates[GAMEOBJECT_FACTION].Int32Value;
			}
			int GAMEOBJECT_TYPE_ID = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_TYPE_ID);
			if (GAMEOBJECT_TYPE_ID >= 0 && updateMaskArray[GAMEOBJECT_TYPE_ID])
			{
				updateData.GameObjectData.TypeID = (sbyte)updates[GAMEOBJECT_TYPE_ID].Int32Value;
			}
			int GAMEOBJECT_LEVEL = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_LEVEL);
			if (GAMEOBJECT_LEVEL >= 0 && updateMaskArray[GAMEOBJECT_LEVEL])
			{
				updateData.GameObjectData.Level = updates[GAMEOBJECT_LEVEL].Int32Value;
			}
			int GAMEOBJECT_ARTKIT = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_ARTKIT);
			if (GAMEOBJECT_ARTKIT >= 0 && updateMaskArray[GAMEOBJECT_ARTKIT])
			{
				updateData.GameObjectData.ArtKit = (byte)updates[GAMEOBJECT_ARTKIT].UInt32Value;
			}
			// 3.3.5a packs State, TypeID, ArtKit, AnimProgress into GAMEOBJECT_BYTES_1
			int GAMEOBJECT_BYTES_1 = LegacyVersion.GetUpdateField(GameObjectField.GAMEOBJECT_BYTES_1);
			if (GAMEOBJECT_BYTES_1 >= 0 && updateMaskArray[GAMEOBJECT_BYTES_1])
			{
				uint packed = updates[GAMEOBJECT_BYTES_1].UInt32Value;
				updateData.GameObjectData.State = (sbyte)(packed & 0xFF);
				updateData.GameObjectData.TypeID = (sbyte)((packed >> 8) & 0xFF);
				updateData.GameObjectData.ArtKit = (byte)((packed >> 16) & 0xFF);
				updateData.GameObjectData.PercentHealth = (byte)((packed >> 24) & 0xFF);
			}
			// Fishing bobbers: force State=0 (READY) so 3.4.3 client allows interaction
			if (updateData.ObjectData.EntryID == 35591)
			{
				updateData.GameObjectData.State = 0;
			}
		}
		if (objectType == ObjectType.DynamicObject)
		{
			int DYNAMICOBJECT_CASTER = LegacyVersion.GetUpdateField(DynamicObjectField.DYNAMICOBJECT_CASTER);
			if (DYNAMICOBJECT_CASTER >= 0 && updateMaskArray[DYNAMICOBJECT_CASTER])
			{
				updateData.DynamicObjectData.Caster = WorldClient.GetGuidValue(updates, DynamicObjectField.DYNAMICOBJECT_CASTER).To128(this.GetSession().GameState);
			}
			int DYNAMICOBJECT_SPELLID = LegacyVersion.GetUpdateField(DynamicObjectField.DYNAMICOBJECT_SPELLID);
			if (DYNAMICOBJECT_SPELLID >= 0 && updateMaskArray[DYNAMICOBJECT_SPELLID])
			{
				updateData.DynamicObjectData.SpellID = updates[DYNAMICOBJECT_SPELLID].Int32Value;
				updateData.DynamicObjectData.SpellXSpellVisualID = (int)GameData.GetSpellVisual((uint)updateData.DynamicObjectData.SpellID.Value);
			}
			int DYNAMICOBJECT_RADIUS = LegacyVersion.GetUpdateField(DynamicObjectField.DYNAMICOBJECT_RADIUS);
			if (DYNAMICOBJECT_RADIUS >= 0 && updateMaskArray[DYNAMICOBJECT_RADIUS])
			{
				updateData.DynamicObjectData.Radius = updates[DYNAMICOBJECT_RADIUS].FloatValue;
			}
		}
		if (objectType != ObjectType.Corpse)
		{
			return;
		}
		int CORPSE_FIELD_OWNER = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_OWNER);
		if (CORPSE_FIELD_OWNER >= 0 && updateMaskArray[CORPSE_FIELD_OWNER])
		{
			updateData.CorpseData.Owner = WorldClient.GetGuidValue(updates, CorpseField.CORPSE_FIELD_OWNER).To128(this.GetSession().GameState);
		}
		int CORPSE_FIELD_DISPLAY_ID = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_DISPLAY_ID);
		if (CORPSE_FIELD_DISPLAY_ID >= 0 && updateMaskArray[CORPSE_FIELD_DISPLAY_ID])
		{
			updateData.CorpseData.DisplayID = updates[CORPSE_FIELD_DISPLAY_ID].UInt32Value;
		}
		int CORPSE_FIELD_ITEM = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_ITEM);
		if (CORPSE_FIELD_ITEM >= 0)
		{
			for (int i45 = 0; i45 < 19; i45++)
			{
				if (updateMaskArray[CORPSE_FIELD_ITEM + i45])
				{
					updateData.CorpseData.Items[i45] = updates[CORPSE_FIELD_ITEM + i45].UInt32Value;
				}
			}
		}
		int CORPSE_FIELD_BYTES_1 = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_BYTES_1);
		if (CORPSE_FIELD_BYTES_1 >= 0 && updateMaskArray[CORPSE_FIELD_BYTES_1])
		{
			updateData.CorpseData.RaceId = (byte)((updates[CORPSE_FIELD_BYTES_1].UInt32Value >> 8) & 0xFF);
			updateData.CorpseData.SexId = (byte)((updates[CORPSE_FIELD_BYTES_1].UInt32Value >> 16) & 0xFF);
			byte skin2 = (byte)((updates[CORPSE_FIELD_BYTES_1].UInt32Value >> 24) & 0xFF);
			int CORPSE_FIELD_BYTES_2 = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_BYTES_2);
			if (CORPSE_FIELD_BYTES_2 >= 0 && updateMaskArray[CORPSE_FIELD_BYTES_2])
			{
				byte face2 = (byte)(updates[CORPSE_FIELD_BYTES_2].UInt32Value & 0xFF);
				byte hairStyle2 = (byte)((updates[CORPSE_FIELD_BYTES_2].UInt32Value >> 8) & 0xFF);
				byte hairColor2 = (byte)((updates[CORPSE_FIELD_BYTES_2].UInt32Value >> 16) & 0xFF);
				byte facialHair2 = (byte)((updates[CORPSE_FIELD_BYTES_2].UInt32Value >> 24) & 0xFF);
				Array<ChrCustomizationChoice> customizations2 = CharacterCustomizations.ConvertLegacyCustomizationsToModern((Race)updateData.CorpseData.RaceId.Value, (Gender)updateData.CorpseData.SexId.Value, skin2, face2, hairStyle2, hairColor2, facialHair2);
				for (int i46 = 0; i46 < 5; i46++)
				{
					updateData.CorpseData.Customizations[i46] = customizations2[i46];
				}
			}
		}
		int CORPSE_FIELD_GUILD = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_GUILD);
		if (CORPSE_FIELD_GUILD >= 0 && updateMaskArray[CORPSE_FIELD_GUILD])
		{
			updateData.CorpseData.GuildGUID = WowGuid128.Create(HighGuidType703.Guild, updates[CORPSE_FIELD_GUILD].UInt32Value);
		}
		int CORPSE_FIELD_FLAGS = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_FLAGS);
		if (CORPSE_FIELD_FLAGS >= 0 && updateMaskArray[CORPSE_FIELD_FLAGS])
		{
			updateData.CorpseData.Flags = updates[CORPSE_FIELD_FLAGS].UInt32Value;
			if (updateData.CorpseData.Flags.HasAnyFlag(CorpseFlags.HideHelm))
			{
				CorpseData corpseData = updateData.CorpseData;
				corpseData.Flags &= 4294967287u;
				updateData.CorpseData.Items[0] = null;
			}
			if (updateData.CorpseData.Flags.HasAnyFlag(CorpseFlags.HideCloak))
			{
				CorpseData corpseData = updateData.CorpseData;
				corpseData.Flags &= 4294967279u;
				updateData.CorpseData.Items[14] = null;
			}
		}
		int CORPSE_FIELD_DYNAMIC_FLAGS = LegacyVersion.GetUpdateField(CorpseField.CORPSE_FIELD_DYNAMIC_FLAGS);
		if (CORPSE_FIELD_DYNAMIC_FLAGS >= 0 && updateMaskArray[CORPSE_FIELD_DYNAMIC_FLAGS])
		{
			updateData.CorpseData.DynamicFlags = updates[CORPSE_FIELD_DYNAMIC_FLAGS].UInt32Value;
		}
	}

	[PacketHandler(Opcode.SMSG_INIT_WORLD_STATES)]
	private void HandleInitWorldStates(WorldPacket packet)
	{
		InitWorldStates states = new InitWorldStates();
		states.MapID = packet.ReadUInt32();
		this.GetSession().GameState.CurrentMapId = states.MapID;
		states.ZoneID = packet.ReadUInt32();
		states.AreaID = (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_1_0_6692) ? packet.ReadUInt32() : states.ZoneID);
		this.GetSession().GameState.HasWsgAllyFlagCarrier = false;
		this.GetSession().GameState.HasWsgHordeFlagCarrier = false;
		ushort count = packet.ReadUInt16();
		for (ushort i = 0; i < count; i++)
		{
			uint variable = packet.ReadUInt32();
			int value = packet.ReadInt32();
			if (variable != 0 || value != 0)
			{
				states.AddState(variable, value);
			}
			switch (variable)
			{
			case 2339u:
				this.GetSession().GameState.HasWsgAllyFlagCarrier = value == 2;
				break;
			case 2338u:
				this.GetSession().GameState.HasWsgHordeFlagCarrier = value == 2;
				break;
			}
		}
		states.AddClassicStates();
		this.SendPacketToClient(states);
		if (LegacyVersion.ExpansionVersion <= 1 || ModernVersion.ExpansionVersion <= 1)
		{
			this.SendPacketToClient(new SetupCurrency());
		}
		// AllAccountCriteria removed — was sending empty criteria after real SMSG_ALL_ACHIEVEMENT_DATA
		if (this.GetSession().GameState.HasWsgHordeFlagCarrier || this.GetSession().GameState.HasWsgAllyFlagCarrier)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
		}
		if (this.GetSession().GameState.CurrentZoneId == states.ZoneID)
		{
			return;
		}
		string oldZoneName = GameData.GetAreaName(this.GetSession().GameState.CurrentZoneId);
		string newZoneName = GameData.GetAreaName(states.ZoneID);
		this.GetSession().GameState.CurrentZoneId = states.ZoneID;
		if (string.IsNullOrEmpty(oldZoneName) || string.IsNullOrEmpty(newZoneName))
		{
			return;
		}
		foreach (ChatChannel channel in GameData.GetChatChannelsWithFlags(ChannelFlags.AutoJoin | ChannelFlags.ZoneBased))
		{
			this.SendChatLeaveChannel(1, channel.Name + " - " + oldZoneName);
			this.SendChatJoinChannel(1, channel.Name + " - " + newZoneName, "");
		}
	}

	[PacketHandler(Opcode.SMSG_UPDATE_WORLD_STATE)]
	private void HandleUpdateWorldState(WorldPacket packet)
	{
		UpdateWorldState update = new UpdateWorldState();
		update.VariableID = packet.ReadUInt32();
		update.Value = packet.ReadInt32();
		this.SendPacketToClient(update);
		if (update.VariableID == 2339)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet2);
			this.GetSession().GameState.HasWsgAllyFlagCarrier = update.Value == 2;
		}
		else if (update.VariableID == 2338)
		{
			WorldPacket packet3 = new WorldPacket(Opcode.MSG_BATTLEGROUND_PLAYER_POSITIONS);
			this.SendPacket(packet3);
			this.GetSession().GameState.HasWsgHordeFlagCarrier = update.Value == 2;
		}
	}

	public WorldClient()
	{
		this.InitializePacketHandlers();
	}

	public GlobalSessionData GetSession()
	{
		return this._globalSession;
	}

	public bool ConnectToWorldServer(Realm realm, GlobalSessionData globalSession)
	{
		this._worldCrypt = null;
		this._realm = realm;
		this._globalSession = globalSession;
		this._username = globalSession.Username;
		this._isSuccessful = null;
		this._delayedPacketsToServer = new Dictionary<Opcode, List<WorldPacket>>();
		this._delayedPacketsToClient = new Dictionary<Opcode, List<ServerPacket>>();
		Log.Print(LogType.Network, "Connecting to world server...", "ConnectToWorldServer", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
		try
		{
			IPAddress ip = NetworkUtils.ResolveOrDirectIPv4(realm.ExternalAddress);
			Log.Print(LogType.Network, $"World Server address {realm.ExternalAddress}:{realm.Port} resolved as {ip}:{realm.Port}", "ConnectToWorldServer", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			this._clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			IPEndPoint endPoint = new IPEndPoint(ip, realm.Port);
			this._clientSocket.BeginConnect(endPoint, ConnectCallback, null);
		}
		catch (Exception ex)
		{
			Log.Print(LogType.Error, "Socket Error: " + ex.Message, "ConnectToWorldServer", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			this._isSuccessful = false;
		}
		while (!this._isSuccessful.HasValue)
		{
		}
		return this._isSuccessful.Value;
	}

	public bool IsAuthenticated()
	{
		return this._isSuccessful == true;
	}

	private void InitializeEncryption(byte[] sessionKey)
	{
		switch (Settings.ServerBuild)
		{
		case ClientVersionBuild.V1_12_1_5875:
		case ClientVersionBuild.V1_12_2_6005:
		case ClientVersionBuild.V1_12_3_6141:
			this._worldCrypt = new VanillaWorldCrypt();
			break;
		case ClientVersionBuild.V2_4_3_8606:
			this._worldCrypt = new TbcWorldCrypt();
			break;
		case ClientVersionBuild.V3_3_5a_12340:
			this._worldCrypt = new WotlkWorldCrypt();
			break;
		}
		if (this._worldCrypt != null)
		{
			this._worldCrypt.Initialize(sessionKey);
		}
	}

	public void Disconnect()
	{
		if (this.IsConnected())
		{
			this._clientSocket.Shutdown(SocketShutdown.Both);
			this._clientSocket.Disconnect(reuseSocket: false);
			if (this.GetSession().WorldClient == this)
			{
				this.GetSession().WorldClient = null;
			}
		}
	}

	public bool IsConnected()
	{
		return this._clientSocket != null && this._clientSocket.Connected;
	}

	public uint GetQueuePosition()
	{
		return this._queuePosition;
	}

	private void ConnectCallback(IAsyncResult AR)
	{
		try
		{
			Log.Print(LogType.Network, "Connection established!", "ConnectCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			this._clientSocket.EndConnect(AR);
			this._clientSocket.ReceiveBufferSize = 65535;
			Task.Run((Func<Task?>)ReceiveLoop);
		}
		catch (Exception ex)
		{
			Log.Print(LogType.Error, "Connect Error: " + ex.Message, "ConnectCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (!this._isSuccessful.HasValue)
			{
				this._isSuccessful = false;
			}
		}
	}

	private async Task<bool> ReceiveBufferFully(ArraySegment<byte> bufferToFill)
	{
		int receive;
		for (int alreadyReceived = 0; alreadyReceived < bufferToFill.Count; alreadyReceived += receive)
		{
			ArraySegment<byte> tmpArrayBuffer = new ArraySegment<byte>(bufferToFill.Array, alreadyReceived + bufferToFill.Offset, bufferToFill.Count - alreadyReceived);
			receive = await this._clientSocket.ReceiveAsync(tmpArrayBuffer, SocketFlags.None);
			if (receive == 0)
			{
				return false;
			}
		}
		return true;
	}

	private async Task ReceiveLoop()
	{
		try
		{
			while (true)
			{
				byte[] headerBuffer = new byte[4];
				if (!(await this.ReceiveBufferFully(headerBuffer)))
				{
					Log.PrintNet(LogType.Error, LogNetDir.S2P, "Socket Closed By GameWorldServer (header)", "ReceiveLoop", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
					if (!this._isSuccessful.HasValue)
					{
						this._isSuccessful = false;
					}
					else if (this.GetSession().WorldClient == this)
					{
						this.GetSession().OnDisconnect();
					}
					return;
				}
				if (this._worldCrypt != null)
				{
					this._worldCrypt.Decrypt(headerBuffer, 4);
				}
				LegacyServerPacketHeader header = new LegacyServerPacketHeader();
				header.Read(headerBuffer);
				ushort packetSize = header.Size;
				if (header.Opcode != 221)
				{
					Log.PrintNet(LogType.Debug, LogNetDir.S2P, $"Decoded header: size={packetSize}, opcode={header.Opcode} (0x{header.Opcode:X4}), crypt={((this._worldCrypt != null) ? "ON" : "OFF")}", "ReceiveLoop", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
				}
				if (packetSize != 0)
				{
					byte[] buffer = new byte[packetSize];
					buffer[0] = headerBuffer[2];
					buffer[1] = headerBuffer[3];
					if (!(await this.ReceiveBufferFully(new ArraySegment<byte>(buffer, 2, buffer.Length - 2))))
					{
						break;
					}
					WorldPacket packet = new WorldPacket(buffer);
					packet.SetReceiveTime(Environment.TickCount);
					this.HandlePacket(packet);
				}
			}
			Log.PrintNet(LogType.Error, LogNetDir.S2P, "Socket Closed By GameWorldServer (payload)", "ReceiveLoop", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (!this._isSuccessful.HasValue)
			{
				this._isSuccessful = false;
			}
			else if (this.GetSession().WorldClient == this)
			{
				this.GetSession().OnDisconnect();
			}
		}
		catch (Exception ex)
		{
			Exception e = ex;
			Log.PrintNet(LogType.Error, LogNetDir.S2P, "Packet Read Error: " + e.Message + Environment.NewLine + e.StackTrace, "ReceiveLoop", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (!this._isSuccessful.HasValue)
			{
				this._isSuccessful = false;
				return;
			}
			this.Disconnect();
			this.GetSession().OnDisconnect();
		}
	}

	private void SendPacket(WorldPacket packet)
	{
		this._sendMutex.WaitOne();
		try
		{
			ByteBuffer buffer = new ByteBuffer();
			LegacyClientPacketHeader header = new LegacyClientPacketHeader();
			header.Size = (ushort)(packet.GetSize() + 4);
			header.Opcode = packet.GetOpcode();
			header.Write(buffer);
			Log.PrintNet(LogType.Debug, LogNetDir.P2S, $"Sending opcode {LegacyVersion.GetUniversalOpcode(header.Opcode)} ({header.Opcode}) with size {header.Size}.", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			byte[] headerArray = buffer.GetData();
			Log.PrintNet(LogType.Debug, LogNetDir.P2S, $"Raw header ({headerArray.Length} bytes): {BitConverter.ToString(headerArray, 0, Math.Min(headerArray.Length, 6))}", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (this._worldCrypt != null)
			{
				this._worldCrypt.Encrypt(headerArray, 6);
			}
			buffer.Clear();
			buffer.WriteBytes(headerArray);
			buffer.WriteBytes(packet.GetData(), packet.GetSize());
			byte[] finalData = buffer.GetData();
			Log.PrintNet(LogType.Debug, LogNetDir.P2S, $"Total bytes on wire: {finalData.Length}, first 16: {BitConverter.ToString(finalData, 0, Math.Min(finalData.Length, 16))}", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			this._clientSocket.Send(finalData, SocketFlags.None);
		}
		catch (Exception ex)
		{
			Log.PrintNet(LogType.Error, LogNetDir.P2S, "Packet Write Error: " + ex.Message, "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (!this._isSuccessful.HasValue)
			{
				this._isSuccessful = false;
			}
		}
		this._sendMutex.ReleaseMutex();
	}

	public void SendPacketToClient(ServerPacket packet, Opcode delayUntilOpcode = Opcode.MSG_NULL_ACTION)
	{
		Opcode opcode = packet.GetUniversalOpcode();
		if (delayUntilOpcode != Opcode.MSG_NULL_ACTION)
		{
			if (this._delayedPacketsToClient.ContainsKey(delayUntilOpcode))
			{
				this._delayedPacketsToClient[delayUntilOpcode].Add(packet);
				return;
			}
			List<ServerPacket> packets = new List<ServerPacket>();
			packets.Add(packet);
			this._delayedPacketsToClient.Add(delayUntilOpcode, packets);
		}
		else
		{
			this.SendPacketToClientDirect(packet);
			this.SendDelayedPacketsToClientOnOpcode(opcode);
		}
	}

	private void SendPacketToClientDirect(ServerPacket packet)
	{
		if (this.GetSession()?.GameState == null)
		{
			Log.PrintNet(LogType.Warn, LogNetDir.P2C, $"Dropping {packet.GetUniversalOpcode()} - session/gamestate not ready", "SendPacketToClientDirect", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			return;
		}
		Queue<ServerPacket> pendingPackets = this.GetSession().GameState.PendingUninstancedPackets;
		if (packet.GetConnection() == ConnectionType.Realm)
		{
			if (this.GetSession().RealmSocket == null)
			{
				Log.PrintNet(LogType.Warn, LogNetDir.P2C, $"Queuing {packet.GetUniversalOpcode()} - RealmSocket not ready yet", "SendPacketToClientDirect", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
				lock (pendingPackets)
				{
					pendingPackets.Enqueue(packet);
					return;
				}
			}
			WorldSocket realmSocket = this.GetSession().RealmSocket;
			if (pendingPackets.Count > 0)
			{
				lock (pendingPackets)
				{
					ServerPacket oldPacket;
					while (pendingPackets.TryDequeue(out oldPacket))
					{
						realmSocket.SendPacket(oldPacket);
					}
				}
			}
			realmSocket.SendPacket(packet);
			return;
		}
		if (this.GetSession().InstanceSocket == null && !this.GetSession().GameState.IsConnectedToInstance)
		{
			lock (pendingPackets)
			{
				if (this.GetSession().InstanceSocket == null && !this.GetSession().GameState.IsConnectedToInstance)
				{
					pendingPackets.Enqueue(packet);
					Log.PrintNet(LogType.Warn, LogNetDir.P2C, $"Can't send opcode {packet.GetUniversalOpcode()} ({packet.GetOpcode()}) before entering world! Queue (Initial Check)", "SendPacketToClientDirect", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
					return;
				}
			}
		}
		while (this.GetSession().InstanceSocket == null && this.GetSession().GameState.IsConnectedToInstance)
		{
			Log.PrintNet(LogType.Network, LogNetDir.P2C, $"Waiting to send {packet.GetUniversalOpcode()} ({packet.GetOpcode()}).", "SendPacketToClientDirect", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			Thread.Sleep(200);
			if (this.GetSession()?.GameState == null) return;
		}
		if (this.GetSession().InstanceSocket == null)
		{
			lock (pendingPackets)
			{
				pendingPackets.Enqueue(packet);
				Log.PrintNet(LogType.Warn, LogNetDir.P2C, $"Can't send opcode {packet.GetUniversalOpcode()} ({packet.GetOpcode()}) before entering world! Queue (State: {this.GetSession().GameState.IsConnectedToInstance})", "SendPacketToClientDirect", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
				return;
			}
		}
		WorldSocket instanceSocket = this.GetSession().InstanceSocket;
		if (pendingPackets.Count > 0)
		{
			lock (pendingPackets)
			{
				ServerPacket oldPacket;
				while (pendingPackets.TryDequeue(out oldPacket))
				{
					instanceSocket.SendPacket(oldPacket);
				}
			}
		}
		instanceSocket.SendPacket(packet);
	}

	public void SendPacketToServer(WorldPacket packet, Opcode delayUntilOpcode = Opcode.MSG_NULL_ACTION)
	{
		Opcode opcode = packet.GetUniversalOpcode(isModern: false);
		if (delayUntilOpcode != Opcode.MSG_NULL_ACTION)
		{
			if (this._delayedPacketsToServer.ContainsKey(delayUntilOpcode))
			{
				this._delayedPacketsToServer[delayUntilOpcode].Add(packet);
				return;
			}
			List<WorldPacket> packets = new List<WorldPacket>();
			packets.Add(packet);
			this._delayedPacketsToServer.Add(delayUntilOpcode, packets);
		}
		else
		{
			this.SendPacket(packet);
			this.SendDelayedPacketsToServerOnOpcode(opcode);
		}
	}

	private void SendDelayedPacketsToServerOnOpcode(Opcode opcode)
	{
		if (this._delayedPacketsToServer.ContainsKey(opcode))
		{
			List<WorldPacket> packets = this._delayedPacketsToServer[opcode];
			for (int i = packets.Count - 1; i >= 0; i--)
			{
				this.SendPacket(packets[i]);
				packets.RemoveAt(i);
			}
		}
	}

	private void SendDelayedPacketsToClientOnOpcode(Opcode opcode)
	{
		if (this._delayedPacketsToClient.ContainsKey(opcode))
		{
			List<ServerPacket> packets = this._delayedPacketsToClient[opcode];
			for (int i = packets.Count - 1; i >= 0; i--)
			{
				this.SendPacketToClientDirect(packets[i]);
				packets.RemoveAt(i);
			}
		}
	}

	public void FlushPendingPackets()
	{
		if (this.GetSession()?.GameState == null)
		{
			return;
		}
		Queue<ServerPacket> pendingPackets = this.GetSession().GameState.PendingUninstancedPackets;
		if (pendingPackets.Count == 0)
		{
			return;
		}
		lock (pendingPackets)
		{
			ServerPacket next;
			while (pendingPackets.TryPeek(out next))
			{
				WorldSocket socket = (next.GetConnection() == ConnectionType.Realm) ? this.GetSession().RealmSocket : this.GetSession().InstanceSocket;
				if (socket != null)
				{
					pendingPackets.TryDequeue(out next);
					socket.SendPacket(next);
					continue;
				}
				break;
			}
		}
	}

	private static readonly HashSet<Opcode> _suppressedLogOpcodes = new HashSet<Opcode>
	{
		Opcode.SMSG_ON_MONSTER_MOVE,
		Opcode.MSG_MOVE_HEARTBEAT,
		Opcode.MSG_MOVE_START_FORWARD,
		Opcode.MSG_MOVE_STOP,
		Opcode.MSG_MOVE_SET_FACING,
	};

	private void HandlePacket(WorldPacket packet)
	{
		Opcode universalOpcode = packet.GetUniversalOpcode(isModern: false);
		if (!_suppressedLogOpcodes.Contains(universalOpcode))
			Log.PrintNet(LogType.Debug, LogNetDir.S2P, $"Received opcode {universalOpcode} ({packet.GetOpcode()}).", "HandlePacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
		switch (universalOpcode)
		{
		case Opcode.SMSG_AUTH_CHALLENGE:
			this.HandleAuthChallenge(packet);
			break;
		case Opcode.SMSG_AUTH_RESPONSE:
			this.HandleAuthResponse(packet);
			break;
		default:
			if (this._packetHandlers.ContainsKey(universalOpcode))
			{
				try
				{
					this._packetHandlers[universalOpcode](packet);
				}
				catch (System.OutOfMemoryException ex)
				{
					Log.Print(LogType.Error, $"OOM handling {universalOpcode}: {ex.Message}");
				}
				break;
			}
			Log.PrintNet(LogType.Warn, LogNetDir.S2P, $"No handler for opcode {universalOpcode} ({packet.GetOpcode()}) (Got unknown packet from WorldServer)", "HandlePacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			MissingOpcodeTracker.LogUnhandledLegacySMSG(universalOpcode, packet.GetOpcode());
			if (!this._isSuccessful.HasValue)
			{
				this._isSuccessful = false;
			}
			break;
		case Opcode.SMSG_ADDON_INFO:
			break;
		}
		this.SendDelayedPacketsToServerOnOpcode(universalOpcode);
	}

	private void HandleAuthChallenge(WorldPacket packet)
	{
		if (Settings.ServerBuild >= ClientVersionBuild.V3_3_5a_12340)
		{
			uint one = packet.ReadUInt32();
		}
		uint seed = packet.ReadUInt32();
		if (Settings.ServerBuild >= ClientVersionBuild.V3_3_5a_12340)
		{
			BigInteger seed2 = packet.ReadBytes(16u).ToBigInteger();
			BigInteger seed3 = packet.ReadBytes(16u).ToBigInteger();
		}
		RandomNumberGenerator rand = RandomNumberGenerator.Create();
		byte[] bytes = new byte[4];
		rand.GetBytes(bytes);
		BigInteger ourSeed = bytes.ToBigInteger();
		this.SendAuthResponse((uint)ourSeed, seed);
	}

	public void SendAuthResponse(uint clientSeed, uint serverSeed)
	{
		uint zero = 0u;
		byte[] authResponse = Framework.Cryptography.HashAlgorithm.SHA1.Hash(Encoding.ASCII.GetBytes(this._username.ToUpper()), BitConverter.GetBytes(zero), BitConverter.GetBytes(clientSeed), BitConverter.GetBytes(serverSeed), this.GetSession().AuthClient.GetSessionKey());
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUTH_SESSION);
		packet.WriteUInt32((uint)Settings.ServerBuild);
		packet.WriteUInt32(this._realm.Id.Index);
		packet.WriteBytes(this._username.ToUpper().ToCString());
		if (Settings.ServerBuild >= ClientVersionBuild.V3_0_2_9056)
		{
			packet.WriteUInt32(zero);
		}
		packet.WriteUInt32(clientSeed);
		if (Settings.ServerBuild >= ClientVersionBuild.V3_3_5a_12340)
		{
			packet.WriteUInt32(this._realm.Id.Region);
			packet.WriteUInt32(this._realm.Id.Site);
			packet.WriteUInt32(this._realm.Id.Index);
		}
		if (Settings.ServerBuild >= ClientVersionBuild.V3_2_0_10192)
		{
			packet.WriteUInt64(zero);
		}
		packet.WriteBytes(authResponse);
		byte[] addonBytes = new byte[178]
		{
			208, 1, 0, 0, 120, 156, 117, 207, 61, 14,
			194, 48, 12, 5, 224, 114, 14, 184, 12, 97,
			64, 149, 154, 133, 150, 25, 153, 196, 173, 172,
			38, 78, 21, 82, 126, 58, 113, 66, 206, 68,
			81, 133, 24, 98, 188, 126, 126, 79, 182, 114,
			52, 77, 16, 237, 105, 59, 154, 68, 129, 143,
			101, 177, 242, 183, 77, 85, 204, 163, 190, 166,
			32, 37, 135, 45, 161, 179, 154, 152, 60, 12,
			210, 18, 177, 37, 238, 230, 130, 87, 102, 187,
			224, 207, 144, 170, 208, 9, 185, 197, 26, 188,
			39, 9, 35, 180, 73, 188, 105, 175, 235, 49,
			94, 241, 33, 227, 72, 206, 42, 224, 94, 212,
			146, 47, 3, 154, 79, 237, 58, 183, 132, 190,
			14, 166, 199, 180, 252, 146, 167, 53, 152, 24,
			102, 121, 102, 114, 0, 178, 51, 196, 12, 26,
			112, 200, 242, 27, 77, 4, 139, 117, 79, 206,
			253, 99, 98, 140, 178, 145, 71, 13, 12, 29,
			198, 159, 190, 1, 43, 0, 141, 195
		};
		packet.WriteBytes(addonBytes);
		this.SendPacket(packet);
		this.InitializeEncryption(this.GetSession().AuthClient.GetSessionKey());
	}

	private void HandleInitialSpells(WorldPacket packet)
	{
		// Legacy SMSG_INITIAL_SPELLS (298): uint8 unknown + uint16 count + count * (uint32 spellid + uint16 unknown)
		packet.ReadUInt8();
		ushort count = packet.ReadUInt16();
		ModernInitialSpells modern = new ModernInitialSpells();
		for (int i = 0; i < count; i++)
		{
			uint spellId = packet.ReadUInt32();
			packet.ReadUInt16(); // unknown
			modern.Spells.Add(spellId);
		}
		this.SendPacketToClient(modern);
	}
	private void HandleAuthResponse(WorldPacket packet)
	{
		AuthResult result = (AuthResult)packet.ReadUInt8();
		if (!this._isSuccessful.HasValue)
		{
			uint billingTimeRemaining = packet.ReadUInt32();
			byte billingFlags = packet.ReadUInt8();
			uint billingTimeRested = packet.ReadUInt32();
			if (Settings.ServerBuild >= ClientVersionBuild.V2_0_1_6180)
			{
				byte expansion = packet.ReadUInt8();
			}
		}
		switch (result)
		{
		case AuthResult.AUTH_OK:
			Log.Print(LogType.Network, "Authentication succeeded!", "HandleAuthResponse", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (this._queuePosition != 0 && this.GetSession().RealmSocket != null)
			{
				this._queuePosition = 0u;
				this.GetSession().RealmSocket.SendAuthWaitQue(this._queuePosition);
			}
			// Proactively query all transport entries so cache is populated before CreateObjects
			foreach (uint transportEntry in GameData.TransportPeriods.Keys)
			{
				WorldPacket goQuery = new WorldPacket(Opcode.CMSG_QUERY_GAME_OBJECT);
				goQuery.WriteUInt32(transportEntry);
				goQuery.WriteUInt64(0); // empty guid
				SendPacket(goQuery);
			}
			Log.Print(LogType.Network, $"Pre-queried {GameData.TransportPeriods.Count} transport entries");
			this._isSuccessful = true;
			break;
		case AuthResult.AUTH_WAIT_QUEUE:
			this._queuePosition = packet.ReadUInt32();
			Log.Print(LogType.Network, $"Position in queue is {this._queuePosition}.", "HandleAuthResponse", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			if (this._isSuccessful.HasValue && this.GetSession().RealmSocket != null)
			{
				this.GetSession().RealmSocket.SendAuthWaitQue(this._queuePosition);
			}
			this._isSuccessful = true;
			break;
		default:
			Log.Print(LogType.Network, "Authentication failed!", "HandleAuthResponse", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
			this._isSuccessful = false;
			break;
		}
	}

	public void SendPing(uint ping, uint latency)
	{
		if (this.IsConnected() && this._isSuccessful != false)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_PING);
			packet.WriteUInt32(ping);
			packet.WriteUInt32(latency);
			this.SendPacket(packet);
		}
	}

	public void InitializePacketHandlers()
	{
		this._packetHandlers = new Dictionary<Opcode, Action<WorldPacket>>();
		MethodInfo[] methods = typeof(WorldClient).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			foreach (PacketHandlerAttribute msgAttr in methodInfo.GetCustomAttributes<PacketHandlerAttribute>())
			{
				if (msgAttr == null || msgAttr.Opcode == Opcode.MSG_NULL_ACTION)
				{
					continue;
				}
				if (this._packetHandlers.ContainsKey(msgAttr.Opcode))
				{
					Log.Print(LogType.Error, $"Tried to override OpcodeHandler of {this._packetHandlers[msgAttr.Opcode]} with {methodInfo.Name} (Opcode {msgAttr.Opcode})", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
				}
				else
				{
					ParameterInfo[] parameters = methodInfo.GetParameters();
					if (parameters.Length == 0)
					{
						Log.Print(LogType.Error, "Method: " + methodInfo.Name + " Has no parameters", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
						continue;
					}
					if (parameters[0].ParameterType != typeof(WorldPacket))
					{
						Log.Print(LogType.Error, "Method: " + methodInfo.Name + " has wrong BaseType", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Client\\WorldClient.cs");
						continue;
					}
					Action<WorldPacket> del = (Action<WorldPacket>)Delegate.CreateDelegate(typeof(Action<WorldPacket>), this, methodInfo);
					this._packetHandlers[msgAttr.Opcode] = del;
				}
			}
		}
	}
}
