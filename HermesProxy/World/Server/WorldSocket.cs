using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BNetServer;
using BNetServer.Services;
using Framework;
using Framework.Constants;
using Framework.Cryptography;
using Framework.IO;
using Framework.Logging;
using Framework.Networking;
using Framework.Realm;
using Google.Protobuf;
using HermesProxy.Auth;
using HermesProxy.Enums;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server;

public class WorldSocket : SocketBase, BnetServices.INetwork
{
	public struct ConnectToKey
	{
		public uint AccountId;

		public ConnectionType connectionType;

		public ulong Key;

		public ulong Raw
		{
			get
			{
				return (ulong)(this.AccountId | ((long)this.connectionType << 32)) | (this.Key << 33);
			}
			set
			{
				this.AccountId = (uint)(value & 0xFFFFFFFFu);
				this.connectionType = (ConnectionType)((value >> 32) & 1);
				this.Key = value >> 33;
			}
		}
	}

	public class CharacterLoginFailed : ServerPacket
	{
		private LoginFailureReason Code;

		public CharacterLoginFailed(LoginFailureReason code)
			: base(Opcode.SMSG_CHARACTER_LOGIN_FAILED)
		{
			this.Code = code;
		}

		public override void Write()
		{
			base._worldPacket.WriteUInt8((byte)this.Code);
		}
	}

	public class PacketHandler
	{
		private Action<WorldSocket, ClientPacket> methodCaller;

		private Type packetType;

		public PacketHandler(MethodInfo info, Type type)
		{
			this.methodCaller = (Action<WorldSocket, ClientPacket>)base.GetType().GetMethod("CreateDelegate", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type)
				.Invoke(null, new object[1] { info });
			this.packetType = type;
		}

		public void Invoke(WorldSocket session, WorldPacket packet)
		{
			if (this.packetType == null)
			{
				return;
			}
			using ClientPacket clientPacket = (ClientPacket)Activator.CreateInstance(this.packetType, packet);
			clientPacket.LogPacket(ref session.GetSession().ModernSniff);
			clientPacket.Read();
			this.methodCaller(session, clientPacket);
		}

		private static Action<WorldSocket, ClientPacket> CreateDelegate<P1>(MethodInfo method) where P1 : ClientPacket
		{
			Action<WorldSocket, P1> d = (Action<WorldSocket, P1>)method.CreateDelegate(typeof(Action<WorldSocket, P1>));
			return delegate(WorldSocket target, ClientPacket p)
			{
				d(target, (P1)p);
			};
		}
	}

	private static readonly string ClientConnectionInitialize = "WORLD OF WARCRAFT CONNECTION - CLIENT TO SERVER - V2";

	private static readonly string ServerConnectionInitialize = "WORLD OF WARCRAFT CONNECTION - SERVER TO CLIENT - V2";

	private static readonly byte[] AuthCheckSeed = new byte[16]
	{
		197, 198, 152, 149, 118, 63, 29, 205, 182, 161,
		55, 40, 179, 18, 255, 138
	};

	private static readonly byte[] SessionKeySeed = new byte[16]
	{
		88, 203, 207, 64, 254, 46, 206, 166, 90, 144,
		184, 1, 104, 108, 40, 11
	};

	private static readonly byte[] ContinuedSessionSeed = new byte[16]
	{
		22, 173, 12, 212, 70, 249, 79, 178, 239, 125,
		234, 42, 23, 102, 77, 47
	};

	private static readonly byte[] EncryptionKeySeed = new byte[16]
	{
		233, 117, 60, 80, 144, 147, 97, 218, 59, 7,
		238, 250, 255, 157, 65, 184
	};

	private static readonly int HeaderSize = 16;

	private SocketBuffer _headerBuffer;

	private SocketBuffer _packetBuffer;

	private ConnectionType _connectType;

	private ulong _key;

	private byte[] _serverChallenge;

	private WorldCrypt _worldCrypt;

	private byte[] _sessionKey;

	private byte[] _encryptKey;

	private ConnectToKey _instanceConnectKey;

	private RealmId _realmId;

	private ZLib.z_stream _compressionStream;

	private ConcurrentDictionary<Opcode, PacketHandler> _clientPacketTable = new ConcurrentDictionary<Opcode, PacketHandler>();

	private GlobalSessionData _globalSession;

	private Mutex _sendMutex = new Mutex();

	private BnetServices.ServiceManager _bnetRpc;

	public GlobalSessionData Session => this._globalSession;

