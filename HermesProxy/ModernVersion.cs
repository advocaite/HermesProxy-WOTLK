using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Framework;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Enums.V1_14_1_40688;
using HermesProxy.World.Enums.V2_5_2_39570;

namespace HermesProxy;

public static class ModernVersion
{
	private static readonly Dictionary<uint, HermesProxy.World.Enums.Opcode> CurrentToUniversalOpcodeDictionary;

	private static readonly Dictionary<HermesProxy.World.Enums.Opcode, uint> UniversalToCurrentOpcodeDictionary;

	private static readonly Dictionary<Type, SortedList<int, UpdateFieldInfo>> UpdateFieldDictionary;

	private static readonly Dictionary<Type, Dictionary<string, int>> UpdateFieldNameDictionary;

	public static byte ExpansionVersion { get; private set; }

	public static byte MajorVersion { get; private set; }

	public static byte MinorVersion { get; private set; }

	public static ClientVersionBuild Build { get; private set; }

	public static int BuildInt => (int)ModernVersion.Build;

	public static string VersionString => ModernVersion.Build.ToString();

	static ModernVersion()
	{
		ModernVersion.CurrentToUniversalOpcodeDictionary = new Dictionary<uint, HermesProxy.World.Enums.Opcode>();
		ModernVersion.UniversalToCurrentOpcodeDictionary = new Dictionary<HermesProxy.World.Enums.Opcode, uint>();
		ModernVersion.Build = Settings.ClientBuild;
		ModernVersion.ExpansionVersion = ModernVersion.GetExpansionVersion();
		ModernVersion.MajorVersion = ModernVersion.GetMajorPatchVersion();
		ModernVersion.MinorVersion = ModernVersion.GetMinorPatchVersion();
		ModernVersion.UpdateFieldDictionary = new Dictionary<Type, SortedList<int, UpdateFieldInfo>>();
		ModernVersion.UpdateFieldNameDictionary = new Dictionary<Type, Dictionary<string, int>>();
		if (!ModernVersion.LoadUFDictionariesInto(ModernVersion.UpdateFieldDictionary, ModernVersion.UpdateFieldNameDictionary))
		{
			Log.Print(LogType.Error, "Could not load update fields for current modern version.", ".cctor", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\VersionChecker.cs");
		}
		if (!ModernVersion.LoadOpcodeDictionaries())
		{
			Log.Print(LogType.Error, "Could not load opcodes for current modern version.", ".cctor", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\VersionChecker.cs");
		}
	}

	private static bool LoadOpcodeDictionaries()
	{
		Type enumType = Opcodes.GetOpcodesEnumForVersion(ModernVersion.Build);
		if (enumType == null)
		{
			return false;
		}
		foreach (object item in Enum.GetValues(enumType))
		{
			string oldOpcodeName = Enum.GetName(enumType, item);
			if ((uint)item == 0 && oldOpcodeName != "MSG_NULL_ACTION")
			{
				continue;
			}
			HermesProxy.World.Enums.Opcode universalOpcode = Opcodes.GetUniversalOpcode(oldOpcodeName);
			if (universalOpcode == HermesProxy.World.Enums.Opcode.MSG_NULL_ACTION && oldOpcodeName != "MSG_NULL_ACTION")
			{
				Log.Print(LogType.Error, "Opcode " + oldOpcodeName + " is missing from the universal opcode enum!", "LoadOpcodeDictionaries", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\VersionChecker.cs");
				continue;
			}
			if (!ModernVersion.CurrentToUniversalOpcodeDictionary.ContainsKey((uint)item))
			{
				ModernVersion.CurrentToUniversalOpcodeDictionary.Add((uint)item, universalOpcode);
			}
			if (!ModernVersion.UniversalToCurrentOpcodeDictionary.ContainsKey(universalOpcode))
			{
				ModernVersion.UniversalToCurrentOpcodeDictionary.Add(universalOpcode, (uint)item);
			}
		}
		if (ModernVersion.CurrentToUniversalOpcodeDictionary.Count < 1)
		{
			return false;
		}
		Log.Print(LogType.Server, $"Loaded {ModernVersion.CurrentToUniversalOpcodeDictionary.Count} modern opcodes.", "LoadOpcodeDictionaries", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\VersionChecker.cs");
		return true;
	}

	public static HermesProxy.World.Enums.Opcode GetUniversalOpcode(uint opcode)
	{
		if (ModernVersion.CurrentToUniversalOpcodeDictionary.TryGetValue(opcode, out var universalOpcode))
		{
			return universalOpcode;
		}
		return HermesProxy.World.Enums.Opcode.MSG_NULL_ACTION;
	}

	public static uint GetCurrentOpcode(HermesProxy.World.Enums.Opcode universalOpcode)
	{
		if (ModernVersion.UniversalToCurrentOpcodeDictionary.TryGetValue(universalOpcode, out var opcode))
		{
			return opcode;
		}
		return 0u;
	}

	public static ClientVersionBuild GetUpdateFieldsDefiningBuild()
	{
		return ModernVersion.GetUpdateFieldsDefiningBuild(ModernVersion.Build);
	}

	public static ClientVersionBuild GetUpdateFieldsDefiningBuild(ClientVersionBuild version)
	{
		switch (version)
		{
		case ClientVersionBuild.V1_14_0_39802:
		case ClientVersionBuild.V1_14_0_39958:
		case ClientVersionBuild.V1_14_0_40140:
		case ClientVersionBuild.V1_14_0_40179:
		case ClientVersionBuild.V1_14_0_40237:
		case ClientVersionBuild.V1_14_0_40347:
		case ClientVersionBuild.V1_14_0_40441:
		case ClientVersionBuild.V1_14_0_40618:
			return ClientVersionBuild.V1_14_0_40237;
		case ClientVersionBuild.V1_14_1_40487:
		case ClientVersionBuild.V1_14_1_40594:
		case ClientVersionBuild.V1_14_1_40666:
		case ClientVersionBuild.V1_14_1_40688:
		case ClientVersionBuild.V1_14_1_40800:
		case ClientVersionBuild.V1_14_1_40818:
		case ClientVersionBuild.V1_14_1_40926:
		case ClientVersionBuild.V1_14_1_40962:
		case ClientVersionBuild.V1_14_1_41009:
		case ClientVersionBuild.V1_14_1_41030:
		case ClientVersionBuild.V1_14_1_41077:
		case ClientVersionBuild.V1_14_1_41137:
		case ClientVersionBuild.V1_14_1_41243:
		case ClientVersionBuild.V1_14_1_41511:
		case ClientVersionBuild.V1_14_1_41794:
		case ClientVersionBuild.V1_14_2_41858:
		case ClientVersionBuild.V1_14_2_41959:
		case ClientVersionBuild.V1_14_1_42032:
		case ClientVersionBuild.V1_14_2_42065:
		case ClientVersionBuild.V1_14_2_42082:
		case ClientVersionBuild.V1_14_2_42214:
		case ClientVersionBuild.V1_14_2_42597:
			return ClientVersionBuild.V1_14_1_40688;
		case ClientVersionBuild.V2_5_2_39570:
		case ClientVersionBuild.V2_5_2_39618:
		case ClientVersionBuild.V2_5_2_39926:
		case ClientVersionBuild.V2_5_2_40011:
		case ClientVersionBuild.V2_5_2_40045:
		case ClientVersionBuild.V2_5_2_40203:
		case ClientVersionBuild.V2_5_2_40260:
		case ClientVersionBuild.V2_5_2_40422:
		case ClientVersionBuild.V2_5_2_40488:
		case ClientVersionBuild.V2_5_2_40617:
		case ClientVersionBuild.V2_5_2_40892:
		case ClientVersionBuild.V2_5_2_41446:
		case ClientVersionBuild.V2_5_2_41510:
			return ClientVersionBuild.V2_5_2_39570;
		case ClientVersionBuild.V2_5_3_41402:
		case ClientVersionBuild.V2_5_3_41531:
		case ClientVersionBuild.V2_5_3_41750:
		case ClientVersionBuild.V2_5_3_41812:
		case ClientVersionBuild.V2_5_3_42083:
		case ClientVersionBuild.V2_5_3_42328:
		case ClientVersionBuild.V2_5_3_42598:
			return ClientVersionBuild.V2_5_3_41750;
		case ClientVersionBuild.V3_4_3_54261:
			return ClientVersionBuild.V3_4_3_54261;
		default:
			return ClientVersionBuild.Zero;
		}
	}

	private static bool LoadUFDictionariesInto(Dictionary<Type, SortedList<int, UpdateFieldInfo>> dicts, Dictionary<Type, Dictionary<string, int>> nameToValueDict)
	{
		Type[] enumTypes = new Type[28]
		{
			typeof(HermesProxy.World.Enums.ObjectField),
			typeof(HermesProxy.World.Enums.ItemField),
			typeof(HermesProxy.World.Enums.ContainerField),
			typeof(AzeriteEmpoweredItemField),
			typeof(AzeriteItemField),
			typeof(HermesProxy.World.Enums.UnitField),
			typeof(HermesProxy.World.Enums.PlayerField),
			typeof(HermesProxy.World.Enums.ActivePlayerField),
			typeof(HermesProxy.World.Enums.GameObjectField),
			typeof(HermesProxy.World.Enums.DynamicObjectField),
			typeof(HermesProxy.World.Enums.CorpseField),
			typeof(HermesProxy.World.Enums.AreaTriggerField),
			typeof(HermesProxy.World.Enums.SceneObjectField),
			typeof(HermesProxy.World.Enums.ConversationField),
			typeof(HermesProxy.World.Enums.ObjectDynamicField),
			typeof(HermesProxy.World.Enums.ItemDynamicField),
			typeof(HermesProxy.World.Enums.ContainerDynamicField),
			typeof(AzeriteEmpoweredItemDynamicField),
			typeof(AzeriteItemDynamicField),
			typeof(HermesProxy.World.Enums.UnitDynamicField),
			typeof(HermesProxy.World.Enums.PlayerDynamicField),
			typeof(HermesProxy.World.Enums.ActivePlayerDynamicField),
			typeof(HermesProxy.World.Enums.GameObjectDynamicField),
			typeof(HermesProxy.World.Enums.DynamicObjectDynamicField),
			typeof(HermesProxy.World.Enums.CorpseDynamicField),
			typeof(HermesProxy.World.Enums.AreaTriggerDynamicField),
			typeof(HermesProxy.World.Enums.SceneObjectDynamicField),
			typeof(HermesProxy.World.Enums.ConversationDynamicField)
		};
		ClientVersionBuild ufDefiningBuild = ModernVersion.GetUpdateFieldsDefiningBuild(ModernVersion.Build);
		bool loaded = false;
		Type[] array = enumTypes;
		foreach (Type enumType in array)
		{
			string vTypeString = "HermesProxy.World.Enums." + ufDefiningBuild.ToString() + "." + enumType.Name;
			Type vEnumType = Assembly.GetExecutingAssembly().GetType(vTypeString);
			if (vEnumType == null)
			{
				vTypeString = "HermesProxy.World.Enums." + ufDefiningBuild.ToString() + "." + enumType.Name;
				vEnumType = Assembly.GetExecutingAssembly().GetType(vTypeString);
				if (vEnumType == null)
				{
					continue;
				}
			}
			Array vValues = Enum.GetValues(vEnumType);
			string[] vNames = Enum.GetNames(vEnumType);
			SortedList<int, UpdateFieldInfo> result = new SortedList<int, UpdateFieldInfo>(vValues.Length);
			Dictionary<string, int> namesResult = new Dictionary<string, int>(vNames.Length);
			for (int j = 0; j < vValues.Length; j++)
			{
				UpdateFieldType format = (from attribute in enumType.GetMember(vNames[j]).SelectMany((MemberInfo member) => member.GetCustomAttributes(typeof(UpdateFieldAttribute), inherit: false))
					where ((UpdateFieldAttribute)attribute).Version <= ModernVersion.Build
					orderby ((UpdateFieldAttribute)attribute).Version descending
					select ((UpdateFieldAttribute)attribute).UFAttribute).DefaultIfEmpty(UpdateFieldType.Default).First();
				result.Add((int)vValues.GetValue(j), new UpdateFieldInfo
				{
					Value = (int)vValues.GetValue(j),
					Name = vNames[j],
					Size = 0,
					Format = format
				});
				namesResult.Add(vNames[j], (int)vValues.GetValue(j));
			}
			for (int i2 = 0; i2 < result.Count - 1; i2++)
			{
				result.Values[i2].Size = result.Keys[i2 + 1] - result.Keys[i2];
			}
			dicts.Add(enumType, result);
			nameToValueDict.Add(enumType, namesResult);
			loaded = true;
		}
		return loaded;
	}

	public static int GetUpdateField<T>(T field)
	{
		if (ModernVersion.UpdateFieldNameDictionary.TryGetValue(typeof(T), out var byNamesDict) && byNamesDict.TryGetValue(field.ToString(), out var fieldValue))
		{
			return fieldValue;
		}
		return -1;
	}

	public static string GetUpdateFieldName<T>(int field)
	{
		if (ModernVersion.UpdateFieldDictionary.TryGetValue(typeof(T), out var infoDict) && infoDict.Count != 0)
		{
			int index = infoDict.BinarySearch(field);
			if (index >= 0)
			{
				return infoDict.Values[index].Name;
			}
			index = ~index - 1;
			int start = infoDict.Keys[index];
			return infoDict.Values[index].Name + " + " + (field - start);
		}
		return field.ToString(CultureInfo.InvariantCulture);
	}

	public static UpdateFieldInfo GetUpdateFieldInfo<T>(int field)
	{
		if (ModernVersion.UpdateFieldDictionary.TryGetValue(typeof(T), out var infoDict) && infoDict.Count != 0)
		{
			int index = infoDict.BinarySearch(field);
			if (index >= 0)
			{
				return infoDict.Values[index];
			}
			return infoDict.Values[~index - 1];
		}
		return null;
	}

	public static Type GetResponseCodesEnum()
	{
		switch (Opcodes.GetOpcodesDefiningBuild(ModernVersion.Build))
		{
		case ClientVersionBuild.V2_5_2_39570:
			return typeof(HermesProxy.World.Enums.V2_5_2_39570.ResponseCodes);
		case ClientVersionBuild.V1_14_1_40688:
		case ClientVersionBuild.V2_5_3_41750:
			return typeof(HermesProxy.World.Enums.V1_14_1_40688.ResponseCodes);
		case ClientVersionBuild.V3_4_3_54261:
			return typeof(HermesProxy.World.Enums.V3_4_3_54261.ResponseCodes);
		default:
			return null;
		}
	}

	private static byte GetExpansionVersion()
	{
		string str = ModernVersion.VersionString;
		str = str.Replace("V", "");
		str = str.Substring(0, str.IndexOf("_"));
		return (byte)uint.Parse(str);
	}

	private static byte GetMajorPatchVersion()
	{
		string str = ModernVersion.VersionString;
		str = str.Substring(str.IndexOf('_') + 1);
		str = str.Substring(0, str.IndexOf("_"));
		return (byte)uint.Parse(str);
	}

	private static byte GetMinorPatchVersion()
	{
		string str = ModernVersion.VersionString;
		str = str.Substring(str.IndexOf('_') + 1);
		str = str.Substring(str.IndexOf('_') + 1);
		str = str.Substring(0, str.IndexOf("_"));
		str = new string(str.TakeWhile(char.IsDigit).ToArray());
		return (byte)uint.Parse(str);
	}

	public static bool AddedInVersion(byte expansion, byte major, byte minor)
	{
		if (ModernVersion.ExpansionVersion < expansion)
		{
			return false;
		}
		if (ModernVersion.ExpansionVersion > expansion)
		{
			return true;
		}
		if (ModernVersion.MajorVersion < major)
		{
			return false;
		}
		if (ModernVersion.MajorVersion > major)
		{
			return true;
		}
		return ModernVersion.MinorVersion >= minor;
	}

	public static bool AddedInVersion(byte retailExpansion, byte retailMajor, byte retailMinor, byte classicEraExpansion, byte classicEraMajor, byte classicEraMinor, byte classicExpansion, byte classicMajor, byte classicMinor)
	{
		if (ModernVersion.ExpansionVersion == 1)
		{
			return ModernVersion.AddedInVersion(classicEraExpansion, classicEraMajor, classicEraMinor);
		}
		if (ModernVersion.ExpansionVersion == 2 || ModernVersion.ExpansionVersion == 3)
		{
			return ModernVersion.AddedInVersion(classicExpansion, classicMajor, classicMinor);
		}
		return ModernVersion.AddedInVersion(retailExpansion, retailMajor, retailMinor);
	}

	public static bool RemovedInVersion(byte retailExpansion, byte retailMajor, byte retailMinor, byte classicEraExpansion, byte classicEraMajor, byte classicEraMinor, byte classicExpansion, byte classicMajor, byte classicMinor)
	{
		return !ModernVersion.AddedInVersion(retailExpansion, retailMajor, retailMinor, classicEraExpansion, classicEraMajor, classicEraMinor, classicExpansion, classicMajor, classicMinor);
	}

	public static bool AddedInClassicVersion(byte classicEraExpansion, byte classicEraMajor, byte classicEraMinor, byte classicExpansion, byte classicMajor, byte classicMinor)
	{
		if (ModernVersion.ExpansionVersion == 1)
		{
			return ModernVersion.AddedInVersion(classicEraExpansion, classicEraMajor, classicEraMinor);
		}
		if (ModernVersion.ExpansionVersion == 2 || ModernVersion.ExpansionVersion == 3)
		{
			return ModernVersion.AddedInVersion(classicExpansion, classicMajor, classicMinor);
		}
		return false;
	}

	public static bool RemovedInClassicVersion(byte classicEraExpansion, byte classicEraMajor, byte classicEraMinor, byte classicExpansion, byte classicMajor, byte classicMinor)
	{
		return !ModernVersion.AddedInClassicVersion(classicEraExpansion, classicEraMajor, classicEraMinor, classicExpansion, classicMajor, classicMinor);
	}

	public static bool IsVersion(byte expansion, byte major, byte minor)
	{
		return ModernVersion.ExpansionVersion == expansion && ModernVersion.MajorVersion == major && ModernVersion.MinorVersion == minor;
	}

	public static bool InVersion(ClientVersionBuild build1, ClientVersionBuild build2)
	{
		return ModernVersion.AddedInVersion(build1) && ModernVersion.RemovedInVersion(build2);
	}

	public static bool AddedInVersion(ClientVersionBuild build)
	{
		return ModernVersion.Build >= build;
	}

	public static bool RemovedInVersion(ClientVersionBuild build)
	{
		return ModernVersion.Build < build;
	}

	public static bool IsClassicVersionBuild()
	{
		return (ModernVersion.ExpansionVersion == 1 && ModernVersion.MajorVersion >= 13) || (ModernVersion.ExpansionVersion == 2 && ModernVersion.MajorVersion >= 5) || (ModernVersion.ExpansionVersion == 3 && ModernVersion.MajorVersion >= 4);
	}

	public static int GetAccountDataCount()
	{
		if (ModernVersion.ExpansionVersion == 1 && ModernVersion.MajorVersion >= 14)
		{
			if (ModernVersion.AddedInVersion(1, 14, 1))
			{
				return 13;
			}
			return 10;
		}
		if (ModernVersion.ExpansionVersion == 2 && ModernVersion.MajorVersion >= 5)
		{
			if (ModernVersion.AddedInVersion(2, 5, 3))
			{
				return 13;
			}
		}
		else
		{
			if (ModernVersion.ExpansionVersion == 3 && ModernVersion.MajorVersion >= 4)
			{
				return 15;
			}
			if (!ModernVersion.IsClassicVersionBuild())
			{
				if (ModernVersion.AddedInVersion(9, 2, 0))
				{
					return 13;
				}
				if (ModernVersion.AddedInVersion(9, 1, 5))
				{
					return 12;
				}
			}
		}
		return 8;
	}

	public static int GetPowerCountForClientVersion()
	{
		if (ModernVersion.IsClassicVersionBuild())
		{
			if (ModernVersion.AddedInClassicVersion(1, 14, 1, 2, 5, 3))
			{
				return 7;
			}
			return 6;
		}
		if (ModernVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			return 5;
		}
		if (ModernVersion.RemovedInVersion(ClientVersionBuild.V4_0_6_13596))
		{
			return 7;
		}
		if (ModernVersion.RemovedInVersion(ClientVersionBuild.V6_0_2_19033))
		{
			return 5;
		}
		if (ModernVersion.RemovedInVersion(ClientVersionBuild.V9_1_5_40772))
		{
			return 6;
		}
		return 7;
	}

	public static uint GetGameObjectStateAnimId()
	{
		if (ModernVersion.IsVersion(1, 14, 0) || ModernVersion.IsVersion(2, 5, 2))
		{
			return 1556u;
		}
		if (ModernVersion.IsVersion(1, 14, 1))
		{
			return 1618u;
		}
		if (ModernVersion.IsVersion(1, 14, 2) || ModernVersion.IsVersion(2, 5, 3))
		{
			return 1672u;
		}
		if (ModernVersion.IsVersion(3, 4, 3))
		{
			return 1772u;
		}
		return 0u;
	}

	public static byte AdjustInventorySlot(byte slot)
	{
		byte offset = 0;
		if (slot >= 30 && slot <= 33)
		{
			// Bag slots: modern 30-33 → legacy 19-22
			offset = 11;
		}
		else if (slot >= 35 && slot < 51)
		{
			// Backpack items: modern 35-50 → legacy 23-38
			offset = 12;
		}
		else if (slot >= 51 && slot < 75)
		{
			offset = (byte)(LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) ? 8 : ((!LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056)) ? 8 : 8));
		}
		else if (slot >= 75 && slot < 82)
		{
			offset = (byte)(LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) ? 12 : ((!LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056)) ? 8 : 8));
		}
		else if (slot >= 82 && slot < 94)
		{
			offset = (byte)(LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) ? 13 : ((!LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056)) ? 8 : 8));
		}
		else if (slot >= 94 && slot < 126)
		{
			offset = (byte)(LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180) ? 13 : ((!LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056)) ? 8 : 8));
		}
		return (byte)(slot - offset);
	}

	public static void ConvertAuraFlags(ushort oldFlags, byte slot, out AuraFlagsModern newFlags, out uint activeFlags)
	{
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			activeFlags = 0u;
			newFlags = AuraFlagsModern.None;
			if (slot >= 32)
			{
				newFlags |= AuraFlagsModern.Negative;
			}
			else
			{
				newFlags |= AuraFlagsModern.Positive;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsVanilla.Cancelable))
			{
				newFlags |= AuraFlagsModern.Cancelable;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsVanilla.EffectIndex0))
			{
				activeFlags |= 1u;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsVanilla.EffectIndex1))
			{
				activeFlags |= 2u;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsVanilla.EffectIndex2))
			{
				activeFlags |= 4u;
			}
			return;
		}
		if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			activeFlags = 1u;
			newFlags = AuraFlagsModern.None;
			if (oldFlags.HasAnyFlag(AuraFlagsTBC.NotCancelable))
			{
				newFlags |= AuraFlagsModern.Negative;
			}
			else if (oldFlags.HasAnyFlag(AuraFlagsTBC.Cancelable))
			{
				newFlags |= AuraFlagsModern.Cancelable | AuraFlagsModern.Positive;
			}
			else if (slot >= 40)
			{
				newFlags |= AuraFlagsModern.Negative;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsTBC.EffectIndex0))
			{
				activeFlags |= 1u;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsTBC.EffectIndex1))
			{
				activeFlags |= 2u;
			}
			if (oldFlags.HasAnyFlag(AuraFlagsTBC.EffectIndex2))
			{
				activeFlags |= 4u;
			}
			return;
		}
		activeFlags = 0u;
		newFlags = AuraFlagsModern.None;
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.Negative))
		{
			newFlags |= AuraFlagsModern.Negative;
		}
		else if (oldFlags.HasAnyFlag(AuraFlagsWotLK.Positive))
		{
			newFlags |= AuraFlagsModern.Cancelable | AuraFlagsModern.Positive;
		}
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.NoCaster))
		{
			newFlags |= AuraFlagsModern.NoCaster;
		}
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.Duration))
		{
			newFlags |= AuraFlagsModern.Duration;
		}
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.EffectIndex0))
		{
			activeFlags |= 1u;
		}
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.EffectIndex1))
		{
			activeFlags |= 2u;
		}
		if (oldFlags.HasAnyFlag(AuraFlagsWotLK.EffectIndex2))
		{
			activeFlags |= 4u;
		}
	}

	public static uint GetArenaTeamSizeFromIndex(uint index)
	{
		return index switch
		{
			0u => 2u, 
			1u => 3u, 
			2u => 5u, 
			_ => 0u, 
		};
	}

	public static uint GetArenaTeamIndexFromSize(uint size)
	{
		return size switch
		{
			2u => 0u, 
			3u => 1u, 
			5u => 2u, 
			_ => 0u, 
		};
	}

	public static byte ConvertResponseCodesValue(byte legacyValue)
	{
		string legacyName = Enum.ToObject(LegacyVersion.GetResponseCodesEnum(), legacyValue).ToString();
		return (byte)Enum.Parse(ModernVersion.GetResponseCodesEnum(), legacyName);
	}

	public static byte ConvertSocketColor(byte legacyValue)
	{
		Type typeFromHandle = typeof(SocketColorModern);
		SocketColorLegacy socketColorLegacy = (SocketColorLegacy)legacyValue;
		return (byte)Enum.Parse(typeFromHandle, socketColorLegacy.ToString());
	}
}
