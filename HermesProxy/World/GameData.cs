using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Framework.IO;
using Framework.Logging;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Microsoft.VisualBasic.FileIO;

namespace HermesProxy.World;

public static class GameData
{
	public static Dictionary<uint, Dictionary<string, byte[]>> BuildAuthSeeds = new Dictionary<uint, Dictionary<string, byte[]>>();

	public static SortedDictionary<uint, BroadcastText> BroadcastTextStore = new SortedDictionary<uint, BroadcastText>();

	public static Dictionary<uint, uint> ItemDisplayIdStore = new Dictionary<uint, uint>();

	public static Dictionary<uint, uint> ItemDisplayIdToFileDataIdStore = new Dictionary<uint, uint>();

	public static Dictionary<uint, ItemSpellsData> ItemSpellsDataStore = new Dictionary<uint, ItemSpellsData>();

	public static Dictionary<uint, ItemRecord> ItemRecordsStore = new Dictionary<uint, ItemRecord>();

	public static Dictionary<uint, ItemSparseRecord> ItemSparseRecordsStore = new Dictionary<uint, ItemSparseRecord>();

	public static Dictionary<uint, ItemAppearance> ItemAppearanceStore = new Dictionary<uint, ItemAppearance>();

	public static Dictionary<uint, ItemModifiedAppearance> ItemModifiedAppearanceStore = new Dictionary<uint, ItemModifiedAppearance>();

	public static Dictionary<uint, ItemEffect> ItemEffectStore = new Dictionary<uint, ItemEffect>();

	public static Dictionary<uint, Battleground> Battlegrounds = new Dictionary<uint, Battleground>();

	public static Dictionary<uint, ChatChannel> ChatChannels = new Dictionary<uint, ChatChannel>();

	public static Dictionary<uint, Dictionary<uint, byte>> ItemEffects = new Dictionary<uint, Dictionary<uint, byte>>();

	public static Dictionary<uint, uint> ItemEnchantVisuals = new Dictionary<uint, uint>();

	public static Dictionary<uint, uint> SpellVisuals = new Dictionary<uint, uint>();

	public static Dictionary<uint, uint> LearnSpells = new Dictionary<uint, uint>();

	public static Dictionary<uint, uint> TotemSpells = new Dictionary<uint, uint>();

	public static Dictionary<uint, uint> Gems = new Dictionary<uint, uint>();

	public static Dictionary<uint, CreatureDisplayInfo> CreatureDisplayInfos = new Dictionary<uint, CreatureDisplayInfo>();

	public static Dictionary<uint, CreatureModelCollisionHeight> CreatureModelCollisionHeights = new Dictionary<uint, CreatureModelCollisionHeight>();

	public static Dictionary<uint, uint> TransportPeriods = new Dictionary<uint, uint>();

	// Entries from TransportAnimation DB2 — elevators the 3.4.3 client knows about
	public static HashSet<uint> TransportAnimationEntries = new HashSet<uint>();

	public static Dictionary<uint, string> AreaNames = new Dictionary<uint, string>();

	public static Dictionary<uint, uint> RaceFaction = new Dictionary<uint, uint>();

	public static HashSet<uint> DispellSpells = new HashSet<uint>();

	public static Dictionary<uint, List<float>> SpellEffectPoints = new Dictionary<uint, List<float>>();

	public static HashSet<uint> StackableAuras = new HashSet<uint>();

	public static HashSet<uint> MountAuras = new HashSet<uint>();

	public static HashSet<uint> NextMeleeSpells = new HashSet<uint>();

	public static HashSet<uint> AutoRepeatSpells = new HashSet<uint>();

	public static HashSet<uint> AuraSpells = new HashSet<uint>();

	public static Dictionary<uint, TaxiPath> TaxiPaths = new Dictionary<uint, TaxiPath>();

	public static int[,] TaxiNodesGraph = new int[250, 250];

	public static Dictionary<uint, uint> QuestBits = new Dictionary<uint, uint>();

	public static Dictionary<uint, ItemTemplate> ItemTemplates = new Dictionary<uint, ItemTemplate>();

	public static Dictionary<uint, CreatureTemplate> CreatureTemplates = new Dictionary<uint, CreatureTemplate>();

	public static Dictionary<uint, QuestTemplate> QuestTemplates = new Dictionary<uint, QuestTemplate>();

	public static Dictionary<uint, string> ItemNames = new Dictionary<uint, string>();

	public const uint HotfixAreaTriggerBegin = 100000u;

	public const uint HotfixSkillLineBegin = 110000u;

	public const uint HotfixSkillRaceClassInfoBegin = 120000u;

	public const uint HotfixSkillLineAbilityBegin = 130000u;

	public const uint HotfixSpellBegin = 140000u;

	public const uint HotfixSpellNameBegin = 150000u;

	public const uint HotfixSpellLevelsBegin = 160000u;

	public const uint HotfixSpellAuraOptionsBegin = 170000u;

	public const uint HotfixSpellMiscBegin = 180000u;

	public const uint HotfixSpellEffectBegin = 190000u;

	public const uint HotfixSpellXSpellVisualBegin = 200000u;

	public const uint HotfixItemBegin = 210000u;

	public const uint HotfixItemSparseBegin = 220000u;

	public const uint HotfixItemAppearanceBegin = 230000u;

	public const uint HotfixItemModifiedAppearanceBegin = 240000u;

	public const uint HotfixItemEffectBegin = 250000u;

	public const uint HotfixItemDisplayInfoBegin = 260000u;

	public const uint HotfixCreatureDisplayInfoBegin = 270000u;

	public const uint HotfixCreatureDisplayInfoExtraBegin = 280000u;

	public const uint HotfixCreatureDisplayInfoOptionBegin = 290000u;

	public static Dictionary<uint, HotfixRecord> Hotfixes = new Dictionary<uint, HotfixRecord>();

	public static void StoreItemName(uint entry, string name)
	{
		if (GameData.ItemNames.ContainsKey(entry))
		{
			GameData.ItemNames[entry] = name;
		}
		else
		{
			GameData.ItemNames.Add(entry, name);
		}
	}

	public static string GetItemName(uint entry)
	{
		if (GameData.ItemNames.TryGetValue(entry, out var data))
		{
			return data;
		}
		ItemTemplate template = GameData.GetItemTemplate(entry);
		if (template != null)
		{
			return template.Name[0];
		}
		return "";
	}

	public static void StoreItemTemplate(uint entry, ItemTemplate template)
	{
		if (GameData.ItemTemplates.ContainsKey(entry))
		{
			GameData.ItemTemplates[entry] = template;
		}
		else
		{
			GameData.ItemTemplates.Add(entry, template);
		}
	}

	public static ItemTemplate GetItemTemplate(uint entry)
	{
		if (GameData.ItemTemplates.TryGetValue(entry, out var data))
		{
			return data;
		}
		return null;
	}

	public static void StoreQuestTemplate(uint entry, QuestTemplate template)
	{
		if (GameData.QuestTemplates.ContainsKey(entry))
		{
			GameData.QuestTemplates[entry] = template;
		}
		else
		{
			GameData.QuestTemplates.Add(entry, template);
		}
	}

	public static QuestTemplate GetQuestTemplate(uint entry)
	{
		if (GameData.QuestTemplates.TryGetValue(entry, out var data))
		{
			return data;
		}
		return null;
	}

	public static QuestObjective GetQuestObjectiveForItem(uint entry)
	{
		foreach (KeyValuePair<uint, QuestTemplate> questTemplate in GameData.QuestTemplates)
		{
			foreach (QuestObjective objective in questTemplate.Value.Objectives)
			{
				if (objective.ObjectID == entry && objective.Type == QuestObjectiveType.Item)
				{
					return objective;
				}
			}
		}
		return null;
	}

	public static uint? GetUniqueQuestBit(uint questId)
	{
		if (!GameData.QuestBits.TryGetValue(questId, out var result))
		{
			return null;
		}
		return result;
	}

	public static void StoreCreatureTemplate(uint entry, CreatureTemplate template)
	{
		if (GameData.CreatureTemplates.ContainsKey(entry))
		{
			GameData.CreatureTemplates[entry] = template;
		}
		else
		{
			GameData.CreatureTemplates.Add(entry, template);
		}
	}

	public static CreatureTemplate GetCreatureTemplate(uint entry)
	{
		if (GameData.CreatureTemplates.TryGetValue(entry, out var data))
		{
			return data;
		}
		return null;
	}

	public static uint GetItemDisplayId(uint entry)
	{
		if (GameData.ItemDisplayIdStore.TryGetValue(entry, out var displayId))
		{
			return displayId;
		}
		return 0u;
	}

	public static uint GetItemIdWithDisplayId(uint displayId)
	{
		foreach (KeyValuePair<uint, uint> item in GameData.ItemDisplayIdStore)
		{
			if (item.Value == displayId)
			{
				return item.Key;
			}
		}
		return 0u;
	}

	public static ItemAppearance GetItemAppearanceByDisplayId(uint displayId)
	{
		foreach (KeyValuePair<uint, ItemAppearance> item in GameData.ItemAppearanceStore)
		{
			if (item.Value.ItemDisplayInfoID == (int)displayId)
			{
				return item.Value;
			}
		}
		return null;
	}

	public static ItemAppearance GetItemAppearanceByItemId(uint itemId)
	{
		ItemModifiedAppearance modAppearance = GameData.GetItemModifiedAppearanceByItemId(itemId);
		if (modAppearance == null)
		{
			return null;
		}
		if (GameData.ItemAppearanceStore.TryGetValue((uint)modAppearance.ItemAppearanceID, out var data))
		{
			return data;
		}
		return null;
	}

	public static uint GetItemIconFileDataIdByDisplayId(uint displayId)
	{
		if (GameData.ItemDisplayIdToFileDataIdStore.TryGetValue(displayId, out var fileDataId))
		{
			return fileDataId;
		}
		return 0u;
	}

	public static ItemModifiedAppearance GetItemModifiedAppearanceByDisplayId(uint displayId)
	{
		ItemAppearance appearance = GameData.GetItemAppearanceByDisplayId(displayId);
		if (appearance != null)
		{
			foreach (KeyValuePair<uint, ItemModifiedAppearance> item in GameData.ItemModifiedAppearanceStore)
			{
				if (item.Value.ItemAppearanceID == appearance.Id)
				{
					return item.Value;
				}
			}
		}
		return null;
	}

	public static ItemModifiedAppearance GetItemModifiedAppearanceByItemId(uint itemId)
	{
		foreach (KeyValuePair<uint, ItemModifiedAppearance> item in GameData.ItemModifiedAppearanceStore)
		{
			if (item.Value.ItemID == (int)itemId)
			{
				return item.Value;
			}
		}
		return null;
	}

	public static ItemEffect GetItemEffectByItemId(uint itemId, byte slot)
	{
		foreach (KeyValuePair<uint, ItemEffect> item in GameData.ItemEffectStore)
		{
			if (item.Value.ParentItemID == itemId && item.Value.LegacySlotIndex == slot)
			{
				return item.Value;
			}
		}
		return null;
	}

	public static uint GetFirstFreeId(IDictionary dict, uint after = 0u)
	{
		uint firstEntry = 0u;
		foreach (object item in dict)
		{
			Type type = item.GetType();
			PropertyInfo key = type.GetProperty("Key");
			object keyObj = key.GetValue(item, null);
			if (after != 0 && (uint)keyObj <= after)
			{
				continue;
			}
			firstEntry = (uint)keyObj;
			break;
		}
		for (; dict.Contains(firstEntry); firstEntry++)
		{
		}
		return firstEntry;
	}

	public static void SaveItemEffectSlot(uint itemId, uint spellId, byte slot)
	{
		if (GameData.ItemEffects.ContainsKey(itemId))
		{
			if (GameData.ItemEffects[itemId].ContainsKey(spellId))
			{
				GameData.ItemEffects[itemId][spellId] = slot;
			}
			else
			{
				GameData.ItemEffects[itemId].Add(spellId, slot);
			}
		}
		else
		{
			Dictionary<uint, byte> dict = new Dictionary<uint, byte>();
			dict.Add(spellId, slot);
			GameData.ItemEffects.Add(itemId, dict);
		}
	}

	public static byte GetItemEffectSlot(uint itemId, uint spellId)
	{
		if (GameData.ItemEffects.ContainsKey(itemId) && GameData.ItemEffects[itemId].ContainsKey(spellId))
		{
			return GameData.ItemEffects[itemId][spellId];
		}
		return 0;
	}

	public static uint GetItemEnchantVisual(uint enchantId)
	{
		if (GameData.ItemEnchantVisuals.TryGetValue(enchantId, out var visualId))
		{
			return visualId;
		}
		return 0u;
	}

	public static uint GetSpellVisual(uint spellId)
	{
		if (GameData.SpellVisuals.TryGetValue(spellId, out var visual))
		{
			return visual;
		}
		return 0u;
	}

	public static int GetTotemSlotForSpell(uint spellId)
	{
		if (GameData.TotemSpells.TryGetValue(spellId, out var slot))
		{
			return (int)slot;
		}
		return -1;
	}

	public static uint GetRealSpell(uint learnSpellId)
	{
		if (GameData.LearnSpells.TryGetValue(learnSpellId, out var realSpellId))
		{
			return realSpellId;
		}
		return learnSpellId;
	}

	public static uint GetGemFromEnchantId(uint enchantId)
	{
		if (GameData.Gems.TryGetValue(enchantId, out var itemId))
		{
			return itemId;
		}
		return 0u;
	}

	public static uint GetEnchantIdFromGem(uint itemId)
	{
		foreach (KeyValuePair<uint, uint> itr in GameData.Gems)
		{
			if (itr.Value == itemId)
			{
				return itr.Key;
			}
		}
		return 0u;
	}

	public static float GetUnitCompleteDisplayScale(uint displayId)
	{
		CreatureDisplayInfo displayData = GameData.GetDisplayInfo(displayId);
		if (displayData.ModelId == 0)
		{
			return 1f;
		}
		CreatureModelCollisionHeight modelData = GameData.GetModelData(displayId);
		return displayData.DisplayScale * modelData.ModelScale;
	}

	public static CreatureDisplayInfo GetDisplayInfo(uint displayId)
	{
		if (GameData.CreatureDisplayInfos.TryGetValue(displayId, out var info))
		{
			return info;
		}
		return new CreatureDisplayInfo(0u, 1f);
	}

	public static CreatureModelCollisionHeight GetModelData(uint modelId)
	{
		if (GameData.CreatureModelCollisionHeights.TryGetValue(modelId, out var info))
		{
			return info;
		}
		return new CreatureModelCollisionHeight(1f, 0f, 0f);
	}

	public static uint GetTransportPeriod(uint entry)
	{
		if (GameData.TransportPeriods.TryGetValue(entry, out var period))
		{
			return period;
		}
		return 0u;
	}

	public static string GetAreaName(uint id)
	{
		if (GameData.AreaNames.TryGetValue(id, out var name))
		{
			return name;
		}
		return "";
	}

	public static uint GetFactionForRace(uint race)
	{
		if (GameData.RaceFaction.TryGetValue(race, out var faction))
		{
			return faction;
		}
		return 1u;
	}

	public static uint GetBattlegroundIdFromMapId(uint mapId)
	{
		foreach (KeyValuePair<uint, Battleground> bg in GameData.Battlegrounds)
		{
			if (bg.Value.MapIds.Contains(mapId))
			{
				return bg.Key;
			}
		}
		return 0u;
	}

	public static uint GetMapIdFromBattlegroundId(uint bgId)
	{
		if (GameData.Battlegrounds.TryGetValue(bgId, out var bg))
		{
			return bg.MapIds[0];
		}
		return 0u;
	}

	public static uint GetChatChannelIdFromName(string name)
	{
		foreach (KeyValuePair<uint, ChatChannel> channel in GameData.ChatChannels)
		{
			if (name.Contains(channel.Value.Name))
			{
				return channel.Key;
			}
		}
		return 0u;
	}

	public static List<ChatChannel> GetChatChannelsWithFlags(ChannelFlags flags)
	{
		List<ChatChannel> channels = new List<ChatChannel>();
		foreach (KeyValuePair<uint, ChatChannel> channel in GameData.ChatChannels)
		{
			if ((channel.Value.Flags & flags) == flags)
			{
				channels.Add(channel.Value);
			}
		}
		return channels;
	}

	public static bool IsAllianceRace(Race raceId)
	{
		switch (raceId)
		{
		case Race.Human:
		case Race.Dwarf:
		case Race.NightElf:
		case Race.Gnome:
		case Race.Draenei:
		case Race.Worgen:
			return true;
		default:
			return false;
		}
	}

	public static bool IsHordeRace(Race raceId)
	{
		switch (raceId)
		{
		case Race.Orc:
		case Race.Undead:
		case Race.Tauren:
		case Race.Troll:
		case Race.Goblin:
		case Race.BloodElf:
			return true;
		default:
			return false;
		}
	}

	public static int GetFactionByRace(Race race)
	{
		if (GameData.IsAllianceRace(race))
		{
			return 1;
		}
		if (GameData.IsHordeRace(race))
		{
			return 2;
		}
		return 0;
	}

	public static BroadcastText GetBroadcastText(uint entry)
	{
		if (GameData.BroadcastTextStore.TryGetValue(entry, out var data))
		{
			return data;
		}
		return null;
	}

	public static uint GetBroadcastTextId(string maleText, string femaleText, uint language, ushort[] emoteDelays, ushort[] emotes)
	{
		foreach (KeyValuePair<uint, BroadcastText> itr in GameData.BroadcastTextStore)
		{
			if (((!string.IsNullOrEmpty(maleText) && itr.Value.MaleText == maleText) || (!string.IsNullOrEmpty(femaleText) && itr.Value.FemaleText == femaleText)) && itr.Value.Language == language && itr.Value.EmoteDelays.SequenceEqual(emoteDelays) && itr.Value.Emotes.SequenceEqual(emotes))
			{
				return itr.Key;
			}
		}
		BroadcastText broadcastText = new BroadcastText();
		broadcastText.Entry = GameData.BroadcastTextStore.Keys.Last() + 1;
		broadcastText.MaleText = maleText;
		broadcastText.FemaleText = femaleText;
		broadcastText.Language = language;
		broadcastText.EmoteDelays = emoteDelays;
		broadcastText.Emotes = emotes;
		GameData.BroadcastTextStore.Add(broadcastText.Entry, broadcastText);
		return broadcastText.Entry;
	}

