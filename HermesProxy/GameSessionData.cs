using System;
using System.Collections.Generic;
using System.Threading;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server;
using HermesProxy.World.Server.Packets;

namespace HermesProxy;

public class GameSessionData
{
	public bool HasWsgHordeFlagCarrier;

	public bool HasWsgAllyFlagCarrier;

	public bool ChannelDisplayList;

	public bool ShowPlayedTime;

	public bool IsInFarSight;

	public bool IsInTaxiFlight;

	public bool IsWaitingForTaxiStart;

	public bool IsWaitingForNewWorld;

	public bool IsWaitingForWorldPortAck;

	public bool IsFirstEnterWorld;

	public bool IsConnectedToInstance;

	public Queue<ServerPacket> PendingUninstancedPackets = new Queue<ServerPacket>();

	public bool IsInWorld;

	// Talent points tracking for 3.4.3 client
	public int TotalTalentPoints;

	// Glyph tracking for 3.4.3 client
	public byte GlyphsEnabled;
	public ushort[] ActiveGlyphs = new ushort[6];

	// Known spells tracking for mount collection
	public HashSet<uint> KnownSpells = new HashSet<uint>();


	// Login update buffering (moved from UpdateObject [ThreadStatic] to fix thread-switching issues)
	public bool PlayerObjectSent;
	public List<ObjectUpdate> PendingLoginUpdates;
	public List<WowGuid128> PendingLoginDestroys;

	public uint? CurrentMapId;

	// Cached QueryGameObjectResponse data for pre-sending before transport CreateObjects
	public Dictionary<uint, HermesProxy.World.Server.Packets.QueryGameObjectResponse> GameObjectQueryCache = new Dictionary<uint, HermesProxy.World.Server.Packets.QueryGameObjectResponse>();

	public uint CurrentZoneId;

	public uint CurrentTaxiNode;

	public List<byte> UsableTaxiNodes = new List<byte>();

	public uint PendingTransferMapId;

	public uint LastEnteredAreaTrigger;

	public uint LastDispellSpellId;

	public string LeftChannelName = "";

	public bool IsPassingOnLoot;

	public int GroupUpdateCounter;

	public uint GroupReadyCheckResponses;

	public PartyUpdate[] CurrentGroups = new PartyUpdate[2];

	public bool WeWantToLeaveGroup;

	public List<OwnCharacterInfo> OwnCharacters = new List<OwnCharacterInfo>();

	public WowGuid128 CurrentPlayerGuid;

	// Death/revive: track ghost state and cache MoveInfo for DestroyObject+CreateObject2 on revive
	public bool IsPlayerGhost;
	public bool NeedPlayerRecreate;
	public MovementInfo LastSelfPlayerMoveInfo;

	// Cached quest log: QuestID per slot (0-24), updated on create and values updates
	public int[] QuestLogQuestIDs = new int[25];

	public long CurrentPlayerCreateTime;

	public OwnCharacterInfo CurrentPlayerInfo;

	public CurrentPlayerStorage CurrentPlayerStorage;

	public uint CurrentGuildCreateTime;

	public uint CurrentGuildNumAccounts;

	public WowGuid128 CurrentInteractedWithNPC;

	public WowGuid128 CurrentInteractedWithGO;

	public uint LastWhoRequestId;

	public WowGuid128 CurrentPetGuid;

	public uint[] CurrentArenaTeamIds = new uint[3];

	public ClientCastRequest CurrentClientNormalCast;

	public ClientCastRequest CurrentClientSpecialCast;

	public ClientCastRequest CurrentClientPetCast;

	public List<ClientCastRequest> PendingClientCasts = new List<ClientCastRequest>();

	public List<ClientCastRequest> PendingClientPetCasts = new List<ClientCastRequest>();

	public WowGuid64 LastLootTargetGuid;

	public List<int> ActionButtons = new List<int>();

	public Dictionary<WowGuid128, Dictionary<byte, int>> UnitAuraDurationUpdateTime = new Dictionary<WowGuid128, Dictionary<byte, int>>();