	[PacketHandler(Opcode.CMSG_ARENA_TEAM_ROSTER)]
	private void HandleArenaTeamRoster(ArenaTeamRosterRequest arena)
	{
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) || this.GetSession().GameState.CurrentArenaTeamIds[arena.TeamIndex] == 0)
		{
			ArenaTeamRosterResponse response = new ArenaTeamRosterResponse();
			response.TeamSize = ModernVersion.GetArenaTeamSizeFromIndex(arena.TeamIndex);
			this.SendPacket(response);
			return;
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ARENA_TEAM_QUERY);
		packet.WriteUInt32(this.GetSession().GameState.CurrentArenaTeamIds[arena.TeamIndex]);
		this.SendPacketToServer(packet);
		WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ARENA_TEAM_ROSTER);
		packet2.WriteUInt32(this.GetSession().GameState.CurrentArenaTeamIds[arena.TeamIndex]);
		this.SendPacketToServer(packet2);
	}

	[PacketHandler(Opcode.CMSG_ARENA_TEAM_QUERY)]
	private void HandleArenaTeamQuery(ArenaTeamQuery arena)
	{
		if (this.GetSession().GameState.ArenaTeams.TryGetValue(arena.TeamId, out var team))
		{
			ArenaTeamQueryResponse response = new ArenaTeamQueryResponse();
			response.TeamId = arena.TeamId;
			response.Emblem = new ArenaTeamEmblem();
			response.Emblem.TeamId = arena.TeamId;
			response.Emblem.TeamSize = team.TeamSize;
			response.Emblem.BackgroundColor = team.BackgroundColor;
			response.Emblem.EmblemStyle = team.EmblemStyle;
			response.Emblem.EmblemColor = team.EmblemColor;
			response.Emblem.BorderStyle = team.BorderStyle;
			response.Emblem.BorderColor = team.BorderColor;
			response.Emblem.TeamName = team.Name;
			this.SendPacket(response);
		}
	}

	[PacketHandler(Opcode.CMSG_BATTLEMASTER_JOIN_ARENA)]
	private void HandleBattlematerJoinArena(BattlemasterJoinArena join)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEMASTER_JOIN_ARENA);
		packet.WriteGuid(join.Guid.To64());
		packet.WriteUInt8(join.TeamIndex);
		packet.WriteBool(data: true);
		packet.WriteBool(data: true);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BATTLEMASTER_JOIN_SKIRMISH)]
	private void HandleBattlematerJoinSkirmish(BattlemasterJoinSkirmish join)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEMASTER_JOIN_ARENA);
		packet.WriteGuid(join.Guid.To64());
		packet.WriteUInt8(join.TeamSize);
		packet.WriteBool(join.AsGroup);
		packet.WriteBool(data: false);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ARENA_TEAM_REMOVE)]
	[PacketHandler(Opcode.CMSG_ARENA_TEAM_LEADER)]
	private void HandleArenaUnimplemented(ArenaTeamRemove arena)
	{
		WorldPacket packet = new WorldPacket(arena.GetUniversalOpcode());
		packet.WriteUInt32(arena.TeamId);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(arena.PlayerGuid));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ARENA_TEAM_DISBAND)]
	[PacketHandler(Opcode.CMSG_ARENA_TEAM_LEAVE)]
	private void HandleArenaTeamLeave(ArenaTeamLeave arena)
	{
		WorldPacket packet = new WorldPacket(arena.GetUniversalOpcode());
		packet.WriteUInt32(arena.TeamId);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ARENA_TEAM_ACCEPT)]
	[PacketHandler(Opcode.CMSG_ARENA_TEAM_DECLINE)]
	private void HandleArenaTeamInviteResponse(ArenaTeamAccept arena)
	{
		WorldPacket packet = new WorldPacket(arena.GetUniversalOpcode());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_HELLO_REQUEST)]
	private void HandleAuctionHelloRequest(InteractWithNPC interact)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_AUCTION_HELLO);
		packet.WriteGuid(interact.CreatureGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_LIST_BIDDED_ITEMS)]
	private void HandleAuctionListBidderItems(AuctionListBidderItems auction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_BIDDED_ITEMS);
		packet.WriteGuid(auction.Auctioneer.To64());
		packet.WriteUInt32(auction.Offset);
		packet.WriteInt32(auction.AuctionItemIDs.Count);
		foreach (uint itemId in auction.AuctionItemIDs)
		{
			packet.WriteUInt32(itemId);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_LIST_OWNED_ITEMS)]
	private void HandleAuctionListOwnerItems(AuctionListOwnerItems auction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_OWNED_ITEMS);
		packet.WriteGuid(auction.Auctioneer.To64());
		packet.WriteUInt32(auction.Offset);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_LIST_ITEMS)]
	private void HandleAuctionListItems(AuctionListItems auction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_LIST_ITEMS);
		packet.WriteGuid(auction.Auctioneer.To64());
		packet.WriteUInt32(auction.Offset);
		packet.WriteCString(auction.Name);
		packet.WriteUInt8(auction.MinLevel);
		packet.WriteUInt8(auction.MaxLevel);
		if (auction.ClassFilters.Count > 0)
		{
			if (auction.ClassFilters[0].SubClassFilters.Count == 1)
			{
				packet.WriteInt32(ModernToLegacyInventorySlotType(auction.ClassFilters[0].SubClassFilters[0].InvTypeMask));
				packet.WriteInt32(auction.ClassFilters[0].ItemClass);
				packet.WriteInt32(auction.ClassFilters[0].SubClassFilters[0].ItemSubclass);
			}
			else
			{
				packet.WriteInt32(-1);
				packet.WriteInt32(auction.ClassFilters[0].ItemClass);
				packet.WriteInt32(-1);
			}
		}
		else
		{
			packet.WriteInt32(-1);
			packet.WriteInt32(-1);
			packet.WriteInt32(-1);
		}
		packet.WriteInt32(auction.Quality);
		packet.WriteBool(auction.OnlyUsable);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteBool(auction.ExactMatch);
			packet.WriteUInt8((byte)auction.Sorts.Count);
			foreach (AuctionSort sort in auction.Sorts)
			{
				packet.WriteUInt8(sort.Type);
				packet.WriteUInt8(sort.Direction);
			}
		}
		this.SendPacketToServer(packet);
		static int ModernToLegacyInventorySlotType(uint modernInventoryFlag)
		{
			if (modernInventoryFlag == uint.MaxValue)
			{
				return -1;
			}
			for (int i = 0; i < 32; i++)
			{
				if ((modernInventoryFlag & (1 << i)) > 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	private int ModernToLegacyInventorySlotType(uint modernInventoryFlag)
	{
		if (modernInventoryFlag == uint.MaxValue)
		{
			return -1;
		}
		for (byte i = 0; i < 32; i++)
		{
			if ((modernInventoryFlag & (uint)(1 << (int)i)) != 0)
			{
				return i;
			}
		}
		return -1;
	}

	[PacketHandler(Opcode.CMSG_AUCTION_SELL_ITEM)]
	private void HandleAuctionSellItem(AuctionSellItem auction)
	{
		uint expireTime = auction.ExpireTime;
		if (LegacyVersion.ExpansionVersion <= 1 && ModernVersion.ExpansionVersion > 1)
		{
			switch (expireTime)
			{
			case 720u:
				expireTime = 120u;
				break;
			case 1440u:
				expireTime = 480u;
				break;
			case 2880u:
				expireTime = 1440u;
				break;
			}
		}
		else if (LegacyVersion.ExpansionVersion > 1 && ModernVersion.ExpansionVersion <= 1)
		{
			switch (expireTime)
			{
			case 120u:
				expireTime = 720u;
				break;
			case 480u:
				expireTime = 1440u;
				break;
			case 1440u:
				expireTime = 2880u;
				break;
			}
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_2_2a_10505))
		{
			foreach (AuctionItemForSale item in auction.Items)
			{
				WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_SELL_ITEM);
				packet.WriteGuid(auction.Auctioneer.To64());
				packet.WriteGuid(item.Guid.To64());
				packet.WriteUInt32((uint)auction.MinBid);
				packet.WriteUInt32((uint)auction.BuyoutPrice);
				packet.WriteUInt32(expireTime);
				this.SendPacketToServer(packet);
			}
			return;
		}
		WorldPacket packet2 = new WorldPacket(Opcode.CMSG_AUCTION_SELL_ITEM);
		packet2.WriteGuid(auction.Auctioneer.To64());
		packet2.WriteInt32(auction.Items.Count);
		foreach (AuctionItemForSale item2 in auction.Items)
		{
			packet2.WriteGuid(item2.Guid.To64());
			packet2.WriteUInt32(item2.UseCount);
		}
		packet2.WriteUInt32((uint)auction.MinBid);
		packet2.WriteUInt32((uint)auction.BuyoutPrice);
		packet2.WriteUInt32(expireTime);
		this.SendPacketToServer(packet2);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_REMOVE_ITEM)]
	private void HandleAuctionRemoveItem(AuctionRemoveItem auction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_REMOVE_ITEM);
		packet.WriteGuid(auction.Auctioneer.To64());
		packet.WriteUInt32(auction.AuctionID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUCTION_PLACE_BID)]
	private void HandleAuctionPlaceBId(AuctionPlaceBid auction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUCTION_PLACE_BID);
		packet.WriteGuid(auction.Auctioneer.To64());
		packet.WriteUInt32(auction.AuctionID);
		packet.WriteUInt32((uint)auction.BidAmount);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BATTLEMASTER_JOIN)]
	private void HandleBattlefieldJoin(BattlemasterJoin join)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEMASTER_JOIN);
		packet.WriteGuid(join.BattlemasterGuid.To64());
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(GameData.GetMapIdFromBattlegroundId(join.BattlefieldListId));
		}
		else
		{
			packet.WriteUInt32(join.BattlefieldListId);
		}
		packet.WriteInt32(join.BattlefieldInstanceID);
		packet.WriteBool(join.JoinAsGroup);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BATTLEFIELD_PORT)]
	private void HandleBattlefieldPort(BattlefieldPort port)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEFIELD_PORT);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt8(2);
			packet.WriteUInt8(0);
			packet.WriteUInt32(this.GetSession().GameState.GetBattleFieldQueueType(port.Ticket.Id));
			packet.WriteUInt16(8080);
			packet.WriteBool(port.AcceptedInvite);
		}
		else
		{
			packet.WriteUInt32(this.GetSession().GameState.GetBattleFieldQueueType(port.Ticket.Id));
			packet.WriteBool(port.AcceptedInvite);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_BATTLEFIELD_STATUS)]
	private void HandleRequestBattlefieldStatus(RequestBattlefieldStatus log)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEFIELD_STATUS);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PVP_LOG_DATA)]
	private void HandlePvPLogData(PVPLogDataRequest log)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_PVP_LOG_DATA);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BATTLEFIELD_LIST)]
	private void HandleBattlefieldList(BattlefieldListRequest request)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEFIELD_LIST);
		packet.WriteUInt32((uint)request.ListID);
		packet.WriteUInt8(0); // fromWhere: 0=battlemaster, 1=UI
		packet.WriteUInt8(1); // canGainXP
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BATTLEFIELD_LEAVE)]
	private void HandleBattlefieldLeave(BattlefieldLeave leave)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BATTLEFIELD_LEAVE);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt8(2);
			packet.WriteUInt8(0);
			packet.WriteUInt32(this.GetSession().GameState.GetBattleFieldQueueType(1u));
			packet.WriteUInt16(8080);
		}
		else
		{
			packet.WriteUInt32(this.GetSession().GameState.CurrentMapId.Value);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ENUM_CHARACTERS)]
	private void HandleEnumCharacters(EnumCharacters charEnum)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ENUM_CHARACTERS);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GET_ACCOUNT_CHARACTER_LIST)]
	private void HandleGetAccountCharacterList(GetAccountCharacterListRequest request)
	{
		GetAccountCharacterListResult response = new GetAccountCharacterListResult();
		response.Token = request.Token;
		foreach (OwnCharacterInfo ownCharacter in this.GetSession().GameState.OwnCharacters)
		{
			response.CharacterList.Add(new AccountCharacterListEntry
			{
				AccountId = WowGuid128.Create(HighGuidType703.WowAccount, this.GetSession().GameAccountInfo.Id),
				CharacterGuid = ownCharacter.CharacterGuid,
				RealmVirtualAddress = this.GetSession().RealmId.GetAddress(),
				RealmName = "",
				LastLoginUnixSec = ownCharacter.LastLoginUnixSec,
				Name = ownCharacter.Name,
				Race = ownCharacter.RaceId,
				Class = ownCharacter.ClassId,
				Sex = ownCharacter.SexId,
				Level = ownCharacter.Level
			});
		}
		this.SendPacket(response);
	}

	[PacketHandler(Opcode.CMSG_GENERATE_RANDOM_CHARACTER_NAME)]
	private void HandleGenerateRandomCharacterNameRequest(GenerateRandomCharacterNameRequest randomCharacterName)
	{
		GenerateRandomCharacterNameResult result = new GenerateRandomCharacterNameResult();
		result.Success = false;
		this.SendPacket(result);
	}

	[PacketHandler(Opcode.CMSG_CREATE_CHARACTER)]
	private void HandleCreateCharacter(CreateCharacter charCreate)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CREATE_CHARACTER);
		packet.WriteCString(charCreate.CreateInfo.Name);
		packet.WriteUInt8((byte)charCreate.CreateInfo.RaceId);
		packet.WriteUInt8((byte)charCreate.CreateInfo.ClassId);
		packet.WriteUInt8((byte)charCreate.CreateInfo.Sex);
		CharacterCustomizations.ConvertModernCustomizationsToLegacy(charCreate.CreateInfo.Customizations, out var skin, out var face, out var hairStyle, out var hairColor, out var facialhair);
		packet.WriteUInt8(skin);
		packet.WriteUInt8(face);
		packet.WriteUInt8(hairStyle);
		packet.WriteUInt8(hairColor);
		packet.WriteUInt8(facialhair);
		packet.WriteUInt8(0);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CHAR_DELETE)]
	private void HandleCharDelete(CharDelete charDelete)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAR_DELETE);
		packet.WriteGuid(charDelete.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOADING_SCREEN_NOTIFY)]
	private void HandleLoadScreen(LoadingScreenNotify loadingScreenNotify)
	{
		if (loadingScreenNotify.MapID >= 0)
		{
			this.GetSession().GameState.CurrentMapId = loadingScreenNotify.MapID;
		}
	}

	[PacketHandler(Opcode.CMSG_QUERY_PLAYER_NAME)]
	private void HandleNameQueryRequest(QueryPlayerName queryPlayerName)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_NAME_QUERY);
		packet.WriteGuid(queryPlayerName.Player.To64());
		this.SendPacketToServer(packet, (!this.GetSession().GameState.IsInWorld) ? Opcode.SMSG_LOGIN_VERIFY_WORLD : Opcode.MSG_NULL_ACTION);
	}

	[PacketHandler(Opcode.CMSG_QUERY_PLAYER_NAMES)]
	private void HandleNamesQueryRequest(QueryPlayerNames queryPlayerNames)
	{
		foreach (WowGuid128 guid in queryPlayerNames.Players)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_NAME_QUERY);
			packet.WriteGuid(guid.To64());
			this.SendPacketToServer(packet, (!this.GetSession().GameState.IsInWorld) ? Opcode.SMSG_LOGIN_VERIFY_WORLD : Opcode.MSG_NULL_ACTION);
		}
	}

	[PacketHandler(Opcode.CMSG_PLAYER_LOGIN)]
	private void HandlePlayerLogin(PlayerLogin playerLogin)
	{
		if (!this.GetSession().GameState.CachedPlayers.TryGetValue(playerLogin.Guid, out var selectedChar))
		{
			Log.Print(LogType.Error, $"Player tried to log in with unknown char id: {playerLogin.Guid}", "HandlePlayerLogin", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\CharacterHandler.cs");
			return;
		}
		Realm realm = this.GetSession().RealmManager.GetRealm(this.GetSession().RealmId);
		if (realm == null)
		{
			Log.Print(LogType.Error, $"Player tried to log in to unknown realm id: {this.GetSession().RealmId}", "HandlePlayerLogin", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\CharacterHandler.cs");
			return;
		}
		this.GetSession().AccountMetaDataMgr.SaveLastSelectedCharacter(realm.Name, selectedChar.Name, playerLogin.Guid.Low, Time.UnixTime);
		if (this.GetSession().AuthClient != null)
		{
			this.GetSession().AuthClient.Disconnect();
		}
		this.SendConnectToInstance(ConnectToSerial.WorldAttempt1);
		this.GetSession().GameState.IsConnectedToInstance = true;
		this.GetSession().GameState.IsFirstEnterWorld = true;
		this.GetSession().GameState.CurrentPlayerGuid = playerLogin.Guid;
		this.GetSession().GameState.CurrentPlayerInfo = this.GetSession().GameState.OwnCharacters.Single((OwnCharacterInfo x) => x.CharacterGuid == playerLogin.Guid);
		this.GetSession().GameState.CurrentPlayerStorage.LoadCurrentPlayer();
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PLAYER_LOGIN);
		packet.WriteGuid(playerLogin.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOGOUT_REQUEST)]
	private void HandleLogoutRequest(LogoutRequest logoutRequest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOGOUT_REQUEST);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOGOUT_CANCEL)]
	private void HandleLogoutCancel(LogoutCancel logoutCancel)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOGOUT_CANCEL);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_PLAYED_TIME)]
	private void HandleRequestPlayedTime(RequestPlayedTime played)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REQUEST_PLAYED_TIME);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteBool(played.TriggerScriptEvent);
		}
		this.SendPacketToServer(packet);
		this.GetSession().GameState.ShowPlayedTime = played.TriggerScriptEvent;
	}

	[PacketHandler(Opcode.CMSG_SET_TITLE)]
	private void HandleTogglePvP(SetTitle title)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_TITLE);
		packet.WriteInt32(title.TitleID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TOGGLE_PVP)]
	private void HandleTogglePvP(TogglePvP pvp)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_TOGGLE_PVP);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_PVP)]
	private void HandleTogglePvP(SetPvP pvp)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_TOGGLE_PVP);
		packet.WriteBool(pvp.Enable);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_ACTION_BUTTON)]
	private void HandleSetActionButton(SetActionButton button)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ACTION_BUTTON);
		packet.WriteUInt8(button.Index);
		packet.WriteUInt16(button.Action);
		packet.WriteUInt16(button.Type);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_ACTION_BAR_TOGGLES)]
	private void HandleSetActionBarToggles(SetActionBarToggles bars)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ACTION_BAR_TOGGLES);
		packet.WriteUInt8(bars.Mask);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_UNLEARN_SKILL)]
	private void HandleUnlearnSkill(UnlearnSkill skill)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_UNLEARN_SKILL);
		packet.WriteUInt32(skill.SkillLine);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PLAYER_SHOWING_CLOAK)]
	[PacketHandler(Opcode.CMSG_PLAYER_SHOWING_HELM)]
	private void HandleShowHelmOrCloak(PlayerShowingHelmOrCloak show)
	{
		WorldPacket packet = new WorldPacket(show.GetUniversalOpcode());
		packet.WriteBool(show.Showing);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_INSPECT)]
	private void HandleInspect(Inspect inspect)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_INSPECT);
		packet.WriteGuid(inspect.Target.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_INSPECT_HONOR_STATS)]
	private void HandleInspectHonorStats(Inspect inspect)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_INSPECT_HONOR_STATS);
		packet.WriteGuid(inspect.Target.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_INSPECT_PVP)]
	private void HandleInspectArenaTeams(Inspect inspect)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.MSG_INSPECT_ARENA_TEAMS);
			packet.WriteGuid(inspect.Target.To64());
			this.SendPacketToServer(packet);
			return;
		}
		InspectPvP pvp = new InspectPvP();
		pvp.PlayerGUID = inspect.Target;
		pvp.ArenaTeams.Add(new ArenaTeamInspectData());
		pvp.ArenaTeams.Add(new ArenaTeamInspectData());
		pvp.ArenaTeams.Add(new ArenaTeamInspectData());
		this.SendPacket(pvp);
	}

	[PacketHandler(Opcode.CMSG_CHARACTER_RENAME_REQUEST)]
	private void HandleCharacterRenameRequest(CharacterRenameRequest rename)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CHARACTER_RENAME_REQUEST);
		packet.WriteGuid(rename.Guid.To64());
		packet.WriteCString(rename.NewName);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CHAT_JOIN_CHANNEL)]
	private void HandleChatJoinChannel(JoinChannel join)
	{
		if (this.GetSession().WorldClient != null)
		{
			this.GetSession().WorldClient.SendChatJoinChannel(join.ChatChannelId, join.ChannelName, join.Password);
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_LEAVE_CHANNEL)]
	private void HandleChatLeaveChannel(LeaveChannel leave)
	{
		if (this.GetSession().WorldClient != null)
		{
			this.GetSession().GameState.LeftChannelName = leave.ChannelName;
			this.GetSession().WorldClient.SendChatLeaveChannel(leave.ZoneChannelID, leave.ChannelName);
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_CHANNEL_OWNER)]
	[PacketHandler(Opcode.CMSG_CHAT_CHANNEL_ANNOUNCEMENTS)]
	private void HandleChatChannelCommand(ChannelCommand command)
	{
		WorldPacket packet = new WorldPacket(command.GetUniversalOpcode());
		packet.WriteCString(command.ChannelName);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CHAT_CHANNEL_LIST)]
	private void HandleChatChannelList(ChannelCommand command)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAT_CHANNEL_LIST);
		packet.WriteCString(command.ChannelName);
		this.SendPacketToServer(packet);
		this.GetSession().GameState.ChannelDisplayList = false;
	}

	[PacketHandler(Opcode.CMSG_CHAT_CHANNEL_DISPLAY_LIST)]
	private void HandleChatChannelDisplayList(ChannelCommand command)
	{
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAT_CHANNEL_LIST);
			packet.WriteCString(command.ChannelName);
			this.SendPacketToServer(packet);
		}
		else
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_CHAT_CHANNEL_DISPLAY_LIST);
			packet2.WriteCString(command.ChannelName);
			this.SendPacketToServer(packet2);
		}
		this.GetSession().GameState.ChannelDisplayList = true;
	}

	[PacketHandler(Opcode.CMSG_CHAT_CHANNEL_DECLINE_INVITE)]
	private void HandleChatChannelDeclineInvite(ChannelCommand command)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_CHAT_CHANNEL_DECLINE_INVITE);
			packet.WriteCString(command.ChannelName);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_AFK)]
	private void HandleChatMessageAFK(ChatMessageAFK afk)
	{
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(afk.Text);
		if (toBeSentTextParts.Count >= 1)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.GetSession().WorldClient.SendMessageChatWotLK(ChatMessageTypeWotLK.Afk, 0u, toBeSentTextParts[0], "", "");
			}
			else
			{
				this.GetSession().WorldClient.SendMessageChatVanilla(ChatMessageTypeVanilla.Afk, 0u, toBeSentTextParts[0], "", "");
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_DND)]
	private void HandleChatMessageDND(ChatMessageDND dnd)
	{
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(dnd.Text);
		if (toBeSentTextParts.Count >= 1)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.GetSession().WorldClient.SendMessageChatWotLK(ChatMessageTypeWotLK.Dnd, 0u, toBeSentTextParts[0], "", "");
			}
			else
			{
				this.GetSession().WorldClient.SendMessageChatVanilla(ChatMessageTypeVanilla.Dnd, 0u, toBeSentTextParts[0], "", "");
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_CHANNEL)]
	private void HandleChatMessageChannel(ChatMessageChannel channel)
	{
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(channel.Text);
		foreach (string text in toBeSentTextParts)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.GetSession().WorldClient.SendMessageChatWotLK(ChatMessageTypeWotLK.Channel, channel.Language, text, channel.Target, "");
			}
			else
			{
				this.GetSession().WorldClient.SendMessageChatVanilla(ChatMessageTypeVanilla.Channel, channel.Language, text, channel.Target, "");
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_WHISPER)]
	private void HandleChatMessageWhisper(ChatMessageWhisper whisper)
	{
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(whisper.Text);
		foreach (string text in toBeSentTextParts)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.GetSession().WorldClient.SendMessageChatWotLK(ChatMessageTypeWotLK.Whisper, whisper.Language, text, "", whisper.Target);
			}
			else
			{
				this.GetSession().WorldClient.SendMessageChatVanilla(ChatMessageTypeVanilla.Whisper, whisper.Language, text, "", whisper.Target);
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_EMOTE)]
	private void HandleChatMessageEmote(ChatMessageEmote emote)
	{
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(emote.Text);
		if (toBeSentTextParts.Count >= 1)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.GetSession().WorldClient.SendMessageChatWotLK(ChatMessageTypeWotLK.Emote, 0u, toBeSentTextParts[0], "", "");
			}
			else
			{
				this.GetSession().WorldClient.SendMessageChatVanilla(ChatMessageTypeVanilla.Emote, 0u, toBeSentTextParts[0], "", "");
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_GUILD)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_OFFICER)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_PARTY)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_RAID)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_RAID_WARNING)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_SAY)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_YELL)]
	[PacketHandler(Opcode.CMSG_CHAT_MESSAGE_INSTANCE_CHAT)]
	private void HandleChatMessage(ChatMessage packet)
	{
		ChatMessageTypeModern type;
		switch (packet.GetUniversalOpcode())
		{
		case Opcode.CMSG_CHAT_MESSAGE_SAY:
			type = ChatMessageTypeModern.Say;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_YELL:
			type = ChatMessageTypeModern.Yell;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_GUILD:
			type = ChatMessageTypeModern.Guild;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_OFFICER:
			type = ChatMessageTypeModern.Officer;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_PARTY:
			type = ChatMessageTypeModern.Party;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_RAID:
			type = ChatMessageTypeModern.Raid;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_RAID_WARNING:
			type = ChatMessageTypeModern.RaidWarning;
			break;
		case Opcode.CMSG_CHAT_MESSAGE_INSTANCE_CHAT:
			type = ((!this.GetSession().GameState.IsInBattleground()) ? ChatMessageTypeModern.Party : ChatMessageTypeModern.Battleground);
			break;
		default:
			Log.Print(LogType.Error, $"HandleMessagechatOpcode : Unknown chat opcode ({packet.GetOpcode()})", "HandleChatMessage", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\ChatHandler.cs");
			return;
		}
		List<string> toBeSentTextParts = WorldSocket.ConvertTextMessageIntoMaxLengthParts(packet.Text);
		foreach (string text in toBeSentTextParts)
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				ChatMessageTypeWotLK chatMsg = (ChatMessageTypeWotLK)Enum.Parse(typeof(ChatMessageTypeWotLK), type.ToString());
				this.GetSession().WorldClient.SendMessageChatWotLK(chatMsg, packet.Language, text, "", "");
			}
			else
			{
				ChatMessageTypeVanilla chatMsg2 = (ChatMessageTypeVanilla)Enum.Parse(typeof(ChatMessageTypeVanilla), type.ToString());
				this.GetSession().WorldClient.SendMessageChatVanilla(chatMsg2, packet.Language, text, "", "");
			}
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_ADDON_MESSAGE)]
	private void HandleAddonMessage(ChatAddonMessage packet)
	{
		uint language = uint.MaxValue;
		string text = packet.Params.Prefix + "\t" + packet.Params.Text;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			ChatMessageTypeWotLK chatMsg = (ChatMessageTypeWotLK)Enum.Parse(typeof(ChatMessageTypeWotLK), packet.Params.Type.ToString());
			this.GetSession().WorldClient.SendMessageChatWotLK(chatMsg, language, text, "", "");
		}
		else
		{
			ChatMessageTypeVanilla chatMsg2 = (ChatMessageTypeVanilla)Enum.Parse(typeof(ChatMessageTypeVanilla), packet.Params.Type.ToString());
			this.GetSession().WorldClient.SendMessageChatVanilla(chatMsg2, language, text, "", "");
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_ADDON_MESSAGE_TARGETED)]
	private void HandleAddonMessageTargeted(ChatAddonMessageTargeted packet)
	{
		uint language = uint.MaxValue;
		string text = packet.Params.Prefix + "\t" + packet.Params.Text;
		string channelName = (packet.ChannelGuid.IsEmpty() ? "" : this.GetSession().GameState.GetChannelName((int)packet.ChannelGuid.GetCounter()));
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			ChatMessageTypeWotLK chatMsg = (ChatMessageTypeWotLK)Enum.Parse(typeof(ChatMessageTypeWotLK), packet.Params.Type.ToString());
			this.GetSession().WorldClient.SendMessageChatWotLK(chatMsg, language, text, channelName, packet.Target);
		}
		else
		{
			ChatMessageTypeVanilla chatMsg2 = (ChatMessageTypeVanilla)Enum.Parse(typeof(ChatMessageTypeVanilla), packet.Params.Type.ToString());
			this.GetSession().WorldClient.SendMessageChatVanilla(chatMsg2, language, text, channelName, packet.Target);
		}
	}

	[PacketHandler(Opcode.CMSG_SEND_TEXT_EMOTE)]
	private void HandleSendTextEmote(CTextEmote emote)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SEND_TEXT_EMOTE);
		packet.WriteInt32(emote.EmoteID);
		packet.WriteInt32(emote.SoundIndex);
		packet.WriteGuid(emote.Target.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CHAT_REGISTER_ADDON_PREFIXES)]
	private void HandleChatRegisterAddonPrefixes(ChatRegisterAddonPrefixes addons)
	{
		foreach (string prefix in addons.Prefixes)
		{
			this.GetSession().GameState.AddonPrefixes.Add(prefix);
		}
	}

	[PacketHandler(Opcode.CMSG_CHAT_UNREGISTER_ALL_ADDON_PREFIXES)]
	private void HandleChatUnregisterAllAddonPrefixes(EmptyClientPacket addons)
	{
		this.GetSession().GameState.AddonPrefixes.Clear();
	}

	private static List<string> ConvertTextMessageIntoMaxLengthParts(string originalTextMessage)
	{
		List<string> toBeSendTextParts = new List<string>();
		if (originalTextMessage.Length <= 255)
		{
			toBeSendTextParts.Add(originalTextMessage);
		}
		else
		{
			string linkBegin = "(?=\\|c[a-f0-9]{8}\\|H)";
			string linkEnd = "(?<=\\|h\\|r)";
			string[] splitted = Regex.Split(originalTextMessage, linkBegin + "|" + linkEnd);
			IEnumerable<char[]> splittedAndSlicedToMaxLength = splitted.SelectMany((string x) => x.Chunk(255));
			StringBuilder strBuilder = new StringBuilder();
			foreach (char[] part in splittedAndSlicedToMaxLength)
			{
				if (strBuilder.Length + part.Length > 255)
				{
					toBeSendTextParts.Add(strBuilder.ToString());
					strBuilder.Clear();
				}
				strBuilder.Append(part);
			}
			toBeSendTextParts.Add(strBuilder.ToString());
		}
		return toBeSendTextParts;
	}

	[PacketHandler(Opcode.CMSG_UPDATE_ACCOUNT_DATA)]
	private void HandleUpdateAccountData(UserClientUpdateAccountData data)
	{
		this.GetSession().AccountDataMgr.SaveData(data.PlayerGuid, data.Time, data.DataType, data.Size, data.CompressedData);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_ACCOUNT_DATA)]
	private void HandleRequestAccountData(RequestAccountData data)
	{
		if (this.GetSession().AccountDataMgr.Data[data.DataType] == null)
		{
			Log.Print(LogType.Error, $"Client requested missing account data {data.DataType}.", "HandleRequestAccountData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\ClientConfigHandler.cs");
			this.GetSession().AccountDataMgr.Data[data.DataType] = new AccountData();
			this.GetSession().AccountDataMgr.Data[data.DataType].Type = data.DataType;
			this.GetSession().AccountDataMgr.Data[data.DataType].Timestamp = Time.UnixTime;
			this.GetSession().AccountDataMgr.Data[data.DataType].UncompressedSize = 0u;
			this.GetSession().AccountDataMgr.Data[data.DataType].CompressedData = new byte[0];
		}
		this.GetSession().AccountDataMgr.Data[data.DataType].Guid = data.PlayerGuid;
		UpdateAccountData update = new UpdateAccountData(this.GetSession().AccountDataMgr.Data[data.DataType]);
		this.SendPacket(update);
	}

	[PacketHandler(Opcode.CMSG_SAVE_CUF_PROFILES)]
	private void HandleUpdateAccountData(SaveCUFProfiles cuf)
	{
		this.GetSession().AccountDataMgr.SaveCUFProfiles(cuf.Data);
	}

	[PacketHandler(Opcode.CMSG_ATTACK_SWING)]
	private void HandleAttackSwing(AttackSwing attack)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ATTACK_SWING);
		packet.WriteGuid(attack.Victim.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ATTACK_STOP)]
	private void HandleAttackSwing(AttackStop attack)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ATTACK_STOP);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_SHEATHED)]
	private void HandleSetSheathed(SetSheathed sheath)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_SHEATHED);
		packet.WriteInt32(sheath.SheathState);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CAN_DUEL)]
	private void HandleCanDuel(CanDuel request)
	{
		CanDuelResult result = new CanDuelResult();
		result.TargetGUID = request.TargetGUID;
		result.Result = true;
		this.SendPacket(result);
	}

	[PacketHandler(Opcode.CMSG_DUEL_RESPONSE)]
	private void HandleDuelResponse(DuelResponse response)
	{
		if (response.Accepted)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_DUEL_ACCEPTED);
			packet.WriteGuid(response.ArbiterGUID.To64());
			this.SendPacketToServer(packet);
		}
		else
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_DUEL_CANCELLED);
			packet2.WriteGuid(response.ArbiterGUID.To64());
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.CMSG_GAME_OBJ_USE)]
	private void HandleGameObjUse(GameObjUse use)
	{
		WowGuid64 guid64 = use.Guid.To64();
		Framework.Logging.Log.Print(Framework.Logging.LogType.Debug, $"[GameObjUse] Modern GUID={use.Guid} -> Legacy GUID={guid64} raw=0x{guid64.GetLowValue():X16}");
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GAME_OBJ_USE);
		packet.WriteGuid(guid64);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GAME_OBJ_REPORT_USE)]
	private void HandleGameObjUse(GameObjReportUse use)
	{
		this.GetSession().GameState.CurrentInteractedWithGO = use.Guid;
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GAME_OBJ_REPORT_USE);
		packet.WriteGuid(use.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PARTY_INVITE)]
	private void HandleUpdateRaidTarget(PartyInviteClient invite)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PARTY_INVITE);
		packet.WriteCString(invite.TargetName);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PARTY_INVITE_RESPONSE)]
	private void HandlePartyInviteResponse(PartyInviteResponse invite)
	{
		if (invite.Accept)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_GROUP_ACCEPT);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				packet.WriteUInt32(0u);
			}
			this.SendPacketToServer(packet);
		}
		else
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_GROUP_DECLINE);
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.CMSG_LEAVE_GROUP)]
	private void HandleLeaveGroup(LeaveGroup leave)
	{
		this.GetSession().GameState.WeWantToLeaveGroup = true;
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GROUP_DISBAND);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PARTY_UNINVITE)]
	private void HandlePartyUninvite(PartyUninvite kick)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GROUP_UNINVITE_GUID);
		packet.WriteGuid(kick.TargetGUID.To64());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteCString(kick.Reason);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_ASSISTANT_LEADER)]
	private void HandleSetAssistantLeader(SetAssistantLeader assist)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ASSISTANT_LEADER);
		packet.WriteGuid(assist.TargetGUID.To64());
		packet.WriteBool(assist.Apply);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_EVERYONE_IS_ASSISTANT)]
	private void HandleSetAssistantLeader(SetEveryoneIsAssistant assist)
	{
		List<PartyPlayerInfo> groupMembers = this.GetSession().GameState.GetCurrentGroup().PlayerList;
		foreach (PartyPlayerInfo member in groupMembers)
		{
			if (!(member.GUID == this.GetSession().GameState.CurrentPlayerGuid))
			{
				WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ASSISTANT_LEADER);
				packet.WriteGuid(member.GUID.To64());
				packet.WriteBool(assist.Apply);
				this.SendPacketToServer(packet);
			}
		}
	}

	[PacketHandler(Opcode.CMSG_SET_PARTY_LEADER)]
	private void HandleSetPartyLeader(SetPartyLeader leader)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_PARTY_LEADER);
		packet.WriteGuid(leader.TargetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CONVERT_RAID)]
	private void HandleConvertRaid(ConvertRaid raid)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CONVERT_RAID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DO_READY_CHECK)]
	private void HandlReadyCheck(DoReadyCheck raid)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_RAID_READY_CHECK);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_READY_CHECK_RESPONSE)]
	private void HandlReadyCheckResponse(ReadyCheckResponseClient raid)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_RAID_READY_CHECK);
		packet.WriteBool(raid.IsReady);
		this.SendPacketToServer(packet);
		ReadyCheckResponse ready = new ReadyCheckResponse();
		ready.Player = this.GetSession().GameState.CurrentPlayerGuid;
		ready.IsReady = raid.IsReady;
		ready.PartyGUID = WowGuid128.Create(HighGuidType703.Party, 1000uL);
		this.SendPacket(ready);
	}

	[PacketHandler(Opcode.CMSG_UPDATE_RAID_TARGET)]
	private void HandleUpdateRaidTarget(UpdateRaidTarget update)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_RAID_TARGET_UPDATE);
		packet.WriteInt8(update.Symbol);
		packet.WriteGuid(update.Target.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SUMMON_RESPONSE)]
	private void HandleSummonResponse(SummonResponse update)
	{
		if (update.Accept || LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_SUMMON_RESPONSE);
			packet.WriteGuid(update.SummonerGUID.To64());
			packet.WriteBool(update.Accept);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_MINIMAP_PING)]
	private void HandleMinimapPing(MinimapPingClient ping)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_MINIMAP_PING);
		packet.WriteVector2(ping.Position);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_RANDOM_ROLL)]
	private void HandleMinimapPing(RandomRollClient roll)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_RANDOM_ROLL);
		packet.WriteInt32(roll.Min);
		packet.WriteInt32(roll.Max);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_PARTY_MEMBER_STATS)]
	private void HandleRequestPartyMemberStats(RequestPartyMemberStats request)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REQUEST_PARTY_MEMBER_STATS);
		packet.WriteGuid(request.TargetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GROUP_CHANGE_SUB_GROUP)]
	private void HandleGroupChangeSubGroup(ChangeSubGroup group)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GROUP_CHANGE_SUB_GROUP);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(group.TargetGUID));
		packet.WriteUInt8(group.NewSubGroup);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GROUP_SWAP_SUB_GROUP)]
	private void HandleGroupSwapSubGroup(SwapSubGroups group)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GROUP_SWAP_SUB_GROUP);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(group.FirstTarget));
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(group.SecondTarget));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_GUILD_INFO)]
	private void HandleQueryGuildInfo(QueryGuildInfo query)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_GUILD_INFO);
		packet.WriteUInt32((uint)query.GuildGuid.GetCounter());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_PERMISSIONS_QUERY)]
	private void HandleGuildPermissionsQuery(GuildPermissionsQuery query)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.MSG_GUILD_PERMISSIONS);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_REMAINING_WITHDRAW_MONEY_QUERY)]
	private void HandleGuildBankRemainingWithdrawnMoneyQuery(GuildBankRemainingWithdrawMoneyQuery query)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.MSG_GUILD_BANK_MONEY_WITHDRAWN);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_GUILD_GET_ROSTER)]
	private void HandleGuildGetRoster(GuildGetRoster query)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_INFO);
		this.SendPacketToServer(packet);
		WorldPacket packet2 = new WorldPacket(Opcode.CMSG_GUILD_GET_ROSTER);
		this.SendPacketToServer(packet2);
	}

	[PacketHandler(Opcode.CMSG_GUILD_UPDATE_MOTD_TEXT)]
	private void HandleGuildUpdateMotdText(GuildUpdateMotdText text)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_UPDATE_MOTD_TEXT);
		packet.WriteCString(text.MotdText);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_UPDATE_INFO_TEXT)]
	private void HandleGuildUpdateInfoText(GuildUpdateInfoText text)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_UPDATE_INFO_TEXT);
		packet.WriteCString(text.InfoText);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_SET_MEMBER_NOTE)]
	private void HandleGuildSetMemberNote(GuildSetMemberNote note)
	{
		WorldPacket packet = new WorldPacket(note.IsPublic ? Opcode.CMSG_GUILD_SET_PUBLIC_NOTE : Opcode.CMSG_GUILD_SET_OFFICER_NOTE);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(note.NoteeGUID));
		packet.WriteCString(note.Note);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_PROMOTE_MEMBER)]
	private void HandleGuildPromoteMember(GuildPromoteMember promote)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_PROMOTE_MEMBER);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(promote.Promotee));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_DEMOTE_MEMBER)]
	private void HandleGuildDemoteMember(GuildDemoteMember demote)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_DEMOTE_MEMBER);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(demote.Demotee));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_OFFICER_REMOVE_MEMBER)]
	private void HandleGuildOfficerRemoveMember(GuildOfficerRemoveMember remove)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_OFFICER_REMOVE_MEMBER);
		packet.WriteCString(this.GetSession().GameState.GetPlayerName(remove.Removee));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_INVITE_BY_NAME)]
	private void HandleGuildInviteByName(GuildInviteByName invite)
	{
		if (invite.ArenaTeamId == 0)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_INVITE_BY_NAME);
			packet.WriteCString(invite.Name);
			this.SendPacketToServer(packet);
		}
		else
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ARENA_TEAM_INVITE);
			packet2.WriteUInt32(invite.ArenaTeamId);
			packet2.WriteCString(invite.Name);
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.CMSG_GUILD_SET_RANK_PERMISSIONS)]
	private void HandleGuildSetRankPermissions(GuildSetRankPermissions rank)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_SET_RANK_PERMISSIONS);
		packet.WriteUInt32(rank.RankID);
		packet.WriteUInt32(rank.Flags);
		packet.WriteCString(rank.RankName);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteInt32(rank.WithdrawGoldLimit);
			for (int i = 0; i < 6; i++)
			{
				packet.WriteUInt32(rank.TabFlags[i]);
				packet.WriteUInt32(rank.TabWithdrawItemLimit[i]);
			}
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_ADD_RANK)]
	private void HandleGuildAddRank(GuildAddRank rank)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_ADD_RANK);
		packet.WriteCString(rank.Name);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_DELETE_RANK)]
	private void HandleGuildDeleteRank(GuildDeleteRank rank)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_DELETE_RANK);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_SET_GUILD_MASTER)]
	private void HandleGuildSetGuildMaster(GuildSetGuildMaster master)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_SET_GUILD_MASTER);
		packet.WriteCString(master.NewMasterName);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_LEAVE)]
	private void HandleGuildLeave(GuildLeave leave)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_LEAVE);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ACCEPT_GUILD_INVITE)]
	private void HandleGuildAccept(AcceptGuildInvite accept)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ACCEPT_GUILD_INVITE);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_DECLINE_INVITATION)]
	private void HandleGuildDecline(DeclineGuildInvite decline)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_DECLINE_INVITATION);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_DELETE)]
	private void HandleGuildDelete(GuildDelete delete)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_DELETE);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SAVE_GUILD_EMBLEM)]
	private void HandleSaveGuildEmblem(SaveGuildEmblem emblem)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_SAVE_GUILD_EMBLEM);
		packet.WriteGuid(emblem.DesignerGUID.To64());
		packet.WriteUInt32(emblem.EmblemStyle);
		packet.WriteUInt32(emblem.EmblemColor);
		packet.WriteUInt32(emblem.BorderStyle);
		packet.WriteUInt32(emblem.BorderColor);
		packet.WriteUInt32(emblem.BackgroundColor);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DECLINE_GUILD_INVITES)]
	private void HandleDeclineGuildInvites(SetAutoDeclineGuildInvites packet)
	{
		this.GetSession().GameState.CurrentPlayerStorage.Settings.SetAutoBlockGuildInvites(packet.GuildInvitesShouldGetBlocked);
		ObjectUpdate updateData = new ObjectUpdate(this.GetSession().GameState.CurrentPlayerGuid, UpdateTypeModern.Values, this.GetSession());
		PlayerFlags flags = this.GetSession().GameState.CurrentPlayerStorage.Settings.CreateNewFlags();
		updateData.PlayerData.PlayerFlags = (uint)flags;
		UpdateObject updatePacket = new UpdateObject(this.GetSession().GameState);
		updatePacket.ObjectUpdates.Add(updateData);
		this.GetSession().WorldClient.SendPacketToClient(updatePacket);
	}

	[PacketHandler(Opcode.CMSG_GUILD_AUTO_DECLINE_INVITATION)]
	private void HandleGuildAutoDeclineInvitation(AutoDeclineGuildInvite autoDecline)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_DECLINE_INVITATION);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_ACTIVATE)]
	private void HandleGuildBankActivate(GuildBankAtivate activate)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_ACTIVATE);
		packet.WriteGuid(activate.BankGuid.To64());
		packet.WriteBool(activate.FullUpdate);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_QUERY_TAB)]
	private void HandleGuildBankQueryTab(GuildBankQueryTab query)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_QUERY_TAB);
		packet.WriteGuid(query.BankGuid.To64());
		packet.WriteUInt8(query.Tab);
		packet.WriteBool(query.FullUpdate);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_DEPOSIT_MONEY)]
	private void HandleGuildBankDepositMoney(GuildBankDepositMoney deposit)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_DEPOSIT_MONEY);
		packet.WriteGuid(deposit.BankGuid.To64());
		packet.WriteUInt32((uint)deposit.Money);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_TEXT_QUERY)]
	private void HandleGuildBankTextQuery(GuildBankTextQuery query)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_QUERY_GUILD_BANK_TEXT);
		packet.WriteUInt8((byte)query.Tab);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_UPDATE_TAB)]
	private void HandleGuildBankUpdateTab(GuildBankUpdateTab update)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_UPDATE_TAB);
		packet.WriteGuid(update.BankGuid.To64());
		packet.WriteUInt8(update.BankTab);
		packet.WriteCString(update.Name);
		packet.WriteCString(update.Icon);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_LOG_QUERY)]
	private void HandleGuildBankLogQuery(GuildBankLogQuery query)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_GUILD_BANK_LOG_QUERY);
		packet.WriteUInt8((byte)query.Tab);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_SET_TAB_TEXT)]
	private void HandleGuildBankSetTabText(GuildBankSetTabText query)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SET_TAB_TEXT);
		packet.WriteUInt8((byte)query.Tab);
		packet.WriteCString(query.TabText);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_BUY_TAB)]
	private void HandleGuildBankBuyTab(GuildBankBuyTab buy)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_BUY_TAB);
		packet.WriteGuid(buy.BankGuid.To64());
		packet.WriteUInt8(buy.BankTab);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GUILD_BANK_WITHDRAW_MONEY)]
	private void HandleGuildBankBuyTab(GuildBankWithdrawMoney withdraw)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_WITHDRAW_MONEY);
		packet.WriteGuid(withdraw.BankGuid.To64());
		packet.WriteUInt32((uint)withdraw.Money);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUTO_GUILD_BANK_ITEM)]
	private void HandleGuildBankItem(AutoGuildBankItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: false);
		packet.WriteUInt8(item.BankTab);
		packet.WriteUInt8(item.BankSlot);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (item.ContainerSlot.HasValue)
		{
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerSlot.Value));
			packet.WriteUInt8(item.ContainerItemSlot);
		}
		else
		{
			packet.WriteUInt8(byte.MaxValue);
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerItemSlot));
		}
		packet.WriteBool(data: false);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		else
		{
			packet.WriteUInt8(0);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SPLIT_ITEM_TO_GUILD_BANK)]
	[PacketHandler(Opcode.CMSG_MERGE_ITEM_WITH_GUILD_BANK_ITEM)]
	private void HandleSplitItemToGuildBank(SplitItemToGuildBank item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: false);
		packet.WriteUInt8(item.BankTab);
		packet.WriteUInt8(item.BankSlot);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (item.ContainerSlot.HasValue)
		{
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerSlot.Value));
			packet.WriteUInt8(item.ContainerItemSlot);
		}
		else
		{
			packet.WriteUInt8(byte.MaxValue);
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerItemSlot));
		}
		packet.WriteBool(data: false);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(item.StackCount);
		}
		else
		{
			packet.WriteUInt8((byte)item.StackCount);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUTO_STORE_GUILD_BANK_ITEM)]
	private void HandleAutoStoreGuildBankItem(AutoStoreGuildBankItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: false);
		packet.WriteUInt8(item.BankTab);
		packet.WriteUInt8(item.BankSlot);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: true);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		else
		{
			packet.WriteUInt8(0);
		}
		packet.WriteBool(data: true);
		packet.WriteUInt8(0);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_STORE_GUILD_BANK_ITEM)]
	private void HandleStoreGuildBankItem(AutoGuildBankItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: false);
		packet.WriteUInt8(item.BankTab);
		packet.WriteUInt8(item.BankSlot);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (item.ContainerSlot.HasValue)
		{
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerSlot.Value));
			packet.WriteUInt8(item.ContainerItemSlot);
		}
		else
		{
			packet.WriteUInt8(byte.MaxValue);
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerItemSlot));
		}
		packet.WriteBool(data: true);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		else
		{
			packet.WriteUInt8(0);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MERGE_GUILD_BANK_ITEM_WITH_ITEM)]
	[PacketHandler(Opcode.CMSG_SPLIT_GUILD_BANK_ITEM_TO_INVENTORY)]
	private void HandleMergeGuildBankItemWithItem(SplitItemToGuildBank item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: false);
		packet.WriteUInt8(item.BankTab);
		packet.WriteUInt8(item.BankSlot);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (item.ContainerSlot.HasValue)
		{
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerSlot.Value));
			packet.WriteUInt8(item.ContainerItemSlot);
		}
		else
		{
			packet.WriteUInt8(byte.MaxValue);
			packet.WriteUInt8(ModernVersion.AdjustInventorySlot(item.ContainerItemSlot));
		}
		packet.WriteBool(data: true);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(item.StackCount);
		}
		else
		{
			packet.WriteUInt8((byte)item.StackCount);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_GUILD_BANK_ITEM)]
	private void HandleMoveGuildBankItem(MoveGuildBankItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: true);
		packet.WriteUInt8(item.BankTab2);
		packet.WriteUInt8(item.BankSlot2);
		packet.WriteUInt32(0u);
		packet.WriteUInt8(item.BankTab1);
		packet.WriteUInt8(item.BankSlot1);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		else
		{
			packet.WriteUInt8(0);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SPLIT_GUILD_BANK_ITEM)]
	[PacketHandler(Opcode.CMSG_MERGE_GUILD_BANK_ITEM_WITH_GUILD_BANK_ITEM)]
	private void HandleMoveGuildBankItem(SplitGuildBankItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS);
		packet.WriteGuid(item.BankGuid.To64());
		packet.WriteBool(data: true);
		packet.WriteUInt8(item.BankTab2);
		packet.WriteUInt8(item.BankSlot2);
		packet.WriteUInt32(0u);
		packet.WriteUInt8(item.BankTab1);
		packet.WriteUInt8(item.BankSlot1);
		packet.WriteUInt32(0u);
		packet.WriteBool(data: false);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(item.StackCount);
		}
		else
		{
			packet.WriteUInt8((byte)item.StackCount);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DB_QUERY_BULK)]
	private void HandleDbQueryBulk(DBQueryBulk query)
	{
		foreach (uint id in query.Queries)
		{
			DBReply reply = new DBReply();
			reply.RecordID = id;
			reply.TableHash = query.TableHash;
			reply.Status = HotfixStatus.Invalid;
			reply.Timestamp = (uint)Time.UnixTime;
			Log.PrintNet(LogType.Debug, LogNetDir.C2P, $"DB_QUERY_BULK requested ({query.TableHash}) #{id}", "HandleDbQueryBulk", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\HotfixHandler.cs");
			if (query.TableHash == DB2Hash.BroadcastText)
			{
				BroadcastText bct = GameData.GetBroadcastText(id);
				if (bct == null)
				{
					bct = new BroadcastText();
					bct.Entry = id;
					bct.MaleText = "Clear your cache!";
					bct.FemaleText = "Clear your cache!";
				}
				reply.Status = HotfixStatus.Valid;
				reply.Data.WriteCString(bct.MaleText);
				reply.Data.WriteCString(bct.FemaleText);
				reply.Data.WriteUInt32(bct.Entry);
				reply.Data.WriteUInt32(bct.Language);
				reply.Data.WriteUInt32(0u);
				reply.Data.WriteUInt16(0);
				reply.Data.WriteUInt8(0);
				reply.Data.WriteUInt32(0u);
				if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
				{
					reply.Data.WriteUInt32(0u);
				}
				for (int i = 0; i < 2; i++)
				{
					reply.Data.WriteUInt32(0u);
				}
				for (int j = 0; j < 3; j++)
				{
					reply.Data.WriteUInt16(bct.Emotes[j]);
				}
				for (int k = 0; k < 3; k++)
				{
					reply.Data.WriteUInt16(bct.EmoteDelays[k]);
				}
			}
			else if (query.TableHash == DB2Hash.Item)
			{
				ItemTemplate item = GameData.GetItemTemplate(id);
				if (item != null)
				{
					reply.Status = HotfixStatus.Valid;
					GameData.WriteItemHotfix(item, reply.Data);
				}
				else if (!this.GetSession().GameState.RequestedItemHotfixes.Contains(id) && this.GetSession().WorldClient != null && this.GetSession().WorldClient.IsConnected())
				{
					this.GetSession().GameState.RequestedItemHotfixes.Add(id);
					WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ITEM_QUERY_SINGLE);
					packet2.WriteUInt32(id);
					if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
					{
						packet2.WriteGuid(WowGuid64.Empty);
					}
					this.SendPacketToServer(packet2);
					continue;
				}
			}
			else if (query.TableHash == DB2Hash.ItemSparse)
			{
				ItemTemplate item2 = GameData.GetItemTemplate(id);
				if (item2 != null)
				{
					reply.Status = HotfixStatus.Valid;
					GameData.WriteItemSparseHotfix(item2, reply.Data);
				}
				else if (!this.GetSession().GameState.RequestedItemSparseHotfixes.Contains(id) && this.GetSession().WorldClient != null && this.GetSession().WorldClient.IsConnected())
				{
					this.GetSession().GameState.RequestedItemSparseHotfixes.Add(id);
					WorldPacket packet3 = new WorldPacket(Opcode.CMSG_ITEM_QUERY_SINGLE);
					packet3.WriteUInt32(id);
					if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
					{
						packet3.WriteGuid(WowGuid64.Empty);
					}
					this.SendPacketToServer(packet3);
					continue;
				}
			}
			this.SendPacket(reply);
		}
	}

	[PacketHandler(Opcode.CMSG_HOTFIX_REQUEST)]
	private void HandleHotfixRequest(HotfixRequest request)
	{
		HotfixConnect connect = new HotfixConnect();
		foreach (uint id in request.Hotfixes)
		{
			if (GameData.Hotfixes.TryGetValue(id, out var record))
			{
				Log.Print(LogType.Debug, $"Hotfix record {record.RecordId} from {record.TableHash}.", "HandleHotfixRequest", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\HotfixHandler.cs");
				connect.Hotfixes.Add(record);
			}
		}
		this.SendPacket(connect);
	}

	[PacketHandler(Opcode.CMSG_RESET_INSTANCES)]
	private void HandleResetInstances(EmptyClientPacket reset)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_RESET_INSTANCES);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_RAID_INFO)]
	private void HandleRequestRaidInfo(EmptyClientPacket reset)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REQUEST_RAID_INFO);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BUY_ITEM)]
	private void HandleBuyItem(BuyItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BUY_ITEM);
		packet.WriteGuid(item.VendorGUID.To64());
		packet.WriteUInt32(item.Item.ItemID);
		uint quantity = item.Quantity / this.GetSession().GameState.GetItemBuyCount(item.Item.ItemID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
		{
			packet.WriteUInt32((ModernVersion.ExpansionVersion >= 3) ? item.Muid : item.Slot);
			packet.WriteUInt32(quantity);
		}
		else
		{
			packet.WriteUInt8((byte)quantity);
		}
		packet.WriteUInt8((byte)item.BagSlot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SELL_ITEM)]
	private void HandleSellItem(SellItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SELL_ITEM);
		packet.WriteGuid(item.VendorGUID.To64());
		packet.WriteGuid(item.ItemGUID.To64());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WriteUInt32(item.Amount);
		}
		else
		{
			packet.WriteUInt8((byte)item.Amount);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SPLIT_ITEM)]
	private void HandleSplitItem(SplitItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SPLIT_ITEM);
		byte containerSlot1 = ((item.FromPackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.FromPackSlot) : item.FromPackSlot);
		byte slot1 = ((item.FromPackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.FromSlot) : item.FromSlot);
		byte containerSlot2 = ((item.ToPackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ToPackSlot) : item.ToPackSlot);
		byte slot2 = ((item.ToPackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ToSlot) : item.ToSlot);
		packet.WriteUInt8(containerSlot1);
		packet.WriteUInt8(slot1);
		packet.WriteUInt8(containerSlot2);
		packet.WriteUInt8(slot2);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WriteInt32(item.Quantity);
		}
		else
		{
			packet.WriteUInt8((byte)item.Quantity);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SWAP_INV_ITEM)]
	private void HandleSwapInvItem(SwapInvItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SWAP_INV_ITEM);
		byte slot1 = ModernVersion.AdjustInventorySlot(item.Slot1);
		byte slot2 = ModernVersion.AdjustInventorySlot(item.Slot2);
		// Modern client: Slot2=source, Slot1=destination (reversed from field names)
		// Legacy server expects: srcSlot first, dstSlot second
		packet.WriteUInt8(slot2);
		packet.WriteUInt8(slot1);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SWAP_ITEM)]
	private void HandleSwapItem(SwapItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SWAP_ITEM);
		byte containerSlotB = ((item.ContainerSlotB != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ContainerSlotB) : item.ContainerSlotB);
		byte slotB = ((item.ContainerSlotB == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.SlotB) : item.SlotB);
		byte containerSlotA = ((item.ContainerSlotA != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ContainerSlotA) : item.ContainerSlotA);
		byte slotA = ((item.ContainerSlotA == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.SlotA) : item.SlotA);
		packet.WriteUInt8(containerSlotB);
		packet.WriteUInt8(slotB);
		packet.WriteUInt8(containerSlotA);
		packet.WriteUInt8(slotA);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DESTROY_ITEM)]
	private void HandleDestroyItem(DestroyItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_DESTROY_ITEM);
		byte containerSlot = ((item.ContainerId != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ContainerId) : item.ContainerId);
		byte slot = ((item.ContainerId == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.SlotNum) : item.SlotNum);
		packet.WriteUInt8(containerSlot);
		packet.WriteUInt8(slot);
		packet.WriteUInt32(item.Count);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUTO_EQUIP_ITEM)]
	[PacketHandler(Opcode.CMSG_AUTOSTORE_BANK_ITEM)]
	[PacketHandler(Opcode.CMSG_AUTOBANK_ITEM)]
	private void HandleAutoEquipItem(AutoEquipItem item)
	{
		WorldPacket packet = new WorldPacket(item.GetUniversalOpcode());
		byte containerSlot = ((item.PackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.PackSlot) : item.PackSlot);
		byte slot = ((item.PackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.Slot) : item.Slot);
		packet.WriteUInt8(containerSlot);
		packet.WriteUInt8(slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_AUTO_EQUIP_ITEM_SLOT)]
	private void HandleAutoEquipItemSlot(AutoEquipItemSlot item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_AUTO_EQUIP_ITEM_SLOT);
		packet.WriteGuid(item.Item.To64());
		byte slot = ModernVersion.AdjustInventorySlot(item.ItemDstSlot);
		packet.WriteUInt8(slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_READ_ITEM)]
	private void HandleReadItem(ReadItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_READ_ITEM);
		byte containerSlot = ((item.PackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.PackSlot) : item.PackSlot);
		byte slot = ((item.PackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.Slot) : item.Slot);
		packet.WriteUInt8(containerSlot);
		packet.WriteUInt8(slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BUY_BACK_ITEM)]
	private void HandleBuyBackItem(BuyBackItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BUY_BACK_ITEM);
		packet.WriteGuid(item.VendorGUID.To64());
		byte slot = ModernVersion.AdjustInventorySlot((byte)item.Slot);
		packet.WriteUInt32(slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REPAIR_ITEM)]
	private void HandleRepairItem(RepairItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REPAIR_ITEM);
		packet.WriteGuid(item.VendorGUID.To64());
		packet.WriteGuid(item.ItemGUID.To64());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteBool(item.UseGuildBank);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SOCKET_GEMS)]
	private void HandleSocketGems(SocketGems gems)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SOCKET_GEMS);
		packet.WriteGuid(gems.ItemGuid.To64());
		for (int i = 0; i < 3; i++)
		{
			packet.WriteGuid(gems.Gems[i].To64());
		}
		this.SendPacketToServer(packet);
		SocketGemsSuccess success = new SocketGemsSuccess();
		success.ItemGuid = gems.ItemGuid;
		this.SendPacket(success);
	}

	[PacketHandler(Opcode.CMSG_OPEN_ITEM)]
	private void HandleOpenItem(OpenItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_OPEN_ITEM);
		byte containerSlot = ((item.PackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.PackSlot) : item.PackSlot);
		byte slot = ((item.PackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.Slot) : item.Slot);
		packet.WriteUInt8(containerSlot);
		packet.WriteUInt8(slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_AMMO)]
	private void HandleSetAmmo(SetAmmo ammo)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_AMMO);
		packet.WriteUInt32(ammo.ItemId);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CANCEL_TEMP_ENCHANTMENT)]
	private void HandleCancelTempEnchantment(CancelTempEnchantment cancel)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_TEMP_ENCHANTMENT);
			packet.WriteUInt32(cancel.EnchantmentSlot);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_WRAP_ITEM)]
	private void HandleWrapItem(WrapItem item)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_WRAP_ITEM);
		byte giftBag = ((item.GiftBag != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.GiftBag) : item.GiftBag);
		byte giftSlot = ((item.GiftBag == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.GiftSlot) : item.GiftSlot);
		byte itemBag = ((item.ItemBag != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ItemBag) : item.ItemBag);
		byte itemSlot = ((item.ItemBag == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(item.ItemSlot) : item.ItemSlot);
		packet.WriteUInt8(giftBag);
		packet.WriteUInt8(giftSlot);
		packet.WriteUInt8(itemBag);
		packet.WriteUInt8(itemSlot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOOT_RELEASE)]
	private void HandleLootRelease(LootRelease loot)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOOT_RELEASE);
		packet.WriteGuid(loot.Owner.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOOT_ITEM)]
	[PacketHandler(Opcode.CMSG_AUTOSTORE_LOOT_ITEM)]
	private void HandleLootItem(LootItemPkt loot)
	{
		foreach (LootRequest item in loot.Loot)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_AUTOSTORE_LOOT_ITEM);
			packet.WriteUInt8(item.LootListID);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_LOOT_UNIT)]
	private void HandleLootUnit(LootUnit loot)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOOT_UNIT);
		packet.WriteGuid(loot.Unit.To64());
		this.SendPacketToServer(packet);
		this.GetSession().GameState.LastLootTargetGuid = loot.Unit.To64();
	}

	[PacketHandler(Opcode.CMSG_LOOT_MONEY)]
	private void HandleLootMoney(LootMoney loot)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOOT_MONEY);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_LOOT_METHOD)]
	private void HandleSetLootMethod(SetLootMethod loot)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_LOOT_METHOD);
		packet.WriteUInt32((uint)loot.LootMethod);
		packet.WriteGuid(loot.LootMasterGUID.To64());
		packet.WriteUInt32(loot.LootThreshold);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_OPT_OUT_OF_LOOT)]
	private void HandleOptOutOfLoot(OptOutOfLoot loot)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_OPT_OUT_OF_LOOT);
			packet.WriteInt32(loot.PassOnLoot ? 1 : 0);
			this.SendPacketToServer(packet);
		}
		else
		{
			this.GetSession().GameState.IsPassingOnLoot = loot.PassOnLoot;
		}
	}

	[PacketHandler(Opcode.CMSG_LOOT_ROLL)]
	private void HandleLootRoll(LootRoll loot)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LOOT_ROLL);
		packet.WriteGuid(loot.LootObj.To64());
		packet.WriteUInt32(loot.LootListID);
		packet.WriteUInt8((byte)loot.RollType);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_LOOT_MASTER_GIVE)]
	private void HandleLootMasterGive(LootMasterGive loot)
	{
		foreach (LootRequest item in loot.Loot)
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_LOOT_MASTER_GIVE);
			packet.WriteGuid(item.LootObj.To64());
			packet.WriteUInt8(item.LootListID);
			packet.WriteGuid(loot.TargetGUID.To64());
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_QUERY_NEXT_MAIL_TIME)]
	private void HandleMailGetList(EmptyClientPacket mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_QUERY_NEXT_MAIL_TIME);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_GET_LIST)]
	private void HandleMailGetList(MailGetList mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_GET_LIST);
		packet.WriteGuid(mail.Mailbox.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_CREATE_TEXT_ITEM)]
	private void HandleMailCreateTextItem(MailCreateTextItem mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_CREATE_TEXT_ITEM);
		packet.WriteGuid(mail.Mailbox.To64());
		packet.WriteUInt32(mail.MailID);
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(0u);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_DELETE)]
	private void HandleMailDelete(MailDelete mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_DELETE);
		packet.WriteGuid(this.GetSession().GameState.CurrentInteractedWithGO.To64());
		packet.WriteUInt32(mail.MailID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(0u);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_MARK_AS_READ)]
	private void HandleMailMarkAsRead(MailMarkAsRead mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_MARK_AS_READ);
		packet.WriteGuid(mail.Mailbox.To64());
		packet.WriteUInt32(mail.MailID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_RETURN_TO_SENDER)]
	private void HandleMailReturnToSender(MailReturnToSender mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_RETURN_TO_SENDER);
		packet.WriteGuid(this.GetSession().GameState.CurrentInteractedWithGO.To64());
		packet.WriteUInt32(mail.MailID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteGuid(mail.SenderGUID.To64());
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_TAKE_ITEM)]
	private void HandleMailTakeItem(MailTakeItem mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_TAKE_ITEM);
		packet.WriteGuid(mail.Mailbox.To64());
		packet.WriteUInt32(mail.MailID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(mail.AttachID);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MAIL_TAKE_MONEY)]
	private void HandleMailTakeMoney(MailTakeMoney mail)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MAIL_TAKE_MONEY);
		packet.WriteGuid(mail.Mailbox.To64());
		packet.WriteUInt32(mail.MailID);
		this.SendPacketToServer(packet);
	}

	private void BuildSendMail(SendMail mail, List<SendMail.MailAttachment> attachments)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SEND_MAIL);
		packet.WriteGuid(mail.Mailbox.To64());
		packet.WriteCString(mail.Target);
		packet.WriteCString(mail.Subject);
		packet.WriteCString(mail.Body);
		packet.WriteInt32(mail.StationeryID);
		packet.WriteUInt32(0u);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt8((byte)attachments.Count);
			foreach (SendMail.MailAttachment item in attachments)
			{
				packet.WriteUInt8(item.AttachPosition);
				packet.WriteGuid(item.ItemGUID.To64());
			}
		}
		else if (attachments.Count > 0)
		{
			packet.WriteGuid(attachments[0].ItemGUID.To64());
		}
		else
		{
			packet.WriteGuid(WowGuid64.Empty);
		}
		packet.WriteUInt32((uint)mail.SendMoney);
		packet.WriteUInt32((uint)mail.Cod);
		packet.WriteUInt64(0uL);
		packet.WriteUInt8(0);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SEND_MAIL)]
	private void HandleSendMail(SendMail mail)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) || mail.Attachments.Count <= 1)
		{
			this.BuildSendMail(mail, mail.Attachments);
			return;
		}
		mail.SendMoney /= mail.Attachments.Count;
		mail.Cod /= mail.Attachments.Count;
		foreach (SendMail.MailAttachment item in mail.Attachments)
		{
			List<SendMail.MailAttachment> attachments = new List<SendMail.MailAttachment>();
			attachments.Add(item);
			this.BuildSendMail(mail, attachments);
			Thread.Sleep(500);
		}
	}

	[PacketHandler(Opcode.CMSG_TIME_SYNC_RESPONSE)]
	private void HandleTimeSyncResponse(TimeSyncResponse response)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_TIME_SYNC_RESPONSE);
			packet.WriteUInt32(response.SequenceIndex);
			packet.WriteUInt32(response.ClientTime);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_AREA_TRIGGER)]
	private void HandleAreaTrigger(AreaTriggerPkt at)
	{
		if (at.Entered)
		{
			this.GetSession().GameState.LastEnteredAreaTrigger = at.AreaTriggerID;
			WorldPacket packet = new WorldPacket(Opcode.CMSG_AREA_TRIGGER);
			packet.WriteUInt32(at.AreaTriggerID);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_SET_SELECTION)]
	private void HandleSetSelection(SetSelection selection)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_SELECTION);
		packet.WriteGuid(selection.TargetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REPOP_REQUEST)]
	private void HandleRepopRequest(RepopRequest repop)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REPOP_REQUEST);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteBool(repop.CheckInstance);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_CORPSE_LOCATION_FROM_CLIENT)]
	private void HandleQueryCorpseLocationFromClient(QueryCorpseLocationFromClient query)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_CORPSE_QUERY);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_RECLAIM_CORPSE)]
	private void HandleReclaimCorpse(ReclaimCorpse corpse)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_RECLAIM_CORPSE);
		packet.WriteGuid(corpse.CorpseGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_STAND_STATE_CHANGE)]
	private void HandleStandStateChange(StandStateChange state)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_STAND_STATE_CHANGE);
		packet.WriteUInt32(state.StandState);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_OPENING_CINEMATIC)]
	[PacketHandler(Opcode.CMSG_NEXT_CINEMATIC_CAMERA)]
	[PacketHandler(Opcode.CMSG_COMPLETE_CINEMATIC)]
	private void HandleCinematicPacket(ClientCinematicPkt cinematic)
	{
		WorldPacket packet = new WorldPacket(cinematic.GetUniversalOpcode());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_FAR_SIGHT)]
	private void HandleFarSight(FarSight sight)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_FAR_SIGHT);
		packet.WriteBool(sight.Enable);
		this.SendPacketToServer(packet);
		this.GetSession().GameState.IsInFarSight = sight.Enable;
	}

	[PacketHandler(Opcode.CMSG_MOUNT_SPECIAL_ANIM)]
	private void HandleMountSpecialAnim(MountSpecial mount)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MOUNT_SPECIAL_ANIM);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TUTORIAL_FLAG)]
	private void HandleTutorialFlag(TutorialSetFlag tutorial)
	{
		switch (tutorial.Action)
		{
		case TutorialAction.Clear:
		{
			WorldPacket packet3 = new WorldPacket(Opcode.CMSG_TUTORIAL_CLEAR);
			this.SendPacketToServer(packet3);
			break;
		}
		case TutorialAction.Reset:
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_TUTORIAL_RESET);
			this.SendPacketToServer(packet2);
			break;
		}
		case TutorialAction.Update:
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_TUTORIAL_FLAG);
			packet.WriteUInt32(tutorial.TutorialBit);
			this.SendPacketToServer(packet);
			break;
		}
		}
	}

	[PacketHandler(Opcode.CMSG_REQUEST_LFG_LIST_BLACKLIST)]
	private void HandleRequestLFGListBlacklist(EmptyClientPacket request)
	{
		LFGListUpdateBlacklist blacklist = new LFGListUpdateBlacklist();
		if (ModernVersion.ExpansionVersion > 1)
		{
			blacklist.AddBlacklist(796, 3);
			blacklist.AddBlacklist(797, 3);
			blacklist.AddBlacklist(798, 3);
			blacklist.AddBlacklist(799, 3);
			blacklist.AddBlacklist(800, 3);
			blacklist.AddBlacklist(801, 3);
			blacklist.AddBlacklist(802, 3);
			blacklist.AddBlacklist(803, 3);
			blacklist.AddBlacklist(804, 3);
			blacklist.AddBlacklist(805, 3);
			blacklist.AddBlacklist(806, 3);
			blacklist.AddBlacklist(807, 3);
			blacklist.AddBlacklist(808, 3);
			blacklist.AddBlacklist(809, 3);
			blacklist.AddBlacklist(810, 3);
			blacklist.AddBlacklist(811, 3);
			blacklist.AddBlacklist(812, 3);
			blacklist.AddBlacklist(813, 3);
			blacklist.AddBlacklist(814, 3);
			blacklist.AddBlacklist(815, 3);
			blacklist.AddBlacklist(816, 3);
			blacklist.AddBlacklist(817, 3);
			blacklist.AddBlacklist(818, 3);
			blacklist.AddBlacklist(820, 3);
			blacklist.AddBlacklist(827, 3);
			blacklist.AddBlacklist(828, 3);
			blacklist.AddBlacklist(829, 3);
			blacklist.AddBlacklist(835, 1031);
			blacklist.AddBlacklist(837, 3);
			blacklist.AddBlacklist(849, 1031);
			blacklist.AddBlacklist(850, 1031);
			blacklist.AddBlacklist(851, 1031);
			blacklist.AddBlacklist(852, 1031);
			blacklist.AddBlacklist(853, 3);
			blacklist.AddBlacklist(854, 3);
			blacklist.AddBlacklist(855, 3);
			blacklist.AddBlacklist(856, 3);
			blacklist.AddBlacklist(857, 3);
			blacklist.AddBlacklist(858, 3);
			blacklist.AddBlacklist(859, 3);
			blacklist.AddBlacklist(860, 3);
			blacklist.AddBlacklist(861, 3);
			blacklist.AddBlacklist(862, 3);
			blacklist.AddBlacklist(863, 3);
			blacklist.AddBlacklist(864, 3);
			blacklist.AddBlacklist(865, 3);
			blacklist.AddBlacklist(866, 3);
			blacklist.AddBlacklist(867, 3);
			blacklist.AddBlacklist(868, 3);
			blacklist.AddBlacklist(869, 3);
			blacklist.AddBlacklist(870, 3);
			blacklist.AddBlacklist(871, 3);
			blacklist.AddBlacklist(872, 3);
			blacklist.AddBlacklist(873, 3);
			blacklist.AddBlacklist(874, 3);
			blacklist.AddBlacklist(875, 3);
			blacklist.AddBlacklist(876, 3);
			blacklist.AddBlacklist(877, 3);
			blacklist.AddBlacklist(878, 3);
			blacklist.AddBlacklist(879, 3);
			blacklist.AddBlacklist(880, 3);
			blacklist.AddBlacklist(881, 3);
			blacklist.AddBlacklist(882, 3);
			blacklist.AddBlacklist(883, 3);
			blacklist.AddBlacklist(884, 3);
			blacklist.AddBlacklist(885, 3);
			blacklist.AddBlacklist(886, 3);
			blacklist.AddBlacklist(887, 3);
			blacklist.AddBlacklist(888, 3);
			blacklist.AddBlacklist(889, 3);
			blacklist.AddBlacklist(890, 3);
			blacklist.AddBlacklist(891, 3);
			blacklist.AddBlacklist(892, 3);
			blacklist.AddBlacklist(893, 3);
			blacklist.AddBlacklist(898, 3);
			blacklist.AddBlacklist(899, 3);
			blacklist.AddBlacklist(900, 3);
			blacklist.AddBlacklist(901, 3);
			blacklist.AddBlacklist(902, 1031);
			blacklist.AddBlacklist(917, 1031);
			blacklist.AddBlacklist(919, 3);
			blacklist.AddBlacklist(920, 3);
			blacklist.AddBlacklist(921, 3);
			blacklist.AddBlacklist(922, 3);
			blacklist.AddBlacklist(923, 3);
			blacklist.AddBlacklist(924, 3);
			blacklist.AddBlacklist(926, 3);
			blacklist.AddBlacklist(927, 3);
			blacklist.AddBlacklist(928, 3);
			blacklist.AddBlacklist(929, 3);
			blacklist.AddBlacklist(930, 3);
			blacklist.AddBlacklist(932, 3);
			blacklist.AddBlacklist(934, 3);
		}
		this.SendPacket(blacklist);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_CONQUEST_FORMULA_CONSTANTS)]
	private void HandleRequestConquestFormulaConstants(EmptyClientPacket request)
	{
		ConquestFormulaConstants response = new ConquestFormulaConstants();
		response.PvpMinCPPerWeek = 1500;
		response.PvpMaxCPPerWeek = 3000;
		response.PvpCPBaseCoefficient = 1511.26f;
		response.PvpCPExpCoefficient = 1639.28f;
		response.PvpCPNumerator = 0.00412f;
		this.SendPacket(response);
	}

	[PacketHandler(Opcode.CMSG_OBJECT_UPDATE_FAILED)]
	private void HandleObjectUpdateFailed(ObjectUpdateFailed fail)
	{
		Log.Print(LogType.Error, $"Object update failed for {fail.ObjectGuid}.", "HandleObjectUpdateFailed", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\MiscHandler.cs");
	}

	[PacketHandler(Opcode.CMSG_SET_DUNGEON_DIFFICULTY)]
	private void HandleSetDungeonDifficulty(SetDungeonDifficulty difficulty)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_SET_DUNGEON_DIFFICULTY);
		uint dificultyId = (byte)Enum.Parse(typeof(DifficultyLegacy), ((DifficultyModern)difficulty.DifficultyID/*cast due to .constrained prefix*/).ToString());
		packet.WriteUInt32(dificultyId);
		this.SendPacketToServer(packet);
		DungeonDifficultySet difficultySet = new DungeonDifficultySet();
		difficultySet.DifficultyID = (int)difficulty.DifficultyID;
		this.SendPacket(difficultySet);
	}

	[PacketHandler(Opcode.CMSG_VIOLENCE_LEVEL)]
	private void HandleViolenceLevel(ViolenceLevelPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_REQUEST_PVP_REWARDS)]
	private void HandleRequestPvpRewards(RequestPvpRewardsPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_REQUEST_RATED_PVP_INFO)]
	private void HandleRequestRatedPvpInfo(RequestRatedPvpInfoPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_OVERRIDE_SCREEN_FLASH)]
	private void HandleOverrideScreenFlash(OverrideScreenFlashPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_QUEUED_MESSAGES_END)]
	private void HandleQueuedMessagesEnd(QueuedMessagesEndPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_BATTLE_PAY_GET_PRODUCT_LIST)]
	private void HandleBattlePayGetProductList(BattlePayGetProductListPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_BATTLE_PAY_GET_PURCHASE_LIST)]
	private void HandleBattlePayGetPurchaseList(BattlePayGetPurchaseListPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_GET_UNDELETE_CHARACTER_COOLDOWN_STATUS)]
	private void HandleGetUndeleteCharacterCooldownStatus(GetUndeleteCharCooldownPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_UPDATE_VAS_PURCHASE_STATES)]
	private void HandleUpdateVasPurchaseStates(UpdateVasPurchaseStatesPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_DF_GET_SYSTEM_INFO)]
	private void HandleDfGetSystemInfo(DfGetSystemInfoPkt packet)
	{
		if (packet.Player)
		{
			WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_PLAYER_LOCK_INFO_REQUEST);
			this.SendPacketToServer(legacyPacket);
		}
		else
		{
			WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_PARTY_LOCK_INFO_REQUEST);
			this.SendPacketToServer(legacyPacket);
		}
	}

	[PacketHandler(Opcode.CMSG_DF_JOIN)]
	private void HandleDfJoin(DfJoinPkt packet)
	{
		// Legacy 3.3.5a format: uint32 Roles, bool NoPartialClear, bool Achievements, uint8 slotCount, uint32[] Slots, uint8 needsCount(3), uint8[3] Needs, string Comment
		WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_JOIN);
		legacyPacket.WriteUInt32((uint)packet.Roles);
		legacyPacket.WriteUInt8(0); // NoPartialClear
		legacyPacket.WriteUInt8(0); // Achievements
		legacyPacket.WriteUInt8((byte)packet.Slots.Length);
		for (int i = 0; i < packet.Slots.Length; i++)
			legacyPacket.WriteUInt32(packet.Slots[i]);
		legacyPacket.WriteUInt8(3); // Needs count
		legacyPacket.WriteUInt8(0); // Need 1
		legacyPacket.WriteUInt8(0); // Need 2
		legacyPacket.WriteUInt8(0); // Need 3
		legacyPacket.WriteCString(""); // Comment
		this.SendPacketToServer(legacyPacket);
	}

	[PacketHandler(Opcode.CMSG_DF_PROPOSAL_RESPONSE)]
	private void HandleDfProposalResponse(WorldPacket packet)
	{
		// Modern: RideTicket + uint64 InstanceID + uint32 ProposalID + bit Accepted
		RideTicket ticket = new RideTicket();
		ticket.Read(packet);
		ulong instanceID = packet.ReadUInt64();
		uint proposalID = packet.ReadUInt32();
		bool accepted = packet.HasBit();
		// Legacy: uint32 ProposalID + uint8 Accept
		WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_PROPOSAL_RESULT);
		legacyPacket.WriteUInt32(proposalID);
		legacyPacket.WriteUInt8((byte)(accepted ? 1 : 0));
		this.SendPacketToServer(legacyPacket);
	}

	[PacketHandler(Opcode.CMSG_DF_PROPOSAL_RESPONSE)]
	private void HandleDfProposalResponse(DfProposalResponsePkt packet)
	{
		WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_PROPOSAL_RESULT);
		legacyPacket.WriteUInt32(packet.ProposalID);
		legacyPacket.WriteUInt8((byte)(packet.Accepted ? 1 : 0));
		this.SendPacketToServer(legacyPacket);
	}

	[PacketHandler(Opcode.CMSG_DF_LEAVE)]
	private void HandleDfLeave(DfLeavePkt packet)
	{
		WorldPacket legacyPacket = new WorldPacket(Opcode.CMSG_LFG_LEAVE);
		this.SendPacketToServer(legacyPacket);
	}

	[PacketHandler(Opcode.CMSG_DF_GET_JOIN_STATUS)]
	private void HandleDfGetJoinStatus(DfGetJoinStatusPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_CALENDAR_GET_NUM_PENDING)]
	private void HandleCalendarGetNumPending(CalendarGetNumPendingPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_GUILD_SET_ACHIEVEMENT_TRACKING)]
	private void HandleGuildSetAchievementTracking(GuildSetAchievementTrackingPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_QUERY_COUNTDOWN_TIMER)]
	private void HandleQueryCountdownTimer(QueryCountdownTimerPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_REQUEST_FORCED_REACTIONS)]
	private void HandleRequestForcedReactions(RequestForcedReactionsPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_LFG_LIST_GET_STATUS)]
	private void HandleLfgListGetStatus(LfgListGetStatusPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_BATTLE_PET_REQUEST_JOURNAL)]
	private void HandleBattlePetRequestJournal(BattlePetRequestJournalPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_GM_TICKET_GET_CASE_STATUS)]
	private void HandleGmTicketGetCaseStatus(GmTicketGetCaseStatusPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_TUTORIAL_FLAG)]
	private void HandleTutorialFlag(TutorialPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_REQUEST_CEMETERY_LIST)]
	private void HandleRequestCemeteryList(RequestCemeteryListPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_CLOSE_INTERACTION)]
	private void HandleCloseInteraction(CloseInteractionPkt packet)
	{
	}

	[PacketHandler(Opcode.CMSG_REPORT_CLIENT_VARIABLES)]
	private void HandleReportClientVariables(GenericNoOpPkt pkt)
	{
	}

	[PacketHandler(Opcode.CMSG_REPORT_ENABLED_ADDONS)]
	private void HandleReportEnabledAddons(GenericNoOpPkt pkt)
	{
	}

	[PacketHandler(Opcode.CMSG_REPORT_KEYBINDING_EXECUTION_COUNTS)]
	private void HandleReportKeybindingCounts(GenericNoOpPkt pkt)
	{
	}

	[PacketHandler(Opcode.CMSG_DISCARDED_TIME_SYNC_ACKS)]
	private void HandleDiscardedTimeSyncAcks(GenericNoOpPkt pkt)
	{
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_CLOSE_QUEST)]
	private void HandleQuestGiverCloseQuest(GenericNoOpPkt pkt)
	{
	}

	[PacketHandler(Opcode.CMSG_MOVE_CHANGE_TRANSPORT)]
	[PacketHandler(Opcode.CMSG_MOVE_DISMISS_VEHICLE)]
	[PacketHandler(Opcode.CMSG_MOVE_FALL_LAND)]
	[PacketHandler(Opcode.CMSG_MOVE_FALL_RESET)]
	[PacketHandler(Opcode.CMSG_MOVE_HEARTBEAT)]
	[PacketHandler(Opcode.CMSG_MOVE_JUMP)]
	[PacketHandler(Opcode.CMSG_MOVE_REMOVE_MOVEMENT_FORCES)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_FACING)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_FACING_HEARTBEAT)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_FLY)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_PITCH)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_RUN_MODE)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_WALK_MODE)]
	[PacketHandler(Opcode.CMSG_MOVE_START_ASCEND)]
	[PacketHandler(Opcode.CMSG_MOVE_START_BACKWARD)]
	[PacketHandler(Opcode.CMSG_MOVE_START_DESCEND)]
	[PacketHandler(Opcode.CMSG_MOVE_START_FORWARD)]
	[PacketHandler(Opcode.CMSG_MOVE_START_PITCH_DOWN)]
	[PacketHandler(Opcode.CMSG_MOVE_START_PITCH_UP)]
	[PacketHandler(Opcode.CMSG_MOVE_START_SWIM)]
	[PacketHandler(Opcode.CMSG_MOVE_START_TURN_LEFT)]
	[PacketHandler(Opcode.CMSG_MOVE_START_TURN_RIGHT)]
	[PacketHandler(Opcode.CMSG_MOVE_START_STRAFE_LEFT)]
	[PacketHandler(Opcode.CMSG_MOVE_START_STRAFE_RIGHT)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP_ASCEND)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP_PITCH)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP_STRAFE)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP_SWIM)]
	[PacketHandler(Opcode.CMSG_MOVE_STOP_TURN)]
	[PacketHandler(Opcode.CMSG_MOVE_DOUBLE_JUMP)]
	private void HandlePlayerMove(ClientPlayerMovement movement)
	{
		string opcodeName = movement.GetUniversalOpcode().ToString();
		opcodeName = opcodeName.Replace("CMSG", "MSG");
		uint opcode = Opcodes.GetOpcodeValueForVersion(opcodeName, Settings.ServerBuild);
		if (opcode == 0)
		{
			opcode = Opcodes.GetOpcodeValueForVersion("MSG_MOVE_SET_FACING", Settings.ServerBuild);
		}
		WorldPacket packet = new WorldPacket(opcode);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(movement.Guid.To64());
		}
		movement.MoveInfo.WriteMovementInfoLegacy(packet);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_TELEPORT_ACK)]
	private void HandleMoveTeleportAck(MoveTeleportAck teleport)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_MOVE_TELEPORT_ACK);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(teleport.MoverGUID.To64());
		}
		else
		{
			packet.WriteGuid(teleport.MoverGUID.To64());
		}
		packet.WriteUInt32(teleport.MoveCounter);
		packet.WriteUInt32(teleport.MoveTime);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_WORLD_PORT_RESPONSE)]
	private void HandleWorldPortResponse(WorldPortResponse teleport)
	{
		this.GetSession().GameState.IsWaitingForWorldPortAck = false;
		WorldPacket packet = new WorldPacket(Opcode.MSG_MOVE_WORLDPORT_ACK);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_FLIGHT_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_PITCH_RATE_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_RUN_BACK_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_RUN_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_SWIM_BACK_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_SWIM_SPEED_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_TURN_RATE_CHANGE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_WALK_SPEED_CHANGE_ACK)]
	private void HandleMoveForceSpeedChangeAck(MovementSpeedAck speed)
	{
		Opcode opcode = speed.GetUniversalOpcode();
		bool flag = LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180);
		bool flag2 = flag;
		if (flag2)
		{
			bool flag3 = opcode - 743 <= Opcode.CMSG_ABANDON_NPE_RESPONSE;
			flag2 = flag3;
		}
		if (!flag2)
		{
			WorldPacket packet = new WorldPacket(opcode);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
			{
				packet.WritePackedGuid(speed.MoverGUID.To64());
			}
			else
			{
				packet.WriteGuid(speed.MoverGUID.To64());
			}
			packet.WriteUInt32(speed.Ack.MoveCounter);
			speed.Ack.MoveInfo.WriteMovementInfoLegacy(packet);
			packet.WriteFloat(speed.Speed);
			this.SendPacketToServer(packet);
		}
	}

	private MovementFlagModern GetFlagForAckOpcode(Opcode opcode)
	{
		return opcode switch
		{
			Opcode.CMSG_MOVE_FEATHER_FALL_ACK => MovementFlagModern.CanSafeFall, 
			Opcode.CMSG_MOVE_HOVER_ACK => MovementFlagModern.Hover, 
			Opcode.CMSG_MOVE_SET_CAN_FLY_ACK => MovementFlagModern.CanFly, 
			Opcode.CMSG_MOVE_WATER_WALK_ACK => MovementFlagModern.Waterwalking, 
			_ => MovementFlagModern.None, 
		};
	}

	[PacketHandler(Opcode.CMSG_MOVE_FEATHER_FALL_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_HOVER_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_SET_CAN_FLY_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_WATER_WALK_ACK)]
	private void HandleMoveForceAck1(MovementAckMessage movementAck)
	{
		WorldPacket packet = new WorldPacket(movementAck.GetUniversalOpcode());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(movementAck.MoverGUID.To64());
		}
		else
		{
			packet.WriteGuid(movementAck.MoverGUID.To64());
		}
		packet.WriteUInt32(movementAck.Ack.MoveCounter);
		movementAck.Ack.MoveInfo.WriteMovementInfoLegacy(packet);
		packet.WriteInt32(movementAck.Ack.MoveInfo.Flags.HasAnyFlag(this.GetFlagForAckOpcode(movementAck.GetUniversalOpcode())) ? 1 : 0);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_FORCE_ROOT_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_FORCE_UNROOT_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_KNOCK_BACK_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_GRAVITY_DISABLE_ACK)]
	[PacketHandler(Opcode.CMSG_MOVE_GRAVITY_ENABLE_ACK)]
	private void HandleMoveForceAck2(MovementAckMessage movementAck)
	{
		WorldPacket packet = new WorldPacket(movementAck.GetUniversalOpcode());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(movementAck.MoverGUID.To64());
		}
		else
		{
			packet.WriteGuid(movementAck.MoverGUID.To64());
		}
		packet.WriteUInt32(movementAck.Ack.MoveCounter);
		movementAck.Ack.MoveInfo.WriteMovementInfoLegacy(packet);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_ACTIVE_MOVER)]
	private void HandleMoveSetActiveMover(SetActiveMover move)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ACTIVE_MOVER);
		packet.WriteGuid(move.MoverGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_INIT_ACTIVE_MOVER_COMPLETE)]
	private void HandleMoveInitActiveMoverComplete(InitActiveMoverComplete move)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_ACTIVE_MOVER);
		packet.WriteGuid(this.GetSession().GameState.CurrentPlayerGuid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_SPLINE_DONE)]
	private void HandleMoveSplineDone(MoveSplineDone movement)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MOVE_SPLINE_DONE);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(movement.Guid.To64());
		}
		movement.MoveInfo.WriteMovementInfoLegacy(packet);
		packet.WriteInt32(movement.SplineID);
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteFloat(0f);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_MOVE_TIME_SKIPPED)]
	private void HandleMoveSplineDone(MoveTimeSkipped movement)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_MOVE_TIME_SKIPPED);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
		{
			packet.WritePackedGuid(movement.MoverGUID.To64());
		}
		else
		{
			packet.WriteGuid(movement.MoverGUID.To64());
		}
		packet.WriteUInt32(movement.TimeSkipped);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BANKER_ACTIVATE)]
	[PacketHandler(Opcode.CMSG_BINDER_ACTIVATE)]
	[PacketHandler(Opcode.CMSG_LIST_INVENTORY)]
	[PacketHandler(Opcode.CMSG_SPIRIT_HEALER_ACTIVATE)]
	[PacketHandler(Opcode.CMSG_TALK_TO_GOSSIP)]
	[PacketHandler(Opcode.CMSG_TRAINER_LIST)]
	[PacketHandler(Opcode.CMSG_BATTLEMASTER_HELLO)]
	[PacketHandler(Opcode.CMSG_AREA_SPIRIT_HEALER_QUERY)]
	[PacketHandler(Opcode.CMSG_AREA_SPIRIT_HEALER_QUEUE)]
	private void HandleInteractWithNPC(InteractWithNPC interact)
	{
		WorldPacket packet = new WorldPacket(interact.GetUniversalOpcode());
		packet.WriteGuid(interact.CreatureGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_GOSSIP_SELECT_OPTION)]
	private void HandleGossipSelectOption(GossipSelectOption gossip)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GOSSIP_SELECT_OPTION);
		packet.WriteGuid(gossip.GossipUnit.To64());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(gossip.GossipID);
		}
		packet.WriteUInt32(gossip.GossipIndex);
		if (!string.IsNullOrEmpty(gossip.PromotionCode))
		{
			packet.WriteCString(gossip.PromotionCode);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BUY_BANK_SLOT)]
	private void HandleBuyBankSlot(BuyBankSlot bank)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BUY_BANK_SLOT);
		packet.WriteGuid(bank.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TRAINER_BUY_SPELL)]
	private void HandleTrainerBuySpell(TrainerBuySpell buy)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_TRAINER_BUY_SPELL);
		packet.WriteGuid(buy.TrainerGUID.To64());
		if (ModernVersion.ExpansionVersion > 1 && LegacyVersion.ExpansionVersion <= 1)
		{
			buy.SpellID = this.GetSession().GameState.GetLearnSpellFromRealSpell(buy.SpellID);
		}
		packet.WriteUInt32(buy.SpellID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CONFIRM_RESPEC_WIPE)]
	private void HandleConfirmRespecWipe(ConfirmRespecWipe respec)
	{
		switch (respec.RespecType)
		{
		case SpecResetType.Talents:
		{
			WorldPacket packet2 = new WorldPacket(Opcode.MSG_TALENT_WIPE_CONFIRM);
			packet2.WriteGuid(respec.TrainerGUID.To64());
			this.SendPacketToServer(packet2);
			break;
		}
		case SpecResetType.PetTalents:
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_UNLEARN);
			packet.WriteGuid(respec.TrainerGUID.To64());
			this.SendPacketToServer(packet);
			break;
		}
		default:
			Log.Print(LogType.Error, $"Unhandled respec type {respec.RespecType}.", "HandleConfirmRespecWipe", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\NPCHandler.cs");
			break;
		}
	}

	[PacketHandler(Opcode.CMSG_PET_ACTION)]
	private void HandlePetAction(PetAction act)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_ACTION);
		packet.WriteGuid(act.PetGUID.To64());
		packet.WriteUInt32(act.Action);
		packet.WriteGuid(act.TargetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_STOP_ATTACK)]
	private void HandlePetStopAttack(PetStopAttack stop)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_STOP_ATTACK);
		packet.WriteGuid(stop.PetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_SET_ACTION)]
	private void HandlePetStopAttack(PetSetAction action)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_SET_ACTION);
		packet.WriteGuid(action.PetGUID.To64());
		packet.WriteUInt32(action.Index);
		packet.WriteUInt32(action.Action);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_RENAME)]
	private void HandlePetRename(PetRename pet)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_RENAME);
		packet.WriteGuid(pet.RenameData.PetGUID.To64());
		packet.WriteCString(pet.RenameData.NewName);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteBool(pet.RenameData.HasDeclinedNames);
			if (pet.RenameData.HasDeclinedNames)
			{
				for (int i = 0; i < 5; i++)
				{
					packet.WriteCString(pet.RenameData.DeclinedNames.name[i]);
				}
			}
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_STABLED_PETS)]
	private void HandleRequestStabledPets(RequestStabledPets stable)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_LIST_STABLED_PETS);
		packet.WriteGuid(stable.StableMaster.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BUY_STABLE_SLOT)]
	private void HandleBuyStableSlot(BuyStableSlot stable)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_BUY_STABLE_SLOT);
		packet.WriteGuid(stable.StableMaster.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_ABANDON)]
	private void HandlePetAbandon(PetAbandon pet)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_ABANDON);
		packet.WriteGuid(pet.PetGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_STABLE_PET)]
	private void HandleStablePet(StablePet pet)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_STABLE_PET);
		packet.WriteGuid(pet.StableMaster.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_UNSTABLE_PET)]
	private void HandleUnstablePet(UnstablePet pet)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_UNSTABLE_PET);
		packet.WriteGuid(pet.StableMaster.To64());
		packet.WriteUInt32(pet.PetNumber);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_STABLE_SWAP_PET)]
	private void HandleStableSwapPet(StableSwapPet pet)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_STABLE_SWAP_PET);
		packet.WriteGuid(pet.StableMaster.To64());
		packet.WriteUInt32(pet.PetNumber);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_CANCEL_AURA)]
	private void HandlePetCancelAura(PetCancelAura cancel)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_CANCEL_AURA);
		packet.WriteGuid(cancel.PetGUID.To64());
		packet.WriteUInt32(cancel.SpellID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_REQUEST_PET_INFO)]
	private void HandleRequestPetInfo(PetInfoRequest r)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_REQUEST_PET_INFO);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PETITION_BUY)]
	private void HandlePetitionBuy(PetitionBuy petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PETITION_BUY);
		packet.WriteGuid(petition.Unit.To64());
		packet.WriteUInt32(0u);
		packet.WriteUInt64(0uL);
		packet.WriteCString(petition.Title);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteCString("");
		}
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt16(0);
		}
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		packet.WriteUInt32(0u);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			for (int i = 0; i < 10; i++)
			{
				packet.WriteCString("");
			}
		}
		else
		{
			packet.WriteUInt16(0);
			packet.WriteUInt8(0);
		}
		packet.WriteUInt32(petition.Index);
		packet.WriteUInt32(0u);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PETITION_SHOW_SIGNATURES)]
	private void HandlePetitionShowSignatures(PetitionShowSignatures petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PETITION_SHOW_SIGNATURES);
		packet.WriteGuid(petition.Item.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_PETITION)]
	private void HandleQueryPetition(QueryPetition petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_PETITION);
		packet.WriteUInt32(petition.PetitionID);
		packet.WriteGuid(petition.ItemGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PETITION_RENAME_GUILD)]
	private void HandlePetitionRenameGuild(PetitionRenameGuild petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_PETITION_RENAME);
		packet.WriteGuid(petition.PetitionGuid.To64());
		packet.WriteCString(petition.NewGuildName);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_OFFER_PETITION)]
	private void HandleOfferPetition(OfferPetition petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_OFFER_PETITION);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(petition.UnkInt);
		}
		packet.WriteGuid(petition.ItemGUID.To64());
		packet.WriteGuid(petition.TargetPlayer.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DECLINE_PETITION)]
	private void HandleDeclinePetition(DeclinePetition petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_PETITION_DECLINE);
		packet.WriteGuid(petition.PetitionGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SIGN_PETITION)]
	private void HandleSignPetition(SignPetition petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SIGN_PETITION);
		packet.WriteGuid(petition.PetitionGUID.To64());
		packet.WriteUInt8(petition.Choice);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TURN_IN_PETITION)]
	private void HandleTurnInPetition(TurnInPetition petition)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_TURN_IN_PETITION);
		packet.WriteGuid(petition.Item.To64());
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(petition.BackgroundColor);
			packet.WriteUInt32(petition.EmblemStyle);
			packet.WriteUInt32(petition.EmblemColor);
			packet.WriteUInt32(petition.BorderStyle);
			packet.WriteUInt32(petition.BorderColor);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_TIME)]
	private void HandleQueryTime(EmptyClientPacket queryTime)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_TIME);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_QUEST_INFO)]
	private void HandleQueryQuestInfo(QueryQuestInfo queryQuest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_QUEST_INFO);
		packet.WriteUInt32(queryQuest.QuestID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_CREATURE)]
	private void HandleQueryCreature(QueryCreature queryCreature)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_CREATURE);
		packet.WriteUInt32(queryCreature.CreatureID);
		packet.WriteGuid(new WowGuid64(HighGuidTypeLegacy.Creature, queryCreature.CreatureID, 1u));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_GAME_OBJECT)]
	private void HandleQueryGameObject(QueryGameObject queryGo)
	{
		// Respond from cache immediately if available (avoids round-trip for transports)
		if (GetSession().GameState.GameObjectQueryCache.TryGetValue(queryGo.GameObjectID, out var cached))
		{
			var response = new HermesProxy.World.Server.Packets.QueryGameObjectResponse();
			response.GameObjectID = cached.GameObjectID;
			response.Guid = WowGuid128.Empty;
			response.Allow = cached.Allow;
			response.Stats = cached.Stats;
			SendPacket(response);
			return;
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_GAME_OBJECT);
		packet.WriteUInt32(queryGo.GameObjectID);
		packet.WriteGuid(queryGo.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_PAGE_TEXT)]
	private void HandleQueryPageText(QueryPageText queryText)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_PAGE_TEXT);
		packet.WriteUInt32(queryText.PageTextID);
		packet.WriteGuid(queryText.ItemGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_NPC_TEXT)]
	private void HandleQueryNpcText(QueryNPCText queryText)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_NPC_TEXT);
		packet.WriteUInt32(queryText.TextID);
		packet.WriteGuid(queryText.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUERY_PET_NAME)]
	private void HandleQueryPetName(QueryPetName queryName)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUERY_PET_NAME);
		packet.WriteUInt32(queryName.UnitGUID.GetEntry());
		packet.WriteGuid(queryName.UnitGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_WHO)]
	private void HandleWhoRequest(WhoRequestPkt who)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_WHO);
		packet.WriteInt32(who.Request.MinLevel);
		packet.WriteInt32(who.Request.MaxLevel);
		packet.WriteCString(who.Request.Name);
		packet.WriteCString(who.Request.Guild);
		packet.WriteInt32((int)who.Request.RaceFilter);
		packet.WriteInt32(who.Request.ClassFilter);
		packet.WriteInt32(who.Areas.Count);
		foreach (int area in who.Areas)
		{
			packet.WriteInt32(area);
		}
		packet.WriteInt32(who.Request.Words.Count);
		foreach (string word in who.Request.Words)
		{
			packet.WriteCString(word);
		}
		this.SendPacketToServer(packet);
		this.GetSession().GameState.LastWhoRequestId = who.RequestID;
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_QUERY_QUEST)]
	private void HandleQuestGiverQueryQuest(QuestGiverQueryQuest quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_QUERY_QUEST);
		packet.WriteGuid(quest.QuestGiverGUID.To64());
		packet.WriteUInt32(quest.QuestID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteBool(quest.RespondToGiver);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_ACCEPT_QUEST)]
	private void HandleQuestGiverAcceptQuest(QuestGiverAcceptQuest quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_ACCEPT_QUEST);
		packet.WriteGuid(quest.QuestGiverGUID.To64());
		packet.WriteUInt32(quest.QuestID);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901))
		{
			packet.WriteInt32(quest.StartCheat ? 1 : 0);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_LOG_REMOVE_QUEST)]
	private void HandleQuestLogRemoveQuest(QuestLogRemoveQuest quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_LOG_REMOVE_QUEST);
		packet.WriteUInt8(quest.Slot);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_STATUS_QUERY)]
	private void HandleQuestGiverStatusQuery(QuestGiverStatusQuery query)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_STATUS_QUERY);
		packet.WriteGuid(query.QuestGiverGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_STATUS_MULTIPLE_QUERY)]
	private void HandleQuestGiverStatusMultipleQuery(QuestGiverStatusMultipleQuery query)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_STATUS_MULTIPLE_QUERY);
			this.SendPacketToServer(packet);
			return;
		}
		int UNIT_NPC_FLAGS = ModernVersion.GetUpdateField(UnitField.UNIT_NPC_FLAGS);
		if (UNIT_NPC_FLAGS < 0)
		{
			return;
		}
		List<WowGuid128> npcGuids = new List<WowGuid128>();
		this.GetSession().GameState.ObjectCacheMutex.WaitOne();
		foreach (KeyValuePair<WowGuid128, UpdateFieldsArray> obj in this.GetSession().GameState.ObjectCacheModern)
		{
			if (obj.Key.GetObjectType() == ObjectType.Unit && obj.Value.GetUpdateField<uint>(UNIT_NPC_FLAGS, 0).HasAnyFlag(NPCFlags.QuestGiver))
			{
				npcGuids.Add(obj.Key);
			}
		}
		this.GetSession().GameState.ObjectCacheMutex.ReleaseMutex();
		foreach (WowGuid128 guid in npcGuids)
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_QUEST_GIVER_STATUS_QUERY);
			packet2.WriteGuid(guid.To64());
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_HELLO)]
	private void HandleQuestGiverHello(QuestGiverHello hello)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_HELLO);
		packet.WriteGuid(hello.QuestGiverGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_REQUEST_REWARD)]
	private void HandleQuestGiverRequestReward(QuestGiverRequestReward quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_REQUEST_REWARD);
		packet.WriteGuid(quest.QuestGiverGUID.To64());
		packet.WriteUInt32(quest.QuestID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_CHOOSE_REWARD)]
	private void HandleQuestGiverChooseReward(QuestGiverChooseReward quest)
	{
		int choiceIndex = 0;
		if (quest.Choice.Item.ItemID != 0)
		{
			QuestTemplate questTemplate = GameData.GetQuestTemplate(quest.QuestID);
			if (questTemplate == null)
			{
				Log.Print(LogType.Error, "Unable to select quest reward because quest template is missing. Try again.", "HandleQuestGiverChooseReward", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\QuestHandler.cs");
				WorldPacket packet2 = new WorldPacket(Opcode.CMSG_QUERY_QUEST_INFO);
				packet2.WriteUInt32(quest.QuestID);
				this.SendPacketToServer(packet2);
				QuestGiverQuestFailed fail = new QuestGiverQuestFailed();
				fail.QuestID = quest.QuestID;
				fail.Reason = InventoryResult.ItemNotFound;
				this.SendPacket(fail);
				return;
			}
			for (int i = 0; i < questTemplate.UnfilteredChoiceItems.Length; i++)
			{
				if (questTemplate.UnfilteredChoiceItems[i].ItemID == quest.Choice.Item.ItemID)
				{
					choiceIndex = i;
					break;
				}
			}
		}
		WorldPacket packet3 = new WorldPacket(Opcode.CMSG_QUEST_GIVER_CHOOSE_REWARD);
		packet3.WriteGuid(quest.QuestGiverGUID.To64());
		packet3.WriteUInt32(quest.QuestID);
		packet3.WriteInt32(choiceIndex);
		this.SendPacketToServer(packet3);
	}

	[PacketHandler(Opcode.CMSG_QUEST_GIVER_COMPLETE_QUEST)]
	private void HandleQuestGiverCompleteQuest(QuestGiverCompleteQuest quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_GIVER_COMPLETE_QUEST);
		packet.WriteGuid(quest.QuestGiverGUID.To64());
		packet.WriteUInt32(quest.QuestID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_CONFIRM_ACCEPT)]
	private void HandleQuestConfirmAcceptResponse(QuestConfirmAcceptResponse quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_QUEST_CONFIRM_ACCEPT);
		packet.WriteUInt32(quest.QuestID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PUSH_QUEST_TO_PARTY)]
	private void HandlePushQuestToParty(PushQuestToParty quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_PUSH_QUEST_TO_PARTY);
		packet.WriteUInt32(quest.QuestID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_QUEST_PUSH_RESULT)]
	private void HandleQuestPushResult(QuestPushResultResponse quest)
	{
		WorldPacket packet = new WorldPacket(Opcode.MSG_QUEST_PUSH_RESULT);
		packet.WriteGuid(quest.SenderGUID.To64());
		packet.WriteUInt8((byte)quest.Result);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_FACTION_AT_WAR)]
	private void HandleSetFactionAtWar(SetFactionAtWar faction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_FACTION_AT_WAR);
		packet.WriteUInt32(faction.FactionIndex);
		packet.WriteBool(data: true);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_FACTION_NOT_AT_WAR)]
	private void HandleSetFactionNotAtWar(SetFactionNotAtWar faction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_FACTION_AT_WAR);
		packet.WriteUInt32(faction.FactionIndex);
		packet.WriteBool(data: false);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_FACTION_INACTIVE)]
	private void HandleSetFactionInactive(SetFactionInactive faction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_FACTION_INACTIVE);
		packet.WriteUInt32(faction.FactionIndex);
		packet.WriteBool(faction.State);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_WATCHED_FACTION)]
	private void HandleSetFactionInactive(SetWatchedFaction faction)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_WATCHED_FACTION);
		packet.WriteUInt32(faction.FactionIndex);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CHANGE_REALM_TICKET)]
	private void HandleChangeRealmTicket(ChangeRealmTicket request)
	{
		ChangeRealmTicketResponse response = new ChangeRealmTicketResponse();
		response.Token = request.Token;
		if (!this.GetSession().AuthClient.IsConnected() && this.GetSession().AuthClient.Reconnect() != HermesProxy.Auth.AuthResult.SUCCESS)
		{
			Log.Print(LogType.Error, "Failed to reconnect to auth server.", "HandleChangeRealmTicket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SessionHandler.cs");
			response.Allow = false;
			this.SendPacket(response);
		}
		else
		{
			this._bnetRpc.SetClientSecret(request.Secret);
			response.Allow = true;
			response.Ticket = new ByteBuffer(new byte[1]);
			this.SendPacket(response);
		}
	}

	[PacketHandler(Opcode.CMSG_BATTLENET_REQUEST)]
	private void HandleBattlenetRequest(BattlenetRequest request)
	{
		if (this._bnetRpc == null)
		{
			Log.Print(LogType.Error, $"Client tried {108} without authentication", "HandleBattlenetRequest", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SessionHandler.cs");
		}
		else
		{
			this._bnetRpc.Invoke(0u, (OriginalHash)request.Method.GetServiceHash(), request.Method.GetMethodId(), request.Method.Token, new CodedInputStream(request.Data));
		}
	}

	[PacketHandler(Opcode.CMSG_CONTACT_LIST)]
	private void HandleContactList(ContactListRequest contacts)
	{
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_FRIEND_LIST);
			this.SendPacketToServer(packet);
		}
		else
		{
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_CONTACT_LIST);
			packet2.WriteUInt32((uint)contacts.Flags);
			this.SendPacketToServer(packet2);
		}
	}

	[PacketHandler(Opcode.CMSG_ADD_FRIEND)]
	private void HandleAddFriend(AddFriend friend)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ADD_FRIEND);
		packet.WriteCString(friend.Name);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteCString(friend.Note);
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ADD_IGNORE)]
	private void HandleAddIgnore(AddIgnore ignore)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ADD_IGNORE);
		packet.WriteCString(ignore.Name);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_DEL_FRIEND)]
	[PacketHandler(Opcode.CMSG_DEL_IGNORE)]
	private void HandleDelFriend(DelFriend friend)
	{
		WorldPacket packet = new WorldPacket(friend.GetUniversalOpcode());
		packet.WriteGuid(friend.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_CONTACT_NOTES)]
	private void HandleSetContactNotes(SetContactNotes friend)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_CONTACT_NOTES);
			packet.WriteGuid(friend.Guid.To64());
			packet.WriteCString(friend.Notes);
			this.SendPacketToServer(packet);
		}
	}

	private SpellCastTargetFlags ConvertSpellTargetFlags(SpellTargetData target)
	{
		SpellCastTargetFlags targetFlags = SpellCastTargetFlags.None;
		if (target.Unit != null && !target.Unit.IsEmpty())
		{
			if (target.Flags.HasFlag(SpellCastTargetFlags.Unit))
			{
				targetFlags |= SpellCastTargetFlags.Unit;
			}
			if (target.Flags.HasFlag(SpellCastTargetFlags.CorpseEnemy))
			{
				targetFlags |= SpellCastTargetFlags.CorpseEnemy;
			}
			if (target.Flags.HasFlag(SpellCastTargetFlags.GameObject))
			{
				targetFlags |= SpellCastTargetFlags.GameObject;
			}
			if (target.Flags.HasFlag(SpellCastTargetFlags.CorpseAlly))
			{
				targetFlags |= SpellCastTargetFlags.CorpseAlly;
			}
			if (target.Flags.HasFlag(SpellCastTargetFlags.UnitMinipet))
			{
				targetFlags |= SpellCastTargetFlags.UnitMinipet;
			}
		}
		if ((target.Item != null) & !target.Item.IsEmpty())
		{
			if (target.Flags.HasFlag(SpellCastTargetFlags.Item))
			{
				targetFlags |= SpellCastTargetFlags.Item;
			}
			if (target.Flags.HasFlag(SpellCastTargetFlags.TradeItem))
			{
				targetFlags |= SpellCastTargetFlags.TradeItem;
			}
		}
		if (target.SrcLocation != null)
		{
			targetFlags |= SpellCastTargetFlags.SourceLocation;
		}
		if (target.DstLocation != null)
		{
			targetFlags |= SpellCastTargetFlags.DestLocation;
		}
		if (!string.IsNullOrEmpty(target.Name))
		{
			targetFlags |= SpellCastTargetFlags.String;
		}
		return targetFlags;
	}

	private void WriteSpellTargets(SpellTargetData target, SpellCastTargetFlags targetFlags, WorldPacket packet)
	{
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt16((ushort)targetFlags);
		}
		else
		{
			packet.WriteUInt32((uint)targetFlags);
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.CorpseMask | SpellCastTargetFlags.Unit | SpellCastTargetFlags.GameObject | SpellCastTargetFlags.UnitMinipet))
		{
			packet.WritePackedGuid(target.Unit.To64());
		}
		if (targetFlags.HasFlag(SpellCastTargetFlags.TradeItem) && target.Item == WowGuid128.Create(HighGuidType703.Uniq, 10uL))
		{
			packet.WritePackedGuid(new WowGuid64(6uL));
		}
		else if (targetFlags.HasFlag(SpellCastTargetFlags.Item))
		{
			packet.WritePackedGuid(target.Item.To64());
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.SourceLocation))
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192))
			{
				packet.WritePackedGuid(target.SrcLocation.Transport.To64());
			}
			packet.WriteVector3(target.SrcLocation.Location);
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.DestLocation))
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_8_9464))
			{
				packet.WritePackedGuid(target.DstLocation.Transport.To64());
			}
			packet.WriteVector3(target.DstLocation.Location);
		}
		if (targetFlags.HasAnyFlag(SpellCastTargetFlags.String))
		{
			packet.WriteCString(target.Name);
		}
	}

	public void SendCastRequestFailed(ClientCastRequest castRequest, bool isPet)
	{
		if (!castRequest.HasStarted)
		{
			SpellPrepare prepare2 = new SpellPrepare();
			prepare2.ClientCastID = castRequest.ClientGUID;
			prepare2.ServerCastID = castRequest.ServerGUID;
			this.SendPacket(prepare2);
		}
		if (isPet)
		{
			PetCastFailed failed = new PetCastFailed();
			failed.SpellID = castRequest.SpellId;
			failed.Reason = 123u;
			failed.CastID = castRequest.ServerGUID;
			this.SendPacket(failed);
		}
		else
		{
			CastFailed failed2 = new CastFailed();
			failed2.SpellID = castRequest.SpellId;
			failed2.SpellXSpellVisualID = castRequest.SpellXSpellVisualId;
			failed2.Reason = 123u;
			failed2.CastID = castRequest.ServerGUID;
			this.SendPacket(failed2);
		}
	}

	[PacketHandler(Opcode.CMSG_CAST_SPELL)]
	private void HandleCastSpell(CastSpell cast)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		if (GameData.NextMeleeSpells.Contains(cast.Cast.SpellID) || GameData.AutoRepeatSpells.Contains(cast.Cast.SpellID))
		{
			ClientCastRequest castRequest = new ClientCastRequest();
			castRequest.Timestamp = Environment.TickCount;
			castRequest.SpellId = cast.Cast.SpellID;
			castRequest.SpellXSpellVisualId = cast.Cast.SpellXSpellVisualID;
			castRequest.ClientGUID = cast.Cast.CastID;
			if (this.GetSession().GameState.CurrentClientSpecialCast != null)
			{
				castRequest.ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, cast.Cast.SpellID, 10000 + cast.Cast.CastID.GetCounter());
				this.SendCastRequestFailed(castRequest, isPet: false);
				return;
			}
			castRequest.ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, cast.Cast.SpellID, cast.Cast.SpellID + this.GetSession().GameState.CurrentPlayerGuid.GetCounter());
			SpellPrepare prepare = new SpellPrepare();
			prepare.ClientCastID = cast.Cast.CastID;
			prepare.ServerCastID = castRequest.ServerGUID;
			this.SendPacket(prepare);
			this.GetSession().GameState.CurrentClientSpecialCast = castRequest;
		}
		else
		{
			ClientCastRequest castRequest2 = new ClientCastRequest();
			castRequest2.Timestamp = Environment.TickCount;
			castRequest2.SpellId = cast.Cast.SpellID;
			castRequest2.SpellXSpellVisualId = cast.Cast.SpellXSpellVisualID;
			castRequest2.ClientGUID = cast.Cast.CastID;
			castRequest2.ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, cast.Cast.SpellID, 10000 + cast.Cast.CastID.GetCounter());
			if (this.GetSession().GameState.CurrentClientNormalCast != null)
			{
				if (this.GetSession().GameState.CurrentClientNormalCast.HasStarted)
				{
					this.SendCastRequestFailed(castRequest2, isPet: false);
				}
				else if (this.GetSession().GameState.CurrentClientNormalCast.Timestamp + 10000 < castRequest2.Timestamp)
				{
					Log.Print(LogType.Warn, $"Clearing CurrentClientNormalCast because of 10 sec timeout! (oldSpell:{this.GetSession().GameState.CurrentClientNormalCast.SpellId} newSpell:{castRequest2.SpellId})", "HandleCastSpell", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SpellHandler.cs");
					Log.Print(LogType.Warn, "Are you playing on a server with another patch?", "HandleCastSpell", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SpellHandler.cs");
					this.SendCastRequestFailed(this.GetSession().GameState.CurrentClientNormalCast, isPet: false);
					this.GetSession().GameState.CurrentClientNormalCast = null;
					foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientCasts)
					{
						this.SendCastRequestFailed(pending, isPet: false);
					}
					this.GetSession().GameState.PendingClientCasts.Clear();
					this.SendCastRequestFailed(castRequest2, isPet: false);
				}
				else
				{
					this.GetSession().GameState.PendingClientCasts.Add(castRequest2);
				}
				return;
			}
			this.GetSession().GameState.CurrentClientNormalCast = castRequest2;
		}
		SpellCastTargetFlags targetFlags = this.ConvertSpellTargetFlags(cast.Cast.Target);
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CAST_SPELL);
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt32(cast.Cast.SpellID);
		}
		else if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt32(cast.Cast.SpellID);
			packet.WriteUInt8(0);
		}
		else
		{
			packet.WriteUInt8(0);
			packet.WriteUInt32(cast.Cast.SpellID);
			packet.WriteUInt8((byte)cast.Cast.SendCastFlags);
		}
		this.WriteSpellTargets(cast.Cast.Target, targetFlags, packet);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_PET_CAST_SPELL)]
	private void HandlePetCastSpell(PetCastSpell cast)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		ClientCastRequest castRequest = new ClientCastRequest();
		castRequest.Timestamp = Environment.TickCount;
		castRequest.SpellId = cast.Cast.SpellID;
		castRequest.SpellXSpellVisualId = cast.Cast.SpellXSpellVisualID;
		castRequest.ClientGUID = cast.Cast.CastID;
		castRequest.ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, cast.Cast.SpellID, 10000 + cast.Cast.CastID.GetCounter());
		if (this.GetSession().GameState.CurrentClientPetCast != null)
		{
			if (this.GetSession().GameState.CurrentClientPetCast.HasStarted)
			{
				this.SendCastRequestFailed(castRequest, isPet: true);
			}
			else if (this.GetSession().GameState.CurrentClientPetCast.Timestamp + 10000 < castRequest.Timestamp)
			{
				Log.Print(LogType.Warn, $"Clearing CurrentClientPetCast because of 10 sec timeout! (oldSpell:{this.GetSession().GameState.CurrentClientPetCast.SpellId} newSpell:{castRequest.SpellId})", "HandlePetCastSpell", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SpellHandler.cs");
				this.SendCastRequestFailed(this.GetSession().GameState.CurrentClientPetCast, isPet: true);
				this.GetSession().GameState.CurrentClientPetCast = null;
				foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientPetCasts)
				{
					this.SendCastRequestFailed(pending, isPet: true);
				}
				this.GetSession().GameState.PendingClientPetCasts.Clear();
				this.SendCastRequestFailed(castRequest, isPet: true);
			}
			else
			{
				this.GetSession().GameState.PendingClientPetCasts.Add(castRequest);
			}
		}
		else
		{
			this.GetSession().GameState.CurrentClientPetCast = castRequest;
			SpellCastTargetFlags targetFlags = this.ConvertSpellTargetFlags(cast.Cast.Target);
			WorldPacket packet = new WorldPacket(Opcode.CMSG_PET_CAST_SPELL);
			packet.WriteGuid(cast.PetGUID.To64());
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				packet.WriteUInt8(0);
			}
			packet.WriteUInt32(cast.Cast.SpellID);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				packet.WriteUInt8((byte)cast.Cast.SendCastFlags);
			}
			this.WriteSpellTargets(cast.Cast.Target, targetFlags, packet);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_USE_ITEM)]
	private void HandleUseItem(UseItem use)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		ClientCastRequest castRequest = new ClientCastRequest();
		castRequest.Timestamp = Environment.TickCount;
		castRequest.SpellId = use.Cast.SpellID;
		castRequest.SpellXSpellVisualId = use.Cast.SpellXSpellVisualID;
		castRequest.ClientGUID = use.Cast.CastID;
		castRequest.ServerGUID = WowGuid128.Create(HighGuidType703.Cast, SpellCastSource.Normal, this.GetSession().GameState.CurrentMapId.Value, use.Cast.SpellID, 10000 + use.Cast.CastID.GetCounter());
		castRequest.ItemGUID = use.CastItem;
		Log.Print(LogType.Debug, $"[UseItem] SpellID={use.Cast.SpellID} PackSlot={use.PackSlot} Slot={use.Slot} ItemGUID={use.CastItem} PendingCast={this.GetSession().GameState.CurrentClientNormalCast != null}", "HandleUseItem", "");
		if (this.GetSession().GameState.CurrentClientNormalCast != null)
		{
			if (this.GetSession().GameState.CurrentClientNormalCast.HasStarted)
			{
				this.SendCastRequestFailed(castRequest, isPet: false);
			}
			else if (this.GetSession().GameState.CurrentClientNormalCast.Timestamp + 10000 < castRequest.Timestamp)
			{
				Log.Print(LogType.Warn, $"Clearing CurrentClientNormalCast because of 10 sec timeout! (oldSpell:{this.GetSession().GameState.CurrentClientNormalCast.SpellId} newSpell:{castRequest.SpellId})", "HandleUseItem", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\SpellHandler.cs");
				this.SendCastRequestFailed(this.GetSession().GameState.CurrentClientNormalCast, isPet: false);
				this.GetSession().GameState.CurrentClientNormalCast = null;
				foreach (ClientCastRequest pending in this.GetSession().GameState.PendingClientCasts)
				{
					this.SendCastRequestFailed(pending, isPet: false);
				}
				this.GetSession().GameState.PendingClientCasts.Clear();
				this.SendCastRequestFailed(castRequest, isPet: false);
			}
			else
			{
				this.GetSession().GameState.PendingClientCasts.Add(castRequest);
			}
		}
		else
		{
			this.GetSession().GameState.CurrentClientNormalCast = castRequest;
			WorldPacket packet = new WorldPacket(Opcode.CMSG_USE_ITEM);
			byte containerSlot = ((use.PackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(use.PackSlot) : use.PackSlot);
			byte slot = ((use.PackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(use.Slot) : use.Slot);
			packet.WriteUInt8(containerSlot); // bagIndex
			packet.WriteUInt8(slot); // slot
			packet.WriteUInt8(this.GetSession().GameState.GetItemSpellSlot(use.CastItem, use.Cast.SpellID)); // castCount
			packet.WriteUInt32(use.Cast.SpellID); // spellId
			packet.WriteGuid(use.CastItem.To64()); // itemGUID
			packet.WriteUInt32(0u); // glyphIndex
			packet.WriteUInt8(0); // castFlags
			SpellCastTargetFlags targetFlags = this.ConvertSpellTargetFlags(use.Cast.Target);
			this.WriteSpellTargets(use.Cast.Target, targetFlags, packet);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_CANCEL_CAST)]
	private void HandleCancelCast(CancelCast cast)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_CAST);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			packet.WriteUInt8(0);
		}
		packet.WriteUInt32(cast.SpellID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CANCEL_CHANNELLING)]
	private void HandleCancelChannelling(CancelChannelling cast)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_CHANNELLING);
		packet.WriteInt32(cast.SpellID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CANCEL_AUTO_REPEAT_SPELL)]
	private void HandleCancelAutoRepeatSpell(CancelAutoRepeatSpell spell)
	{
		if (Settings.ServerSpellDelay > 0)
		{
			Thread.Sleep(Settings.ServerSpellDelay);
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_AUTO_REPEAT_SPELL);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CANCEL_AURA)]
	private void HandleCancelAura(CancelAura aura)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_AURA);
		packet.WriteUInt32(aura.SpellID);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CANCEL_MOUNT_AURA)]
	private void HandleCancelMountAura(EmptyClientPacket cancel)
	{
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_CANCEL_MOUNT_AURA);
			this.SendPacketToServer(packet);
			return;
		}
		WowGuid128 guid = this.GetSession().GameState.CurrentPlayerGuid;
		Dictionary<int, UpdateField> updateFields = this.GetSession().GameState.GetCachedObjectFieldsLegacy(guid);
		if (updateFields == null)
		{
			return;
		}
		for (byte i = 0; i < 32; i++)
		{
			AuraDataInfo aura = this.GetSession().WorldClient.ReadAuraSlot(i, guid, updateFields);
			if (aura != null && GameData.MountAuras.Contains(aura.SpellID))
			{
				WorldPacket packet2 = new WorldPacket(Opcode.CMSG_CANCEL_AURA);
				packet2.WriteUInt32(aura.SpellID);
				this.SendPacketToServer(packet2);
			}
		}
	}

	[PacketHandler(Opcode.CMSG_LEARN_TALENT)]
	private void HandleLearnTalent(LearnTalent talent)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_LEARN_TALENT);
		packet.WriteUInt32(talent.TalentID);
		packet.WriteUInt32(talent.Rank);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_RESURRECT_RESPONSE)]
	private void HandleResurrectResponse(ResurrectResponse revive)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_RESURRECT_RESPONSE);
		packet.WriteGuid(revive.CasterGUID.To64());
		packet.WriteUInt8((revive.Response == 0) ? ((byte)1) : ((byte)0));
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SELF_RES)]
	private void HandleSelfRes(SelfRes revive)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SELF_RES);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TOTEM_DESTROYED)]
	private void HandleTotemDestroyed(TotemDestroyed totem)
	{
		if (!LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_TOTEM_DESTROYED);
			packet.WriteUInt8(totem.Slot);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_SUPPORT_TICKET_SUBMIT_COMPLAINT)]
	private void HandleSupportTicketSubmitComplaint(SupportTicketSubmitComplaint complaint)
	{
		string targetPlayerName = this.Session.GameState.GetPlayerName(complaint.TargetCharacterGuid);
		if (string.IsNullOrWhiteSpace(targetPlayerName))
		{
			this.Session.SendHermesTextMessage("Unable to report player because CharacterName was not resolved (can be fixed by restarting the client)", isError: true);
			return;
		}
		string ticketText = "[REPORTED VIA QUICKMENU]\r\nI would like to report player '" + targetPlayerName + "'";
		if (!WowGuid128.IsUnknownPlayerGuid(complaint.TargetCharacterGuid))
		{
			ticketText += $"  (id: {complaint.TargetCharacterGuid.GetCounter()})";
		}
		if (complaint.ComplaintType != GmTicketComplaintType.Unknown)
		{
			ticketText += $" for {complaint.ComplaintType}";
		}
		if (complaint.SelectedMailInfo != null)
		{
			ticketText = ticketText + "\r\n" + $"Mail in question (id: {complaint.SelectedMailInfo.MailId}) with subject '{complaint.SelectedMailInfo.MailSubject}'";
		}
		if (!complaint.TextNote.IsEmpty())
		{
			ticketText += "\r\n-------------";
			ticketText = ticketText + "\r\n" + complaint.TextNote;
		}
		WorldPacket packet = new WorldPacket(Opcode.CMSG_GM_TICKET_CREATE);
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			packet.WriteUInt8(2);
			packet.WriteUInt32(complaint.Header.SelfPlayerMapId);
			packet.WriteVector3(complaint.Header.SelfPlayerPos);
			packet.WriteCString(ticketText);
			packet.WriteCString("");
		}
		else
		{
			packet.WriteUInt32(complaint.Header.SelfPlayerMapId);
			packet.WriteVector3(complaint.Header.SelfPlayerPos);
			packet.WriteCString(ticketText);
			packet.WriteUInt32(0u);
			packet.WriteUInt32(0u);
			packet.WriteUInt32(0u);
			packet.WriteBytes(Array.Empty<byte>());
		}
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_TAXI_NODE_STATUS_QUERY)]
	[PacketHandler(Opcode.CMSG_TAXI_QUERY_AVAILABLE_NODES)]
	private void HandleTaxiNodesQuery(InteractWithNPC interact)
	{
		WorldPacket packet = new WorldPacket(interact.GetUniversalOpcode());
		packet.WriteGuid(interact.CreatureGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ENABLE_TAXI_NODE)]
	private void HandleEnableTaxiNode(InteractWithNPC interact)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_TALK_TO_GOSSIP);
		packet.WriteGuid(interact.CreatureGUID.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_ACTIVATE_TAXI)]
	private void HandleActivateTaxi(ActivateTaxi taxi)
	{
		if (this.TaxiPathExist(this.GetSession().GameState.CurrentTaxiNode, taxi.Node))
		{
			WorldPacket packet = new WorldPacket(Opcode.CMSG_ACTIVATE_TAXI);
			packet.WriteGuid(taxi.FlightMaster.To64());
			packet.WriteUInt32(this.GetSession().GameState.CurrentTaxiNode);
			packet.WriteUInt32(taxi.Node);
			this.SendPacketToServer(packet);
		}
		else
		{
			HashSet<uint> path = this.GetTaxiPath(this.GetSession().GameState.CurrentTaxiNode, taxi.Node, this.GetSession().GameState.UsableTaxiNodes);
			if (path.Count <= 1)
			{
				return;
			}
			WorldPacket packet2 = new WorldPacket(Opcode.CMSG_ACTIVATE_TAXI_EXPRESS);
			packet2.WriteGuid(taxi.FlightMaster.To64());
			packet2.WriteUInt32(0u);
			packet2.WriteUInt32((uint)path.Count);
			foreach (uint itr in path)
			{
				packet2.WriteUInt32(itr);
			}
			this.SendPacketToServer(packet2);
		}
		this.GetSession().GameState.IsWaitingForTaxiStart = true;
	}

	private bool TaxiPathExist(uint from, uint to)
	{
		foreach (KeyValuePair<uint, TaxiPath> itr in GameData.TaxiPaths)
		{
			if ((itr.Value.From == from && itr.Value.To == to) || (itr.Value.From == to && itr.Value.To == from))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsTaxiNodeKnown(uint node, List<byte> usableNodes)
	{
		byte field = (byte)((node - 1) / 8);
		uint submask = (uint)(1 << (int)(byte)((node - 1) % 8));
		return (usableNodes[field] & submask) == submask;
	}

	private HashSet<uint> GetTaxiPath(uint from, uint to, List<byte> usableNodes)
	{
		HashSet<uint> nodes = new HashSet<uint> { from };
		int[,] graphCopy = new int[GameData.TaxiNodesGraph.GetLength(0), GameData.TaxiNodesGraph.GetLength(1)];
		Buffer.BlockCopy(GameData.TaxiNodesGraph, 0, graphCopy, 0, GameData.TaxiNodesGraph.Length * 4);
		for (uint i = 1u; i < graphCopy.GetLength(0); i++)
		{
			if (!this.IsTaxiNodeKnown(i, usableNodes))
			{
				for (uint itr = 0u; itr < graphCopy.GetLength(1); itr++)
				{
					graphCopy[i, itr] = 0;
				}
				for (uint itr2 = 0u; itr2 < graphCopy.GetLength(0); itr2++)
				{
					graphCopy[itr2, i] = 0;
				}
			}
		}
		int minDist = this.Dijkstra(graphCopy, (int)from, (int)to, graphCopy.GetLength(0), nodes);
		return nodes;
	}

	private int MinDistance(int[] dist, bool[] sptSet, int vCnt)
	{
		int min = int.MaxValue;
		int min_index = -1;
		for (int v = 0; v < vCnt; v++)
		{
			if (!sptSet[v] && dist[v] <= min)
			{
				min = dist[v];
				min_index = v;
			}
		}
		return min_index;
	}

	private void SavePath(int[] parent, int j, HashSet<uint> nodes)
	{
		if (parent[j] != -1)
		{
			this.SavePath(parent, parent[j], nodes);
			nodes.Add((uint)j);
		}
	}

	private int Dijkstra(int[,] graph, int src, int dest, int vCnt, HashSet<uint> nodes)
	{
		int[] dist = new int[vCnt];
		int[] parent = new int[vCnt];
		bool[] sptSet = new bool[vCnt];
		for (int i = 0; i < vCnt; i++)
		{
			dist[i] = int.MaxValue;
			sptSet[i] = false;
			parent[i] = -1;
		}
		dist[src] = 0;
		for (int count = 0; count < vCnt - 1; count++)
		{
			int u = this.MinDistance(dist, sptSet, vCnt);
			sptSet[u] = true;
			for (int v = 0; v < vCnt; v++)
			{
				if (!sptSet[v] && graph[u, v] != 0 && dist[u] != int.MaxValue && dist[u] + graph[u, v] < dist[v])
				{
					parent[v] = u;
					dist[v] = dist[u] + graph[u, v];
				}
			}
		}
		this.SavePath(parent, dest, nodes);
		return dist[dest];
	}

	[PacketHandler(Opcode.CMSG_INITIATE_TRADE)]
	private void HandleInitiateTrade(InitiateTrade trade)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_INITIATE_TRADE);
		packet.WriteGuid(trade.Guid.To64());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_SET_TRADE_GOLD)]
	private void HandleSetTradeGold(SetTradeGold trade)
	{
		TradeSession tradeSession = this.GetSession().GameState.CurrentTrade;
		if (tradeSession == null)
		{
			Log.Print(LogType.Error, $"Got {trade.GetUniversalOpcode()} without trade session", "HandleSetTradeGold", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\TradeHandler.cs");
		}
		else
		{
			tradeSession.ClientStateIndex++;
			WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_TRADE_GOLD);
			packet.WriteInt32((int)trade.Coinage);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_ACCEPT_TRADE)]
	private void HandleAcceptTrade(AcceptTrade trade)
	{
		WorldPacket packet = new WorldPacket(Opcode.CMSG_ACCEPT_TRADE);
		packet.WriteUInt32(trade.StateIndex);
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_BEGIN_TRADE)]
	[PacketHandler(Opcode.CMSG_BUSY_TRADE)]
	[PacketHandler(Opcode.CMSG_CANCEL_TRADE)]
	[PacketHandler(Opcode.CMSG_UNACCEPT_TRADE)]
	[PacketHandler(Opcode.CMSG_IGNORE_TRADE)]
	private void HandleEmptyTradePacket(EmptyClientPacket trade)
	{
		WorldPacket packet = new WorldPacket(trade.GetUniversalOpcode());
		this.SendPacketToServer(packet);
	}

	[PacketHandler(Opcode.CMSG_CLEAR_TRADE_ITEM)]
	private void HandleClearTradeItem(ClearTradeItem trade)
	{
		TradeSession tradeSession = this.GetSession().GameState.CurrentTrade;
		if (tradeSession == null)
		{
			Log.Print(LogType.Error, $"Got {trade.GetUniversalOpcode()} without trade session", "HandleClearTradeItem", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\TradeHandler.cs");
		}
		else
		{
			tradeSession.ClientStateIndex++;
			WorldPacket packet = new WorldPacket(Opcode.CMSG_CLEAR_TRADE_ITEM);
			packet.WriteUInt8(trade.TradeSlot);
			this.SendPacketToServer(packet);
		}
	}

	[PacketHandler(Opcode.CMSG_SET_TRADE_ITEM)]
	private void HandleSetTradeItem(SetTradeItem trade)
	{
		TradeSession tradeSession = this.GetSession().GameState.CurrentTrade;
		if (tradeSession == null)
		{
			Log.Print(LogType.Error, $"Got {trade.GetUniversalOpcode()} without trade session", "HandleSetTradeItem", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\PacketHandlers\\TradeHandler.cs");
			return;
		}
		tradeSession.ClientStateIndex++;
		WorldPacket packet = new WorldPacket(Opcode.CMSG_SET_TRADE_ITEM);
		packet.WriteUInt8(trade.TradeSlot);
		byte containerSlot = ((trade.PackSlot != byte.MaxValue) ? ModernVersion.AdjustInventorySlot(trade.PackSlot) : trade.PackSlot);
		byte slot = ((trade.PackSlot == byte.MaxValue) ? ModernVersion.AdjustInventorySlot(trade.ItemSlotInPack) : trade.ItemSlotInPack);
		packet.WriteUInt8(containerSlot);
		packet.WriteUInt8(slot);
		this.SendPacketToServer(packet);
	}

	public WorldSocket(Socket socket)
		: base(socket)
	{
		this._connectType = ConnectionType.Realm;
		this._serverChallenge = Array.Empty<byte>().GenerateRandomKey(16);
		this._worldCrypt = new WorldCrypt();
		this._encryptKey = new byte[16];
		this._headerBuffer = new SocketBuffer(WorldSocket.HeaderSize);
		this._packetBuffer = new SocketBuffer();
		this.InitializePacketHandlers();
	}

	public override void Dispose()
	{
		this._serverChallenge = null;
		this._sessionKey = null;
		this._compressionStream = null;
		base.Dispose();
	}

	public GlobalSessionData GetSession()
	{
		return this._globalSession;
	}

	public override void Accept()
	{
		string ip_address = base.GetRemoteIpAddress().ToString();
		this._packetBuffer.Resize(WorldSocket.ClientConnectionInitialize.Length + 1);
		base.AsyncReadWithCallback(InitializeHandler);
		ByteBuffer packet = new ByteBuffer();
		packet.WriteString(WorldSocket.ServerConnectionInitialize);
		packet.WriteString("\n");
		base.AsyncWrite(packet.GetData());
	}

	private void InitializeHandler(SocketAsyncEventArgs args)
	{
		if (args.SocketError != SocketError.Success)
		{
			base.CloseSocket();
		}
		else
		{
			if (args.BytesTransferred <= 0 || this._packetBuffer.GetRemainingSpace() <= 0)
			{
				return;
			}
			int readHeaderSize = Math.Min(args.BytesTransferred, this._packetBuffer.GetRemainingSpace());
			this._packetBuffer.Write(args.Buffer, 0, readHeaderSize);
			if (this._packetBuffer.GetRemainingSpace() > 0)
			{
				base.AsyncReadWithCallback(InitializeHandler);
				return;
			}
			ByteBuffer buffer = new ByteBuffer(this._packetBuffer.GetData());
			string initializer = buffer.ReadString((uint)WorldSocket.ClientConnectionInitialize.Length);
			if (initializer != WorldSocket.ClientConnectionInitialize)
			{
				base.CloseSocket();
				return;
			}
			byte terminator = buffer.ReadUInt8();
			if (terminator != 10)
			{
				base.CloseSocket();
				return;
			}
			this._compressionStream = new ZLib.z_stream();
			int z_res1 = ZLib.deflateInit2(this._compressionStream, 1, 8, -15, 8, 0);
			if (z_res1 != 0)
			{
				base.CloseSocket();
				Log.Print(LogType.Error, $"Can't initialize packet compression (zlib: deflateInit2_) Error code: {z_res1}", "InitializeHandler", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			}
			else
			{
				this._packetBuffer.Resize(0);
				this._packetBuffer.Reset();
				this.HandleSendAuthSession();
				base.AsyncRead();
			}
		}
	}

	public override void ReadHandler(SocketAsyncEventArgs args)
	{
		if (!base.IsOpen())
		{
			return;
		}
		int currentReadIndex = 0;
		while (currentReadIndex < args.BytesTransferred)
		{
			if (this._headerBuffer.GetRemainingSpace() > 0)
			{
				int readHeaderSize = Math.Min(args.BytesTransferred - currentReadIndex, this._headerBuffer.GetRemainingSpace());
				this._headerBuffer.Write(args.Buffer, currentReadIndex, readHeaderSize);
				currentReadIndex += readHeaderSize;
				if (this._headerBuffer.GetRemainingSpace() > 0)
				{
					break;
				}
				if (!this.ReadHeader())
				{
					base.CloseSocket();
					return;
				}
			}
			if (this._packetBuffer.GetRemainingSpace() > 0)
			{
				int readDataSize = Math.Min(args.BytesTransferred - currentReadIndex, this._packetBuffer.GetRemainingSpace());
				this._packetBuffer.Write(args.Buffer, currentReadIndex, readDataSize);
				currentReadIndex += readDataSize;
				if (this._packetBuffer.GetRemainingSpace() > 0)
				{
					break;
				}
			}
			ReadDataHandlerResult result = this.ReadData();
			this._headerBuffer.Reset();
			switch (result)
			{
			case ReadDataHandlerResult.WaitingForQuery:
				return;
			case ReadDataHandlerResult.Ok:
				continue;
			}
			base.CloseSocket();
			return;
		}
		base.AsyncRead();
	}

	private bool ReadHeader()
	{
		PacketHeader header = new PacketHeader();
		header.Read(this._headerBuffer.GetData());
		this._packetBuffer.Resize(header.Size);
		return true;
	}

	private ReadDataHandlerResult ReadData()
	{
		PacketHeader header = new PacketHeader();
		header.Read(this._headerBuffer.GetData());
		if (!this._worldCrypt.Decrypt(this._packetBuffer.GetData(), header.Tag))
		{
			Log.Print(LogType.Error, $"WorldSocket.ReadData(): client {base.GetRemoteIpAddress()} failed to decrypt packet (size: {header.Size})", "ReadData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			return ReadDataHandlerResult.Error;
		}
		WorldPacket packet = new WorldPacket(this._packetBuffer.GetData());
		this._packetBuffer.Reset();
		Opcode opcode = packet.GetUniversalOpcode(isModern: true);
		Log.PrintNet(LogType.Debug, LogNetDir.C2P, $"Received opcode {opcode.ToString()} ({packet.GetOpcode()}).", "ReadData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
		if (opcode != Opcode.CMSG_HOTFIX_REQUEST && !header.IsValidSize())
		{
			Log.Print(LogType.Error, $"WorldSocket.ReadHeaderHandler(): client {base.GetRemoteIpAddress()} sent malformed packet (size: {header.Size})", "ReadData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			return ReadDataHandlerResult.Error;
		}
		switch (opcode)
		{
		case Opcode.CMSG_PING:
		{
			Ping ping = new Ping(packet);
			ping.Read();
			if (this._connectType == ConnectionType.Realm && this.GetSession().WorldClient != null && this.GetSession().WorldClient.IsConnected() && this.GetSession().WorldClient.IsAuthenticated())
			{
				this.GetSession().WorldClient.SendPing(ping.Serial, ping.Latency);
			}
			else
			{
				this.HandlePing(ping);
			}
			break;
		}
		case Opcode.CMSG_AUTH_SESSION:
		{
			AuthSession authSession = new AuthSession(packet);
			authSession.Read();
			this.HandleAuthSession(authSession);
			return ReadDataHandlerResult.WaitingForQuery;
		}
		case Opcode.CMSG_AUTH_CONTINUED_SESSION:
		{
			AuthContinuedSession authContinuedSession = new AuthContinuedSession(packet);
			authContinuedSession.Read();
			this.HandleAuthContinuedSession(authContinuedSession);
			return ReadDataHandlerResult.WaitingForQuery;
		}
		case Opcode.CMSG_LOG_DISCONNECT:
		{
			uint reason = packet.ReadUInt32();
			Log.Print(LogType.Server, $"Client disconnected with reason {reason}.", "ReadData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			if (this._connectType == ConnectionType.Realm)
			{
				if (this.GetSession().AuthClient != null)
				{
					this.GetSession().AuthClient.Disconnect();
				}
				if (this.GetSession().WorldClient != null)
				{
					this.GetSession().WorldClient.Disconnect();
				}
			}
			if (this.GetSession().ModernSniff != null)
			{
				this.GetSession().ModernSniff.CloseFile();
				this.GetSession().ModernSniff = null;
			}
			break;
		}
		case Opcode.CMSG_ENABLE_NAGLE:
			base.SetNoDelay(enable: false);
			break;
		case Opcode.CMSG_CONNECT_TO_FAILED:
		{
			ConnectToFailed connectToFailed = new ConnectToFailed(packet);
			connectToFailed.Read();
			this.HandleConnectToFailed(connectToFailed);
			break;
		}
		case Opcode.CMSG_ENTER_ENCRYPTED_MODE_ACK:
			this.HandleEnterEncryptedModeAck();
			break;
		case Opcode.CMSG_SERVER_TIME_OFFSET_REQUEST:
			this.SendServerTimeOffset();
			break;
		case Opcode.CMSG_SOCIAL_CONTRACT_REQUEST:
			this.SendSocialContractRequestResponse();
			break;
		default:
			this.HandlePacket(packet);
			break;
		case Opcode.CMSG_KEEP_ALIVE:
			break;
		}
		return ReadDataHandlerResult.Ok;
	}

	public void HandlePacket(WorldPacket packet)
	{
		Opcode universalOpcode = packet.GetUniversalOpcode(isModern: true);
		PacketHandler handler = this.GetHandler(universalOpcode);
		if (handler != null)
		{
			handler.Invoke(this, packet);
			return;
		}
		Log.PrintNet(LogType.Warn, LogNetDir.C2P, $"No handler for opcode {universalOpcode} ({packet.GetOpcode()}) (Got unknown packet from ModernClient)", "HandlePacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
		MissingOpcodeTracker.LogUnhandledCMSG(universalOpcode, packet.GetOpcode());
	}

	private void SendPacketToServer(WorldPacket packet, Opcode delayUntilOpcode = Opcode.MSG_NULL_ACTION)
	{
		if (this.GetSession().WorldClient != null)
		{
			this.GetSession().WorldClient.SendPacketToServer(packet, delayUntilOpcode);
			return;
		}
		Log.Print(LogType.Error, $"Attempt to send opcode {packet.GetUniversalOpcode(isModern: false)} ({packet.GetOpcode()}) while WorldClient is disconnected!", "SendPacketToServer", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
	}

	public PacketHandler GetHandler(Opcode opcode)
	{
		return this._clientPacketTable.LookupByKey(opcode);
	}

	public void SendPacket(ServerPacket packet)
	{
		if (!base.IsOpen())
		{
			Log.PrintNet(LogType.Error, LogNetDir.P2C, $"Can't send {packet.GetUniversalOpcode()}, socket is closed!", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			if (this.GetSession() != null)
			{
				if (this.GetSession().RealmSocket == this)
				{
					this.GetSession().RealmSocket = null;
				}
				else if (this.GetSession().InstanceSocket == this)
				{
					this.GetSession().InstanceSocket = null;
				}
				this.GetSession().OnDisconnect();
			}
			return;
		}
		packet.WritePacketData();
		if (packet.SkipSend)
		{
			return;
		}
		if (this.GetSession() != null)
		{
			packet.LogPacket(ref this.GetSession().ModernSniff);
		}
		this._sendMutex.WaitOne();
		byte[] data = packet.GetData();
		Opcode universalOpcode = packet.GetUniversalOpcode();
		ushort opcode = (ushort)packet.GetOpcode();
		if (opcode == 0)
		{
			Log.PrintNet(LogType.Warn, LogNetDir.P2C, $"Dropping packet with opcode 0 (universal={universalOpcode}, size={data.Length})", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			MissingOpcodeTracker.LogDroppedSMSG(universalOpcode, data.Length);
			this._sendMutex.ReleaseMutex();
			return;
		}
		if (universalOpcode != Opcode.SMSG_ON_MONSTER_MOVE)
			Log.PrintNet(LogType.Debug, LogNetDir.P2C, $"Sending opcode {universalOpcode} ({opcode}), size={data.Length}.", "SendPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
		ByteBuffer buffer = new ByteBuffer();
		int packetSize = data.Length;
		if (packetSize > 1024 && this._worldCrypt.IsInitialized && ModernVersion.ExpansionVersion < 3)
		{
			buffer.WriteInt32(packetSize + 2);
			buffer.WriteUInt32(ZLib.adler32(ZLib.adler32(2552748273u, BitConverter.GetBytes(opcode), 2u), data, (uint)packetSize));
			byte[] compressedData;
			uint compressedSize = this.CompressPacket(data, opcode, out compressedData);
			buffer.WriteUInt32(ZLib.adler32(2552748273u, compressedData, compressedSize));
			buffer.WriteBytes(compressedData, compressedSize);
			packetSize = (int)(compressedSize + 12);
			opcode = (ushort)ModernVersion.GetCurrentOpcode(Opcode.SMSG_COMPRESSED_PACKET);
			data = buffer.GetData();
		}
		buffer = new ByteBuffer();
		buffer.WriteUInt16(opcode);
		buffer.WriteBytes(data);
		packetSize += 2;
		data = buffer.GetData();
		PacketHeader header = new PacketHeader();
		header.Size = packetSize;
		this._worldCrypt.Encrypt(ref data, ref header.Tag);
		ByteBuffer byteBuffer = new ByteBuffer();
		header.Write(byteBuffer);
		byteBuffer.WriteBytes(data);
		base.AsyncWrite(byteBuffer.GetData());
		this._sendMutex.ReleaseMutex();
	}

	public uint CompressPacket(byte[] data, ushort opcode, out byte[] outData)
	{
		byte[] uncompressedData = BitConverter.GetBytes(opcode).Combine(data);
		uint bufferSize = ZLib.deflateBound(this._compressionStream, (uint)data.Length);
		outData = new byte[bufferSize];
		this._compressionStream.next_out = 0;
		this._compressionStream.avail_out = bufferSize;
		this._compressionStream.out_buf = outData;
		this._compressionStream.next_in = 0u;
		this._compressionStream.avail_in = (uint)uncompressedData.Length;
		this._compressionStream.in_buf = uncompressedData;
		int z_res = ZLib.deflate(this._compressionStream, 2);
		if (z_res != 0)
		{
			Log.PrintNet(LogType.Error, LogNetDir.P2C, $"Can't compress packet data (zlib: deflate) Error code: {z_res} msg: {this._compressionStream.msg}", "CompressPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			return 0u;
		}
		return bufferSize - this._compressionStream.avail_out;
	}

	public override bool Update()
	{
		if (!base.Update())
		{
			return false;
		}
		return true;
	}

	public override void OnClose()
	{
		base.OnClose();
	}

	private void HandleSendAuthSession()
	{
		AuthChallenge challenge = new AuthChallenge();
		challenge.Challenge = this._serverChallenge;
		challenge.DosChallenge = new byte[32].GenerateRandomKey(32);
		challenge.DosZeroBits = 1;
		this.SendPacket(challenge);
	}

	private void HandleAuthSession(AuthSession authSession)
	{
		this._globalSession = BnetSessionTicketStorage.SessionsByName[authSession.RealmJoinTicket];
		this._bnetRpc = new BnetServices.ServiceManager("WorldSocket", this, this._globalSession);
		this.HandleAuthSessionCallback(authSession);
	}

	private void HandleAuthSessionCallback(AuthSession authSession)
	{
		RealmBuildInfo buildInfo = this.GetSession().RealmManager.GetBuildInfo(this.GetSession().Build);
		if (buildInfo == null)
		{
			this.SendAuthResponseError(BattlenetRpcErrorCode.BadVersion);
			Log.Print(LogType.Error, $"WorldSocket.HandleAuthSessionCallback: Missing auth seed for realm build {this.GetSession().Build} ({base.GetRemoteIpAddress()}).", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			base.CloseSocket();
			this.GetSession().OnDisconnect();
			return;
		}
		IPEndPoint address = base.GetRemoteIpAddress();
		if (this.GetSession().OS != "Wn64" && this.GetSession().OS != "Mc64" && this.GetSession().OS != "MacA")
		{
			Log.Print(LogType.Error, $"WorldSocket.HandleAuthSession: Unknown OS for account: {this.GetSession().GameAccountInfo.Id} ('{authSession.RealmJoinTicket}') address: {address}", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			base.CloseSocket();
			this.GetSession().OnDisconnect();
			return;
		}
		byte[] platformSeed = buildInfo.BuildSeeds.GetValueOrDefault(this.GetSession().OS);
		if (platformSeed == null || !TrySeed(platformSeed))
		{
			Log.Print(LogType.Debug, "WorldSocket.HandleAuthSession: Fallback to static seed", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			if (!TrySeed(buildInfo.FallbackStaticSeed))
			{
				Log.Print(LogType.Warn, $"WorldSocket.HandleAuthSession: Seed mismatch for account: {this.GetSession().GameAccountInfo.Id} ('{authSession.RealmJoinTicket}') - BYPASSING for testing", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			}
		}
		Sha256 keyData = new Sha256();
		keyData.Finish(this.GetSession().SessionKey);
		HmacSha256 sessionKeyHmac = new HmacSha256(keyData.Digest);
		sessionKeyHmac.Process(this._serverChallenge, 16);
		sessionKeyHmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Count);
		sessionKeyHmac.Finish(WorldSocket.SessionKeySeed, 16);
		this._sessionKey = new byte[40];
		SessionKeyGenerator sessionKeyGenerator = new SessionKeyGenerator(sessionKeyHmac.Digest, 32);
		sessionKeyGenerator.Generate(this._sessionKey, 40u);
		HmacSha256 encryptKeyGen = new HmacSha256(this._sessionKey);
		encryptKeyGen.Process(authSession.LocalChallenge, authSession.LocalChallenge.Count);
		encryptKeyGen.Process(this._serverChallenge, 16);
		encryptKeyGen.Finish(WorldSocket.EncryptionKeySeed, 16);
		Buffer.BlockCopy(encryptKeyGen.Digest, 0, this._encryptKey, 0, 16);
		this.GetSession().SessionKey = this._sessionKey;
		Log.Print(LogType.Server, $"WorldSocket:HandleAuthSession: Client '{authSession.RealmJoinTicket}' authenticated successfully from {address}.", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
		this._realmId = new RealmId((byte)authSession.RegionID, (byte)authSession.BattlegroupID, authSession.RealmID);
		this.GetSession().WorldClient = new WorldClient();
		if (!this.GetSession().WorldClient.ConnectToWorldServer(this.GetSession().RealmManager.GetRealm(this._realmId), this.GetSession()))
		{
			this.SendAuthResponseError(BattlenetRpcErrorCode.BadServer);
			Log.Print(LogType.Error, "The WorldClient failed to connect to the selected world server!", "HandleAuthSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			this.Session.AccountMetaDataMgr.InvalidateLastSelectedCharacter();
			base.CloseSocket();
			this.GetSession().OnDisconnect();
		}
		else
		{
			this.SendPacket(new EnterEncryptedMode(this._encryptKey, enabled: true));
			base.AsyncRead();
		}
		bool TrySeed(byte[] seed)
		{
			Sha256 digestKeyHash = new Sha256();
			digestKeyHash.Process(this.GetSession().SessionKey, this.GetSession().SessionKey.Length);
			digestKeyHash.Finish(seed);
			HmacSha256 hmac = new HmacSha256(digestKeyHash.Digest);
			hmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Count);
			hmac.Process(this._serverChallenge, 16);
			hmac.Finish(WorldSocket.AuthCheckSeed, 16);
			return hmac.Digest.Compare(authSession.Digest);
		}
	}

	private void HandleAuthContinuedSession(AuthContinuedSession authSession)
	{
		ConnectToKey key = default(ConnectToKey);
		ulong key2 = (key.Raw = authSession.Key);
		this._key = key2;
		this._connectType = key.connectionType;
		if (this._connectType != ConnectionType.Instance)
		{
			this.SendAuthResponseError(BattlenetRpcErrorCode.Denied);
			base.CloseSocket();
		}
		else
		{
			this.HandleAuthContinuedSessionCallback(authSession);
		}
	}

	private void HandleAuthContinuedSessionCallback(AuthContinuedSession authSession)
	{
		ConnectToKey key = default(ConnectToKey);
		ulong key2 = (key.Raw = authSession.Key);
		this._key = key2;
		this._globalSession = BnetSessionTicketStorage.SessionsByKey[this._key];
		uint accountId = key.AccountId;
		string login = this.GetSession().AccountInfo.Login;
		this._sessionKey = this.GetSession().SessionKey;
		HmacSha256 hmac = new HmacSha256(this._sessionKey);
		hmac.Process(BitConverter.GetBytes(authSession.Key), 8);
		hmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
		hmac.Process(this._serverChallenge, 16);
		hmac.Finish(WorldSocket.ContinuedSessionSeed, 16);
		if (!hmac.Digest.Compare(authSession.Digest))
		{
			Log.Print(LogType.Error, $"WorldSocket.HandleAuthContinuedSession: Authentication failed for account: {accountId} ('{login}') address: {base.GetRemoteIpAddress()}", "HandleAuthContinuedSessionCallback", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			base.CloseSocket();
		}
		else
		{
			HmacSha256 encryptKeyGen = new HmacSha256(this._sessionKey);
			encryptKeyGen.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
			encryptKeyGen.Process(this._serverChallenge, 16);
			encryptKeyGen.Finish(WorldSocket.EncryptionKeySeed, 16);
			Buffer.BlockCopy(encryptKeyGen.Digest, 0, this._encryptKey, 0, 16);
			this.SendPacket(new EnterEncryptedMode(this._encryptKey, enabled: true));
			base.AsyncRead();
		}
	}

	public void SendConnectToInstance(ConnectToSerial serial)
	{
		IPAddress externalIp = IPAddress.Parse(Settings.ExternalAddress);
		IPEndPoint instanceAddress = new IPEndPoint(externalIp, Settings.InstancePort);
		this._instanceConnectKey.AccountId = this.GetSession().AccountInfo.Id;
		this._instanceConnectKey.connectionType = ConnectionType.Instance;
		this._instanceConnectKey.Key = RandomHelper.URand(0, int.MaxValue);
		BnetSessionTicketStorage.AddNewSessionByKey(this._instanceConnectKey.Raw, this.GetSession());
		ConnectTo connectTo = new ConnectTo();
		connectTo.Key = this._instanceConnectKey.Raw;
		connectTo.Serial = serial;
		connectTo.Payload.Port = (ushort)Settings.InstancePort;
		connectTo.Con = 1;
		if (instanceAddress.AddressFamily == AddressFamily.InterNetwork)
		{
			connectTo.Payload.Where.IPv4 = instanceAddress.Address.GetAddressBytes();
			connectTo.Payload.Where.Type = ConnectTo.AddressType.IPv4;
		}
		else
		{
			connectTo.Payload.Where.IPv6 = instanceAddress.Address.GetAddressBytes();
			connectTo.Payload.Where.Type = ConnectTo.AddressType.IPv6;
		}
		this.SendPacket(connectTo);
	}

	public void AbortLogin(LoginFailureReason reason)
	{
		this.SendPacket(new CharacterLoginFailed(reason));
	}

	private void HandleConnectToFailed(ConnectToFailed connectToFailed)
	{
		switch (connectToFailed.Serial)
		{
		case ConnectToSerial.WorldAttempt1:
			this.SendConnectToInstance(ConnectToSerial.WorldAttempt2);
			break;
		case ConnectToSerial.WorldAttempt2:
			this.SendConnectToInstance(ConnectToSerial.WorldAttempt3);
			break;
		case ConnectToSerial.WorldAttempt3:
			this.SendConnectToInstance(ConnectToSerial.WorldAttempt4);
			break;
		case ConnectToSerial.WorldAttempt4:
			this.SendConnectToInstance(ConnectToSerial.WorldAttempt5);
			break;
		case ConnectToSerial.WorldAttempt5:
			Log.Print(LogType.Error, "Failed to connect 5 times to world socket, aborting login", "HandleConnectToFailed", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			this.AbortLogin(LoginFailureReason.NoWorld);
			break;
		}
	}

	private void HandleEnterEncryptedModeAck()
	{
		this._worldCrypt.Initialize(this._encryptKey);
		if (this._connectType == ConnectionType.Realm)
		{
			this.SendAuthResponse(BattlenetRpcErrorCode.Ok, this.GetSession().WorldClient.GetQueuePosition());
			this.SendSetTimeZoneInformation();
			this.SendFeatureSystemStatusGlueScreen();
			this.SendClientCacheVersion(0u);
			this.SendAvailableHotfixes();
			this.SendBnetConnectionState(1);
			this.GetSession().AccountDataMgr = new AccountDataManager(this.GetSession().Username, this.GetSession().RealmManager.GetRealm(this._realmId).Name);
			this.GetSession().RealmSocket = this;
		}
		else
		{
			Log.Print(LogType.Server, "Client has connected to the instance server.", "HandleEnterEncryptedModeAck", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
			this.SendPacket(new ResumeComms(ConnectionType.Instance));
			this.GetSession().InstanceSocket = this;
		}
	}

	public void SendAuthResponseError(BattlenetRpcErrorCode code)
	{
		AuthResponse response = new AuthResponse();
		response.SuccessInfo = null;
		response.WaitInfo = null;
		response.Result = code;
		this.SendPacket(response);
	}

	public void SendAuthResponse(BattlenetRpcErrorCode code, uint queuePos = 0u)
	{
		AuthResponse response = new AuthResponse();
		response.Result = code;
		if (code == BattlenetRpcErrorCode.Ok)
		{
			response.SuccessInfo = new AuthResponse.AuthSuccessInfo();
			response.SuccessInfo.ActiveExpansionLevel = (byte)LegacyVersion.ExpansionVersion;
			response.SuccessInfo.AccountExpansionLevel = (byte)LegacyVersion.ExpansionVersion;
			response.SuccessInfo.VirtualRealmAddress = this._realmId.GetAddress();
			response.SuccessInfo.Time = (uint)Time.UnixTime;
			Realm realm = this.GetSession().RealmManager.GetRealm(this._realmId);
			response.SuccessInfo.VirtualRealms.Add(new VirtualRealmInfo(realm.Id.GetAddress(), isHomeRealm: true, isInternalRealm: false, realm.Name, realm.NormalizedName));
			List<AuthResponse.RaceClassAvailability> availableRaces = new List<AuthResponse.RaceClassAvailability>();
			AuthResponse.RaceClassAvailability race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 1;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(2, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(9, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 2;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(7, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(9, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 3;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(2, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 4;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(11, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 5;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(9, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 6;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(7, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(11, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 7;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(9, 0, 0));
			availableRaces.Add(race);
			race = new AuthResponse.RaceClassAvailability();
			race.RaceID = 8;
			race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(7, 0, 0));
			race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
			availableRaces.Add(race);
			if (ModernVersion.ExpansionVersion >= 2 && LegacyVersion.ExpansionVersion >= 2)
			{
				race = new AuthResponse.RaceClassAvailability();
				race.RaceID = 10;
				race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(4, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(9, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(2, 0, 0));
				availableRaces.Add(race);
				race = new AuthResponse.RaceClassAvailability();
				race.RaceID = 11;
				race.Classes.Add(new AuthResponse.ClassAvailability(1, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(2, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(3, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(5, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(8, 0, 0));
				race.Classes.Add(new AuthResponse.ClassAvailability(7, 0, 0));
				availableRaces.Add(race);
			}
			response.SuccessInfo.AvailableClasses = availableRaces;
		}
		if (queuePos != 0)
		{
			response.WaitInfo = new AuthWaitInfo();
			response.WaitInfo.WaitCount = queuePos;
		}
		this.SendPacket(response);
	}

	public void SendAuthWaitQue(uint position)
	{
		if (position != 0)
		{
			WaitQueueUpdate waitQueueUpdate = new WaitQueueUpdate();
			waitQueueUpdate.WaitInfo.WaitCount = position;
			waitQueueUpdate.WaitInfo.WaitTime = 0u;
			waitQueueUpdate.WaitInfo.HasFCM = false;
			this.SendPacket(waitQueueUpdate);
		}
		else
		{
			this.SendPacket(new WaitQueueFinish());
		}
	}

	public void SendSetTimeZoneInformation()
	{
		SetTimeZoneInformation packet = new SetTimeZoneInformation();
		packet.ServerTimeTZ = "Europe/Paris";
		packet.GameTimeTZ = "Europe/Paris";
		this.SendPacket(packet);
	}

	public void SendFeatureSystemStatusGlueScreen()
	{
		FeatureSystemStatusGlueScreen features = new FeatureSystemStatusGlueScreen();
		features.BpayStoreAvailable = false;
		features.BpayStoreDisabledByParentalControls = false;
		features.CharUndeleteEnabled = false;
		features.BpayStoreEnabled = false;
		features.MaxCharactersPerRealm = 10;
		features.MinimumExpansionLevel = 0;
		features.MaximumExpansionLevel = (int)LegacyVersion.ExpansionVersion;
		features.ActiveSeason = 2;
		features.Unk14 = true;
		EuropaTicketConfig europaTicketConfig = new EuropaTicketConfig();
		europaTicketConfig.ThrottleState.MaxTries = 10u;
		europaTicketConfig.ThrottleState.PerMilliseconds = 60000u;
		europaTicketConfig.ThrottleState.TryCount = 1u;
		europaTicketConfig.ThrottleState.LastResetTimeBeforeNow = 111111u;
		europaTicketConfig.TicketsEnabled = true;
		europaTicketConfig.BugsEnabled = true;
		europaTicketConfig.ComplaintsEnabled = true;
		europaTicketConfig.SuggestionsEnabled = true;
		features.EuropaTicketSystemStatus = europaTicketConfig;
		this.SendPacket(features);
	}

	public void SendFeatureSystemStatus()
	{
		FeatureSystemStatus features = new FeatureSystemStatus();
		features.ComplaintStatus = 2;
		features.ScrollOfResurrectionRequestsRemaining = 1u;
		features.ScrollOfResurrectionMaxRequestsPerDay = 1u;
		features.CfgRealmID = 1u;
		features.CfgRealmRecID = 1;
		features.TwitterPostThrottleLimit = 60u;
		features.TwitterPostThrottleCooldown = 20u;
		features.TokenPollTimeSeconds = 300u;
		features.KioskSessionMinutes = 30u;
		features.BpayStoreProductDeliveryDelay = 180u;
		features.HiddenUIClubsPresenceUpdateTimer = 60000u;
		features.VoiceEnabled = false;
		features.BrowserEnabled = false;
		features.EuropaTicketSystemStatus = new EuropaTicketConfig();
		features.EuropaTicketSystemStatus.ThrottleState.MaxTries = 10u;
		features.EuropaTicketSystemStatus.ThrottleState.PerMilliseconds = 60000u;
		features.EuropaTicketSystemStatus.ThrottleState.TryCount = 1u;
		features.EuropaTicketSystemStatus.ThrottleState.LastResetTimeBeforeNow = 111111u;
		features.TutorialsEnabled = true;
		features.Unk67 = true;
		features.QuestSessionEnabled = true;
		features.BattlegroundsEnabled = true;
		features.QuickJoinConfig.ToastDuration = 7f;
		features.QuickJoinConfig.DelayDuration = 10f;
		features.QuickJoinConfig.QueueMultiplier = 1f;
		features.QuickJoinConfig.PlayerMultiplier = 1f;
		features.QuickJoinConfig.PlayerFriendValue = 5f;
		features.QuickJoinConfig.PlayerGuildValue = 1f;
		features.QuickJoinConfig.ThrottleDecayTime = 60f;
		features.QuickJoinConfig.ThrottlePrioritySpike = 20f;
		features.QuickJoinConfig.ThrottlePvPPriorityNormal = 50f;
		features.QuickJoinConfig.ThrottlePvPPriorityLow = 1f;
		features.QuickJoinConfig.ThrottlePvPHonorThreshold = 10f;
		features.QuickJoinConfig.ThrottleLfgListPriorityDefault = 50f;
		features.QuickJoinConfig.ThrottleLfgListPriorityAbove = 100f;
		features.QuickJoinConfig.ThrottleLfgListPriorityBelow = 50f;
		features.QuickJoinConfig.ThrottleLfgListIlvlScalingAbove = 1f;
		features.QuickJoinConfig.ThrottleLfgListIlvlScalingBelow = 1f;
		features.QuickJoinConfig.ThrottleRfPriorityAbove = 100f;
		features.QuickJoinConfig.ThrottleRfIlvlScalingAbove = 1f;
		features.QuickJoinConfig.ThrottleDfMaxItemLevel = 850f;
		features.QuickJoinConfig.ThrottleDfBestPriority = 80f;
		features.Squelch.IsSquelched = false;
		features.Squelch.BnetAccountGuid = WowGuid128.Create(HighGuidType703.BNetAccount, this.GetSession().AccountInfo.Id);
		features.Squelch.GuildGuid = WowGuid128.Empty;
		features.EuropaTicketSystemStatus.TicketsEnabled = true;
		features.EuropaTicketSystemStatus.BugsEnabled = true;
		features.EuropaTicketSystemStatus.ComplaintsEnabled = true;
		features.EuropaTicketSystemStatus.SuggestionsEnabled = true;
		features.EuropaTicketSystemStatus.ThrottleState.MaxTries = 10u;
		features.EuropaTicketSystemStatus.ThrottleState.PerMilliseconds = 60000u;
		features.EuropaTicketSystemStatus.ThrottleState.TryCount = 1u;
		features.EuropaTicketSystemStatus.ThrottleState.LastResetTimeBeforeNow = 10627480u;
		this.SendPacket(features);
	}

	public void SendSeasonInfo()
	{
		SeasonInfo seasonInfo = new SeasonInfo();
		if (LegacyVersion.ExpansionVersion > 1 && ModernVersion.ExpansionVersion > 1)
		{
			seasonInfo.CurrentSeason = 2;
			seasonInfo.PreviousSeason = 1;
		}
		uint resolved = ModernVersion.GetCurrentOpcode(Opcode.SMSG_SEASON_INFO);
		Log.Print(LogType.Debug, $"SeasonInfo opcode resolved to: {resolved} (0x{resolved:X4})", "SendSeasonInfo", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
		this.SendPacket(seasonInfo);
	}

	public void SendMotd()
	{
		MOTD motd = new MOTD();
		this.SendPacket(motd);
	}

	public void SendClientCacheVersion(uint version)
	{
		ClientCacheVersion cache = new ClientCacheVersion();
		cache.CacheVersion = version;
		this.SendPacket(cache);
	}

	public void SendAvailableHotfixes()
	{
		AvailableHotfixes hotfixes = new AvailableHotfixes();
		hotfixes.VirtualRealmAddress = this.GetSession().RealmId.GetAddress();
		this.SendPacket(hotfixes);
	}

	public void SendBnetConnectionState(byte state)
	{
		ConnectionStatus bnetConnected = new ConnectionStatus();
		bnetConnected.State = state;
		this.SendPacket(bnetConnected);
	}

	public void SendServerTimeOffset()
	{
		ServerTimeOffset response = new ServerTimeOffset();
		response.Time = Time.UnixTime;
		this.SendPacket(response);
	}

	public void SendSocialContractRequestResponse()
	{
		SocialContractRequestResponse response = new SocialContractRequestResponse();
		this.SendPacket(response);
	}

	private void HandlePing(Ping ping)
	{
		this.SendPacket(new Pong(ping.Serial));
	}

	public void SendAccountDataTimes()
	{
		WowGuid128 guid = this.GetSession().GameState.CurrentPlayerGuid;
		this.GetSession().AccountDataMgr.LoadAllData(guid);
		AccountDataTimes accountData = new AccountDataTimes();
		accountData.PlayerGuid = guid;
		accountData.ServerTime = Time.UnixTime;
		int count = ModernVersion.GetAccountDataCount();
		accountData.AccountTimes = new long[count];
		for (int i = 0; i < count; i++)
		{
			accountData.AccountTimes[i] = ((this.GetSession().AccountDataMgr.Data[i] != null) ? this.GetSession().AccountDataMgr.Data[i].Timestamp : 0);
		}
		this.SendPacket(accountData);
	}

	public void SendRpcMessage(uint serviceId, OriginalHash service, uint methodId, uint token, BattlenetRpcErrorCode status, IMessage? message)
	{
		MethodCall methodInfo = default(MethodCall);
		methodInfo.SetServiceHash((uint)service);
		methodInfo.SetMethodId(methodId);
		methodInfo.Token = token;
		methodInfo.ObjectId = serviceId;
		byte[] bytes = ((message == null) ? Array.Empty<byte>() : message.ToByteArray());
		BattlenetResponse response = new BattlenetResponse
		{
			Method = methodInfo,
			Status = status,
			Data = new ByteBuffer(bytes)
		};
		this.SendPacket(response);
	}

	public IPEndPoint GetRemoteIpEndPoint()
	{
		return base.GetRemoteIpAddress();
	}

	public void InitializePacketHandlers()
	{
		MethodInfo[] methods = typeof(WorldSocket).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			foreach (PacketHandlerAttribute msgAttr in methodInfo.GetCustomAttributes<PacketHandlerAttribute>())
			{
				if (msgAttr == null || msgAttr.Opcode == Opcode.MSG_NULL_ACTION)
				{
					continue;
				}
				if (this._clientPacketTable.ContainsKey(msgAttr.Opcode))
				{
					Log.Print(LogType.Error, $"Tried to override OpcodeHandler of {this._clientPacketTable[msgAttr.Opcode].ToString()} with {methodInfo.Name} (Opcode {msgAttr.Opcode})", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
				}
				else
				{
					ParameterInfo[] parameters = methodInfo.GetParameters();
					if (parameters.Length == 0)
					{
						Log.Print(LogType.Error, "Method: " + methodInfo.Name + " Has no paramters", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
					}
					else if (parameters[0].ParameterType.BaseType != typeof(ClientPacket))
					{
						Log.Print(LogType.Error, "Method: " + methodInfo.Name + " has wrong BaseType", "InitializePacketHandlers", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\WorldSocket.cs");
					}
					else
					{
						this._clientPacketTable[msgAttr.Opcode] = new PacketHandler(methodInfo, parameters[0].ParameterType);
					}
				}
			}
		}
	}
}