	public static void LoadEverything()
	{
		Log.Print(LogType.Storage, "Loading data files...", "LoadEverything", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		GameData.LoadBuildAuthSeeds();
		GameData.LoadBroadcastTexts();
		GameData.LoadItemDisplayIds();
		GameData.LoadItemRecords();
		GameData.LoadItemSparseRecords();
		GameData.LoadItemAppearance();
		GameData.LoadItemModifiedAppearance();
		GameData.LoadItemEffect();
		GameData.LoadItemSpellsData();
		GameData.LoadItemDisplayIdToFileDataId();
		GameData.LoadBattlegrounds();
		GameData.LoadChatChannels();
		GameData.LoadItemEnchantVisuals();
		GameData.LoadSpellVisuals();
		GameData.LoadLearnSpells();
		GameData.LoadTotemSpells();
		GameData.LoadGems();
		GameData.LoadCreatureDisplayInfo();
		GameData.LoadCreatureModelCollisionHeights();
		GameData.LoadTransports();
		GameData.LoadAreaNames();
		GameData.LoadRaceFaction();
		GameData.LoadDispellSpells();
		GameData.LoadSpellEffectPoints();
		GameData.LoadStackableAuras();
		GameData.LoadMountAuras();
		GameData.LoadMeleeSpells();
		GameData.LoadAutoRepeatSpells();
		GameData.LoadAuraSpells();
		GameData.LoadTaxiPaths();
		GameData.LoadTaxiPathNodesGraph();
		GameData.LoadQuestBits();
		GameData.LoadHotfixes();
		Log.Print(LogType.Storage, "Finished loading data.", "LoadEverything", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
	}

	public static void LoadBuildAuthSeeds()
	{
		string path = Path.Combine("CSV", "BuildAuthSeeds.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint build = uint.Parse(fields[0]);
			string platform = fields[1];
			byte[] seed = fields[2].ParseAsByteArray();
			if (!GameData.BuildAuthSeeds.TryGetValue(build, out var seeds))
			{
				seeds = new Dictionary<string, byte[]>();
				GameData.BuildAuthSeeds.Add(build, seeds);
			}
			seeds.Add(platform, seed);
		}
	}

	public static void LoadBroadcastTexts()
	{
		string path = Path.Combine("CSV", $"BroadcastTexts{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			BroadcastText broadcastText = new BroadcastText();
			broadcastText.Entry = uint.Parse(fields[0]);
			broadcastText.MaleText = fields[1].TrimEnd().Replace("\0", "").Replace("~", "\n");
			broadcastText.FemaleText = fields[2].TrimEnd().Replace("\0", "").Replace("~", "\n");
			broadcastText.Language = uint.Parse(fields[3]);
			broadcastText.Emotes[0] = ushort.Parse(fields[4]);
			broadcastText.Emotes[1] = ushort.Parse(fields[5]);
			broadcastText.Emotes[2] = ushort.Parse(fields[6]);
			broadcastText.EmoteDelays[0] = ushort.Parse(fields[7]);
			broadcastText.EmoteDelays[1] = ushort.Parse(fields[8]);
			broadcastText.EmoteDelays[2] = ushort.Parse(fields[9]);
			GameData.BroadcastTextStore.Add(broadcastText.Entry, broadcastText);
		}
	}

	public static void LoadItemDisplayIds()
	{
		string path = Path.Combine("CSV", $"ItemIdToDisplayId{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint entry = uint.Parse(fields[0]);
			uint displayId = uint.Parse(fields[1]);
			GameData.ItemDisplayIdStore.Add(entry, displayId);
		}
	}

	public static void LoadItemRecords()
	{
		string path = Path.Combine("CSV", $"Item{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			ItemRecord row = new ItemRecord();
			row.Id = int.Parse(fields[0]);
			row.ClassId = byte.Parse(fields[1]);
			row.SubclassId = byte.Parse(fields[2]);
			row.Material = byte.Parse(fields[3]);
			row.InventoryType = sbyte.Parse(fields[4]);
			row.RequiredLevel = int.Parse(fields[5]);
			row.SheatheType = byte.Parse(fields[6]);
			row.RandomProperty = ushort.Parse(fields[7]);
			row.ItemRandomSuffixGroupId = ushort.Parse(fields[8]);
			row.SoundOverrideSubclassId = sbyte.Parse(fields[9]);
			row.ScalingStatDistributionId = ushort.Parse(fields[10]);
			row.IconFileDataId = int.Parse(fields[11]);
			row.ItemGroupSoundsId = byte.Parse(fields[12]);
			row.ContentTuningId = int.Parse(fields[13]);
			row.MaxDurability = uint.Parse(fields[14]);
			row.AmmoType = byte.Parse(fields[15]);
			row.DamageType[0] = byte.Parse(fields[16]);
			row.DamageType[1] = byte.Parse(fields[17]);
			row.DamageType[2] = byte.Parse(fields[18]);
			row.DamageType[3] = byte.Parse(fields[19]);
			row.DamageType[4] = byte.Parse(fields[20]);
			row.Resistances[0] = short.Parse(fields[21]);
			row.Resistances[1] = short.Parse(fields[22]);
			row.Resistances[2] = short.Parse(fields[23]);
			row.Resistances[3] = short.Parse(fields[24]);
			row.Resistances[4] = short.Parse(fields[25]);
			row.Resistances[5] = short.Parse(fields[26]);
			row.Resistances[6] = short.Parse(fields[27]);
			row.MinDamage[0] = ushort.Parse(fields[28]);
			row.MinDamage[1] = ushort.Parse(fields[29]);
			row.MinDamage[2] = ushort.Parse(fields[30]);
			row.MinDamage[3] = ushort.Parse(fields[31]);
			row.MinDamage[4] = ushort.Parse(fields[32]);
			row.MaxDamage[0] = ushort.Parse(fields[33]);
			row.MaxDamage[1] = ushort.Parse(fields[34]);
			row.MaxDamage[2] = ushort.Parse(fields[35]);
			row.MaxDamage[3] = ushort.Parse(fields[36]);
			row.MaxDamage[4] = ushort.Parse(fields[37]);
			GameData.ItemRecordsStore.Add((uint)row.Id, row);
		}
	}

	public static void LoadItemSparseRecords()
	{
		string path = Path.Combine("CSV", $"ItemSparse{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			ItemSparseRecord row = new ItemSparseRecord();
			row.Id = int.Parse(fields[0]);
			row.AllowableRace = long.Parse(fields[1]);
			row.Description = fields[2];
			row.Name4 = fields[3];
			row.Name3 = fields[4];
			row.Name2 = fields[5];
			row.Name1 = fields[6];
			row.DmgVariance = float.Parse(fields[7]);
			row.DurationInInventory = uint.Parse(fields[8]);
			row.QualityModifier = float.Parse(fields[9]);
			row.BagFamily = uint.Parse(fields[10]);
			row.RangeMod = float.Parse(fields[11]);
			row.StatPercentageOfSocket[0] = float.Parse(fields[12]);
			row.StatPercentageOfSocket[1] = float.Parse(fields[13]);
			row.StatPercentageOfSocket[2] = float.Parse(fields[14]);
			row.StatPercentageOfSocket[3] = float.Parse(fields[15]);
			row.StatPercentageOfSocket[4] = float.Parse(fields[16]);
			row.StatPercentageOfSocket[5] = float.Parse(fields[17]);
			row.StatPercentageOfSocket[6] = float.Parse(fields[18]);
			row.StatPercentageOfSocket[7] = float.Parse(fields[19]);
			row.StatPercentageOfSocket[8] = float.Parse(fields[20]);
			row.StatPercentageOfSocket[9] = float.Parse(fields[21]);
			row.StatPercentEditor[0] = int.Parse(fields[22]);
			row.StatPercentEditor[1] = int.Parse(fields[23]);
			row.StatPercentEditor[2] = int.Parse(fields[24]);
			row.StatPercentEditor[3] = int.Parse(fields[25]);
			row.StatPercentEditor[4] = int.Parse(fields[26]);
			row.StatPercentEditor[5] = int.Parse(fields[27]);
			row.StatPercentEditor[6] = int.Parse(fields[28]);
			row.StatPercentEditor[7] = int.Parse(fields[29]);
			row.StatPercentEditor[8] = int.Parse(fields[30]);
			row.StatPercentEditor[9] = int.Parse(fields[31]);
			row.Stackable = int.Parse(fields[32]);
			row.MaxCount = int.Parse(fields[33]);
			row.RequiredAbility = uint.Parse(fields[34]);
			row.SellPrice = uint.Parse(fields[35]);
			row.BuyPrice = uint.Parse(fields[36]);
			row.VendorStackCount = uint.Parse(fields[37]);
			row.PriceVariance = float.Parse(fields[38]);
			row.PriceRandomValue = float.Parse(fields[39]);
			row.Flags[0] = uint.Parse(fields[40]);
			row.Flags[1] = uint.Parse(fields[41]);
			row.Flags[2] = uint.Parse(fields[42]);
			row.Flags[3] = uint.Parse(fields[43]);
			row.OppositeFactionItemId = int.Parse(fields[44]);
			row.MaxDurability = uint.Parse(fields[45]);
			row.ItemNameDescriptionId = ushort.Parse(fields[46]);
			row.RequiredTransmogHoliday = ushort.Parse(fields[47]);
			row.RequiredHoliday = ushort.Parse(fields[48]);
			row.LimitCategory = ushort.Parse(fields[49]);
			row.GemProperties = ushort.Parse(fields[50]);
			row.SocketMatchEnchantmentId = ushort.Parse(fields[51]);
			row.TotemCategoryId = ushort.Parse(fields[52]);
			row.InstanceBound = ushort.Parse(fields[53]);
			row.ZoneBound[0] = ushort.Parse(fields[54]);
			row.ZoneBound[1] = ushort.Parse(fields[55]);
			row.ItemSet = ushort.Parse(fields[56]);
			row.LockId = ushort.Parse(fields[57]);
			row.StartQuestId = ushort.Parse(fields[58]);
			row.PageText = ushort.Parse(fields[59]);
			row.Delay = ushort.Parse(fields[60]);
			row.RequiredReputationId = ushort.Parse(fields[61]);
			row.RequiredSkillRank = ushort.Parse(fields[62]);
			row.RequiredSkill = ushort.Parse(fields[63]);
			row.ItemLevel = ushort.Parse(fields[64]);
			row.AllowableClass = short.Parse(fields[65]);
			row.ItemRandomSuffixGroupId = ushort.Parse(fields[66]);
			row.RandomProperty = ushort.Parse(fields[67]);
			row.MinDamage[0] = ushort.Parse(fields[68]);
			row.MinDamage[1] = ushort.Parse(fields[69]);
			row.MinDamage[2] = ushort.Parse(fields[70]);
			row.MinDamage[3] = ushort.Parse(fields[71]);
			row.MinDamage[4] = ushort.Parse(fields[72]);
			row.MaxDamage[0] = ushort.Parse(fields[73]);
			row.MaxDamage[1] = ushort.Parse(fields[74]);
			row.MaxDamage[2] = ushort.Parse(fields[75]);
			row.MaxDamage[3] = ushort.Parse(fields[76]);
			row.MaxDamage[4] = ushort.Parse(fields[77]);
			row.Resistances[0] = short.Parse(fields[78]);
			row.Resistances[1] = short.Parse(fields[79]);
			row.Resistances[2] = short.Parse(fields[80]);
			row.Resistances[3] = short.Parse(fields[81]);
			row.Resistances[4] = short.Parse(fields[82]);
			row.Resistances[5] = short.Parse(fields[83]);
			row.Resistances[6] = short.Parse(fields[84]);
			row.ScalingStatDistributionId = ushort.Parse(fields[85]);
			row.ExpansionId = byte.Parse(fields[86]);
			row.ArtifactId = byte.Parse(fields[87]);
			row.SpellWeight = byte.Parse(fields[88]);
			row.SpellWeightCategory = byte.Parse(fields[89]);
			row.SocketType[0] = byte.Parse(fields[90]);
			row.SocketType[1] = byte.Parse(fields[91]);
			row.SocketType[2] = byte.Parse(fields[92]);
			row.SheatheType = byte.Parse(fields[93]);
			row.Material = byte.Parse(fields[94]);
			row.PageMaterial = byte.Parse(fields[95]);
			row.PageLanguage = byte.Parse(fields[96]);
			row.Bonding = byte.Parse(fields[97]);
			row.DamageType = byte.Parse(fields[98]);
			row.StatType[0] = sbyte.Parse(fields[99]);
			row.StatType[1] = sbyte.Parse(fields[100]);
			row.StatType[2] = sbyte.Parse(fields[101]);
			row.StatType[3] = sbyte.Parse(fields[102]);
			row.StatType[4] = sbyte.Parse(fields[103]);
			row.StatType[5] = sbyte.Parse(fields[104]);
			row.StatType[6] = sbyte.Parse(fields[105]);
			row.StatType[7] = sbyte.Parse(fields[106]);
			row.StatType[8] = sbyte.Parse(fields[107]);
			row.StatType[9] = sbyte.Parse(fields[108]);
			row.ContainerSlots = byte.Parse(fields[109]);
			row.RequiredReputationRank = byte.Parse(fields[110]);
			row.RequiredCityRank = byte.Parse(fields[111]);
			row.RequiredHonorRank = byte.Parse(fields[112]);
			row.InventoryType = byte.Parse(fields[113]);
			row.OverallQualityId = byte.Parse(fields[114]);
			row.AmmoType = byte.Parse(fields[115]);
			row.StatValue[0] = sbyte.Parse(fields[116]);
			row.StatValue[1] = sbyte.Parse(fields[117]);
			row.StatValue[2] = sbyte.Parse(fields[118]);
			row.StatValue[3] = sbyte.Parse(fields[119]);
			row.StatValue[4] = sbyte.Parse(fields[120]);
			row.StatValue[5] = sbyte.Parse(fields[121]);
			row.StatValue[6] = sbyte.Parse(fields[122]);
			row.StatValue[7] = sbyte.Parse(fields[123]);
			row.StatValue[8] = sbyte.Parse(fields[124]);
			row.StatValue[9] = sbyte.Parse(fields[125]);
			row.RequiredLevel = sbyte.Parse(fields[126]);
			GameData.ItemSparseRecordsStore.Add((uint)row.Id, row);
		}
	}

	public static void LoadItemAppearance()
	{
		string path = Path.Combine("CSV", $"ItemAppearance{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			ItemAppearance appearance = new ItemAppearance();
			appearance.Id = int.Parse(fields[0]);
			appearance.DisplayType = byte.Parse(fields[1]);
			appearance.ItemDisplayInfoID = int.Parse(fields[2]);
			appearance.DefaultIconFileDataID = int.Parse(fields[3]);
			appearance.UiOrder = int.Parse(fields[4]);
			GameData.ItemAppearanceStore.Add((uint)appearance.Id, appearance);
		}
	}

	public static void LoadItemModifiedAppearance()
	{
		string path = Path.Combine("CSV", $"ItemModifiedAppearance{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			ItemModifiedAppearance modifiedAppearance = new ItemModifiedAppearance();
			modifiedAppearance.Id = int.Parse(fields[0]);
			modifiedAppearance.ItemID = int.Parse(fields[1]);
			modifiedAppearance.ItemAppearanceModifierID = int.Parse(fields[2]);
			modifiedAppearance.ItemAppearanceID = int.Parse(fields[3]);
			modifiedAppearance.OrderIndex = int.Parse(fields[4]);
			modifiedAppearance.TransmogSourceTypeEnum = int.Parse(fields[5]);
			GameData.ItemModifiedAppearanceStore.Add((uint)modifiedAppearance.Id, modifiedAppearance);
		}
	}

	public static void LoadItemEffect()
	{
		string path = Path.Combine("CSV", $"ItemEffect{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			ItemEffect effect = new ItemEffect();
			effect.Id = int.Parse(fields[0]);
			effect.LegacySlotIndex = byte.Parse(fields[1]);
			effect.TriggerType = sbyte.Parse(fields[2]);
			effect.Charges = short.Parse(fields[3]);
			effect.CoolDownMSec = int.Parse(fields[4]);
			effect.CategoryCoolDownMSec = int.Parse(fields[5]);
			effect.SpellCategoryID = ushort.Parse(fields[6]);
			effect.SpellID = int.Parse(fields[7]);
			effect.ChrSpecializationID = ushort.Parse(fields[8]);
			effect.ParentItemID = int.Parse(fields[9]);
			GameData.ItemEffectStore.Add((uint)effect.Id, effect);
		}
	}

	public static void LoadItemSpellsData()
	{
		string path = Path.Combine("CSV", $"ItemSpellsData{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			ItemSpellsData data = new ItemSpellsData();
			data.Id = int.Parse(fields[0]);
			data.Category = int.Parse(fields[1]);
			data.RecoveryTime = int.Parse(fields[2]);
			data.CategoryRecoveryTime = int.Parse(fields[3]);
			GameData.ItemSpellsDataStore.Add((uint)data.Id, data);
		}
	}

	public static void LoadItemDisplayIdToFileDataId()
	{
		string path = Path.Combine("CSV", $"ItemDisplayIdToFileDataId{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint displayId = uint.Parse(fields[0]);
			uint fileDataId = uint.Parse(fields[1]);
			GameData.ItemDisplayIdToFileDataIdStore.Add(displayId, fileDataId);
		}
	}

	public static void LoadBattlegrounds()
	{
		string path = Path.Combine("CSV", "Battlegrounds.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			Battleground bg = new Battleground();
			uint bgId = uint.Parse(fields[0]);
			bg.IsArena = byte.Parse(fields[1]) != 0;
			for (int i = 0; i < 6; i++)
			{
				uint mapId = uint.Parse(fields[2 + i]);
				if (mapId != 0)
				{
					bg.MapIds.Add(mapId);
				}
			}
			GameData.Battlegrounds.Add(bgId, bg);
		}
	}

	public static void LoadChatChannels()
	{
		string path = Path.Combine("CSV", "ChatChannels.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			ChatChannel channel = new ChatChannel();
			channel.Id = uint.Parse(fields[0]);
			channel.Flags = (ChannelFlags)uint.Parse(fields[1]);
			channel.Name = fields[2];
			GameData.ChatChannels.Add(channel.Id, channel);
		}
	}

	public static void LoadItemEnchantVisuals()
	{
		string path = Path.Combine("CSV", $"ItemEnchantVisuals{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint enchantId = uint.Parse(fields[0]);
			uint visualId = uint.Parse(fields[1]);
			GameData.ItemEnchantVisuals.Add(enchantId, visualId);
		}
	}

	public static void LoadSpellVisuals()
	{
		string path = Path.Combine("CSV", $"SpellVisuals{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			uint visualId = uint.Parse(fields[1]);
			GameData.SpellVisuals.Add(spellId, visualId);
		}
	}

	public static void LoadLearnSpells()
	{
		string path = Path.Combine("CSV", "LearnSpells.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint learnSpellId = uint.Parse(fields[0]);
			uint realSpellId = uint.Parse(fields[1]);
			if (!GameData.LearnSpells.ContainsKey(learnSpellId))
			{
				GameData.LearnSpells.Add(learnSpellId, realSpellId);
			}
		}
	}

	public static void LoadTotemSpells()
	{
		if (LegacyVersion.ExpansionVersion > 1)
		{
			return;
		}
		string path = Path.Combine("CSV", "TotemSpells.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			uint totemSlot = uint.Parse(fields[1]);
			GameData.TotemSpells.Add(spellId, totemSlot);
		}
	}

	public static void LoadGems()
	{
		if (ModernVersion.ExpansionVersion <= 1)
		{
			return;
		}
		string path = Path.Combine("CSV", $"Gems{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint enchantId = uint.Parse(fields[0]);
			uint itemId = uint.Parse(fields[1]);
			GameData.Gems.Add(enchantId, itemId);
		}
	}

	public static void LoadCreatureDisplayInfo()
	{
		string path = Path.Combine("CSV", "CreatureDisplayInfo.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint displayId = uint.Parse(fields[0]);
			uint modelId = uint.Parse(fields[1]);
			float scale = float.Parse(fields[2]);
			GameData.CreatureDisplayInfos.Add(displayId, new CreatureDisplayInfo(modelId, scale));
		}
	}

	public static void LoadCreatureModelCollisionHeights()
	{
		string path = Path.Combine("CSV", $"CreatureModelCollisionHeightsModern{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint modelId = uint.Parse(fields[0]);
			float modelScale = float.Parse(fields[1]);
			float collisionHeight = float.Parse(fields[2]);
			float collisionHeightMounted = float.Parse(fields[3]);
			GameData.CreatureModelCollisionHeights.Add(modelId, new CreatureModelCollisionHeight(modelScale, collisionHeight, collisionHeightMounted));
		}
	}

	public static void LoadTransports()
	{
		string path = Path.Combine("CSV", $"Transports{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint entry = uint.Parse(fields[0]);
			uint period = uint.Parse(fields[1]);
			GameData.TransportPeriods.Add(entry, period);
		}

		// Load TransportAnimation DB2 entries (elevators the client knows about)
		string animPath = Path.Combine("CSV", "TransportAnimation.3.4.3.54261.csv");
		if (System.IO.File.Exists(animPath))
		{
			using TextFieldParser animParser = new TextFieldParser(animPath);
			animParser.CommentTokens = new string[1] { "#" };
			animParser.SetDelimiters(",");
			animParser.HasFieldsEnclosedInQuotes = false;
			animParser.ReadLine();
			while (!animParser.EndOfData)
			{
				string[] fields = animParser.ReadFields();
				uint transportId = uint.Parse(fields[6]); // TransportID column
				GameData.TransportAnimationEntries.Add(transportId);
			}
			Framework.Logging.Log.Print(Framework.Logging.LogType.Network, $"Loaded {GameData.TransportAnimationEntries.Count} TransportAnimation entries");
		}
	}

	public static void LoadAreaNames()
	{
		string path = Path.Combine("CSV", "AreaNames.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			string name = fields[1];
			GameData.AreaNames.Add(id, name);
		}
	}

	public static void LoadRaceFaction()
	{
		string path = Path.Combine("CSV", "RaceFaction.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			uint faction = uint.Parse(fields[1]);
			GameData.RaceFaction.Add(id, faction);
		}
	}

	public static void LoadDispellSpells()
	{
		if (LegacyVersion.ExpansionVersion > 1)
		{
			return;
		}
		string path = Path.Combine("CSV", "DispellSpells.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.DispellSpells.Add(spellId);
		}
	}

	public static void LoadSpellEffectPoints()
	{
		string path = Path.Combine("CSV", $"SpellEffectPoints{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			int basePointsEff1 = int.Parse(fields[2]);
			if (basePointsEff1 != 0)
			{
				basePointsEff1++;
			}
			int basePointsEff2 = int.Parse(fields[3]);
			if (basePointsEff2 != 0)
			{
				basePointsEff2++;
			}
			int basePointsEff3 = int.Parse(fields[4]);
			if (basePointsEff3 != 0)
			{
				basePointsEff3++;
			}
			GameData.SpellEffectPoints.Add(spellId, new List<float> { basePointsEff1, basePointsEff2, basePointsEff3 });
		}
	}

	public static void LoadStackableAuras()
	{
		if (LegacyVersion.ExpansionVersion > 2)
		{
			return;
		}
		string path = Path.Combine("CSV", $"StackableAuras{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.StackableAuras.Add(spellId);
		}
	}

	public static void LoadMountAuras()
	{
		if (LegacyVersion.ExpansionVersion > 1)
		{
			return;
		}
		string path = Path.Combine("CSV", "MountAuras.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.MountAuras.Add(spellId);
		}
	}

	public static void LoadMeleeSpells()
	{
		string path = Path.Combine("CSV", $"MeleeSpells{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.NextMeleeSpells.Add(spellId);
		}
	}

	public static void LoadAutoRepeatSpells()
	{
		string path = Path.Combine("CSV", $"AutoRepeatSpells{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.AutoRepeatSpells.Add(spellId);
		}
	}

	public static void LoadAuraSpells()
	{
		string path = Path.Combine("CSV", $"AuraSpells{LegacyVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint spellId = uint.Parse(fields[0]);
			GameData.AuraSpells.Add(spellId);
		}
	}

	public static void LoadTaxiPaths()
	{
		string path = Path.Combine("CSV", $"TaxiPath{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			TaxiPath taxiPath = new TaxiPath();
			taxiPath.Id = uint.Parse(fields[0]);
			taxiPath.From = uint.Parse(fields[1]);
			taxiPath.To = uint.Parse(fields[2]);
			taxiPath.Cost = int.Parse(fields[3]);
			GameData.TaxiPaths.Add(counter, taxiPath);
			counter++;
		}
	}

	public static void LoadTaxiPathNodesGraph()
	{
		Dictionary<uint, TaxiNode> TaxiNodes = new Dictionary<uint, TaxiNode>();
		string pathNodes = Path.Combine("CSV", $"TaxiNodes{ModernVersion.ExpansionVersion}.csv");
		using (TextFieldParser csvParser = new TextFieldParser(pathNodes))
		{
			csvParser.CommentTokens = new string[1] { "#" };
			csvParser.SetDelimiters(",");
			csvParser.HasFieldsEnclosedInQuotes = false;
			csvParser.ReadLine();
			while (!csvParser.EndOfData)
			{
				string[] fields = csvParser.ReadFields();
				TaxiNode taxiNode = new TaxiNode();
				taxiNode.Id = uint.Parse(fields[0]);
				taxiNode.mapId = uint.Parse(fields[1]);
				taxiNode.x = float.Parse(fields[2]);
				taxiNode.y = float.Parse(fields[3]);
				taxiNode.z = float.Parse(fields[4]);
				TaxiNodes.Add(taxiNode.Id, taxiNode);
			}
		}
		Dictionary<uint, TaxiPathNode> TaxiPathNodes = new Dictionary<uint, TaxiPathNode>();
		string pathPathNodes = Path.Combine("CSV", $"TaxiPathNode{ModernVersion.ExpansionVersion}.csv");
		using (TextFieldParser csvParser2 = new TextFieldParser(pathPathNodes))
		{
			csvParser2.CommentTokens = new string[1] { "#" };
			csvParser2.SetDelimiters(",");
			csvParser2.HasFieldsEnclosedInQuotes = true;
			csvParser2.ReadLine();
			while (!csvParser2.EndOfData)
			{
				string[] fields2 = csvParser2.ReadFields();
				TaxiPathNode taxiPathNode = new TaxiPathNode();
				taxiPathNode.Id = uint.Parse(fields2[0]);
				taxiPathNode.pathId = uint.Parse(fields2[1]);
				taxiPathNode.nodeIndex = uint.Parse(fields2[2]);
				taxiPathNode.mapId = uint.Parse(fields2[3]);
				taxiPathNode.x = float.Parse(fields2[4]);
				taxiPathNode.y = float.Parse(fields2[5]);
				taxiPathNode.z = float.Parse(fields2[6]);
				taxiPathNode.flags = uint.Parse(fields2[7]);
				taxiPathNode.delay = uint.Parse(fields2[8]);
				TaxiPathNodes.Add(taxiPathNode.Id, taxiPathNode);
			}
		}
		for (uint i = 0u; i < GameData.TaxiPaths.Count; i++)
		{
			if (!GameData.TaxiPaths.ContainsKey(i))
			{
				continue;
			}
			float dist = 0f;
			TaxiPath taxiPath = GameData.TaxiPaths[i];
			TaxiNode nodeFrom = TaxiNodes[GameData.TaxiPaths[i].From];
			TaxiNode nodeTo = TaxiNodes[GameData.TaxiPaths[i].To];
			if ((nodeFrom.x == 0f && nodeFrom.x == 0f && nodeFrom.z == 0f) || (nodeTo.x == 0f && nodeTo.x == 0f && nodeTo.z == 0f))
			{
				continue;
			}
			HashSet<uint> pathNodeList = new HashSet<uint>();
			foreach (KeyValuePair<uint, TaxiPathNode> item in TaxiPathNodes)
			{
				TaxiPathNode pNode = item.Value;
				if (pNode.pathId == taxiPath.Id)
				{
					pathNodeList.Add(pNode.Id);
				}
			}
			IEnumerable<uint> query = pathNodeList.OrderBy((uint node) => TaxiPathNodes[node].nodeIndex);
			uint curNode = 0u;
			foreach (uint itr in query)
			{
				TaxiPathNode pNode2 = TaxiPathNodes[itr];
				if (pNode2.nodeIndex == 0)
				{
					dist += (float)Math.Sqrt(Math.Pow(nodeFrom.x - pNode2.x, 2.0) + Math.Pow(nodeFrom.y - pNode2.y, 2.0));
				}
				else if (curNode == 0)
				{
					curNode = pNode2.Id;
				}
				else if (curNode != 0)
				{
					TaxiPathNode prevNode = TaxiPathNodes[curNode];
					curNode = pNode2.Id;
					if (prevNode.mapId == pNode2.mapId)
					{
						dist += (float)Math.Sqrt(Math.Pow(prevNode.x - pNode2.x, 2.0) + Math.Pow(prevNode.y - pNode2.y, 2.0));
					}
				}
			}
			if (curNode != 0)
			{
				TaxiPathNode lastNode = TaxiPathNodes[curNode];
				dist += (float)Math.Sqrt(Math.Pow(nodeTo.x - lastNode.x, 2.0) + Math.Pow(nodeTo.y - lastNode.y, 2.0));
			}
			GameData.TaxiNodesGraph[GameData.TaxiPaths[i].From, GameData.TaxiPaths[i].To] = ((dist > 0f) ? ((int)dist) : 0);
		}
	}

	public static void LoadQuestBits()
	{
		string path = Path.Combine("CSV", $"QuestV2_{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		while (!csvParser.EndOfData)
		{
			string[] fields = csvParser.ReadFields();
			uint questId = uint.Parse(fields[0]);
			if (!fields[1].StartsWith("-"))
			{
				uint uniqueBitFlag = uint.Parse(fields[1]);
				GameData.QuestBits.Add(questId, uniqueBitFlag);
			}
		}
	}

	public static void LoadHotfixes()
	{
		GameData.LoadAreaTriggerHotfixes();
		GameData.LoadSkillLineHotfixes();
		GameData.LoadSkillRaceClassInfoHotfixes();
		GameData.LoadSkillLineAbilityHotfixes();
		GameData.LoadSpellHotfixes();
		GameData.LoadSpellNameHotfixes();
		GameData.LoadSpellLevelsHotfixes();
		GameData.LoadSpellAuraOptionsHotfixes();
		GameData.LoadSpellMiscHotfixes();
		GameData.LoadSpellEffectHotfixes();
		GameData.LoadSpellXSpellVisualHotfixes();
		GameData.LoadItemSparseHotfixes();
		GameData.LoadItemHotfixes();
		GameData.LoadItemEffectHotfixes();
		GameData.LoadItemDisplayInfoHotfixes();
		GameData.LoadCreatureDisplayInfoHotfixes();
		GameData.LoadCreatureDisplayInfoExtraHotfixes();
		GameData.LoadCreatureDisplayInfoOptionHotfixes();
	}

	public static void LoadAreaTriggerHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"AreaTrigger{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			AreaTrigger at = new AreaTrigger();
			at.Message = fields[0];
			at.PositionX = float.Parse(fields[1]);
			at.PositionY = float.Parse(fields[2]);
			at.PositionZ = float.Parse(fields[3]);
			at.Id = uint.Parse(fields[4]);
			at.MapId = ushort.Parse(fields[5]);
			at.PhaseUseFlags = byte.Parse(fields[6]);
			at.PhaseId = ushort.Parse(fields[7]);
			at.PhaseGroupId = ushort.Parse(fields[8]);
			at.Radius = float.Parse(fields[9]);
			at.BoxLength = float.Parse(fields[10]);
			at.BoxWidth = float.Parse(fields[11]);
			at.BoxHeight = float.Parse(fields[12]);
			at.BoxYaw = float.Parse(fields[13]);
			at.ShapeType = byte.Parse(fields[14]);
			at.ShapeId = ushort.Parse(fields[15]);
			at.ActionSetId = ushort.Parse(fields[16]);
			at.Flags = byte.Parse(fields[17]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.AreaTrigger;
			record.HotfixId = 100000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = at.Id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteCString(at.Message);
			record.HotfixContent.WriteFloat(at.PositionX);
			record.HotfixContent.WriteFloat(at.PositionY);
			record.HotfixContent.WriteFloat(at.PositionZ);
			record.HotfixContent.WriteUInt32(at.Id);
			record.HotfixContent.WriteUInt16(at.MapId);
			record.HotfixContent.WriteUInt8(at.PhaseUseFlags);
			record.HotfixContent.WriteUInt16(at.PhaseId);
			record.HotfixContent.WriteUInt16(at.PhaseGroupId);
			record.HotfixContent.WriteFloat(at.Radius);
			record.HotfixContent.WriteFloat(at.BoxLength);
			record.HotfixContent.WriteFloat(at.BoxWidth);
			record.HotfixContent.WriteFloat(at.BoxHeight);
			record.HotfixContent.WriteFloat(at.BoxYaw);
			record.HotfixContent.WriteUInt8(at.ShapeType);
			record.HotfixContent.WriteUInt16(at.ShapeId);
			record.HotfixContent.WriteUInt16(at.ActionSetId);
			record.HotfixContent.WriteUInt8(at.Flags);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSkillLineHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SkillLine{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			string displayName = fields[0];
			string alternateVerb = fields[1];
			string description = fields[2];
			string hordeDisplayName = fields[3];
			string neutralDisplayName = fields[4];
			uint id = uint.Parse(fields[5]);
			byte categoryID = byte.Parse(fields[6]);
			uint spellIconFileID = uint.Parse(fields[7]);
			byte canLink = byte.Parse(fields[8]);
			uint parentSkillLineID = uint.Parse(fields[9]);
			uint parentTierIndex = uint.Parse(fields[10]);
			ushort flags = ushort.Parse(fields[11]);
			uint spellBookSpellID = uint.Parse(fields[12]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SkillLine;
			record.HotfixId = 110000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteCString(displayName);
			record.HotfixContent.WriteCString(alternateVerb);
			record.HotfixContent.WriteCString(description);
			record.HotfixContent.WriteCString(hordeDisplayName);
			record.HotfixContent.WriteCString(neutralDisplayName);
			record.HotfixContent.WriteUInt32(id);
			record.HotfixContent.WriteUInt8(categoryID);
			record.HotfixContent.WriteUInt32(spellIconFileID);
			record.HotfixContent.WriteUInt8(canLink);
			record.HotfixContent.WriteUInt32(parentSkillLineID);
			record.HotfixContent.WriteUInt32(parentTierIndex);
			record.HotfixContent.WriteUInt16(flags);
			record.HotfixContent.WriteUInt32(spellBookSpellID);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSkillRaceClassInfoHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SkillRaceClassInfo{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			ulong raceMask = ulong.Parse(fields[1]);
			ushort skillId = ushort.Parse(fields[2]);
			uint classMask = uint.Parse(fields[3]);
			ushort flags = ushort.Parse(fields[4]);
			byte availability = byte.Parse(fields[5]);
			byte minLevel = byte.Parse(fields[6]);
			ushort skillTierId = ushort.Parse(fields[7]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SkillRaceClassInfo;
			record.HotfixId = 120000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt64(raceMask);
			record.HotfixContent.WriteUInt16(skillId);
			record.HotfixContent.WriteUInt32(classMask);
			record.HotfixContent.WriteUInt16(flags);
			record.HotfixContent.WriteUInt8(availability);
			record.HotfixContent.WriteUInt8(minLevel);
			record.HotfixContent.WriteUInt16(skillTierId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSkillLineAbilityHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SkillLineAbility{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			ulong raceMask = ulong.Parse(fields[0]);
			uint id = uint.Parse(fields[1]);
			ushort skillId = ushort.Parse(fields[2]);
			uint spellId = uint.Parse(fields[3]);
			ushort minSkillLineRank = ushort.Parse(fields[4]);
			uint classMask = uint.Parse(fields[5]);
			uint supercedesSpellId = uint.Parse(fields[6]);
			byte acquireMethod = byte.Parse(fields[7]);
			ushort trivialSkillLineRankHigh = ushort.Parse(fields[8]);
			ushort trivialSkillLineRankLow = ushort.Parse(fields[9]);
			byte flags = byte.Parse(fields[10]);
			byte numSkillUps = byte.Parse(fields[11]);
			ushort uniqueBit = ushort.Parse(fields[12]);
			ushort tradeSkillCategoryId = ushort.Parse(fields[13]);
			ushort skillUpSkillLineId = ushort.Parse(fields[14]);
			uint characterPoints1 = uint.Parse(fields[15]);
			uint characterPoints2 = uint.Parse(fields[16]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SkillLineAbility;
			record.HotfixId = 130000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt64(raceMask);
			record.HotfixContent.WriteUInt32(id);
			record.HotfixContent.WriteUInt16(skillId);
			record.HotfixContent.WriteUInt32(spellId);
			record.HotfixContent.WriteUInt16(minSkillLineRank);
			record.HotfixContent.WriteUInt32(classMask);
			record.HotfixContent.WriteUInt32(supercedesSpellId);
			record.HotfixContent.WriteUInt8(acquireMethod);
			record.HotfixContent.WriteUInt16(trivialSkillLineRankHigh);
			record.HotfixContent.WriteUInt16(trivialSkillLineRankLow);
			record.HotfixContent.WriteUInt8(flags);
			record.HotfixContent.WriteUInt8(numSkillUps);
			record.HotfixContent.WriteUInt16(uniqueBit);
			record.HotfixContent.WriteUInt16(tradeSkillCategoryId);
			record.HotfixContent.WriteUInt16(skillUpSkillLineId);
			record.HotfixContent.WriteUInt32(characterPoints1);
			record.HotfixContent.WriteUInt32(characterPoints2);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"Spell{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			string nameSubText = fields[1];
			string description = fields[2];
			string auraDescription = fields[3];
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.Spell;
			record.HotfixId = 140000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteCString(nameSubText);
			record.HotfixContent.WriteCString(description);
			record.HotfixContent.WriteCString(auraDescription);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellNameHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellName{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			string name = fields[1];
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellName;
			record.HotfixId = 150000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteCString(name);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellLevelsHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellLevels{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte difficultyId = byte.Parse(fields[1]);
			ushort baseLevel = ushort.Parse(fields[2]);
			ushort maxLevel = ushort.Parse(fields[3]);
			ushort spellLevel = ushort.Parse(fields[4]);
			byte maxPassiveAuraLevel = byte.Parse(fields[5]);
			uint spellId = uint.Parse(fields[6]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellLevels;
			record.HotfixId = 160000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt8(difficultyId);
			record.HotfixContent.WriteUInt16(baseLevel);
			record.HotfixContent.WriteUInt16(maxLevel);
			record.HotfixContent.WriteUInt16(spellLevel);
			record.HotfixContent.WriteUInt8(maxPassiveAuraLevel);
			record.HotfixContent.WriteUInt32(spellId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellAuraOptionsHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellAuraOptions{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte difficultyId = byte.Parse(fields[1]);
			uint cumulatievAura = uint.Parse(fields[2]);
			uint procCategoryRecovery = uint.Parse(fields[3]);
			byte procChance = byte.Parse(fields[4]);
			uint procCharges = uint.Parse(fields[5]);
			ushort spellProcsPerMinuteId = ushort.Parse(fields[6]);
			uint procTypeMask0 = uint.Parse(fields[7]);
			uint procTypeMask1 = uint.Parse(fields[8]);
			uint spellId = uint.Parse(fields[9]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellAuraOptions;
			record.HotfixId = 170000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt8(difficultyId);
			record.HotfixContent.WriteUInt32(cumulatievAura);
			record.HotfixContent.WriteUInt32(procCategoryRecovery);
			record.HotfixContent.WriteUInt8(procChance);
			record.HotfixContent.WriteUInt32(procCharges);
			record.HotfixContent.WriteUInt16(spellProcsPerMinuteId);
			record.HotfixContent.WriteUInt32(procTypeMask0);
			record.HotfixContent.WriteUInt32(procTypeMask1);
			record.HotfixContent.WriteUInt32(spellId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellMiscHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellMisc{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte difficultyId = byte.Parse(fields[1]);
			ushort castingTimeIndex = ushort.Parse(fields[2]);
			ushort durationIndex = ushort.Parse(fields[3]);
			ushort rangeIndex = ushort.Parse(fields[4]);
			byte schoolMask = byte.Parse(fields[5]);
			float speed = float.Parse(fields[6]);
			float launchDelay = float.Parse(fields[7]);
			float minDuration = float.Parse(fields[8]);
			uint spellIconFileDataId = uint.Parse(fields[9]);
			uint activeIconFileDataId = uint.Parse(fields[10]);
			uint attributes1 = uint.Parse(fields[11]);
			uint attributes2 = uint.Parse(fields[12]);
			uint attributes3 = uint.Parse(fields[13]);
			uint attributes4 = uint.Parse(fields[14]);
			uint attributes5 = uint.Parse(fields[15]);
			uint attributes6 = uint.Parse(fields[16]);
			uint attributes7 = uint.Parse(fields[17]);
			uint attributes8 = uint.Parse(fields[18]);
			uint attributes9 = uint.Parse(fields[19]);
			uint attributes10 = uint.Parse(fields[20]);
			uint attributes11 = uint.Parse(fields[21]);
			uint attributes12 = uint.Parse(fields[22]);
			uint attributes13 = uint.Parse(fields[23]);
			uint attributes14 = uint.Parse(fields[24]);
			uint spellId = uint.Parse(fields[25]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellMisc;
			record.HotfixId = 180000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt8(difficultyId);
			record.HotfixContent.WriteUInt16(castingTimeIndex);
			record.HotfixContent.WriteUInt16(durationIndex);
			record.HotfixContent.WriteUInt16(rangeIndex);
			record.HotfixContent.WriteUInt8(schoolMask);
			record.HotfixContent.WriteFloat(speed);
			record.HotfixContent.WriteFloat(launchDelay);
			record.HotfixContent.WriteFloat(minDuration);
			record.HotfixContent.WriteUInt32(spellIconFileDataId);
			record.HotfixContent.WriteUInt32(activeIconFileDataId);
			record.HotfixContent.WriteUInt32(attributes1);
			record.HotfixContent.WriteUInt32(attributes2);
			record.HotfixContent.WriteUInt32(attributes3);
			record.HotfixContent.WriteUInt32(attributes4);
			record.HotfixContent.WriteUInt32(attributes5);
			record.HotfixContent.WriteUInt32(attributes6);
			record.HotfixContent.WriteUInt32(attributes7);
			record.HotfixContent.WriteUInt32(attributes8);
			record.HotfixContent.WriteUInt32(attributes9);
			record.HotfixContent.WriteUInt32(attributes10);
			record.HotfixContent.WriteUInt32(attributes11);
			record.HotfixContent.WriteUInt32(attributes12);
			record.HotfixContent.WriteUInt32(attributes13);
			record.HotfixContent.WriteUInt32(attributes14);
			record.HotfixContent.WriteUInt32(spellId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellEffectHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellEffect{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			uint difficultyId = uint.Parse(fields[1]);
			uint effectIndex = uint.Parse(fields[2]);
			uint effect = uint.Parse(fields[3]);
			float effectAmplitude = float.Parse(fields[4]);
			uint effectAttributes = uint.Parse(fields[5]);
			short effectAura = short.Parse(fields[6]);
			int effectAuraPeriod = int.Parse(fields[7]);
			int effectBasePoints = int.Parse(fields[8]);
			float effectBonusCoefficient = float.Parse(fields[9]);
			float effectChainAmplitude = float.Parse(fields[10]);
			int effectChainTargets = int.Parse(fields[11]);
			int effectDieSides = int.Parse(fields[12]);
			int effectItemType = int.Parse(fields[13]);
			int effectMechanic = int.Parse(fields[14]);
			float effectPointsPerResource = float.Parse(fields[15]);
			float effectPosFacing = float.Parse(fields[16]);
			float effectRealPointsPerLevel = float.Parse(fields[17]);
			int EffectTriggerSpell = int.Parse(fields[18]);
			float bonusCoefficientFromAP = float.Parse(fields[19]);
			float pvpMultiplier = float.Parse(fields[20]);
			float coefficient = float.Parse(fields[21]);
			float variance = float.Parse(fields[22]);
			float resourceCoefficient = float.Parse(fields[23]);
			float groupSizeBasePointsCoefficient = float.Parse(fields[24]);
			int effectMiscValue1 = int.Parse(fields[25]);
			int effectMiscValue2 = int.Parse(fields[26]);
			uint effectRadiusIndex1 = uint.Parse(fields[27]);
			uint effectRadiusIndex2 = uint.Parse(fields[28]);
			int effectSpellClassMask1 = int.Parse(fields[29]);
			int effectSpellClassMask2 = int.Parse(fields[30]);
			int effectSpellClassMask3 = int.Parse(fields[31]);
			int effectSpellClassMask4 = int.Parse(fields[32]);
			short implicitTarget1 = short.Parse(fields[33]);
			short implicitTarget2 = short.Parse(fields[34]);
			uint spellId = uint.Parse(fields[35]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellEffect;
			record.HotfixId = 190000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt32(difficultyId);
			record.HotfixContent.WriteUInt32(effectIndex);
			record.HotfixContent.WriteUInt32(effect);
			record.HotfixContent.WriteFloat(effectAmplitude);
			record.HotfixContent.WriteUInt32(effectAttributes);
			record.HotfixContent.WriteInt16(effectAura);
			record.HotfixContent.WriteInt32(effectAuraPeriod);
			record.HotfixContent.WriteInt32(effectBasePoints);
			record.HotfixContent.WriteFloat(effectBonusCoefficient);
			record.HotfixContent.WriteFloat(effectChainAmplitude);
			record.HotfixContent.WriteInt32(effectChainTargets);
			record.HotfixContent.WriteInt32(effectDieSides);
			record.HotfixContent.WriteInt32(effectItemType);
			record.HotfixContent.WriteInt32(effectMechanic);
			record.HotfixContent.WriteFloat(effectPointsPerResource);
			record.HotfixContent.WriteFloat(effectPosFacing);
			record.HotfixContent.WriteFloat(effectRealPointsPerLevel);
			record.HotfixContent.WriteInt32(EffectTriggerSpell);
			record.HotfixContent.WriteFloat(bonusCoefficientFromAP);
			record.HotfixContent.WriteFloat(pvpMultiplier);
			record.HotfixContent.WriteFloat(coefficient);
			record.HotfixContent.WriteFloat(variance);
			record.HotfixContent.WriteFloat(resourceCoefficient);
			record.HotfixContent.WriteFloat(groupSizeBasePointsCoefficient);
			record.HotfixContent.WriteInt32(effectMiscValue1);
			record.HotfixContent.WriteInt32(effectMiscValue2);
			record.HotfixContent.WriteUInt32(effectRadiusIndex1);
			record.HotfixContent.WriteUInt32(effectRadiusIndex2);
			record.HotfixContent.WriteInt32(effectSpellClassMask1);
			record.HotfixContent.WriteInt32(effectSpellClassMask2);
			record.HotfixContent.WriteInt32(effectSpellClassMask3);
			record.HotfixContent.WriteInt32(effectSpellClassMask4);
			record.HotfixContent.WriteInt16(implicitTarget1);
			record.HotfixContent.WriteInt16(implicitTarget2);
			record.HotfixContent.WriteUInt32(spellId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadSpellXSpellVisualHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"SpellXSpellVisual{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte difficultyId = byte.Parse(fields[1]);
			uint spellVisualId = uint.Parse(fields[2]);
			float probability = float.Parse(fields[3]);
			byte flags = byte.Parse(fields[4]);
			byte priority = byte.Parse(fields[5]);
			int spellIconFileId = int.Parse(fields[6]);
			int activeIconFileId = int.Parse(fields[7]);
			ushort viewerUnitConditionId = ushort.Parse(fields[8]);
			uint viewerPlayerConditionId = uint.Parse(fields[9]);
			ushort casterUnitConditionId = ushort.Parse(fields[10]);
			uint casterPlayerConditionId = uint.Parse(fields[11]);
			uint spellId = uint.Parse(fields[12]);
			if (GameData.SpellVisuals.ContainsKey(spellId))
			{
				GameData.SpellVisuals[spellId] = id;
			}
			else
			{
				GameData.SpellVisuals.Add(spellId, id);
			}
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.SpellXSpellVisual;
			record.HotfixId = 200000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt32(id);
			record.HotfixContent.WriteUInt8(difficultyId);
			record.HotfixContent.WriteUInt32(spellVisualId);
			record.HotfixContent.WriteFloat(probability);
			record.HotfixContent.WriteUInt8(flags);
			record.HotfixContent.WriteUInt8(priority);
			record.HotfixContent.WriteInt32(spellIconFileId);
			record.HotfixContent.WriteInt32(activeIconFileId);
			record.HotfixContent.WriteUInt16(viewerUnitConditionId);
			record.HotfixContent.WriteUInt32(viewerPlayerConditionId);
			record.HotfixContent.WriteUInt16(casterUnitConditionId);
			record.HotfixContent.WriteUInt32(casterPlayerConditionId);
			record.HotfixContent.WriteUInt32(spellId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadItemSparseHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"ItemSparse{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = true;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			long allowableRace = long.Parse(fields[1]);
			string description = fields[2];
			string name4 = fields[3];
			string name5 = fields[4];
			string name6 = fields[5];
			string name7 = fields[6];
			float dmgVariance = float.Parse(fields[7]);
			uint durationInInventory = uint.Parse(fields[8]);
			float qualityModifier = float.Parse(fields[9]);
			uint bagFamily = uint.Parse(fields[10]);
			float rangeMod = float.Parse(fields[11]);
			float statPercentageOfSocket1 = float.Parse(fields[12]);
			float statPercentageOfSocket2 = float.Parse(fields[13]);
			float statPercentageOfSocket3 = float.Parse(fields[14]);
			float statPercentageOfSocket4 = float.Parse(fields[15]);
			float statPercentageOfSocket5 = float.Parse(fields[16]);
			float statPercentageOfSocket6 = float.Parse(fields[17]);
			float statPercentageOfSocket7 = float.Parse(fields[18]);
			float statPercentageOfSocket8 = float.Parse(fields[19]);
			float statPercentageOfSocket9 = float.Parse(fields[20]);
			float statPercentageOfSocket10 = float.Parse(fields[21]);
			int statPercentEditor1 = int.Parse(fields[22]);
			int statPercentEditor2 = int.Parse(fields[23]);
			int statPercentEditor3 = int.Parse(fields[24]);
			int statPercentEditor4 = int.Parse(fields[25]);
			int statPercentEditor5 = int.Parse(fields[26]);
			int statPercentEditor6 = int.Parse(fields[27]);
			int statPercentEditor7 = int.Parse(fields[28]);
			int statPercentEditor8 = int.Parse(fields[29]);
			int statPercentEditor9 = int.Parse(fields[30]);
			int statPercentEditor10 = int.Parse(fields[31]);
			int stackable = int.Parse(fields[32]);
			int maxCount = int.Parse(fields[33]);
			uint requiredAbility = uint.Parse(fields[34]);
			uint sellPrice = uint.Parse(fields[35]);
			uint buyPrice = uint.Parse(fields[36]);
			uint vendorStackCount = uint.Parse(fields[37]);
			float priceVariance = float.Parse(fields[38]);
			float priceRandomValue = float.Parse(fields[39]);
			int flags1 = int.Parse(fields[40]);
			int flags2 = int.Parse(fields[41]);
			int flags3 = int.Parse(fields[42]);
			int flags4 = int.Parse(fields[43]);
			int oppositeFactionItemId = int.Parse(fields[44]);
			uint maxDurability = uint.Parse(fields[45]);
			ushort itemNameDescriptionId = ushort.Parse(fields[46]);
			ushort requiredTransmogHoliday = ushort.Parse(fields[47]);
			ushort requiredHoliday = ushort.Parse(fields[48]);
			ushort limitCategory = ushort.Parse(fields[49]);
			ushort gemProperties = ushort.Parse(fields[50]);
			ushort socketMatchEnchantmentId = ushort.Parse(fields[51]);
			ushort totemCategoryId = ushort.Parse(fields[52]);
			ushort instanceBound = ushort.Parse(fields[53]);
			ushort zoneBound1 = ushort.Parse(fields[54]);
			ushort zoneBound2 = ushort.Parse(fields[55]);
			ushort itemSet = ushort.Parse(fields[56]);
			ushort lockId = ushort.Parse(fields[57]);
			ushort startQuestId = ushort.Parse(fields[58]);
			ushort pageText = ushort.Parse(fields[59]);
			ushort delay = ushort.Parse(fields[60]);
			ushort requiredReputationId = ushort.Parse(fields[61]);
			ushort requiredSkillRank = ushort.Parse(fields[62]);
			ushort requiredSkill = ushort.Parse(fields[63]);
			ushort itemLevel = ushort.Parse(fields[64]);
			short allowableClass = short.Parse(fields[65]);
			ushort itemRandomSuffixGroupId = ushort.Parse(fields[66]);
			ushort randomProperty = ushort.Parse(fields[67]);
			ushort damageMin1 = ushort.Parse(fields[68]);
			ushort damageMin2 = ushort.Parse(fields[69]);
			ushort damageMin3 = ushort.Parse(fields[70]);
			ushort damageMin4 = ushort.Parse(fields[71]);
			ushort damageMin5 = ushort.Parse(fields[72]);
			ushort damageMax1 = ushort.Parse(fields[73]);
			ushort damageMax2 = ushort.Parse(fields[74]);
			ushort damageMax3 = ushort.Parse(fields[75]);
			ushort damageMax4 = ushort.Parse(fields[76]);
			ushort damageMax5 = ushort.Parse(fields[77]);
			short armor = short.Parse(fields[78]);
			short holyResistance = short.Parse(fields[79]);
			short fireResistance = short.Parse(fields[80]);
			short natureResistance = short.Parse(fields[81]);
			short frostResistance = short.Parse(fields[82]);
			short shadowResistance = short.Parse(fields[83]);
			short arcaneResistance = short.Parse(fields[84]);
			ushort scalingStatDistributionId = ushort.Parse(fields[85]);
			byte expansionId = byte.Parse(fields[86]);
			byte artifactId = byte.Parse(fields[87]);
			byte spellWeight = byte.Parse(fields[88]);
			byte spellWeightCategory = byte.Parse(fields[89]);
			byte socketType1 = byte.Parse(fields[90]);
			byte socketType2 = byte.Parse(fields[91]);
			byte socketType3 = byte.Parse(fields[92]);
			byte sheatheType = byte.Parse(fields[93]);
			byte material = byte.Parse(fields[94]);
			byte pageMaterial = byte.Parse(fields[95]);
			byte pageLanguage = byte.Parse(fields[96]);
			byte bonding = byte.Parse(fields[97]);
			byte damageType = byte.Parse(fields[98]);
			sbyte statType1 = sbyte.Parse(fields[99]);
			sbyte statType2 = sbyte.Parse(fields[100]);
			sbyte statType3 = sbyte.Parse(fields[101]);
			sbyte statType4 = sbyte.Parse(fields[102]);
			sbyte statType5 = sbyte.Parse(fields[103]);
			sbyte statType6 = sbyte.Parse(fields[104]);
			sbyte statType7 = sbyte.Parse(fields[105]);
			sbyte statType8 = sbyte.Parse(fields[106]);
			sbyte statType9 = sbyte.Parse(fields[107]);
			sbyte statType10 = sbyte.Parse(fields[108]);
			byte containerSlots = byte.Parse(fields[109]);
			byte requiredReputationRank = byte.Parse(fields[110]);
			byte requiredCityRank = byte.Parse(fields[111]);
			byte requiredHonorRank = byte.Parse(fields[112]);
			byte inventoryType = byte.Parse(fields[113]);
			byte overallQualityId = byte.Parse(fields[114]);
			byte ammoType = byte.Parse(fields[115]);
			sbyte statValue1 = sbyte.Parse(fields[116]);
			sbyte statValue2 = sbyte.Parse(fields[117]);
			sbyte statValue3 = sbyte.Parse(fields[118]);
			sbyte statValue4 = sbyte.Parse(fields[119]);
			sbyte statValue5 = sbyte.Parse(fields[120]);
			sbyte statValue6 = sbyte.Parse(fields[121]);
			sbyte statValue7 = sbyte.Parse(fields[122]);
			sbyte statValue8 = sbyte.Parse(fields[123]);
			sbyte statValue9 = sbyte.Parse(fields[124]);
			sbyte statValue10 = sbyte.Parse(fields[125]);
			sbyte requiredLevel = sbyte.Parse(fields[126]);
			HotfixRecord record = new HotfixRecord();
			record.Status = HotfixStatus.Valid;
			record.TableHash = DB2Hash.ItemSparse;
			record.HotfixId = 220000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.HotfixContent.WriteInt64(allowableRace);
			record.HotfixContent.WriteCString(description);
			record.HotfixContent.WriteCString(name4);
			record.HotfixContent.WriteCString(name5);
			record.HotfixContent.WriteCString(name6);
			record.HotfixContent.WriteCString(name7);
			record.HotfixContent.WriteFloat(dmgVariance);
			record.HotfixContent.WriteUInt32(durationInInventory);
			record.HotfixContent.WriteFloat(qualityModifier);
			record.HotfixContent.WriteUInt32(bagFamily);
			record.HotfixContent.WriteFloat(rangeMod);
			record.HotfixContent.WriteFloat(statPercentageOfSocket1);
			record.HotfixContent.WriteFloat(statPercentageOfSocket2);
			record.HotfixContent.WriteFloat(statPercentageOfSocket3);
			record.HotfixContent.WriteFloat(statPercentageOfSocket4);
			record.HotfixContent.WriteFloat(statPercentageOfSocket5);
			record.HotfixContent.WriteFloat(statPercentageOfSocket6);
			record.HotfixContent.WriteFloat(statPercentageOfSocket7);
			record.HotfixContent.WriteFloat(statPercentageOfSocket8);
			record.HotfixContent.WriteFloat(statPercentageOfSocket9);
			record.HotfixContent.WriteFloat(statPercentageOfSocket10);
			record.HotfixContent.WriteInt32(statPercentEditor1);
			record.HotfixContent.WriteInt32(statPercentEditor2);
			record.HotfixContent.WriteInt32(statPercentEditor3);
			record.HotfixContent.WriteInt32(statPercentEditor4);
			record.HotfixContent.WriteInt32(statPercentEditor5);
			record.HotfixContent.WriteInt32(statPercentEditor6);
			record.HotfixContent.WriteInt32(statPercentEditor7);
			record.HotfixContent.WriteInt32(statPercentEditor8);
			record.HotfixContent.WriteInt32(statPercentEditor9);
			record.HotfixContent.WriteInt32(statPercentEditor10);
			record.HotfixContent.WriteInt32(stackable);
			record.HotfixContent.WriteInt32(maxCount);
			record.HotfixContent.WriteUInt32(requiredAbility);
			record.HotfixContent.WriteUInt32(sellPrice);
			record.HotfixContent.WriteUInt32(buyPrice);
			record.HotfixContent.WriteUInt32(vendorStackCount);
			record.HotfixContent.WriteFloat(priceVariance);
			record.HotfixContent.WriteFloat(priceRandomValue);
			record.HotfixContent.WriteInt32(flags1);
			record.HotfixContent.WriteInt32(flags2);
			record.HotfixContent.WriteInt32(flags3);
			record.HotfixContent.WriteInt32(flags4);
			record.HotfixContent.WriteInt32(oppositeFactionItemId);
			record.HotfixContent.WriteUInt32(maxDurability);
			record.HotfixContent.WriteUInt16(itemNameDescriptionId);
			record.HotfixContent.WriteUInt16(requiredTransmogHoliday);
			record.HotfixContent.WriteUInt16(requiredHoliday);
			record.HotfixContent.WriteUInt16(limitCategory);
			record.HotfixContent.WriteUInt16(gemProperties);
			record.HotfixContent.WriteUInt16(socketMatchEnchantmentId);
			record.HotfixContent.WriteUInt16(totemCategoryId);
			record.HotfixContent.WriteUInt16(instanceBound);
			record.HotfixContent.WriteUInt16(zoneBound1);
			record.HotfixContent.WriteUInt16(zoneBound2);
			record.HotfixContent.WriteUInt16(itemSet);
			record.HotfixContent.WriteUInt16(lockId);
			record.HotfixContent.WriteUInt16(startQuestId);
			record.HotfixContent.WriteUInt16(pageText);
			record.HotfixContent.WriteUInt16(delay);
			record.HotfixContent.WriteUInt16(requiredReputationId);
			record.HotfixContent.WriteUInt16(requiredSkillRank);
			record.HotfixContent.WriteUInt16(requiredSkill);
			record.HotfixContent.WriteUInt16(itemLevel);
			record.HotfixContent.WriteInt16(allowableClass);
			record.HotfixContent.WriteUInt16(itemRandomSuffixGroupId);
			record.HotfixContent.WriteUInt16(randomProperty);
			record.HotfixContent.WriteUInt16(damageMin1);
			record.HotfixContent.WriteUInt16(damageMin2);
			record.HotfixContent.WriteUInt16(damageMin3);
			record.HotfixContent.WriteUInt16(damageMin4);
			record.HotfixContent.WriteUInt16(damageMin5);
			record.HotfixContent.WriteUInt16(damageMax1);
			record.HotfixContent.WriteUInt16(damageMax2);
			record.HotfixContent.WriteUInt16(damageMax3);
			record.HotfixContent.WriteUInt16(damageMax4);
			record.HotfixContent.WriteUInt16(damageMax5);
			record.HotfixContent.WriteInt16(armor);
			record.HotfixContent.WriteInt16(holyResistance);
			record.HotfixContent.WriteInt16(fireResistance);
			record.HotfixContent.WriteInt16(natureResistance);
			record.HotfixContent.WriteInt16(frostResistance);
			record.HotfixContent.WriteInt16(shadowResistance);
			record.HotfixContent.WriteInt16(arcaneResistance);
			record.HotfixContent.WriteUInt16(scalingStatDistributionId);
			record.HotfixContent.WriteUInt8(expansionId);
			record.HotfixContent.WriteUInt8(artifactId);
			record.HotfixContent.WriteUInt8(spellWeight);
			record.HotfixContent.WriteUInt8(spellWeightCategory);
			record.HotfixContent.WriteUInt8(socketType1);
			record.HotfixContent.WriteUInt8(socketType2);
			record.HotfixContent.WriteUInt8(socketType3);
			record.HotfixContent.WriteUInt8(sheatheType);
			record.HotfixContent.WriteUInt8(material);
			record.HotfixContent.WriteUInt8(pageMaterial);
			record.HotfixContent.WriteUInt8(pageLanguage);
			record.HotfixContent.WriteUInt8(bonding);
			record.HotfixContent.WriteUInt8(damageType);
			record.HotfixContent.WriteInt8(statType1);
			record.HotfixContent.WriteInt8(statType2);
			record.HotfixContent.WriteInt8(statType3);
			record.HotfixContent.WriteInt8(statType4);
			record.HotfixContent.WriteInt8(statType5);
			record.HotfixContent.WriteInt8(statType6);
			record.HotfixContent.WriteInt8(statType7);
			record.HotfixContent.WriteInt8(statType8);
			record.HotfixContent.WriteInt8(statType9);
			record.HotfixContent.WriteInt8(statType10);
			record.HotfixContent.WriteUInt8(containerSlots);
			record.HotfixContent.WriteUInt8(requiredReputationRank);
			record.HotfixContent.WriteUInt8(requiredCityRank);
			record.HotfixContent.WriteUInt8(requiredHonorRank);
			record.HotfixContent.WriteUInt8(inventoryType);
			record.HotfixContent.WriteUInt8(overallQualityId);
			record.HotfixContent.WriteUInt8(ammoType);
			record.HotfixContent.WriteInt8(statValue1);
			record.HotfixContent.WriteInt8(statValue2);
			record.HotfixContent.WriteInt8(statValue3);
			record.HotfixContent.WriteInt8(statValue4);
			record.HotfixContent.WriteInt8(statValue5);
			record.HotfixContent.WriteInt8(statValue6);
			record.HotfixContent.WriteInt8(statValue7);
			record.HotfixContent.WriteInt8(statValue8);
			record.HotfixContent.WriteInt8(statValue9);
			record.HotfixContent.WriteInt8(statValue10);
			record.HotfixContent.WriteInt8(requiredLevel);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void WriteItemSparseHotfix(ItemTemplate item, ByteBuffer buffer)
	{
		int[] StatValues = new int[10];
		for (int i = 0; i < item.StatsCount; i++)
		{
			StatValues[i] = item.StatValues[i];
			if (StatValues[i] > 127)
			{
				StatValues[i] = 127;
			}
			if (StatValues[i] < -127)
			{
				StatValues[i] = -127;
			}
		}
		buffer.WriteInt64(item.AllowedRaces);
		buffer.WriteCString(item.Description);
		buffer.WriteCString(item.Name[3]);
		buffer.WriteCString(item.Name[2]);
		buffer.WriteCString(item.Name[1]);
		buffer.WriteCString(item.Name[0]);
		buffer.WriteFloat(1f);
		buffer.WriteUInt32(item.Duration);
		buffer.WriteFloat(0f);
		buffer.WriteUInt32(item.BagFamily);
		buffer.WriteFloat(item.RangedMod);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteFloat(0f);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(item.MaxStackSize);
		buffer.WriteInt32(item.MaxCount);
		buffer.WriteUInt32(item.RequiredSpell);
		buffer.WriteUInt32(item.SellPrice);
		buffer.WriteUInt32(item.BuyPrice);
		buffer.WriteUInt32(item.BuyCount);
		buffer.WriteFloat(1f);
		buffer.WriteFloat(1f);
		buffer.WriteUInt32(item.Flags);
		buffer.WriteUInt32(item.FlagsExtra);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteInt32(0);
		buffer.WriteUInt32(item.MaxDurability);
		buffer.WriteUInt16(0);
		buffer.WriteUInt16(0);
		buffer.WriteUInt16((ushort)item.HolidayID);
		buffer.WriteUInt16((ushort)item.ItemLimitCategory);
		buffer.WriteUInt16((ushort)item.GemProperties);
		buffer.WriteUInt16((ushort)item.SocketBonus);
		buffer.WriteUInt16((ushort)item.TotemCategory);
		buffer.WriteUInt16((ushort)item.MapID);
		buffer.WriteUInt16((ushort)item.AreaID);
		buffer.WriteUInt16(0);
		buffer.WriteUInt16((ushort)item.ItemSet);
		buffer.WriteUInt16((ushort)item.LockId);
		buffer.WriteUInt16((ushort)item.StartQuestId);
		buffer.WriteUInt16((ushort)item.PageText);
		buffer.WriteUInt16((ushort)item.Delay);
		buffer.WriteUInt16((ushort)item.RequiredRepFaction);
		buffer.WriteUInt16((ushort)item.RequiredSkillLevel);
		buffer.WriteUInt16((ushort)item.RequiredSkillId);
		buffer.WriteUInt16((ushort)item.ItemLevel);
		buffer.WriteInt16((short)item.AllowedClasses);
		buffer.WriteUInt16((ushort)item.RandomSuffix);
		buffer.WriteUInt16((ushort)item.RandomProperty);
		buffer.WriteUInt16((ushort)item.DamageMins[0]);
		buffer.WriteUInt16((ushort)item.DamageMins[1]);
		buffer.WriteUInt16((ushort)item.DamageMins[2]);
		buffer.WriteUInt16((ushort)item.DamageMins[3]);
		buffer.WriteUInt16((ushort)item.DamageMins[4]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[0]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[1]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[2]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[3]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[4]);
		buffer.WriteInt16((short)item.Armor);
		buffer.WriteInt16((short)item.HolyResistance);
		buffer.WriteInt16((short)item.FireResistance);
		buffer.WriteInt16((short)item.NatureResistance);
		buffer.WriteInt16((short)item.FrostResistance);
		buffer.WriteInt16((short)item.ShadowResistance);
		buffer.WriteInt16((short)item.ArcaneResistance);
		buffer.WriteUInt16((ushort)item.ScalingStatDistribution);
		buffer.WriteUInt8(254);
		buffer.WriteUInt8(0);
		buffer.WriteUInt8(0);
		buffer.WriteUInt8(0);
		buffer.WriteUInt8((byte)item.ItemSocketColors[0]);
		buffer.WriteUInt8((byte)item.ItemSocketColors[1]);
		buffer.WriteUInt8((byte)item.ItemSocketColors[2]);
		buffer.WriteUInt8((byte)item.SheathType);
		buffer.WriteUInt8((byte)item.Material);
		buffer.WriteUInt8((byte)item.PageMaterial);
		buffer.WriteUInt8((byte)item.Language);
		buffer.WriteUInt8((byte)item.Bonding);
		buffer.WriteUInt8((byte)item.DamageTypes[0]);
		buffer.WriteInt8((sbyte)item.StatTypes[0]);
		buffer.WriteInt8((sbyte)item.StatTypes[1]);
		buffer.WriteInt8((sbyte)item.StatTypes[2]);
		buffer.WriteInt8((sbyte)item.StatTypes[3]);
		buffer.WriteInt8((sbyte)item.StatTypes[4]);
		buffer.WriteInt8((sbyte)item.StatTypes[5]);
		buffer.WriteInt8((sbyte)item.StatTypes[6]);
		buffer.WriteInt8((sbyte)item.StatTypes[7]);
		buffer.WriteInt8((sbyte)item.StatTypes[8]);
		buffer.WriteInt8((sbyte)item.StatTypes[9]);
		buffer.WriteUInt8((byte)item.ContainerSlots);
		buffer.WriteUInt8((byte)item.RequiredRepValue);
		buffer.WriteUInt8((byte)item.RequiredCityRank);
		buffer.WriteUInt8((byte)item.RequiredHonorRank);
		buffer.WriteUInt8((byte)item.InventoryType);
		buffer.WriteUInt8((byte)item.Quality);
		buffer.WriteUInt8((byte)item.AmmoType);
		buffer.WriteInt8((sbyte)StatValues[0]);
		buffer.WriteInt8((sbyte)StatValues[1]);
		buffer.WriteInt8((sbyte)StatValues[2]);
		buffer.WriteInt8((sbyte)StatValues[3]);
		buffer.WriteInt8((sbyte)StatValues[4]);
		buffer.WriteInt8((sbyte)StatValues[5]);
		buffer.WriteInt8((sbyte)StatValues[6]);
		buffer.WriteInt8((sbyte)StatValues[7]);
		buffer.WriteInt8((sbyte)StatValues[8]);
		buffer.WriteInt8((sbyte)StatValues[9]);
		buffer.WriteInt8((sbyte)item.RequiredLevel);
	}

	public static void WriteItemSparseHotfix(ItemSparseRecord row, ByteBuffer buffer)
	{
		int[] StatValues = new int[10];
		for (int i = 0; i < 10; i++)
		{
			StatValues[i] = row.StatValue[i];
			if (StatValues[i] > 127)
			{
				StatValues[i] = 127;
			}
			if (StatValues[i] < -127)
			{
				StatValues[i] = -127;
			}
		}
		buffer.WriteInt64(row.AllowableRace);
		buffer.WriteCString(row.Description);
		buffer.WriteCString(row.Name4);
		buffer.WriteCString(row.Name3);
		buffer.WriteCString(row.Name2);
		buffer.WriteCString(row.Name1);
		buffer.WriteFloat(row.DmgVariance);
		buffer.WriteUInt32(row.DurationInInventory);
		buffer.WriteFloat(row.QualityModifier);
		buffer.WriteUInt32(row.BagFamily);
		buffer.WriteFloat(row.RangeMod);
		buffer.WriteFloat(row.StatPercentageOfSocket[0]);
		buffer.WriteFloat(row.StatPercentageOfSocket[1]);
		buffer.WriteFloat(row.StatPercentageOfSocket[2]);
		buffer.WriteFloat(row.StatPercentageOfSocket[3]);
		buffer.WriteFloat(row.StatPercentageOfSocket[4]);
		buffer.WriteFloat(row.StatPercentageOfSocket[5]);
		buffer.WriteFloat(row.StatPercentageOfSocket[6]);
		buffer.WriteFloat(row.StatPercentageOfSocket[7]);
		buffer.WriteFloat(row.StatPercentageOfSocket[8]);
		buffer.WriteFloat(row.StatPercentageOfSocket[9]);
		buffer.WriteInt32(row.StatPercentEditor[0]);
		buffer.WriteInt32(row.StatPercentEditor[1]);
		buffer.WriteInt32(row.StatPercentEditor[2]);
		buffer.WriteInt32(row.StatPercentEditor[3]);
		buffer.WriteInt32(row.StatPercentEditor[4]);
		buffer.WriteInt32(row.StatPercentEditor[5]);
		buffer.WriteInt32(row.StatPercentEditor[6]);
		buffer.WriteInt32(row.StatPercentEditor[7]);
		buffer.WriteInt32(row.StatPercentEditor[8]);
		buffer.WriteInt32(row.StatPercentEditor[9]);
		buffer.WriteInt32(row.Stackable);
		buffer.WriteInt32(row.MaxCount);
		buffer.WriteUInt32(row.RequiredAbility);
		buffer.WriteUInt32(row.SellPrice);
		buffer.WriteUInt32(row.BuyPrice);
		buffer.WriteUInt32(row.VendorStackCount);
		buffer.WriteFloat(row.PriceVariance);
		buffer.WriteFloat(row.PriceRandomValue);
		buffer.WriteUInt32(row.Flags[0]);
		buffer.WriteUInt32(row.Flags[1]);
		buffer.WriteUInt32(row.Flags[2]);
		buffer.WriteUInt32(row.Flags[3]);
		buffer.WriteInt32(row.OppositeFactionItemId);
		buffer.WriteUInt32(row.MaxDurability);
		buffer.WriteUInt16(row.ItemNameDescriptionId);
		buffer.WriteUInt16(row.RequiredTransmogHoliday);
		buffer.WriteUInt16(row.RequiredHoliday);
		buffer.WriteUInt16(row.LimitCategory);
		buffer.WriteUInt16(row.GemProperties);
		buffer.WriteUInt16(row.SocketMatchEnchantmentId);
		buffer.WriteUInt16(row.TotemCategoryId);
		buffer.WriteUInt16(row.InstanceBound);
		buffer.WriteUInt16(row.ZoneBound[0]);
		buffer.WriteUInt16(row.ZoneBound[1]);
		buffer.WriteUInt16(row.ItemSet);
		buffer.WriteUInt16(row.LockId);
		buffer.WriteUInt16(row.StartQuestId);
		buffer.WriteUInt16(row.PageText);
		buffer.WriteUInt16(row.Delay);
		buffer.WriteUInt16(row.RequiredReputationId);
		buffer.WriteUInt16(row.RequiredSkillRank);
		buffer.WriteUInt16(row.RequiredSkill);
		buffer.WriteUInt16(row.ItemLevel);
		buffer.WriteInt16(row.AllowableClass);
		buffer.WriteUInt16(row.ItemRandomSuffixGroupId);
		buffer.WriteUInt16(row.RandomProperty);
		buffer.WriteUInt16(row.MinDamage[0]);
		buffer.WriteUInt16(row.MinDamage[1]);
		buffer.WriteUInt16(row.MinDamage[2]);
		buffer.WriteUInt16(row.MinDamage[3]);
		buffer.WriteUInt16(row.MinDamage[4]);
		buffer.WriteUInt16(row.MaxDamage[0]);
		buffer.WriteUInt16(row.MaxDamage[1]);
		buffer.WriteUInt16(row.MaxDamage[2]);
		buffer.WriteUInt16(row.MaxDamage[3]);
		buffer.WriteUInt16(row.MaxDamage[4]);
		buffer.WriteInt16(row.Resistances[0]);
		buffer.WriteInt16(row.Resistances[1]);
		buffer.WriteInt16(row.Resistances[2]);
		buffer.WriteInt16(row.Resistances[3]);
		buffer.WriteInt16(row.Resistances[4]);
		buffer.WriteInt16(row.Resistances[5]);
		buffer.WriteInt16(row.Resistances[6]);
		buffer.WriteUInt16(row.ScalingStatDistributionId);
		buffer.WriteUInt8(row.ExpansionId);
		buffer.WriteUInt8(row.ArtifactId);
		buffer.WriteUInt8(row.SpellWeight);
		buffer.WriteUInt8(row.SpellWeightCategory);
		buffer.WriteUInt8(row.SocketType[0]);
		buffer.WriteUInt8(row.SocketType[1]);
		buffer.WriteUInt8(row.SocketType[2]);
		buffer.WriteUInt8(row.SheatheType);
		buffer.WriteUInt8(row.Material);
		buffer.WriteUInt8(row.PageMaterial);
		buffer.WriteUInt8(row.PageLanguage);
		buffer.WriteUInt8(row.Bonding);
		buffer.WriteUInt8(row.DamageType);
		buffer.WriteInt8(row.StatType[0]);
		buffer.WriteInt8(row.StatType[1]);
		buffer.WriteInt8(row.StatType[2]);
		buffer.WriteInt8(row.StatType[3]);
		buffer.WriteInt8(row.StatType[4]);
		buffer.WriteInt8(row.StatType[5]);
		buffer.WriteInt8(row.StatType[6]);
		buffer.WriteInt8(row.StatType[7]);
		buffer.WriteInt8(row.StatType[8]);
		buffer.WriteInt8(row.StatType[9]);
		buffer.WriteUInt8(row.ContainerSlots);
		buffer.WriteUInt8(row.RequiredReputationRank);
		buffer.WriteUInt8(row.RequiredCityRank);
		buffer.WriteUInt8(row.RequiredHonorRank);
		buffer.WriteUInt8(row.InventoryType);
		buffer.WriteUInt8(row.OverallQualityId);
		buffer.WriteUInt8(row.AmmoType);
		buffer.WriteInt8((sbyte)StatValues[0]);
		buffer.WriteInt8((sbyte)StatValues[1]);
		buffer.WriteInt8((sbyte)StatValues[2]);
		buffer.WriteInt8((sbyte)StatValues[3]);
		buffer.WriteInt8((sbyte)StatValues[4]);
		buffer.WriteInt8((sbyte)StatValues[5]);
		buffer.WriteInt8((sbyte)StatValues[6]);
		buffer.WriteInt8((sbyte)StatValues[7]);
		buffer.WriteInt8((sbyte)StatValues[8]);
		buffer.WriteInt8((sbyte)StatValues[9]);
		buffer.WriteInt8(row.RequiredLevel);
	}

	public static void LoadItemHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"Item{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte ClassID = byte.Parse(fields[1]);
			byte SubclassID = byte.Parse(fields[2]);
			byte Material = byte.Parse(fields[3]);
			sbyte InventoryType = sbyte.Parse(fields[4]);
			uint RequiredLevel = uint.Parse(fields[5]);
			byte SheatheType = byte.Parse(fields[6]);
			ushort RandomSelect = ushort.Parse(fields[7]);
			ushort ItemRandomSuffixGroupID = ushort.Parse(fields[8]);
			sbyte Sound_override_subclassID = sbyte.Parse(fields[9]);
			ushort ScalingStatDistributionID = ushort.Parse(fields[10]);
			int IconFileDataID = int.Parse(fields[11]);
			byte ItemGroupSoundsID = byte.Parse(fields[12]);
			int ContentTuningID = int.Parse(fields[13]);
			uint MaxDurability = uint.Parse(fields[14]);
			byte AmmunitionType = byte.Parse(fields[15]);
			byte DamageType1 = byte.Parse(fields[16]);
			byte DamageType2 = byte.Parse(fields[17]);
			byte DamageType3 = byte.Parse(fields[18]);
			byte DamageType4 = byte.Parse(fields[19]);
			byte DamageType5 = byte.Parse(fields[20]);
			short Resistances1 = short.Parse(fields[21]);
			short Resistances2 = short.Parse(fields[22]);
			short Resistances3 = short.Parse(fields[23]);
			short Resistances4 = short.Parse(fields[24]);
			short Resistances5 = short.Parse(fields[25]);
			short Resistances6 = short.Parse(fields[26]);
			short Resistances7 = short.Parse(fields[27]);
			ushort MinDamage1 = ushort.Parse(fields[28]);
			ushort MinDamage2 = ushort.Parse(fields[29]);
			ushort MinDamage3 = ushort.Parse(fields[30]);
			ushort MinDamage4 = ushort.Parse(fields[31]);
			ushort MinDamage5 = ushort.Parse(fields[32]);
			ushort MaxDamage1 = ushort.Parse(fields[33]);
			ushort MaxDamage2 = ushort.Parse(fields[34]);
			ushort MaxDamage3 = ushort.Parse(fields[35]);
			ushort MaxDamage4 = ushort.Parse(fields[36]);
			ushort MaxDamage5 = ushort.Parse(fields[37]);
			HotfixRecord record = new HotfixRecord();
			record.Status = HotfixStatus.Valid;
			record.TableHash = DB2Hash.Item;
			record.HotfixId = 210000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.HotfixContent.WriteUInt8(ClassID);
			record.HotfixContent.WriteUInt8(SubclassID);
			record.HotfixContent.WriteUInt8(Material);
			record.HotfixContent.WriteInt8(InventoryType);
			record.HotfixContent.WriteUInt32(RequiredLevel);
			record.HotfixContent.WriteUInt8(SheatheType);
			record.HotfixContent.WriteUInt16(RandomSelect);
			record.HotfixContent.WriteUInt16(ItemRandomSuffixGroupID);
			record.HotfixContent.WriteInt8(Sound_override_subclassID);
			record.HotfixContent.WriteUInt16(ScalingStatDistributionID);
			record.HotfixContent.WriteInt32(IconFileDataID);
			record.HotfixContent.WriteUInt8(ItemGroupSoundsID);
			record.HotfixContent.WriteInt32(ContentTuningID);
			record.HotfixContent.WriteUInt32(MaxDurability);
			record.HotfixContent.WriteUInt8(AmmunitionType);
			record.HotfixContent.WriteUInt8(DamageType1);
			record.HotfixContent.WriteUInt8(DamageType2);
			record.HotfixContent.WriteUInt8(DamageType3);
			record.HotfixContent.WriteUInt8(DamageType4);
			record.HotfixContent.WriteUInt8(DamageType5);
			record.HotfixContent.WriteInt16(Resistances1);
			record.HotfixContent.WriteInt16(Resistances2);
			record.HotfixContent.WriteInt16(Resistances3);
			record.HotfixContent.WriteInt16(Resistances4);
			record.HotfixContent.WriteInt16(Resistances5);
			record.HotfixContent.WriteInt16(Resistances6);
			record.HotfixContent.WriteInt16(Resistances7);
			record.HotfixContent.WriteUInt16(MinDamage1);
			record.HotfixContent.WriteUInt16(MinDamage2);
			record.HotfixContent.WriteUInt16(MinDamage3);
			record.HotfixContent.WriteUInt16(MinDamage4);
			record.HotfixContent.WriteUInt16(MinDamage5);
			record.HotfixContent.WriteUInt16(MaxDamage1);
			record.HotfixContent.WriteUInt16(MaxDamage2);
			record.HotfixContent.WriteUInt16(MaxDamage3);
			record.HotfixContent.WriteUInt16(MaxDamage4);
			record.HotfixContent.WriteUInt16(MaxDamage5);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void WriteItemHotfix(ItemTemplate item, ByteBuffer buffer)
	{
		int fileDataId = (int)GameData.GetItemIconFileDataIdByDisplayId(item.DisplayID);
		buffer.WriteUInt8((byte)item.Class);
		buffer.WriteUInt8((byte)item.SubClass);
		buffer.WriteUInt8((byte)item.Material);
		buffer.WriteInt8((sbyte)item.InventoryType);
		buffer.WriteInt32((int)item.RequiredLevel);
		buffer.WriteUInt8((byte)item.SheathType);
		buffer.WriteUInt16((ushort)item.RandomProperty);
		buffer.WriteUInt16((ushort)item.RandomSuffix);
		buffer.WriteInt8(-1);
		buffer.WriteUInt16(0);
		buffer.WriteInt32(fileDataId);
		buffer.WriteUInt8(0);
		buffer.WriteInt32(0);
		buffer.WriteUInt32(item.MaxDurability);
		buffer.WriteUInt8((byte)item.AmmoType);
		buffer.WriteUInt8((byte)item.DamageTypes[0]);
		buffer.WriteUInt8((byte)item.DamageTypes[1]);
		buffer.WriteUInt8((byte)item.DamageTypes[2]);
		buffer.WriteUInt8((byte)item.DamageTypes[3]);
		buffer.WriteUInt8((byte)item.DamageTypes[4]);
		buffer.WriteInt16((short)item.Armor);
		buffer.WriteInt16((short)item.HolyResistance);
		buffer.WriteInt16((short)item.FireResistance);
		buffer.WriteInt16((short)item.NatureResistance);
		buffer.WriteInt16((short)item.FrostResistance);
		buffer.WriteInt16((short)item.ShadowResistance);
		buffer.WriteInt16((short)item.ArcaneResistance);
		buffer.WriteUInt16((ushort)item.DamageMins[0]);
		buffer.WriteUInt16((ushort)item.DamageMins[1]);
		buffer.WriteUInt16((ushort)item.DamageMins[2]);
		buffer.WriteUInt16((ushort)item.DamageMins[3]);
		buffer.WriteUInt16((ushort)item.DamageMins[4]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[0]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[1]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[2]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[3]);
		buffer.WriteUInt16((ushort)item.DamageMaxs[4]);
	}

	public static void WriteItemHotfix(ItemRecord row, ByteBuffer buffer)
	{
		buffer.WriteUInt8(row.ClassId);
		buffer.WriteUInt8(row.SubclassId);
		buffer.WriteUInt8(row.Material);
		buffer.WriteInt8(row.InventoryType);
		buffer.WriteInt32(row.RequiredLevel);
		buffer.WriteUInt8(row.SheatheType);
		buffer.WriteUInt16(row.RandomProperty);
		buffer.WriteUInt16(row.ItemRandomSuffixGroupId);
		buffer.WriteInt8(row.SoundOverrideSubclassId);
		buffer.WriteUInt16(row.ScalingStatDistributionId);
		buffer.WriteInt32(row.IconFileDataId);
		buffer.WriteUInt8(row.ItemGroupSoundsId);
		buffer.WriteInt32(row.ContentTuningId);
		buffer.WriteUInt32(row.MaxDurability);
		buffer.WriteUInt8(row.AmmoType);
		buffer.WriteUInt8(row.DamageType[0]);
		buffer.WriteUInt8(row.DamageType[1]);
		buffer.WriteUInt8(row.DamageType[2]);
		buffer.WriteUInt8(row.DamageType[3]);
		buffer.WriteUInt8(row.DamageType[4]);
		buffer.WriteInt16(row.Resistances[0]);
		buffer.WriteInt16(row.Resistances[1]);
		buffer.WriteInt16(row.Resistances[2]);
		buffer.WriteInt16(row.Resistances[3]);
		buffer.WriteInt16(row.Resistances[4]);
		buffer.WriteInt16(row.Resistances[5]);
		buffer.WriteInt16(row.Resistances[6]);
		buffer.WriteUInt16(row.MinDamage[0]);
		buffer.WriteUInt16(row.MinDamage[1]);
		buffer.WriteUInt16(row.MinDamage[2]);
		buffer.WriteUInt16(row.MinDamage[3]);
		buffer.WriteUInt16(row.MinDamage[4]);
		buffer.WriteUInt16(row.MaxDamage[0]);
		buffer.WriteUInt16(row.MaxDamage[1]);
		buffer.WriteUInt16(row.MaxDamage[2]);
		buffer.WriteUInt16(row.MaxDamage[3]);
		buffer.WriteUInt16(row.MaxDamage[4]);
	}

	public static void WriteItemAppearanceHotfix(ItemAppearance appearance, ByteBuffer buffer)
	{
		buffer.WriteUInt8(appearance.DisplayType);
		buffer.WriteInt32(appearance.ItemDisplayInfoID);
		buffer.WriteInt32(appearance.DefaultIconFileDataID);
		buffer.WriteInt32(appearance.UiOrder);
	}

	public static void WriteItemModifiedAppearanceHotfix(ItemModifiedAppearance modAppearance, ByteBuffer buffer)
	{
		buffer.WriteInt32(modAppearance.Id);
		buffer.WriteInt32(modAppearance.ItemID);
		buffer.WriteInt32(modAppearance.ItemAppearanceModifierID);
		buffer.WriteInt32(modAppearance.ItemAppearanceID);
		buffer.WriteInt32(modAppearance.OrderIndex);
		buffer.WriteInt32(modAppearance.TransmogSourceTypeEnum);
	}

	public static void WriteItemEffectHotfix(ItemEffect effect, ByteBuffer buffer)
	{
		buffer.WriteUInt8(effect.LegacySlotIndex);
		buffer.WriteInt8(effect.TriggerType);
		buffer.WriteInt16(effect.Charges);
		buffer.WriteInt32(effect.CoolDownMSec);
		buffer.WriteInt32(effect.CategoryCoolDownMSec);
		buffer.WriteUInt16(effect.SpellCategoryID);
		buffer.WriteInt32(effect.SpellID);
		buffer.WriteUInt16(effect.ChrSpecializationID);
		buffer.WriteInt32(effect.ParentItemID);
	}

	public static List<HotfixRecord> FindHotfixesByRecordIdAndTable(uint id, DB2Hash table, uint startId = 0u)
	{
		return GameData.Hotfixes.Values.Where((HotfixRecord hotfix) => hotfix.HotfixId >= startId && hotfix.TableHash == table && hotfix.RecordId == id).ToList();
	}

	public static void UpdateHotfix(object obj, bool remove = false)
	{
		if (obj is ItemRecord)
		{
			ItemRecord item = (ItemRecord)obj;
			DoStuff((uint)item.Id, DB2Hash.Item, delegate(ByteBuffer hotfixContentTargetBuffer)
			{
				GameData.WriteItemHotfix(item, hotfixContentTargetBuffer);
			});
		}
		if (obj is ItemSparseRecord)
		{
			ItemSparseRecord itemSparse = (ItemSparseRecord)obj;
			DoStuff((uint)itemSparse.Id, DB2Hash.ItemSparse, delegate(ByteBuffer hotfixContentTargetBuffer)
			{
				GameData.WriteItemSparseHotfix(itemSparse, hotfixContentTargetBuffer);
			});
		}
		if (obj is ItemEffect)
		{
			ItemEffect effect = (ItemEffect)obj;
			DoStuff((uint)effect.Id, DB2Hash.ItemEffect, delegate(ByteBuffer hotfixContentTargetBuffer)
			{
				GameData.WriteItemEffectHotfix(effect, hotfixContentTargetBuffer);
			});
		}
		if (obj is ItemAppearance)
		{
			ItemAppearance appearance = (ItemAppearance)obj;
			DoStuff((uint)appearance.Id, DB2Hash.ItemAppearance, delegate(ByteBuffer hotfixContentTargetBuffer)
			{
				GameData.WriteItemAppearanceHotfix(appearance, hotfixContentTargetBuffer);
			});
		}
		if (obj is ItemModifiedAppearance)
		{
			ItemModifiedAppearance modAppearance = (ItemModifiedAppearance)obj;
			DoStuff((uint)modAppearance.Id, DB2Hash.ItemModifiedAppearance, delegate(ByteBuffer hotfixContentTargetBuffer)
			{
				GameData.WriteItemModifiedAppearanceHotfix(modAppearance, hotfixContentTargetBuffer);
			});
		}
		static void DoStuff(uint recordId, DB2Hash table, Action<ByteBuffer> writer)
		{
			List<HotfixRecord> oldRecords = GameData.FindHotfixesByRecordIdAndTable(recordId, table, 210000u);
			if (oldRecords.Count == 0)
			{
				HotfixRecord record = new HotfixRecord();
				record.Status = HotfixStatus.Valid;
				record.TableHash = table;
				record.HotfixId = GameData.GetFirstFreeId(GameData.Hotfixes, 210000u);
				record.UniqueId = record.HotfixId;
				record.RecordId = recordId;
				writer(record.HotfixContent);
				GameData.Hotfixes.Add(record.HotfixId, record);
			}
			else
			{
				IEnumerable<HotfixRecord> oldRecordsToBeInvalided = oldRecords.SkipLast(1);
				foreach (HotfixRecord record2 in oldRecordsToBeInvalided)
				{
					record2.Status = HotfixStatus.Invalid;
					record2.HotfixContent = new ByteBuffer();
					Log.Print(LogType.Storage, $"Got duplicate record for record {record2.RecordId} in {record2.TableHash}", "UpdateHotfix", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				HotfixRecord recordToOverwrite = oldRecords.Last();
				recordToOverwrite.HotfixContent = new ByteBuffer();
				writer(recordToOverwrite.HotfixContent);
				GameData.Hotfixes[recordToOverwrite.HotfixId] = recordToOverwrite;
			}
		}
	}

	public static HotFixMessage? GenerateItemUpdateIfNeeded(ItemTemplate item)
	{
		GameData.ItemRecordsStore.TryGetValue(item.Entry, out var row);
		if (row != null)
		{
			int iconFileDataId = (int)GameData.GetItemIconFileDataIdByDisplayId(item.DisplayID);
			if (row.ClassId != (byte)item.Class || row.SubclassId != (byte)item.SubClass || row.Material != (byte)item.Material || row.InventoryType != (sbyte)item.InventoryType || row.RequiredLevel != (int)item.RequiredLevel || row.SheatheType != (byte)item.SheathType || row.RandomProperty != (ushort)item.RandomProperty || row.ItemRandomSuffixGroupId != (ushort)item.RandomSuffix || (row.IconFileDataId != iconFileDataId && iconFileDataId != 0) || row.MaxDurability != item.MaxDurability || row.AmmoType != (byte)item.AmmoType || row.DamageType[0] != (byte)item.DamageTypes[0] || row.DamageType[1] != (byte)item.DamageTypes[1] || row.DamageType[2] != (byte)item.DamageTypes[2] || row.DamageType[3] != (byte)item.DamageTypes[3] || row.DamageType[4] != (byte)item.DamageTypes[4] || row.Resistances[1] != (short)item.HolyResistance || row.Resistances[2] != (short)item.FireResistance || row.Resistances[3] != (short)item.NatureResistance || row.Resistances[4] != (short)item.FrostResistance || row.Resistances[5] != (short)item.ShadowResistance || row.Resistances[6] != (short)item.ArcaneResistance)
			{
				Log.Print(LogType.Storage, $"Item #{item.Entry} needs to be updated.", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				if (row.ClassId != (byte)item.Class)
				{
					Log.Print(LogType.Storage, $"ClassId {row.ClassId} vs {item.Class}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.SubclassId != (byte)item.SubClass)
				{
					Log.Print(LogType.Storage, $"SubclassId {row.SubclassId} vs {item.SubClass}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Material != (byte)item.Material)
				{
					Log.Print(LogType.Storage, $"Material {row.Material} vs {item.Material}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.InventoryType != (sbyte)item.InventoryType)
				{
					Log.Print(LogType.Storage, $"InventoryType {row.InventoryType} vs {item.InventoryType}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredLevel != (int)item.RequiredLevel)
				{
					Log.Print(LogType.Storage, $"RequiredLevel {row.RequiredLevel} vs {item.RequiredLevel}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.SheatheType != (byte)item.SheathType)
				{
					Log.Print(LogType.Storage, $"SheatheType {row.SheatheType} vs {item.SheathType}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RandomProperty != (ushort)item.RandomProperty)
				{
					Log.Print(LogType.Storage, $"RandomProperty {row.RandomProperty} vs {item.RandomProperty}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ItemRandomSuffixGroupId != (ushort)item.RandomSuffix)
				{
					Log.Print(LogType.Storage, $"ItemRandomSuffixGroupId {row.ItemRandomSuffixGroupId} vs {item.RandomSuffix}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.IconFileDataId != iconFileDataId)
				{
					Log.Print(LogType.Storage, $"IconFileDataId {row.IconFileDataId} vs {iconFileDataId}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.MaxDurability != item.MaxDurability)
				{
					Log.Print(LogType.Storage, $"MaxDurability {row.MaxDurability} vs {item.MaxDurability}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.AmmoType != (byte)item.AmmoType)
				{
					Log.Print(LogType.Storage, $"AmmoType {row.AmmoType} vs {item.AmmoType}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				for (int i = 0; i < 5; i++)
				{
					if (row.DamageType[i] != (byte)item.DamageTypes[i])
					{
						Log.Print(LogType.Storage, $"DamageType[{i}] {row.DamageType[i]} vs {item.DamageTypes[i]}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
				}
				if (row.Resistances[1] != (short)item.HolyResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[1] {row.Resistances[1]} vs {item.HolyResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[2] != (short)item.FireResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[2] {row.Resistances[2]} vs {item.FireResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[3] != (short)item.NatureResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[3] {row.Resistances[3]} vs {item.NatureResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[4] != (short)item.FrostResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[4] {row.Resistances[4]} vs {item.FrostResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[5] != (short)item.ShadowResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[5] {row.Resistances[5]} vs {item.ShadowResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[6] != (short)item.ArcaneResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[6] {row.Resistances[6]} vs {item.ArcaneResistance}", "GenerateItemUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				GameData.UpdateItemRecord(row, item);
				GameData.UpdateHotfix(row);
				return GameData.GenerateHotFixMessage(row);
			}
			return null;
		}
		row = GameData.AddItemRecord(item);
		if (row == null)
		{
			return null;
		}
		GameData.UpdateHotfix(row);
		return GameData.GenerateHotFixMessage(row);
	}

	public static HotFixMessage? GenerateItemSparseUpdateIfNeeded(ItemTemplate item)
	{
		GameData.ItemSparseRecordsStore.TryGetValue(item.Entry, out var row);
		if (row != null)
		{
			if (!row.Description.Equals(item.Description) || !row.Name4.Equals(item.Name[3]) || !row.Name3.Equals(item.Name[2]) || !row.Name2.Equals(item.Name[1]) || !row.Name1.Equals(item.Name[0]) || row.DurationInInventory != item.Duration || row.BagFamily != item.BagFamily || row.RangeMod != item.RangedMod || row.RequiredAbility != item.RequiredSpell || row.SellPrice != item.SellPrice || row.BuyPrice != item.BuyPrice || row.MaxDurability != item.MaxDurability || row.RequiredHoliday != (ushort)item.HolidayID || row.LimitCategory != (ushort)item.ItemLimitCategory || row.GemProperties != (ushort)item.GemProperties || row.SocketMatchEnchantmentId != (ushort)item.SocketBonus || row.TotemCategoryId != (ushort)item.TotemCategory || row.InstanceBound != (ushort)item.MapID || row.ZoneBound[0] != (ushort)item.AreaID || row.ItemSet != (ushort)item.ItemSet || row.LockId != (ushort)item.LockId || row.StartQuestId != (ushort)item.StartQuestId || row.PageText != (ushort)item.PageText || row.Delay != (ushort)item.Delay || row.RequiredReputationId != (ushort)item.RequiredRepFaction || row.RequiredSkillRank != (ushort)item.RequiredSkillLevel || row.RequiredSkill != (ushort)item.RequiredSkillId || row.ItemLevel != (ushort)item.ItemLevel || row.ItemRandomSuffixGroupId != (ushort)item.RandomSuffix || row.RandomProperty != (ushort)item.RandomProperty || row.Resistances[1] != (short)item.HolyResistance || row.Resistances[2] != (short)item.FireResistance || row.Resistances[3] != (short)item.NatureResistance || row.Resistances[4] != (short)item.FrostResistance || row.Resistances[5] != (short)item.ShadowResistance || row.Resistances[6] != (short)item.ArcaneResistance || row.ScalingStatDistributionId != (ushort)item.ScalingStatDistribution || row.SocketType[0] != ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[0]) || row.SocketType[1] != ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[1]) || row.SocketType[2] != ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[2]) || row.SheatheType != (byte)item.SheathType || row.Material != (byte)item.Material || row.PageMaterial != (byte)item.PageMaterial || row.PageLanguage != (byte)item.Language || row.Bonding != (byte)item.Bonding || row.DamageType != (byte)item.DamageTypes[0] || (row.StatType[0] != (sbyte)item.StatTypes[0] && (row.StatValue[0] != 0 || item.StatValues[0] != 0)) || (row.StatType[1] != (sbyte)item.StatTypes[1] && (row.StatValue[1] != 0 || item.StatValues[1] != 0)) || (row.StatType[2] != (sbyte)item.StatTypes[2] && (row.StatValue[2] != 0 || item.StatValues[2] != 0)) || (row.StatType[3] != (sbyte)item.StatTypes[3] && (row.StatValue[3] != 0 || item.StatValues[3] != 0)) || (row.StatType[4] != (sbyte)item.StatTypes[4] && (row.StatValue[4] != 0 || item.StatValues[4] != 0)) || (row.StatType[5] != (sbyte)item.StatTypes[5] && (row.StatValue[5] != 0 || item.StatValues[5] != 0)) || (row.StatType[6] != (sbyte)item.StatTypes[6] && (row.StatValue[6] != 0 || item.StatValues[6] != 0)) || (row.StatType[7] != (sbyte)item.StatTypes[7] && (row.StatValue[7] != 0 || item.StatValues[7] != 0)) || (row.StatType[8] != (sbyte)item.StatTypes[8] && (row.StatValue[8] != 0 || item.StatValues[8] != 0)) || (row.StatType[9] != (sbyte)item.StatTypes[9] && (row.StatValue[9] != 0 || item.StatValues[9] != 0)) || row.ContainerSlots != (byte)item.ContainerSlots || row.RequiredReputationRank != (byte)item.RequiredRepValue || row.RequiredCityRank != (byte)item.RequiredCityRank || row.RequiredHonorRank != (byte)item.RequiredHonorRank || row.InventoryType != (byte)item.InventoryType || row.OverallQualityId != (byte)item.Quality || row.AmmoType != (byte)item.AmmoType || row.StatValue[0] != (sbyte)item.StatValues[0] || row.StatValue[1] != (sbyte)item.StatValues[1] || row.StatValue[2] != (sbyte)item.StatValues[2] || row.StatValue[3] != (sbyte)item.StatValues[3] || row.StatValue[4] != (sbyte)item.StatValues[4] || row.StatValue[5] != (sbyte)item.StatValues[5] || row.StatValue[6] != (sbyte)item.StatValues[6] || row.StatValue[7] != (sbyte)item.StatValues[7] || row.StatValue[8] != (sbyte)item.StatValues[8] || row.StatValue[9] != (sbyte)item.StatValues[9] || row.RequiredLevel != (sbyte)item.RequiredLevel)
			{
				Log.Print(LogType.Storage, $"ItemSparse #{item.Entry} needs to be updated.", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				if (!row.Description.Equals(item.Description))
				{
					Log.Print(LogType.Storage, $"Description \"{row.Description}\" vs \"{item.Description}\"", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (!row.Name4.Equals(item.Name[3]))
				{
					Log.Print(LogType.Storage, $"Name4 \"{row.Name4}\" vs \"{item.Name[3]}\"", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (!row.Name3.Equals(item.Name[2]))
				{
					Log.Print(LogType.Storage, $"Name3 \"{row.Name3}\" vs \"{item.Name[2]}\"", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (!row.Name2.Equals(item.Name[1]))
				{
					Log.Print(LogType.Storage, $"Name2 \"{row.Name2}\" vs \"{item.Name[1]}\"", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (!row.Name1.Equals(item.Name[0]))
				{
					Log.Print(LogType.Storage, $"Name1 \"{row.Name1}\" vs \"{item.Name[0]}\"", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.DurationInInventory != item.Duration)
				{
					Log.Print(LogType.Storage, $"DurationInInventory {row.DurationInInventory} vs {item.Duration}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.BagFamily != item.BagFamily)
				{
					Log.Print(LogType.Storage, $"BagFamily {row.BagFamily} vs {item.BagFamily}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RangeMod != item.RangedMod)
				{
					Log.Print(LogType.Storage, $"RangeMod {row.RangeMod} vs {item.RangedMod}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredAbility != item.RequiredSpell)
				{
					Log.Print(LogType.Storage, $"RequiredAbility {row.RequiredAbility} vs {item.RequiredSpell}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.SellPrice != item.SellPrice)
				{
					Log.Print(LogType.Storage, $"SellPrice {row.SellPrice} vs {item.SellPrice}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.BuyPrice != item.BuyPrice)
				{
					Log.Print(LogType.Storage, $"BuyPrice {row.BuyPrice} vs {item.BuyPrice}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.MaxDurability != item.MaxDurability)
				{
					Log.Print(LogType.Storage, $"MaxDurability {row.MaxDurability} vs {item.MaxDurability}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredHoliday != (ushort)item.HolidayID)
				{
					Log.Print(LogType.Storage, $"RequiredHoliday {row.RequiredHoliday} vs {item.HolidayID}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.LimitCategory != (ushort)item.ItemLimitCategory)
				{
					Log.Print(LogType.Storage, $"LimitCategory {row.LimitCategory} vs {item.ItemLimitCategory}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.GemProperties != (ushort)item.GemProperties)
				{
					Log.Print(LogType.Storage, $"GemProperties {row.GemProperties} vs {item.GemProperties}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.SocketMatchEnchantmentId != (ushort)item.SocketBonus)
				{
					Log.Print(LogType.Storage, $"SocketMatchEnchantmentId {row.SocketMatchEnchantmentId} vs {item.SocketBonus}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.TotemCategoryId != (ushort)item.TotemCategory)
				{
					Log.Print(LogType.Storage, $"TotemCategoryId {row.TotemCategoryId} vs {item.TotemCategory}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.InstanceBound != (ushort)item.MapID)
				{
					Log.Print(LogType.Storage, $"InstanceBound {row.InstanceBound} vs {item.MapID}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ZoneBound[0] != (ushort)item.AreaID)
				{
					Log.Print(LogType.Storage, $"ZoneBound[0] {row.ZoneBound[0]} vs {item.AreaID}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ItemSet != (ushort)item.ItemSet)
				{
					Log.Print(LogType.Storage, $"ItemSet {row.ItemSet} vs {item.ItemSet}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.LockId != (ushort)item.LockId)
				{
					Log.Print(LogType.Storage, $"LockId {row.LockId} vs {item.LockId}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.StartQuestId != (ushort)item.StartQuestId)
				{
					Log.Print(LogType.Storage, $"StartQuestId {row.StartQuestId} vs {item.StartQuestId}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.PageText != (ushort)item.PageText)
				{
					Log.Print(LogType.Storage, $"PageText {row.PageText} vs {item.PageText}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Delay != (ushort)item.Delay)
				{
					Log.Print(LogType.Storage, $"Delay {row.Delay} vs {item.Delay}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredReputationId != (ushort)item.RequiredRepFaction)
				{
					Log.Print(LogType.Storage, $"RequiredReputationId {row.RequiredReputationId} vs {item.RequiredRepFaction}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredSkillRank != (ushort)item.RequiredSkillLevel)
				{
					Log.Print(LogType.Storage, $"RequiredSkillRank {row.RequiredSkillRank} vs {item.RequiredSkillLevel}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredSkill != (ushort)item.RequiredSkillId)
				{
					Log.Print(LogType.Storage, $"RequiredSkill {row.RequiredSkill} vs {item.RequiredSkillId}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ItemLevel != (ushort)item.ItemLevel)
				{
					Log.Print(LogType.Storage, $"ItemLevel {row.ItemLevel} vs {item.ItemLevel}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ItemRandomSuffixGroupId != (ushort)item.RandomSuffix)
				{
					Log.Print(LogType.Storage, $"ItemRandomSuffixGroupId {row.ItemRandomSuffixGroupId} vs {item.RandomSuffix}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RandomProperty != (ushort)item.RandomProperty)
				{
					Log.Print(LogType.Storage, $"RandomProperty {row.RandomProperty} vs {item.RandomProperty}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[1] != (short)item.HolyResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[1] {row.Resistances[1]} vs {item.HolyResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[2] != (short)item.FireResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[2] {row.Resistances[2]} vs {item.FireResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[3] != (short)item.NatureResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[3]  {row.Resistances[3]} vs {item.NatureResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[4] != (short)item.FrostResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[4] {row.Resistances[4]} vs {item.FrostResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[5] != (short)item.ShadowResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[5] {row.Resistances[5]} vs {item.ShadowResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Resistances[6] != (short)item.ArcaneResistance)
				{
					Log.Print(LogType.Storage, $"Resistances[6] {row.Resistances[6]} vs {item.ArcaneResistance}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.ScalingStatDistributionId != (ushort)item.ScalingStatDistribution)
				{
					Log.Print(LogType.Storage, $"ScalingStatDistributionId {row.ScalingStatDistributionId} vs {item.ScalingStatDistribution}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				for (int i = 0; i < 3; i++)
				{
					if (row.SocketType[i] != ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[i]))
					{
						Log.Print(LogType.Storage, $"SocketType[{i}] {row.SocketType[i]} vs {ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[i])}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
				}
				if (row.SheatheType != (byte)item.SheathType)
				{
					Log.Print(LogType.Storage, $"SheatheType {row.SheatheType} vs {item.SheathType}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Material != (byte)item.Material)
				{
					Log.Print(LogType.Storage, $"Material {row.Material} vs {item.Material}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.PageMaterial != (byte)item.PageMaterial)
				{
					Log.Print(LogType.Storage, $"PageMaterial {row.PageMaterial} vs {item.PageMaterial}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.PageLanguage != (byte)item.Language)
				{
					Log.Print(LogType.Storage, $"PageLanguage {row.PageLanguage} vs {item.Language}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.Bonding != (byte)item.Bonding)
				{
					Log.Print(LogType.Storage, $"Bonding {row.Bonding} vs {item.Bonding}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.DamageType != (byte)item.DamageTypes[0])
				{
					Log.Print(LogType.Storage, $"DamageType {row.DamageType} vs {item.DamageTypes[0]}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				for (int j = 0; j < 10; j++)
				{
					if (row.StatType[j] != (sbyte)item.StatTypes[j] && (row.StatValue[j] != 0 || item.StatValues[j] != 0))
					{
						Log.Print(LogType.Storage, $"StatType[{j}] {row.StatType[j]} vs {item.StatTypes[j]}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
				}
				if (row.ContainerSlots != (byte)item.ContainerSlots)
				{
					Log.Print(LogType.Storage, $"ContainerSlots {row.ContainerSlots} vs {item.ContainerSlots}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredReputationRank != (byte)item.RequiredRepValue)
				{
					Log.Print(LogType.Storage, $"RequiredReputationRank {row.RequiredReputationRank} vs {item.RequiredRepValue}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredCityRank != (byte)item.RequiredCityRank)
				{
					Log.Print(LogType.Storage, $"RequiredCityRank {row.RequiredCityRank} vs {item.RequiredCityRank}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.RequiredHonorRank != (byte)item.RequiredHonorRank)
				{
					Log.Print(LogType.Storage, $"RequiredHonorRank {row.RequiredHonorRank} vs {item.RequiredHonorRank}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.InventoryType != (byte)item.InventoryType)
				{
					Log.Print(LogType.Storage, $"InventoryType {row.InventoryType} vs {item.InventoryType}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.OverallQualityId != (byte)item.Quality)
				{
					Log.Print(LogType.Storage, $"OverallQualityId {row.OverallQualityId} vs {item.Quality}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				if (row.AmmoType != (byte)item.AmmoType)
				{
					Log.Print(LogType.Storage, $"AmmoType {row.AmmoType} vs {item.AmmoType}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				for (int k = 0; k < 10; k++)
				{
					if (row.StatValue[0] != (sbyte)item.StatValues[0])
					{
						Log.Print(LogType.Storage, $"StatValue[{k}] {row.StatValue[k]} vs {item.StatValues[k]}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
				}
				if (row.RequiredLevel != (sbyte)item.RequiredLevel)
				{
					Log.Print(LogType.Storage, $"RequiredLevel {row.RequiredLevel} vs {item.RequiredLevel}", "GenerateItemSparseUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				GameData.UpdateItemSparseRecord(row, item);
				GameData.UpdateHotfix(row);
				return null;
			}
			return null;
		}
		row = GameData.AddItemSparseRecord(item);
		if (row == null)
		{
			return null;
		}
		GameData.UpdateHotfix(row);
		return GameData.GenerateHotFixMessage(row);
	}

	public static HotFixMessage? GenerateItemEffectUpdateIfNeeded(ItemTemplate item, byte slot)
	{
		ItemEffect effect = GameData.GetItemEffectByItemId(item.Entry, slot);
		if (effect != null)
		{
			bool wrongCategory = false;
			bool wrongCooldown = false;
			bool wrongCatCooldown = false;
			if (item.TriggeredSpellIds[slot] > 0)
			{
				GameData.ItemSpellsDataStore.TryGetValue((uint)item.TriggeredSpellIds[slot], out var data);
				if (data != null)
				{
					if (effect.SpellCategoryID != item.TriggeredSpellCategories[slot])
					{
						wrongCategory = data.Category != item.TriggeredSpellCategories[slot];
					}
					if (Math.Abs(effect.CoolDownMSec - item.TriggeredSpellCooldowns[slot]) > 1)
					{
						wrongCooldown = data.RecoveryTime != item.TriggeredSpellCooldowns[slot];
					}
					if (Math.Abs(effect.CategoryCoolDownMSec - item.TriggeredSpellCategoryCooldowns[slot]) > 1)
					{
						wrongCatCooldown = data.CategoryRecoveryTime != item.TriggeredSpellCategoryCooldowns[slot];
					}
				}
			}
			if (effect.TriggerType != item.TriggeredSpellTypes[slot] || effect.Charges != item.TriggeredSpellCharges[slot] || wrongCooldown || wrongCatCooldown || wrongCategory || effect.SpellID != item.TriggeredSpellIds[slot])
			{
				if (item.TriggeredSpellIds[slot] > 0)
				{
					Log.Print(LogType.Storage, $"ItemEffect for item #{item.Entry} slot #{slot} needs to be updated.", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					if (effect.TriggerType != item.TriggeredSpellTypes[slot])
					{
						Log.Print(LogType.Storage, $"TriggerType {effect.TriggerType} vs {item.TriggeredSpellTypes[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					if (effect.Charges != item.TriggeredSpellCharges[slot])
					{
						Log.Print(LogType.Storage, $"Charges {effect.Charges} vs {item.TriggeredSpellCharges[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					if (wrongCooldown)
					{
						Log.Print(LogType.Storage, $"CoolDownMSec {effect.CoolDownMSec} vs {item.TriggeredSpellCooldowns[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					if (wrongCatCooldown)
					{
						Log.Print(LogType.Storage, $"CategoryCoolDownMSec {effect.CategoryCoolDownMSec} vs {item.TriggeredSpellCategoryCooldowns[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					if (wrongCategory)
					{
						Log.Print(LogType.Storage, $"SpellCategoryId {effect.SpellCategoryID} vs {item.TriggeredSpellCategories[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					if (effect.SpellID != item.TriggeredSpellIds[slot])
					{
						Log.Print(LogType.Storage, $"SpellId {effect.SpellID} vs {item.TriggeredSpellIds[slot]}", "GenerateItemEffectUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
					}
					effect.TriggerType = (sbyte)item.TriggeredSpellTypes[slot];
					effect.Charges = (short)item.TriggeredSpellCharges[slot];
					effect.CoolDownMSec = (wrongCooldown ? item.TriggeredSpellCooldowns[slot] : (-1));
					effect.CategoryCoolDownMSec = (wrongCatCooldown ? item.TriggeredSpellCategoryCooldowns[slot] : (-1));
					effect.SpellCategoryID = (ushort)(wrongCategory ? ((ushort)item.TriggeredSpellCategories[slot]) : 0);
					effect.SpellID = item.TriggeredSpellIds[slot];
					GameData.UpdateItemEffectRecord(effect, item);
					GameData.UpdateHotfix(effect);
					return GameData.GenerateHotFixMessage(effect);
				}
				GameData.RemoveItemEffectRecord(effect);
				GameData.UpdateHotfix(effect, remove: true);
				return GameData.GenerateHotFixMessage(effect, remove: true);
			}
		}
		else if (item.TriggeredSpellIds[slot] > 0)
		{
			effect = GameData.AddItemEffectRecord(item, slot);
			if (effect == null)
			{
				return null;
			}
			GameData.UpdateHotfix(effect);
			return GameData.GenerateHotFixMessage(effect);
		}
		return null;
	}

	public static HotFixMessage? GenerateItemAppearanceUpdateIfNeeded(ItemTemplate item)
	{
		ItemAppearance appearance = GameData.GetItemAppearanceByDisplayId(item.DisplayID);
		if (appearance == null)
		{
			appearance = GameData.AddItemAppearanceRecord(item);
			if (appearance == null)
			{
				return null;
			}
			GameData.UpdateHotfix(appearance);
			return GameData.GenerateHotFixMessage(appearance);
		}
		return null;
	}

	public static HotFixMessage? GenerateItemModifiedAppearanceUpdateIfNeeded(ItemTemplate item)
	{
		ItemModifiedAppearance modAppearance = GameData.GetItemModifiedAppearanceByItemId(item.Entry);
		if (modAppearance != null)
		{
			GameData.ItemAppearanceStore.TryGetValue((uint)modAppearance.ItemAppearanceID, out var appearance);
			if (appearance == null || appearance.ItemDisplayInfoID != item.DisplayID)
			{
				Log.Print(LogType.Storage, $"ItemModifiedAppearance #{modAppearance.Id} for item #{item.Entry} needs to be updated.", "GenerateItemModifiedAppearanceUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				if (appearance == null)
				{
					Log.Print(LogType.Storage, $"ItemAppearance #{modAppearance.ItemAppearanceID} missing.", "GenerateItemModifiedAppearanceUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				else if (appearance.ItemDisplayInfoID != item.DisplayID)
				{
					Log.Print(LogType.Storage, $"DisplayID {appearance.ItemDisplayInfoID} vs {item.DisplayID}", "GenerateItemModifiedAppearanceUpdateIfNeeded", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				}
				GameData.UpdateItemModifiedAppearanceRecord(modAppearance, item);
				GameData.UpdateHotfix(modAppearance);
				return GameData.GenerateHotFixMessage(modAppearance);
			}
			return null;
		}
		modAppearance = GameData.AddItemModifiedAppearanceRecord(item);
		if (modAppearance == null)
		{
			return null;
		}
		GameData.UpdateHotfix(modAppearance);
		return GameData.GenerateHotFixMessage(modAppearance);
	}

	public static HotFixMessage? GenerateHotFixMessage(object obj, bool remove = false)
	{
		HotFixMessage reply = new HotFixMessage();
		if (obj == null)
		{
			Log.Print(LogType.Error, "DBReply for NULL object requested!", "GenerateHotFixMessage", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
			return null;
		}
		Type type = obj.GetType();
		if (obj is ItemRecord)
		{
			List<HotfixRecord> records = GameData.FindHotfixesByRecordIdAndTable((uint)((ItemRecord)obj).Id, DB2Hash.Item);
			reply.Hotfixes.AddRange(records);
		}
		else if (obj is ItemSparseRecord)
		{
			List<HotfixRecord> records2 = GameData.FindHotfixesByRecordIdAndTable((uint)((ItemSparseRecord)obj).Id, DB2Hash.ItemSparse);
			reply.Hotfixes.AddRange(records2);
		}
		else if (obj is ItemEffect)
		{
			List<HotfixRecord> records3 = GameData.FindHotfixesByRecordIdAndTable((uint)((ItemEffect)obj).Id, DB2Hash.ItemEffect);
			reply.Hotfixes.AddRange(records3);
		}
		else if (obj is ItemAppearance)
		{
			List<HotfixRecord> records4 = GameData.FindHotfixesByRecordIdAndTable((uint)((ItemAppearance)obj).Id, DB2Hash.ItemAppearance);
			reply.Hotfixes.AddRange(records4);
		}
		else
		{
			if (!(obj is ItemModifiedAppearance))
			{
				Log.Print(LogType.Error, $"Unsupported DBReply requested! ({type})", "GenerateHotFixMessage", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
				return null;
			}
			List<HotfixRecord> records5 = GameData.FindHotfixesByRecordIdAndTable((uint)((ItemModifiedAppearance)obj).Id, DB2Hash.ItemModifiedAppearance);
			reply.Hotfixes.AddRange(records5);
		}
		return reply;
	}

	public static ItemRecord AddItemRecord(ItemTemplate item)
	{
		ItemRecord record = new ItemRecord();
		record.Id = (int)item.Entry;
		GameData.UpdateItemRecord(record, item);
		GameData.ItemRecordsStore.Add((uint)record.Id, record);
		Log.Print(LogType.Storage, $"Item #{record.Id} created.", "AddItemRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		return record;
	}

	public static void UpdateItemRecord(ItemRecord row, ItemTemplate item)
	{
		row.ClassId = (byte)item.Class;
		row.SubclassId = (byte)item.SubClass;
		row.Material = (byte)item.Material;
		row.InventoryType = (sbyte)item.InventoryType;
		row.RequiredLevel = (int)item.RequiredLevel;
		row.SheatheType = (byte)item.SheathType;
		row.RandomProperty = (ushort)item.RandomProperty;
		row.ItemRandomSuffixGroupId = (ushort)item.RandomSuffix;
		row.SoundOverrideSubclassId = -1;
		row.ScalingStatDistributionId = 0;
		row.IconFileDataId = (int)GameData.GetItemIconFileDataIdByDisplayId(item.DisplayID);
		row.ItemGroupSoundsId = 0;
		row.ContentTuningId = 0;
		row.MaxDurability = item.MaxDurability;
		row.AmmoType = (byte)item.AmmoType;
		row.DamageType[0] = (byte)item.DamageTypes[0];
		row.DamageType[1] = (byte)item.DamageTypes[1];
		row.DamageType[2] = (byte)item.DamageTypes[2];
		row.DamageType[3] = (byte)item.DamageTypes[3];
		row.DamageType[4] = (byte)item.DamageTypes[4];
		row.Resistances[0] = (short)item.Armor;
		row.Resistances[1] = (short)item.HolyResistance;
		row.Resistances[2] = (short)item.FireResistance;
		row.Resistances[3] = (short)item.NatureResistance;
		row.Resistances[4] = (short)item.FrostResistance;
		row.Resistances[5] = (short)item.ShadowResistance;
		row.Resistances[6] = (short)item.ArcaneResistance;
		row.MinDamage[0] = (ushort)item.DamageMins[0];
		row.MinDamage[1] = (ushort)item.DamageMins[1];
		row.MinDamage[2] = (ushort)item.DamageMins[2];
		row.MinDamage[3] = (ushort)item.DamageMins[3];
		row.MinDamage[4] = (ushort)item.DamageMins[4];
		row.MaxDamage[0] = (ushort)item.DamageMaxs[0];
		row.MaxDamage[1] = (ushort)item.DamageMaxs[1];
		row.MaxDamage[2] = (ushort)item.DamageMaxs[2];
		row.MaxDamage[3] = (ushort)item.DamageMaxs[3];
		row.MaxDamage[4] = (ushort)item.DamageMaxs[4];
		if (GameData.ItemRecordsStore.ContainsKey(item.Entry))
		{
			GameData.ItemRecordsStore[item.Entry] = row;
		}
	}

	public static ItemSparseRecord AddItemSparseRecord(ItemTemplate item)
	{
		ItemSparseRecord record = new ItemSparseRecord();
		record.Id = (int)item.Entry;
		GameData.UpdateItemSparseRecord(record, item);
		GameData.ItemSparseRecordsStore.Add((uint)record.Id, record);
		Log.Print(LogType.Storage, $"ItemSparse #{record.Id} created.", "AddItemSparseRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		return record;
	}

	public static void UpdateItemSparseRecord(ItemSparseRecord row, ItemTemplate item)
	{
		int[] StatValues = new int[10];
		for (int i = 0; i < item.StatsCount; i++)
		{
			StatValues[i] = item.StatValues[i];
			if (StatValues[i] > 127)
			{
				StatValues[i] = 127;
			}
			if (StatValues[i] < -127)
			{
				StatValues[i] = -127;
			}
		}
		row.AllowableRace = item.AllowedRaces;
		row.Description = item.Description;
		row.Name4 = item.Name[3];
		row.Name3 = item.Name[2];
		row.Name2 = item.Name[1];
		row.Name1 = item.Name[0];
		row.DurationInInventory = item.Duration;
		row.BagFamily = item.BagFamily;
		row.RangeMod = item.RangedMod;
		row.Stackable = item.MaxStackSize;
		row.MaxCount = item.MaxCount;
		row.RequiredAbility = item.RequiredSpell;
		row.SellPrice = item.SellPrice;
		row.BuyPrice = item.BuyPrice;
		row.Flags[0] = item.Flags;
		row.Flags[1] = item.FlagsExtra;
		row.MaxDurability = item.MaxDurability;
		row.RequiredHoliday = (ushort)item.HolidayID;
		row.LimitCategory = (ushort)item.ItemLimitCategory;
		row.GemProperties = (ushort)item.GemProperties;
		row.SocketMatchEnchantmentId = (ushort)item.SocketBonus;
		row.TotemCategoryId = (ushort)item.TotemCategory;
		row.InstanceBound = (ushort)item.MapID;
		row.ZoneBound[0] = (ushort)item.AreaID;
		row.ItemSet = (ushort)item.ItemSet;
		row.LockId = (ushort)item.LockId;
		row.StartQuestId = (ushort)item.StartQuestId;
		row.PageText = (ushort)item.PageText;
		row.Delay = (ushort)item.Delay;
		row.RequiredReputationId = (ushort)item.RequiredRepFaction;
		row.RequiredSkillRank = (ushort)item.RequiredSkillLevel;
		row.RequiredSkill = (ushort)item.RequiredSkillId;
		row.ItemLevel = (ushort)item.ItemLevel;
		row.AllowableClass = (short)item.AllowedClasses;
		row.ItemRandomSuffixGroupId = (ushort)item.RandomSuffix;
		row.RandomProperty = (ushort)item.RandomProperty;
		row.MinDamage[0] = (ushort)item.DamageMins[0];
		row.MinDamage[1] = (ushort)item.DamageMins[1];
		row.MinDamage[2] = (ushort)item.DamageMins[2];
		row.MinDamage[3] = (ushort)item.DamageMins[3];
		row.MinDamage[4] = (ushort)item.DamageMins[4];
		row.MaxDamage[0] = (ushort)item.DamageMaxs[0];
		row.MaxDamage[1] = (ushort)item.DamageMaxs[1];
		row.MaxDamage[2] = (ushort)item.DamageMaxs[2];
		row.MaxDamage[3] = (ushort)item.DamageMaxs[3];
		row.MaxDamage[4] = (ushort)item.DamageMaxs[4];
		row.Resistances[0] = (short)item.Armor;
		row.Resistances[1] = (short)item.HolyResistance;
		row.Resistances[2] = (short)item.FireResistance;
		row.Resistances[3] = (short)item.NatureResistance;
		row.Resistances[4] = (short)item.FrostResistance;
		row.Resistances[5] = (short)item.ShadowResistance;
		row.Resistances[6] = (short)item.ArcaneResistance;
		row.ScalingStatDistributionId = (ushort)item.ScalingStatDistribution;
		row.SocketType[0] = ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[0]);
		row.SocketType[1] = ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[1]);
		row.SocketType[2] = ModernVersion.ConvertSocketColor((byte)item.ItemSocketColors[2]);
		row.SheatheType = (byte)item.SheathType;
		row.Material = (byte)item.Material;
		row.PageMaterial = (byte)item.PageMaterial;
		row.PageLanguage = (byte)item.Language;
		row.Bonding = (byte)item.Bonding;
		row.DamageType = (byte)item.DamageTypes[0];
		row.StatType[0] = (sbyte)item.StatTypes[0];
		row.StatType[1] = (sbyte)item.StatTypes[1];
		row.StatType[2] = (sbyte)item.StatTypes[2];
		row.StatType[3] = (sbyte)item.StatTypes[3];
		row.StatType[4] = (sbyte)item.StatTypes[4];
		row.StatType[5] = (sbyte)item.StatTypes[5];
		row.StatType[6] = (sbyte)item.StatTypes[6];
		row.StatType[7] = (sbyte)item.StatTypes[7];
		row.StatType[8] = (sbyte)item.StatTypes[8];
		row.StatType[9] = (sbyte)item.StatTypes[9];
		row.ContainerSlots = (byte)item.ContainerSlots;
		row.RequiredReputationRank = (byte)item.RequiredRepValue;
		row.RequiredCityRank = (byte)item.RequiredCityRank;
		row.RequiredHonorRank = (byte)item.RequiredHonorRank;
		row.InventoryType = (byte)item.InventoryType;
		row.OverallQualityId = (byte)item.Quality;
		row.AmmoType = (byte)item.AmmoType;
		row.StatValue[0] = (sbyte)StatValues[0];
		row.StatValue[1] = (sbyte)StatValues[1];
		row.StatValue[2] = (sbyte)StatValues[2];
		row.StatValue[3] = (sbyte)StatValues[3];
		row.StatValue[4] = (sbyte)StatValues[4];
		row.StatValue[5] = (sbyte)StatValues[5];
		row.StatValue[6] = (sbyte)StatValues[6];
		row.StatValue[7] = (sbyte)StatValues[7];
		row.StatValue[8] = (sbyte)StatValues[8];
		row.StatValue[9] = (sbyte)StatValues[9];
		row.RequiredLevel = (sbyte)item.RequiredLevel;
		if (GameData.ItemSparseRecordsStore.ContainsKey(item.Entry))
		{
			GameData.ItemSparseRecordsStore[item.Entry] = row;
		}
	}

	public static ItemEffect AddItemEffectRecord(ItemTemplate item, byte slot)
	{
		ItemEffect record = new ItemEffect();
		record.Id = (int)GameData.GetFirstFreeId(GameData.ItemEffectStore);
		record.LegacySlotIndex = slot;
		GameData.UpdateItemEffectRecord(record, item);
		GameData.ItemEffectStore.Add((uint)record.Id, record);
		Log.Print(LogType.Storage, $"ItemEffect #{record.Id} created for item #{item.Entry} slot #{slot}.", "AddItemEffectRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		return record;
	}

	public static void UpdateItemEffectRecord(ItemEffect effect, ItemTemplate item)
	{
		byte i = effect.LegacySlotIndex;
		effect.TriggerType = (sbyte)item.TriggeredSpellTypes[i];
		effect.Charges = (short)item.TriggeredSpellCharges[i];
		effect.CoolDownMSec = item.TriggeredSpellCooldowns[i];
		effect.CategoryCoolDownMSec = item.TriggeredSpellCategoryCooldowns[i];
		effect.SpellCategoryID = (ushort)item.TriggeredSpellCategories[i];
		effect.SpellID = item.TriggeredSpellIds[i];
		effect.ChrSpecializationID = 0;
		effect.ParentItemID = (int)item.Entry;
		if (GameData.ItemEffectStore.ContainsKey((uint)effect.Id))
		{
			GameData.ItemEffectStore[(uint)effect.Id] = effect;
		}
	}

	public static void RemoveItemEffectRecord(ItemEffect effect)
	{
		GameData.ItemEffectStore.Remove((uint)effect.Id);
		Log.Print(LogType.Storage, $"ItemEffect #{effect.Id} removed for item #{effect.ParentItemID} slot #{effect.LegacySlotIndex}.", "RemoveItemEffectRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
	}

	public static ItemAppearance AddItemAppearanceRecord(ItemTemplate item)
	{
		ItemAppearance record = new ItemAppearance();
		record.Id = (int)GameData.GetFirstFreeId(GameData.ItemAppearanceStore);
		GameData.UpdateItemAppearanceRecord(record, item);
		GameData.ItemAppearanceStore.Add((uint)record.Id, record);
		Log.Print(LogType.Storage, $"ItemAppearance #{record.Id} created for DisplayID #{item.DisplayID}.", "AddItemAppearanceRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		return record;
	}

	public static void UpdateItemAppearanceRecord(ItemAppearance appearance, ItemTemplate item)
	{
		int fileDataId = (int)GameData.GetItemIconFileDataIdByDisplayId(item.DisplayID);
		appearance.DisplayType = 11;
		appearance.ItemDisplayInfoID = (int)item.DisplayID;
		appearance.DefaultIconFileDataID = fileDataId;
		appearance.UiOrder = 0;
		if (GameData.ItemAppearanceStore.ContainsKey((uint)appearance.Id))
		{
			GameData.ItemAppearanceStore[(uint)appearance.Id] = appearance;
		}
	}

	public static ItemModifiedAppearance AddItemModifiedAppearanceRecord(ItemTemplate item)
	{
		ItemModifiedAppearance record = new ItemModifiedAppearance();
		record.Id = (int)GameData.GetFirstFreeId(GameData.ItemModifiedAppearanceStore);
		GameData.UpdateItemModifiedAppearanceRecord(record, item);
		if (record.ItemID != item.Entry)
		{
			Log.Print(LogType.Error, $"ItemModifiedAppearance #{record.Id} create failed for item #{record.ItemID}.", "AddItemModifiedAppearanceRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
			return null;
		}
		GameData.ItemModifiedAppearanceStore.Add((uint)record.Id, record);
		Log.Print(LogType.Storage, $"ItemModifiedAppearance #{record.Id} created for item #{record.ItemID}.", "AddItemModifiedAppearanceRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
		return record;
	}

	public static void UpdateItemModifiedAppearanceRecord(ItemModifiedAppearance modAppearance, ItemTemplate item)
	{
		ItemAppearance appearance = GameData.GetItemAppearanceByDisplayId(item.DisplayID);
		if (appearance == null)
		{
			Log.Print(LogType.Error, $"ItemModifiedAppearance #{modAppearance.Id} update failed: no ItemAppearance for DisplayID #{item.DisplayID}", "UpdateItemModifiedAppearanceRecord", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\GameData.cs");
			return;
		}
		modAppearance.ItemID = (int)item.Entry;
		modAppearance.ItemAppearanceModifierID = 0;
		modAppearance.ItemAppearanceID = appearance.Id;
		modAppearance.OrderIndex = 0;
		modAppearance.TransmogSourceTypeEnum = 0;
		if (GameData.ItemModifiedAppearanceStore.ContainsKey((uint)modAppearance.Id))
		{
			GameData.ItemModifiedAppearanceStore[(uint)modAppearance.Id] = modAppearance;
		}
	}

	public static bool ItemCanHaveModel(ItemTemplate item)
	{
		if (item.Class == 2)
		{
			return true;
		}
		if (item.Class == 4 && item.SubClass != 7 && item.SubClass != 8 && item.SubClass != 9 && item.InventoryType != 0 && item.InventoryType != 2 && item.InventoryType != 11 && item.InventoryType != 12 && item.InventoryType != 18 && item.InventoryType != 28)
		{
			return true;
		}
		if (item.Class == 11 && item.SubClass == 2)
		{
			return true;
		}
		return false;
	}

	public static void LoadCreatureDisplayInfoHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"CreatureDisplayInfo{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			ushort modelId = ushort.Parse(fields[1]);
			ushort soundId = ushort.Parse(fields[2]);
			sbyte sizeClass = sbyte.Parse(fields[3]);
			float creatureModelScale = float.Parse(fields[4]);
			byte creatureModelAlpha = byte.Parse(fields[5]);
			byte bloodId = byte.Parse(fields[6]);
			int extendedDisplayInfoId = int.Parse(fields[7]);
			ushort nPCSoundId = ushort.Parse(fields[8]);
			ushort particleColorId = ushort.Parse(fields[9]);
			int portraitCreatureDisplayInfoId = int.Parse(fields[10]);
			int portraitTextureFileDataId = int.Parse(fields[11]);
			ushort objectEffectPackageId = ushort.Parse(fields[12]);
			ushort animReplacementSetId = ushort.Parse(fields[13]);
			byte flags = byte.Parse(fields[14]);
			int stateSpellVisualKitId = int.Parse(fields[15]);
			float playerOverrideScale = float.Parse(fields[16]);
			float petInstanceScale = float.Parse(fields[17]);
			sbyte unarmedWeaponType = sbyte.Parse(fields[18]);
			int mountPoofSpellVisualKitId = int.Parse(fields[19]);
			int dissolveEffectId = int.Parse(fields[20]);
			sbyte gender = sbyte.Parse(fields[21]);
			int dissolveOutEffectId = int.Parse(fields[22]);
			sbyte creatureModelMinLod = sbyte.Parse(fields[23]);
			int textureVariationFileDataId1 = int.Parse(fields[24]);
			int textureVariationFileDataId2 = int.Parse(fields[25]);
			int textureVariationFileDataId3 = int.Parse(fields[26]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.CreatureDisplayInfo;
			record.HotfixId = 270000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt32(id);
			record.HotfixContent.WriteUInt16(modelId);
			record.HotfixContent.WriteUInt16(soundId);
			record.HotfixContent.WriteInt8(sizeClass);
			record.HotfixContent.WriteFloat(creatureModelScale);
			record.HotfixContent.WriteUInt8(creatureModelAlpha);
			record.HotfixContent.WriteUInt8(bloodId);
			record.HotfixContent.WriteInt32(extendedDisplayInfoId);
			record.HotfixContent.WriteUInt16(nPCSoundId);
			record.HotfixContent.WriteUInt16(particleColorId);
			record.HotfixContent.WriteInt32(portraitCreatureDisplayInfoId);
			record.HotfixContent.WriteInt32(portraitTextureFileDataId);
			record.HotfixContent.WriteUInt16(objectEffectPackageId);
			record.HotfixContent.WriteUInt16(animReplacementSetId);
			record.HotfixContent.WriteUInt8(flags);
			record.HotfixContent.WriteInt32(stateSpellVisualKitId);
			record.HotfixContent.WriteFloat(playerOverrideScale);
			record.HotfixContent.WriteFloat(petInstanceScale);
			record.HotfixContent.WriteInt8(unarmedWeaponType);
			record.HotfixContent.WriteInt32(mountPoofSpellVisualKitId);
			record.HotfixContent.WriteInt32(dissolveEffectId);
			record.HotfixContent.WriteInt8(gender);
			record.HotfixContent.WriteInt32(dissolveOutEffectId);
			record.HotfixContent.WriteInt8(creatureModelMinLod);
			record.HotfixContent.WriteInt32(textureVariationFileDataId1);
			record.HotfixContent.WriteInt32(textureVariationFileDataId2);
			record.HotfixContent.WriteInt32(textureVariationFileDataId3);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadCreatureDisplayInfoExtraHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"CreatureDisplayInfoExtra{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			sbyte displayRaceId = sbyte.Parse(fields[1]);
			sbyte displaySexId = sbyte.Parse(fields[2]);
			sbyte displayClassId = sbyte.Parse(fields[3]);
			sbyte skinId = sbyte.Parse(fields[4]);
			sbyte faceId = sbyte.Parse(fields[5]);
			sbyte hairStyleId = sbyte.Parse(fields[6]);
			sbyte hairColorId = sbyte.Parse(fields[7]);
			sbyte facialHairId = sbyte.Parse(fields[8]);
			sbyte flags = sbyte.Parse(fields[9]);
			int bakeMaterialResourcesId = int.Parse(fields[10]);
			int hDBakeMaterialResourcesId = int.Parse(fields[11]);
			byte customDisplayOption1 = byte.Parse(fields[12]);
			byte customDisplayOption2 = byte.Parse(fields[13]);
			byte customDisplayOption3 = byte.Parse(fields[14]);
			HotfixRecord record = new HotfixRecord();
			record.TableHash = DB2Hash.CreatureDisplayInfoExtra;
			record.HotfixId = 280000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.Status = HotfixStatus.Valid;
			record.HotfixContent.WriteUInt32(id);
			record.HotfixContent.WriteInt8(displayRaceId);
			record.HotfixContent.WriteInt8(displaySexId);
			record.HotfixContent.WriteInt8(displayClassId);
			record.HotfixContent.WriteInt8(skinId);
			record.HotfixContent.WriteInt8(faceId);
			record.HotfixContent.WriteInt8(hairStyleId);
			record.HotfixContent.WriteInt8(hairColorId);
			record.HotfixContent.WriteInt8(facialHairId);
			record.HotfixContent.WriteInt8(flags);
			record.HotfixContent.WriteInt32(bakeMaterialResourcesId);
			record.HotfixContent.WriteInt32(hDBakeMaterialResourcesId);
			record.HotfixContent.WriteUInt8(customDisplayOption1);
			record.HotfixContent.WriteUInt8(customDisplayOption2);
			record.HotfixContent.WriteUInt8(customDisplayOption3);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadCreatureDisplayInfoOptionHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"CreatureDisplayInfoOption{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			int chrCustomizationOptionId = int.Parse(fields[1]);
			int chrCustomizationChoiceId = int.Parse(fields[2]);
			int creatureDisplayInfoExtraId = int.Parse(fields[3]);
			HotfixRecord record = new HotfixRecord();
			record.Status = HotfixStatus.Valid;
			record.TableHash = DB2Hash.CreatureDisplayInfoOption;
			record.HotfixId = 290000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.HotfixContent.WriteInt32(chrCustomizationOptionId);
			record.HotfixContent.WriteInt32(chrCustomizationChoiceId);
			record.HotfixContent.WriteInt32(creatureDisplayInfoExtraId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadItemEffectHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"ItemEffect{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			byte legacySlotIndex = byte.Parse(fields[1]);
			byte triggerType = byte.Parse(fields[2]);
			short charges = short.Parse(fields[3]);
			int coolDownMSec = int.Parse(fields[4]);
			int categoryCoolDownMSec = int.Parse(fields[5]);
			short spellCategoryId = short.Parse(fields[6]);
			int spellId = int.Parse(fields[7]);
			short chrSpecializationId = short.Parse(fields[8]);
			int parentItemId = int.Parse(fields[9]);
			HotfixRecord record = new HotfixRecord();
			record.Status = HotfixStatus.Valid;
			record.TableHash = DB2Hash.ItemEffect;
			record.HotfixId = 250000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.HotfixContent.WriteUInt8(legacySlotIndex);
			record.HotfixContent.WriteUInt8(triggerType);
			record.HotfixContent.WriteInt16(charges);
			record.HotfixContent.WriteInt32(coolDownMSec);
			record.HotfixContent.WriteInt32(categoryCoolDownMSec);
			record.HotfixContent.WriteInt16(spellCategoryId);
			record.HotfixContent.WriteInt32(spellId);
			record.HotfixContent.WriteInt16(chrSpecializationId);
			record.HotfixContent.WriteInt32(parentItemId);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}

	public static void LoadItemDisplayInfoHotfixes()
	{
		string path = Path.Combine("CSV", "Hotfix", $"ItemDisplayInfo{ModernVersion.ExpansionVersion}.csv");
		using TextFieldParser csvParser = new TextFieldParser(path);
		csvParser.CommentTokens = new string[1] { "#" };
		csvParser.SetDelimiters(",");
		csvParser.HasFieldsEnclosedInQuotes = false;
		csvParser.ReadLine();
		uint counter = 0u;
		while (!csvParser.EndOfData)
		{
			counter++;
			string[] fields = csvParser.ReadFields();
			uint id = uint.Parse(fields[0]);
			int itemVisual = int.Parse(fields[1]);
			int particleColorID = int.Parse(fields[2]);
			uint itemRangedDisplayInfoID = uint.Parse(fields[3]);
			uint overrideSwooshSoundKitID = uint.Parse(fields[4]);
			int sheatheTransformMatrixID = int.Parse(fields[5]);
			int stateSpellVisualKitID = int.Parse(fields[6]);
			int sheathedSpellVisualKitID = int.Parse(fields[7]);
			uint unsheathedSpellVisualKitID = uint.Parse(fields[8]);
			int flags = int.Parse(fields[9]);
			uint modelResourcesID1 = uint.Parse(fields[10]);
			uint modelResourcesID2 = uint.Parse(fields[11]);
			int modelMaterialResourcesID1 = int.Parse(fields[12]);
			int modelMaterialResourcesID2 = int.Parse(fields[13]);
			int modelType1 = int.Parse(fields[14]);
			int modelType2 = int.Parse(fields[15]);
			int geosetGroup1 = int.Parse(fields[16]);
			int geosetGroup2 = int.Parse(fields[17]);
			int geosetGroup3 = int.Parse(fields[18]);
			int geosetGroup4 = int.Parse(fields[19]);
			int geosetGroup5 = int.Parse(fields[20]);
			int geosetGroup6 = int.Parse(fields[21]);
			int attachmentGeosetGroup1 = int.Parse(fields[22]);
			int attachmentGeosetGroup2 = int.Parse(fields[23]);
			int attachmentGeosetGroup3 = int.Parse(fields[24]);
			int attachmentGeosetGroup4 = int.Parse(fields[25]);
			int attachmentGeosetGroup5 = int.Parse(fields[26]);
			int attachmentGeosetGroup6 = int.Parse(fields[27]);
			int helmetGeosetVis1 = int.Parse(fields[28]);
			int helmetGeosetVis2 = int.Parse(fields[29]);
			HotfixRecord record = new HotfixRecord();
			record.Status = HotfixStatus.Valid;
			record.TableHash = DB2Hash.ItemDisplayInfo;
			record.HotfixId = 260000 + counter;
			record.UniqueId = record.HotfixId;
			record.RecordId = id;
			record.HotfixContent.WriteInt32(itemVisual);
			record.HotfixContent.WriteInt32(particleColorID);
			record.HotfixContent.WriteUInt32(itemRangedDisplayInfoID);
			record.HotfixContent.WriteUInt32(overrideSwooshSoundKitID);
			record.HotfixContent.WriteInt32(sheatheTransformMatrixID);
			record.HotfixContent.WriteInt32(stateSpellVisualKitID);
			record.HotfixContent.WriteInt32(sheathedSpellVisualKitID);
			record.HotfixContent.WriteUInt32(unsheathedSpellVisualKitID);
			record.HotfixContent.WriteInt32(flags);
			record.HotfixContent.WriteUInt32(modelResourcesID1);
			record.HotfixContent.WriteUInt32(modelResourcesID2);
			record.HotfixContent.WriteInt32(modelMaterialResourcesID1);
			record.HotfixContent.WriteInt32(modelMaterialResourcesID2);
			record.HotfixContent.WriteInt32(modelType1);
			record.HotfixContent.WriteInt32(modelType2);
			record.HotfixContent.WriteInt32(geosetGroup1);
			record.HotfixContent.WriteInt32(geosetGroup2);
			record.HotfixContent.WriteInt32(geosetGroup3);
			record.HotfixContent.WriteInt32(geosetGroup4);
			record.HotfixContent.WriteInt32(geosetGroup5);
			record.HotfixContent.WriteInt32(geosetGroup6);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup1);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup2);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup3);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup4);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup5);
			record.HotfixContent.WriteInt32(attachmentGeosetGroup6);
			record.HotfixContent.WriteInt32(helmetGeosetVis1);
			record.HotfixContent.WriteInt32(helmetGeosetVis2);
			GameData.Hotfixes.Add(record.HotfixId, record);
		}
	}
}