	public Dictionary<WowGuid128, Dictionary<byte, int>> UnitAuraDurationLeft = new Dictionary<WowGuid128, Dictionary<byte, int>>();

	public Dictionary<WowGuid128, Dictionary<byte, int>> UnitAuraDurationFull = new Dictionary<WowGuid128, Dictionary<byte, int>>();

	public Dictionary<WowGuid128, Dictionary<byte, WowGuid128>> UnitAuraCaster = new Dictionary<WowGuid128, Dictionary<byte, WowGuid128>>();

	public Dictionary<WowGuid128, PlayerCache> CachedPlayers = new Dictionary<WowGuid128, PlayerCache>();

	public HashSet<WowGuid128> IgnoredPlayers = new HashSet<WowGuid128>();

	public Dictionary<WowGuid128, uint> PlayerGuildIds = new Dictionary<WowGuid128, uint>();

	public Mutex ObjectCacheMutex = new Mutex();

	public Dictionary<WowGuid128, Dictionary<int, UpdateField>> ObjectCacheLegacy = new Dictionary<WowGuid128, Dictionary<int, UpdateField>>();

	public Dictionary<WowGuid128, UpdateFieldsArray> ObjectCacheModern = new Dictionary<WowGuid128, UpdateFieldsArray>();

	public Dictionary<WowGuid128, ObjectType> OriginalObjectTypes = new Dictionary<WowGuid128, ObjectType>();

	public Dictionary<WowGuid128, uint[]> ItemGems = new Dictionary<WowGuid128, uint[]>();

	public Dictionary<uint, Class> CreatureClasses = new Dictionary<uint, Class>();

	public Dictionary<string, int> ChannelIds = new Dictionary<string, int>();

	public Dictionary<uint, uint> ItemBuyCount = new Dictionary<uint, uint>();

	public Dictionary<uint, uint> RealSpellToLearnSpell = new Dictionary<uint, uint>();

	public Dictionary<uint, ArenaTeamData> ArenaTeams = new Dictionary<uint, ArenaTeamData>();

	public MailListResult PendingMailListPacket;

	public HashSet<uint> RequestedItemTextIds = new HashSet<uint>();

	public Dictionary<uint, string> ItemTexts = new Dictionary<uint, string>();

	public Dictionary<uint, uint> BattleFieldQueueTypes = new Dictionary<uint, uint>();

	public Dictionary<uint, long> BattleFieldQueueTimes = new Dictionary<uint, long>();

	public Dictionary<uint, uint> DailyQuestsDone = new Dictionary<uint, uint>();

	public HashSet<WowGuid128> FlagCarrierGuids = new HashSet<WowGuid128>();

	public Dictionary<WowGuid64, ushort> ObjectSpawnCount = new Dictionary<WowGuid64, ushort>();

	public HashSet<WowGuid64> DespawnedGameObjects = new HashSet<WowGuid64>();

	public HashSet<WowGuid128> HunterPetGuids = new HashSet<WowGuid128>();

	public Dictionary<WowGuid128, Array<ArenaTeamInspectData>> PlayerArenaTeams = new Dictionary<WowGuid128, Array<ArenaTeamInspectData>>();

	public HashSet<string> AddonPrefixes = new HashSet<string>();

	public Dictionary<byte, Dictionary<byte, int>> FlatSpellMods = new Dictionary<byte, Dictionary<byte, int>>();

	public Dictionary<byte, Dictionary<byte, int>> PctSpellMods = new Dictionary<byte, Dictionary<byte, int>>();

	public Dictionary<WowGuid128, Dictionary<uint, WowGuid128>> LastAuraCasterOnTarget = new Dictionary<WowGuid128, Dictionary<uint, WowGuid128>>();

	public TradeSession? CurrentTrade = null;

	public HashSet<uint> RequestedItemHotfixes = new HashSet<uint>();

	public HashSet<uint> RequestedItemSparseHotfixes = new HashSet<uint>();

	// Buffer item CreateObjects until their templates arrive via hotfix
	public Dictionary<uint, List<ObjectUpdate>> PendingItemCreates = new Dictionary<uint, List<ObjectUpdate>>();

	private GameSessionData()
	{
	}

	public static GameSessionData CreateNewGameSessionData(GlobalSessionData globalSession)
	{
		GameSessionData self = new GameSessionData();
		self.CurrentPlayerStorage = new CurrentPlayerStorage(globalSession);
		return self;
	}

	public uint GetCurrentGroupSize()
	{
		PartyUpdate group = this.GetCurrentGroup();
		if (group == null)
		{
			return 0u;
		}
		return (group.PlayerList.Count > 1) ? ((uint)(group.PlayerList.Count - 1)) : 0u;
	}

	public WowGuid128 GetCurrentGroupLeader()
	{
		PartyUpdate group = this.GetCurrentGroup();
		if (group == null)
		{
			return WowGuid128.Empty;
		}
		return group.LeaderGUID;
	}

	public LootMethod GetCurrentLootMethod()
	{
		return this.GetCurrentGroup()?.LootSettings.Method ?? LootMethod.FreeForAll;
	}

	public WowGuid128 GetCurrentGroupGuid()
	{
		PartyUpdate group = this.GetCurrentGroup();
		if (group == null)
		{
			return WowGuid128.Empty;
		}
		return group.PartyGUID;
	}

	public PartyUpdate GetCurrentGroup()
	{
		return this.CurrentGroups[this.GetCurrentPartyIndex()];
	}

	public sbyte GetCurrentPartyIndex()
	{
		return (sbyte)(this.IsInBattleground() ? 1 : 0);
	}

	public byte GetItemSpellSlot(WowGuid128 guid, uint spellId)
	{
		int OBJECT_FIELD_ENTRY = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY);
		if (OBJECT_FIELD_ENTRY < 0)
		{
			return 0;
		}
		Dictionary<int, UpdateField> updates = this.GetCachedObjectFieldsLegacy(guid);
		if (updates == null)
		{
			return 0;
		}
		uint itemId = updates[OBJECT_FIELD_ENTRY].UInt32Value;
		return GameData.GetItemEffectSlot(itemId, spellId);
	}

	public uint GetItemId(WowGuid128 guid)
	{
		int OBJECT_FIELD_ENTRY = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY);
		if (OBJECT_FIELD_ENTRY < 0)
		{
			return 0u;
		}
		return this.GetCachedObjectFieldsLegacy(guid)?[OBJECT_FIELD_ENTRY].UInt32Value ?? 0;
	}

	public void SetFlatSpellMod(byte spellMod, byte spellMask, int amount)
	{
		if (this.FlatSpellMods.ContainsKey(spellMod))
		{
			if (this.FlatSpellMods[spellMod].ContainsKey(spellMask))
			{
				this.FlatSpellMods[spellMod][spellMask] = amount;
			}
			else
			{
				this.FlatSpellMods[spellMod].Add(spellMask, amount);
			}
		}
		else
		{
			Dictionary<byte, int> dict = new Dictionary<byte, int>();
			dict.Add(spellMask, amount);
			this.FlatSpellMods.Add(spellMod, dict);
		}
	}

	public void SetPctSpellMod(byte spellMod, byte spellMask, int amount)
	{
		if (this.PctSpellMods.ContainsKey(spellMod))
		{
			if (this.PctSpellMods[spellMod].ContainsKey(spellMask))
			{
				this.PctSpellMods[spellMod][spellMask] = amount;
			}
			else
			{
				this.PctSpellMods[spellMod].Add(spellMask, amount);
			}
		}
		else
		{
			Dictionary<byte, int> dict = new Dictionary<byte, int>();
			dict.Add(spellMask, amount);
			this.PctSpellMods.Add(spellMod, dict);
		}
	}

	public ArenaTeamInspectData GetArenaTeamDataForPlayer(WowGuid128 guid, byte slot)
	{
		if (this.PlayerArenaTeams.ContainsKey(guid))
		{
			return this.PlayerArenaTeams[guid][slot];
		}
		return new ArenaTeamInspectData();
	}

	public void StoreArenaTeamDataForPlayer(WowGuid128 guid, byte slot, ArenaTeamInspectData team)
	{
		if (!this.PlayerArenaTeams.ContainsKey(guid))
		{
			this.PlayerArenaTeams.Add(guid, new Array<ArenaTeamInspectData>(3, new ArenaTeamInspectData()));
		}
		this.PlayerArenaTeams[guid][slot] = team;
	}

	public WowGuid64 GetInventorySlotItem(int slot)
	{
		int PLAYER_FIELD_INV_SLOT_HEAD = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_INV_SLOT_HEAD);
		if (PLAYER_FIELD_INV_SLOT_HEAD >= 0)
		{
			Dictionary<int, UpdateField> updates = this.GetCachedObjectFieldsLegacy(this.CurrentPlayerGuid);
			if (updates != null)
			{
				return updates.GetGuidValue(PLAYER_FIELD_INV_SLOT_HEAD + slot * 2).To64();
			}
		}
		return WowGuid64.Empty;
	}

	public ushort GetObjectSpawnCounter(WowGuid64 guid)
	{
		if (this.ObjectSpawnCount.TryGetValue(guid, out var count))
		{
			return count;
		}
		return 0;
	}

	public void IncrementObjectSpawnCounter(WowGuid64 guid)
	{
		if (this.ObjectSpawnCount.ContainsKey(guid))
		{
			this.ObjectSpawnCount[guid]++;
		}
		else
		{
			this.ObjectSpawnCount.Add(guid, 0);
		}
	}

	public void SetDailyQuestSlot(uint slot, uint questId)
	{
		if (this.DailyQuestsDone.ContainsKey(slot))
		{
			if (questId != 0)
			{
				this.DailyQuestsDone[slot] = questId;
			}
			else
			{
				this.DailyQuestsDone.Remove(slot);
			}
		}
		else if (questId != 0)
		{
			this.DailyQuestsDone.Add(slot, questId);
		}
	}

	public bool IsAlliancePlayer(WowGuid128 guid)
	{
		if (this.CachedPlayers.TryGetValue(guid, out var cache))
		{
			return GameData.IsAllianceRace(cache.RaceId);
		}
		return false;
	}

	public bool IsInBattleground()
	{
		if (!this.CurrentMapId.HasValue)
		{
			return false;
		}
		uint bgId = GameData.GetBattlegroundIdFromMapId(this.CurrentMapId.Value);
		if (bgId != 0)
		{
			foreach (KeyValuePair<uint, uint> queue in this.BattleFieldQueueTypes)
			{
				if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
				{
					if (queue.Value == this.CurrentMapId)
					{
						return true;
					}
				}
				else if (queue.Value == bgId)
				{
					return true;
				}
			}
		}
		return false;
	}

	public long GetBattleFieldQueueTime(uint queueSlot)
	{
		if (this.BattleFieldQueueTimes.ContainsKey(queueSlot))
		{
			return this.BattleFieldQueueTimes[queueSlot];
		}
		long time = Time.UnixTime;
		this.BattleFieldQueueTimes.Add(queueSlot, time);
		return time;
	}

	public void StoreBattleFieldQueueType(uint queueSlot, uint mapOrBgId)
	{
		if (this.BattleFieldQueueTypes.ContainsKey(queueSlot))
		{
			this.BattleFieldQueueTypes[queueSlot] = mapOrBgId;
		}
		else
		{
			this.BattleFieldQueueTypes.Add(queueSlot, mapOrBgId);
		}
	}

	public uint GetBattleFieldQueueType(uint queueSlot)
	{
		if (this.BattleFieldQueueTypes.ContainsKey(queueSlot))
		{
			return this.BattleFieldQueueTypes[queueSlot];
		}
		return 0u;
	}

	public void StoreAuraDurationLeft(WowGuid128 guid, byte slot, int duration, int currentTime)
	{
		if (this.UnitAuraDurationLeft.ContainsKey(guid))
		{
			if (this.UnitAuraDurationLeft[guid].ContainsKey(slot))
			{
				this.UnitAuraDurationLeft[guid][slot] = duration;
			}
			else
			{
				this.UnitAuraDurationLeft[guid].Add(slot, duration);
			}
		}
		else
		{
			Dictionary<byte, int> dict = new Dictionary<byte, int>();
			dict.Add(slot, duration);
			this.UnitAuraDurationLeft.Add(guid, dict);
		}
		if (this.UnitAuraDurationUpdateTime.ContainsKey(guid))
		{
			if (this.UnitAuraDurationUpdateTime[guid].ContainsKey(slot))
			{
				this.UnitAuraDurationUpdateTime[guid][slot] = currentTime;
			}
			else
			{
				this.UnitAuraDurationUpdateTime[guid].Add(slot, currentTime);
			}
		}
		else
		{
			Dictionary<byte, int> dict2 = new Dictionary<byte, int>();
			dict2.Add(slot, currentTime);
			this.UnitAuraDurationUpdateTime.Add(guid, dict2);
		}
	}

	public void StoreAuraDurationFull(WowGuid128 guid, byte slot, int duration)
	{
		if (this.UnitAuraDurationFull.ContainsKey(guid))
		{
			if (this.UnitAuraDurationFull[guid].ContainsKey(slot))
			{
				this.UnitAuraDurationFull[guid][slot] = duration;
			}
			else
			{
				this.UnitAuraDurationFull[guid].Add(slot, duration);
			}
		}
		else
		{
			Dictionary<byte, int> dict = new Dictionary<byte, int>();
			dict.Add(slot, duration);
			this.UnitAuraDurationFull.Add(guid, dict);
		}
	}

	public void ClearAuraDuration(WowGuid128 guid, byte slot)
	{
		if (this.UnitAuraDurationUpdateTime.ContainsKey(guid) && this.UnitAuraDurationUpdateTime[guid].ContainsKey(slot))
		{
			this.UnitAuraDurationUpdateTime[guid].Remove(slot);
		}
		if (this.UnitAuraDurationLeft.ContainsKey(guid) && this.UnitAuraDurationLeft[guid].ContainsKey(slot))
		{
			this.UnitAuraDurationLeft[guid].Remove(slot);
		}
		if (this.UnitAuraDurationFull.ContainsKey(guid) && this.UnitAuraDurationFull[guid].ContainsKey(slot))
		{
			this.UnitAuraDurationFull[guid].Remove(slot);
		}
	}

	public void GetAuraDuration(WowGuid128 guid, byte slot, out int left, out int full)
	{
		if (this.UnitAuraDurationLeft.ContainsKey(guid) && this.UnitAuraDurationLeft[guid].ContainsKey(slot))
		{
			left = this.UnitAuraDurationLeft[guid][slot];
		}
		else
		{
			left = -1;
		}
		if (this.UnitAuraDurationFull.ContainsKey(guid) && this.UnitAuraDurationFull[guid].ContainsKey(slot))
		{
			full = this.UnitAuraDurationFull[guid][slot];
		}
		else
		{
			full = left;
		}
		if (left > 0 && this.UnitAuraDurationUpdateTime.ContainsKey(guid) && this.UnitAuraDurationUpdateTime[guid].ContainsKey(slot))
		{
			left -= Environment.TickCount - this.UnitAuraDurationUpdateTime[guid][slot];
		}
	}

	public void StoreAuraCaster(WowGuid128 target, byte slot, WowGuid128 caster)
	{
		if (this.UnitAuraCaster.ContainsKey(target))
		{
			if (this.UnitAuraCaster[target].ContainsKey(slot))
			{
				this.UnitAuraCaster[target][slot] = caster;
			}
			else
			{
				this.UnitAuraCaster[target].Add(slot, caster);
			}
		}
		else
		{
			Dictionary<byte, WowGuid128> dict = new Dictionary<byte, WowGuid128>();
			dict.Add(slot, caster);
			this.UnitAuraCaster.Add(target, dict);
		}
	}

	public void ClearAuraCaster(WowGuid128 guid, byte slot)
	{
		if (this.UnitAuraCaster.ContainsKey(guid) && this.UnitAuraCaster[guid].ContainsKey(slot))
		{
			this.UnitAuraCaster[guid].Remove(slot);
		}
	}

	public WowGuid128 GetAuraCaster(WowGuid128 target, byte slot)
	{
		if (this.UnitAuraCaster.ContainsKey(target) && this.UnitAuraCaster[target].ContainsKey(slot))
		{
			return this.UnitAuraCaster[target][slot];
		}
		return null;
	}

	public WowGuid128 GetAuraCaster(WowGuid128 target, byte slot, uint spellId)
	{
		WowGuid128 caster = this.GetAuraCaster(target, slot);
		if (caster == null)
		{
			caster = this.GetLastAuraCasterOnTarget(target, spellId);
			if (caster != null)
			{
				this.StoreAuraCaster(target, slot, caster);
			}
		}
		return caster;
	}

	public void StoreLastAuraCasterOnTarget(WowGuid128 target, uint spellId, WowGuid128 caster)
	{
		if (this.LastAuraCasterOnTarget.ContainsKey(target))
		{
			if (this.LastAuraCasterOnTarget[target].ContainsKey(spellId))
			{
				this.LastAuraCasterOnTarget[target][spellId] = caster;
			}
			else
			{
				this.LastAuraCasterOnTarget[target].Add(spellId, caster);
			}
		}
		else
		{
			Dictionary<uint, WowGuid128> casterDict = new Dictionary<uint, WowGuid128>();
			casterDict.Add(spellId, caster);
			this.LastAuraCasterOnTarget.Add(target, casterDict);
		}
	}

	public WowGuid128 GetLastAuraCasterOnTarget(WowGuid128 target, uint spellId)
	{
		if (this.LastAuraCasterOnTarget.ContainsKey(target) && this.LastAuraCasterOnTarget[target].TryGetValue(spellId, out var caster))
		{
			this.LastAuraCasterOnTarget[target].Remove(spellId);
			return caster;
		}
		return null;
	}

	public void StorePlayerGuildId(WowGuid128 guid, uint guildId)
	{
		if (this.PlayerGuildIds.ContainsKey(guid))
		{
			this.PlayerGuildIds[guid] = guildId;
		}
		else
		{
			this.PlayerGuildIds.Add(guid, guildId);
		}
	}

	public uint GetPlayerGuildId(WowGuid128 guid)
	{
		if (this.PlayerGuildIds.ContainsKey(guid))
		{
			return this.PlayerGuildIds[guid];
		}
		return 0u;
	}

	public uint[] GetGemsForItem(WowGuid128 guid)
	{
		if (this.ItemGems.ContainsKey(guid))
		{
			return this.ItemGems[guid];
		}
		return null;
	}

	public void SaveGemsForItem(WowGuid128 guid, uint?[] gems)
	{
		uint[] existing;
		if (this.ItemGems.ContainsKey(guid))
		{
			existing = this.ItemGems[guid];
		}
		else
		{
			existing = new uint[3];
			this.ItemGems.Add(guid, existing);
		}
		for (int i = 0; i < 3; i++)
		{
			if (gems[i].HasValue)
			{
				existing[i] = gems[i].Value;
			}
		}
	}

	public WowGuid128 GetPetGuidByNumber(uint petNumber)
	{
		this.ObjectCacheMutex.WaitOne();
		foreach (KeyValuePair<WowGuid128, UpdateFieldsArray> itr in this.ObjectCacheModern)
		{
			if (itr.Key.GetHighType() == HighGuidType.Pet && itr.Key.GetEntry() == petNumber)
			{
				this.ObjectCacheMutex.ReleaseMutex();
				return itr.Key;
			}
		}
		this.ObjectCacheMutex.ReleaseMutex();
		return null;
	}

	public void StoreOriginalObjectType(WowGuid128 guid, ObjectType type)
	{
		if (this.OriginalObjectTypes.ContainsKey(guid))
		{
			this.OriginalObjectTypes[guid] = type;
		}
		else
		{
			this.OriginalObjectTypes.Add(guid, type);
		}
	}

	public ObjectType GetOriginalObjectType(WowGuid128 guid)
	{
		if (this.OriginalObjectTypes.ContainsKey(guid))
		{
			return this.OriginalObjectTypes[guid];
		}
		return guid.GetObjectType();
	}

	public void StoreRealSpell(uint realSpellId, uint learnSpellId)
	{
		if (this.RealSpellToLearnSpell.ContainsKey(realSpellId))
		{
			this.RealSpellToLearnSpell[realSpellId] = learnSpellId;
		}
		else
		{
			this.RealSpellToLearnSpell.Add(realSpellId, learnSpellId);
		}
	}

	public uint GetLearnSpellFromRealSpell(uint spellId)
	{
		if (this.RealSpellToLearnSpell.ContainsKey(spellId))
		{
			return this.RealSpellToLearnSpell[spellId];
		}
		return spellId;
	}

	public void StoreCreatureClass(uint entry, Class classId)
	{
		if (this.CreatureClasses.ContainsKey(entry))
		{
			this.CreatureClasses[entry] = classId;
		}
		else
		{
			this.CreatureClasses.Add(entry, classId);
		}
	}

	public void SetItemBuyCount(uint itemId, uint buyCount)
	{
		if (this.ItemBuyCount.ContainsKey(itemId))
		{
			this.ItemBuyCount[itemId] = buyCount;
		}
		else
		{
			this.ItemBuyCount.Add(itemId, buyCount);
		}
	}

	public uint GetItemBuyCount(uint itemId)
	{
		if (this.ItemBuyCount.ContainsKey(itemId))
		{
			return this.ItemBuyCount[itemId];
		}
		return 1u;
	}

	public void SetChannelId(string name, int id)
	{
		if (this.ChannelIds.ContainsKey(name))
		{
			this.ChannelIds[name] = id;
		}
		else
		{
			this.ChannelIds.Add(name, id);
		}
	}

	public string GetChannelName(int id)
	{
		foreach (KeyValuePair<string, int> itr in this.ChannelIds)
		{
			if (itr.Value == id)
			{
				return itr.Key;
			}
		}
		return "";
	}

	public string GetPlayerName(WowGuid128 guid)
	{
		if (this.CachedPlayers.ContainsKey(guid) && this.CachedPlayers[guid].Name != null)
		{
			return this.CachedPlayers[guid].Name;
		}
		return "";
	}

	public WowGuid128? GetPlayerGuidByName(string name)
	{
		name = name.Trim().Replace("\0", "");
		foreach (KeyValuePair<WowGuid128, PlayerCache> player in this.CachedPlayers)
		{
			if (player.Value.Name == name && !WowGuid128.IsUnknownPlayerGuid(player.Key))
			{
				return player.Key;
			}
		}
		return null;
	}

	public void UpdatePlayerCache(WowGuid128 guid, PlayerCache data)
	{
		if (data.Name != null)
		{
			data.Name = data.Name.Trim().Replace("\0", "");
		}
		if (this.CachedPlayers.ContainsKey(guid))
		{
			if (!string.IsNullOrEmpty(data.Name))
			{
				this.CachedPlayers[guid].Name = data.Name;
			}
			if (data.RaceId != Race.None)
			{
				this.CachedPlayers[guid].RaceId = data.RaceId;
			}
			if (data.ClassId != Class.None)
			{
				this.CachedPlayers[guid].ClassId = data.ClassId;
			}
			if (data.SexId != Gender.None)
			{
				this.CachedPlayers[guid].SexId = data.SexId;
			}
			if (data.Level != 0)
			{
				this.CachedPlayers[guid].Level = data.Level;
			}
		}
		else
		{
			this.CachedPlayers.Add(guid, data);
		}
	}

	public Class GetUnitClass(WowGuid128 guid)
	{
		if (this.CachedPlayers.ContainsKey(guid))
		{
			return this.CachedPlayers[guid].ClassId;
		}
		if (this.CreatureClasses.ContainsKey(guid.GetEntry()))
		{
			return this.CreatureClasses[guid.GetEntry()];
		}
		return Class.Warrior;
	}

	public int GetLegacyFieldValueInt32<T>(WowGuid128 guid, T field)
	{
		int fieldIndex = LegacyVersion.GetUpdateField(field);
		if (fieldIndex < 0)
		{
			return 0;
		}
		Dictionary<int, UpdateField> updates = this.GetCachedObjectFieldsLegacy(guid);
		if (updates == null)
		{
			return 0;
		}
		if (!updates.ContainsKey(fieldIndex))
		{
			return 0;
		}
		return updates[fieldIndex].Int32Value;
	}

	public uint GetLegacyFieldValueUInt32<T>(WowGuid128 guid, T field)
	{
		int fieldIndex = LegacyVersion.GetUpdateField(field);
		if (fieldIndex < 0)
		{
			return 0u;
		}
		Dictionary<int, UpdateField> updates = this.GetCachedObjectFieldsLegacy(guid);
		if (updates == null)
		{
			return 0u;
		}
		if (!updates.ContainsKey(fieldIndex))
		{
			return 0u;
		}
		return updates[fieldIndex].UInt32Value;
	}

	public float GetLegacyFieldValueFloat<T>(WowGuid128 guid, T field)
	{
		int fieldIndex = LegacyVersion.GetUpdateField(field);
		if (fieldIndex < 0)
		{
			return 0f;
		}
		Dictionary<int, UpdateField> updates = this.GetCachedObjectFieldsLegacy(guid);
		if (updates == null)
		{
			return 0f;
		}
		if (!updates.ContainsKey(fieldIndex))
		{
			return 0f;
		}
		return updates[fieldIndex].FloatValue;
	}

	public bool HasRangedWeapon()
	{
		if (this.CurrentPlayerGuid == null) return false;
		var updates = this.GetCachedObjectFieldsLegacy(this.CurrentPlayerGuid);
		if (updates == null) return false;
		int PLAYER_VISIBLE_ITEM_1_ENTRYID = LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID);
		if (PLAYER_VISIBLE_ITEM_1_ENTRYID < 0) return false;
		int offset = LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? 2 : (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180) ? 16 : 12);
		int rangedIdx = PLAYER_VISIBLE_ITEM_1_ENTRYID + 17 * offset;
		return updates.ContainsKey(rangedIdx) && updates[rangedIdx].UInt32Value != 0;
	}

	public Dictionary<int, UpdateField> GetCachedObjectFieldsLegacy(WowGuid128 guid)
	{
		this.ObjectCacheMutex.WaitOne();
		if (this.ObjectCacheLegacy.TryGetValue(guid, out var dict))
		{
			this.ObjectCacheMutex.ReleaseMutex();
			return dict;
		}
		this.ObjectCacheMutex.ReleaseMutex();
		return null;
	}

	public UpdateFieldsArray GetCachedObjectFieldsModern(WowGuid128 guid)
	{
		this.ObjectCacheMutex.WaitOne();
		if (this.ObjectCacheModern.TryGetValue(guid, out var array))
		{
			this.ObjectCacheMutex.ReleaseMutex();
			return array;
		}
		this.ObjectCacheMutex.ReleaseMutex();
		return null;
	}
}
