using System;
using Framework.GameMath;
using Framework.Logging;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Objects.Version.V3_4_3_54261;

public class ObjectUpdateBuilder
{
	protected ObjectUpdate m_updateData;

	protected ObjectTypeBCC m_objectType;

	protected ObjectTypeMask m_objectTypeMask;

	protected CreateObjectBits m_createBits;

	protected GameSessionData m_gameState;

	private ObjectTypeBCC m_realObjectType;

	private static readonly int DEBUG_STRIP_LEVEL;

	public static readonly bool DEBUG_SKIP_GAMEOBJECTS;

	public static readonly bool DEBUG_SKIP_ALL_UPDATES;

	public static readonly bool DEBUG_SKIP_PLAYER_OBJECT;

	private bool IsOwner => this.m_realObjectType == ObjectTypeBCC.ActivePlayer || this.m_realObjectType == ObjectTypeBCC.Item || this.m_realObjectType == ObjectTypeBCC.Container;

	private bool IsGameObjectOwner
	{
		get
		{
			if (this.m_realObjectType != ObjectTypeBCC.GameObject)
				return false;
			var createdBy = this.m_updateData.GameObjectData?.CreatedBy;
			var playerGuid = this.m_gameState.CurrentPlayerGuid;
			if (createdBy != null && playerGuid != null)
				return createdBy.GetCounter() == playerGuid.GetCounter() && createdBy.GetHighType() == playerGuid.GetHighType();
			return false;
		}
	}

	public ObjectUpdateBuilder(ObjectUpdate updateData, GameSessionData gameState)
	{
		this.m_updateData = updateData;
		this.m_gameState = gameState;
		ObjectType objectType = updateData.Guid.GetObjectType();
		if (updateData.CreateData != null)
		{
			objectType = updateData.CreateData.ObjectType;
			if (updateData.CreateData.ThisIsYou)
			{
				objectType = ObjectType.ActivePlayer;
			}
		}
		if (objectType == ObjectType.Player && this.m_gameState.CurrentPlayerGuid == updateData.Guid)
		{
			objectType = ObjectType.ActivePlayer;
		}
		this.m_objectType = ObjectTypeConverter.ConvertToBCC(objectType);
		this.m_realObjectType = this.m_objectType;
		this.m_objectTypeMask = ObjectTypeMask.Object;
		switch (this.m_objectType)
		{
		case ObjectTypeBCC.Item:
			this.m_objectTypeMask |= ObjectTypeMask.Item;
			break;
		case ObjectTypeBCC.Container:
			this.m_objectTypeMask |= ObjectTypeMask.Item | ObjectTypeMask.Container;
			break;
		case ObjectTypeBCC.Unit:
			this.m_objectTypeMask |= ObjectTypeMask.Unit;
			break;
		case ObjectTypeBCC.Player:
			this.m_objectTypeMask |= ObjectTypeMask.Unit | ObjectTypeMask.Player;
			break;
		case ObjectTypeBCC.ActivePlayer:
			this.m_objectTypeMask |= ObjectTypeMask.Unit | ObjectTypeMask.Player | ObjectTypeMask.ActivePlayer;
			break;
		case ObjectTypeBCC.GameObject:
			this.m_objectTypeMask |= ObjectTypeMask.GameObject;
			break;
		case ObjectTypeBCC.DynamicObject:
			this.m_objectTypeMask |= ObjectTypeMask.DynamicObject;
			break;
		case ObjectTypeBCC.Corpse:
			this.m_objectTypeMask |= ObjectTypeMask.Corpse;
			break;
		}
	}

	private static uint ConvertTypeMask(ObjectTypeMask mask)
	{
		uint result = 0u;
		if (mask.HasAnyFlag(ObjectTypeMask.Object))
		{
			result |= 1;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Item))
		{
			result |= 2;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Container))
		{
			result |= 4;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Unit))
		{
			result |= 0x20;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Player))
		{
			result |= 0x40;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.ActivePlayer))
		{
			result |= 0x80;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.GameObject))
		{
			result |= 0x100;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.DynamicObject))
		{
			result |= 0x200;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Corpse))
		{
			result |= 0x400;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.AreaTrigger))
		{
			result |= 0x800;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Sceneobject))
		{
			result |= 0x1000;
		}
		if (mask.HasAnyFlag(ObjectTypeMask.Conversation))
		{
			result |= 0x2000;
		}
		return result;
	}

	private static byte ConvertTypeId(ObjectTypeBCC type)
	{
		if (1 == 0)
		{
		}
		byte result = type switch
		{
			ObjectTypeBCC.Object => 0, 
			ObjectTypeBCC.Item => 1, 
			ObjectTypeBCC.Container => 2, 
			ObjectTypeBCC.Unit => 5, 
			ObjectTypeBCC.Player => 6, 
			ObjectTypeBCC.ActivePlayer => 7, 
			ObjectTypeBCC.GameObject => 8, 
			ObjectTypeBCC.DynamicObject => 9, 
			ObjectTypeBCC.Corpse => 10, 
			ObjectTypeBCC.AreaTrigger => 11, 
			ObjectTypeBCC.SceneObject => 12, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public void WriteToPacket(WorldPacket packet)
	{
		Log.Print(LogType.Debug, $"[UpdateBuilder] Writing {this.m_updateData.Type} for {this.m_updateData.Guid} objType={this.m_objectType} typeMask=0x{ObjectUpdateBuilder.ConvertTypeMask(this.m_objectTypeMask):X4}", "WriteToPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		packet.WriteUInt8((byte)this.m_updateData.Type);
		packet.WritePackedGuid128(this.m_updateData.Guid);
		if (this.m_updateData.Type != UpdateTypeModern.Values)
		{
			ObjectTypeBCC headerType = this.m_objectType;
			packet.WriteUInt8(ObjectUpdateBuilder.ConvertTypeId(headerType));
			this.SetCreateObjectBits();
			Log.Print(LogType.Debug, $"[UpdateBuilder] CreateBits: Move={this.m_createBits.MovementUpdate} Stationary={this.m_createBits.Stationary} Vehicle={this.m_createBits.Vehicle} ActivePlayer={this.m_createBits.ActivePlayer} Transport={this.m_createBits.MovementTransport} Rotation={this.m_createBits.Rotation}", "WriteToPacket", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
			this.BuildMovementUpdate(packet);
		}
		this.WriteValuesModern(packet);
	}

	private void WriteValuesModern(WorldPacket packet)
	{
		// Dump full item create data for debugging login vs loot comparison
		if (this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Item) && this.m_updateData.Type != UpdateTypeModern.Values)
		{
			ItemData item = this.m_updateData.ItemData;
			string itemInfo = item != null ?
				$"Owner={item.Owner} ContainedIn={item.ContainedIn} Stack={item.StackCount} Dur={item.Durability}/{item.MaxDurability} Flags={item.Flags} Charges=[{item.SpellCharges[0]},{item.SpellCharges[1]},{item.SpellCharges[2]},{item.SpellCharges[3]},{item.SpellCharges[4]}] PropSeed={item.PropertySeed} RandProp={item.RandomProperty}" :
				"null";
			Log.Print(LogType.Debug, $"[ItemCreate] {this.m_updateData.Guid} Entry={this.m_updateData.ObjectData?.EntryID} IsOwner={this.IsOwner} ItemData: {itemInfo}", "WriteValuesModern", "");
		}
		WorldPacket valuesBuffer = new WorldPacket();
		if (this.m_updateData.Type == UpdateTypeModern.Values)
		{
			this.WriteValuesUpdate(valuesBuffer);
		}
		else
		{
			this.WriteValuesCreate(valuesBuffer);
		}
		byte[] valuesData = valuesBuffer.GetData();
		if (this.m_updateData.Type == UpdateTypeModern.Values && this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit))
		{
			Log.Print(LogType.Debug, $"[ValuesUpdate] guid={this.m_updateData.Guid} type={this.m_objectType} size={valuesData.Length} hex={BitConverter.ToString(valuesData, 0, System.Math.Min(64, valuesData.Length))}", "WriteValuesModern", "");
		}
		packet.WriteUInt32((uint)valuesData.Length);
		packet.WriteBytes(valuesData);
	}

	private void WriteValuesCreate(WorldPacket data)
	{
		ObjectTypeMask effectiveMask = this.m_objectTypeMask;
		if (this.IsOwner && ObjectUpdateBuilder.DEBUG_STRIP_LEVEL == 1)
		{
			effectiveMask &= ~(ObjectTypeMask.Player | ObjectTypeMask.ActivePlayer);
		}
		else if (this.IsOwner && ObjectUpdateBuilder.DEBUG_STRIP_LEVEL == 2)
		{
			effectiveMask &= ~ObjectTypeMask.ActivePlayer;
		}
		// Owner=0x01, PartyMember=0x02 (needed for QuestLog visibility)
		byte updateFieldFlags = (byte)(this.IsOwner ? 0x03 : 0);
		Log.Print(LogType.Debug, $"[ValuesCreate] type={this.m_objectType} flags=0x{updateFieldFlags:X2} IsOwner={this.IsOwner}", "WriteValuesCreate", "");
		data.WriteUInt8(updateFieldFlags);
		int sectionStart = data.GetData().Length;
		this.WriteCreateObjectData(data);
		int afterObj = data.GetData().Length;
		Log.Print(LogType.Debug, $"[Sizes] ObjectData={afterObj - sectionStart} bytes", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		if (effectiveMask.HasAnyFlag(ObjectTypeMask.Item))
		{
			this.WriteCreateItemData(data);
			int afterItem = data.GetData().Length;
			Log.Print(LogType.Debug, $"[Sizes] ItemData={afterItem - afterObj} bytes", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		if (effectiveMask.HasAnyFlag(ObjectTypeMask.Container))
		{
			this.WriteCreateContainerData(data);
		}
		if (effectiveMask.HasAnyFlag(ObjectTypeMask.Unit))
		{
			int beforeUnit = data.GetData().Length;
			this.WriteCreateUnitData(data);
			int afterUnit = data.GetData().Length;
			Log.Print(LogType.Debug, $"[Sizes] UnitData={afterUnit - beforeUnit} bytes", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		if (effectiveMask.HasAnyFlag(ObjectTypeMask.Player))
		{
			int beforePlayer = data.GetData().Length;
			this.WriteCreatePlayerData(data);
			int afterPlayer = data.GetData().Length;
			Log.Print(LogType.Debug, $"[Sizes] PlayerData={afterPlayer - beforePlayer} bytes", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		if (effectiveMask.HasAnyFlag(ObjectTypeMask.ActivePlayer))
		{
			int beforeActive = data.GetData().Length;
			this.WriteCreateActivePlayerData(data);
			int afterActive = data.GetData().Length;
			Log.Print(LogType.Debug, $"[Sizes] ActivePlayerData={afterActive - beforeActive} bytes", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		if (this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.GameObject))
		{
			this.WriteCreateGameObjectData(data);
		}
		if (this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.DynamicObject))
		{
			this.WriteCreateDynamicObjectData(data);
		}
		if (this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Corpse))
		{
			this.WriteCreateCorpseData(data);
		}
	}

	/// Returns the WowGuid128 for a given modern 3.4.3 InvSlots index (0-140),
	/// mapping from the separate legacy arrays in ActivePlayerData.
	private static WowGuid128 GetModernInvSlot(ActivePlayerData a, int modernIdx)
	{
		if (modernIdx <= 18)
		{
			// Equipment slots
			if (a.InvSlots != null && modernIdx < a.InvSlots.Length)
				return a.InvSlots[modernIdx];
		}
		else if (modernIdx >= 30 && modernIdx <= 33)
		{
			// Bag slots: legacy InvSlots[19-22] -> modern [30-33]
			int legacyIdx = 19 + (modernIdx - 30);
			if (a.InvSlots != null && legacyIdx < a.InvSlots.Length)
				return a.InvSlots[legacyIdx];
		}
		else if (modernIdx >= 35 && modernIdx <= 58)
		{
			// Backpack items: from PackSlots
			int idx = modernIdx - 35;
			if (a.PackSlots != null && idx < a.PackSlots.Length)
				return a.PackSlots[idx];
		}
		else if (modernIdx >= 59 && modernIdx <= 86)
		{
			// Bank items
			int idx = modernIdx - 59;
			if (a.BankSlots != null && idx < a.BankSlots.Length)
				return a.BankSlots[idx];
		}
		else if (modernIdx >= 87 && modernIdx <= 93)
		{
			// Bank bag slots
			int idx = modernIdx - 87;
			if (a.BankBagSlots != null && idx < a.BankBagSlots.Length)
				return a.BankBagSlots[idx];
		}
		else if (modernIdx >= 94 && modernIdx <= 105)
		{
			// Buyback slots
			int idx = modernIdx - 94;
			if (a.BuyBackSlots != null && idx < a.BuyBackSlots.Length)
				return a.BuyBackSlots[idx];
		}
		else if (modernIdx >= 106 && modernIdx <= 137)
		{
			// Keyring slots
			int idx = modernIdx - 106;
			if (a.KeyringSlots != null && idx < a.KeyringSlots.Length)
				return a.KeyringSlots[idx];
		}
		return null;
	}

	private bool HasActivePlayerChanges()
	{
		if (!this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.ActivePlayer))
			return false;
		ActivePlayerData a = this.m_updateData.ActivePlayerData;
		if (a == null) return false;

		// Block 0 scalars (bits 26-37)
		if (a.FarsightObject != null) return true;
		if (a.Coinage.HasValue || a.XP.HasValue || a.NextLevelXP.HasValue || a.TrialXP.HasValue) return true;
		if (a.CharacterPoints.HasValue || a.MaxTalentTiers.HasValue) return true;
		if (a.TrackCreatureMask.HasValue) return true;
		if (a.MainhandExpertise.HasValue || a.OffhandExpertise.HasValue) return true;

		// Block 38 scalars (bits 39-69)
		if (a.RangedExpertise.HasValue || a.CombatRatingExpertise.HasValue) return true;
		if (a.BlockPercentage.HasValue || a.DodgePercentage.HasValue || a.ParryPercentage.HasValue) return true;
		if (a.CritPercentage.HasValue || a.RangedCritPercentage.HasValue || a.OffhandCritPercentage.HasValue) return true;
		if (a.ShieldBlock.HasValue || a.Mastery.HasValue) return true;
		if (a.Speed.HasValue || a.Avoidance.HasValue || a.Sturdiness.HasValue) return true;
		if (a.Versatility.HasValue || a.VersatilityBonus.HasValue) return true;
		if (a.PvpPowerDamage.HasValue || a.PvpPowerHealing.HasValue) return true;
		if (a.ModHealingDonePos.HasValue || a.ModHealingPercent.HasValue) return true;
		if (a.ModHealingDonePercent.HasValue || a.ModPeriodicHealingDonePercent.HasValue) return true;
		if (a.ModSpellPowerPercent.HasValue || a.ModResiliencePercent.HasValue) return true;
		if (a.OverrideSpellPowerByAPPercent.HasValue || a.OverrideAPBySpellPowerPercent.HasValue) return true;
		if (a.ModTargetResistance.HasValue || a.ModTargetPhysicalResistance.HasValue) return true;
		if (a.LocalFlags.HasValue) return true;

		// Block 70 scalars (bits 71-101)
		if (a.GrantableLevels.HasValue || a.MultiActionBars.HasValue) return true;
		if (a.LifetimeMaxRank.HasValue || a.NumRespecs.HasValue) return true;
		if (a.AmmoID.HasValue || a.PvpMedals.HasValue) return true;
		if (a.TodayHonorableKills.HasValue || a.TodayDishonorableKills.HasValue) return true;
		if (a.YesterdayHonorableKills.HasValue || a.YesterdayDishonorableKills.HasValue) return true;
		if (a.LastWeekHonorableKills.HasValue || a.LastWeekDishonorableKills.HasValue) return true;
		if (a.ThisWeekHonorableKills.HasValue || a.ThisWeekDishonorableKills.HasValue) return true;
		if (a.ThisWeekContribution.HasValue || a.LifetimeHonorableKills.HasValue || a.LifetimeDishonorableKills.HasValue) return true;
		if (a.YesterdayContribution.HasValue || a.LastWeekContribution.HasValue || a.LastWeekRank.HasValue) return true;
		if (a.WatchedFactionIndex.HasValue || a.MaxLevel.HasValue) return true;
		if (a.ScalingPlayerLevelDelta.HasValue || a.MaxCreatureScalingLevel.HasValue) return true;
		if (a.PetSpellPower.HasValue || a.UiHitModifier.HasValue || a.UiSpellHitModifier.HasValue) return true;
		if (a.HomeRealmTimeOffset.HasValue || a.ModPetHaste.HasValue || a.LocalRegenFlags.HasValue) return true;

		// Block 102 scalars (bits 103-123)
		if (a.AuraVision.HasValue || a.NumBackpackSlots.HasValue) return true;
		if (a.OverrideSpellsID.HasValue || a.LfgBonusFactionID.HasValue || a.LootSpecID.HasValue) return true;
		if (a.OverrideZonePVPType.HasValue) return true;
		if (a.Honor.HasValue || a.HonorNextLevel.HasValue) return true;
		if (a.PvPTierMaxFromWins.HasValue || a.PvPLastWeeksTierMaxFromWins.HasValue) return true;
		if (a.PvPRankProgress.HasValue) return true;

		// Skill (bit 32) — nested SkillInfo struct
		if (a.Skill != null && HasAnySkillChanged(a.Skill)) return true;

		// KnownTitles (dynamic field, bit 3)
		if (a.KnownTitles != null)
			for (int i = 0; i < a.KnownTitles.Length; i++)
				if (a.KnownTitles[i].HasValue) return true;

		// InvSlots (bits 124-265)
		for (int i = 0; i < 141; i++)
			if (GetModernInvSlot(a, i) != null) return true;

		// TrackResourceMask (bits 266-268)
		if (a.TrackResourceMask != null)
			for (int i = 0; i < a.TrackResourceMask.Length; i++)
				if (a.TrackResourceMask[i].HasValue) return true;

		// SpellCritPercentage / ModDamageDone arrays (bits 269-297)
		if (a.SpellCritPercentage != null)
			for (int i = 0; i < 7; i++)
				if (a.SpellCritPercentage[i].HasValue) return true;
		if (a.ModDamageDonePos != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDonePos[i].HasValue) return true;
		if (a.ModDamageDoneNeg != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDoneNeg[i].HasValue) return true;
		if (a.ModDamageDonePercent != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDonePercent[i].HasValue) return true;

		// ExploredZones (bits 298-538)
		if (a.ExploredZones != null)
			for (int i = 0; i < 240; i++)
				if (a.ExploredZones[i].HasValue) return true;

		// RestInfo (bits 539-541)
		if (a.RestInfo != null)
			for (int i = 0; i < 2; i++)
				if (a.RestInfo[i] != null && (a.RestInfo[i].Threshold.HasValue || a.RestInfo[i].StateID.HasValue)) return true;

		// WeaponDmgMultipliers / WeaponAtkSpeedMultipliers (bits 542-548)
		if (a.WeaponDmgMultipliers != null)
			for (int i = 0; i < 3; i++)
				if (a.WeaponDmgMultipliers[i].HasValue) return true;
		if (a.WeaponAtkSpeedMultipliers != null)
			for (int i = 0; i < 3; i++)
				if (a.WeaponAtkSpeedMultipliers[i].HasValue) return true;

		// Buyback (bits 549-573)
		if (a.BuybackPrice != null)
			for (int i = 0; i < 12; i++)
				if (a.BuybackPrice[i].HasValue) return true;
		if (a.BuybackTimestamp != null)
			for (int i = 0; i < 12; i++)
				if (a.BuybackTimestamp[i].HasValue) return true;

		// CombatRatings (bits 574-606)
		if (a.CombatRatings != null)
			for (int i = 0; i < 32; i++)
				if (a.CombatRatings[i].HasValue) return true;

		// NoReagentCostMask (bits 615-619)
		if (a.NoReagentCostMask != null)
			for (int i = 0; i < 4; i++)
				if (a.NoReagentCostMask[i].HasValue) return true;

		// ProfessionSkillLine (bits 620-622)
		if (a.ProfessionSkillLine != null)
			for (int i = 0; i < 2; i++)
				if (a.ProfessionSkillLine[i].HasValue) return true;

		// BagSlotFlags (bits 623-627)
		if (a.BagSlotFlags != null)
			for (int i = 0; i < 4; i++)
				if (a.BagSlotFlags[i].HasValue) return true;

		// BankBagSlotFlags (bits 628-635)
		if (a.BankBagSlotFlags != null)
			for (int i = 0; i < 7; i++)
				if (a.BankBagSlotFlags[i].HasValue) return true;

		// QuestCompleted (bits 636-1511)
		if (a.QuestCompleted != null)
			for (int i = 0; i < 875; i++)
				if (a.QuestCompleted[i].HasValue) return true;

		// PvpInfo (bits 607-614)
		if (a.PvpInfo != null)
			for (int i = 0; i < a.PvpInfo.Length; i++)
				if (a.PvpInfo[i] != null && (a.PvpInfo[i].Rating != 0 || a.PvpInfo[i].SeasonPlayed != 0 || a.PvpInfo[i].Disqualified)) return true;

		return false;
	}

	/// <summary>
	/// Writes ActivePlayerData update using TC343 bitmask format.
	/// HasChangesMask&lt;1525&gt; = 48 blocks of 32 bits (1536 total).
	/// TC343 UpdateFields.h bit assignments:
	///   Block 0 (group bit 0):  26=FarsightObject, 28=Coinage, 29=XP, 30=NextLevelXP,
	///                           31=TrialXP, 33=CharacterPoints, 34=MaxTalentTiers,
	///                           35=TrackCreatureMask, 36=MainhandExpertise, 37=OffhandExpertise
	///   Block 38 (group bit 38): 39-69 combat percentages, healing/dmg mods, LocalFlags
	///   Block 70 (group bit 70): 71-101 honor/PvP/misc scalars
	///   Block 102 (group bit 102): 103-123 honor, glyphs, backpack, etc.
	///   Arrays: 124=InvSlots, 266=TrackResource, 269=SpellCrit/ModDmg, 298=ExploredZones,
	///           542=WeaponMult, 549=Buyback, 574=CombatRatings, 615=NoReagentCost,
	///           620=ProfSkill, 623=BagFlags, 628=BankBagFlags, 636=QuestCompleted
	/// </summary>
	private void WriteUpdateActivePlayerData(WorldPacket data)
	{
		ActivePlayerData a = this.m_updateData.ActivePlayerData ?? new ActivePlayerData();

		// Build changesMask (1536 bits = 48 blocks of 32)
		uint[] blocks = new uint[48];
		void SetBit(int bit) { blocks[bit / 32] |= (1u << (bit % 32)); }
		bool IsBitSet(int bit) { return (blocks[bit / 32] & (1u << (bit % 32))) != 0; }

		// Pre-compute KnownTitles (uint?[12] → ulong[6])
		int knownTitlesCount = 0;
		ulong[] knownTitles64 = new ulong[6];
		if (a.KnownTitles != null)
		{
			bool hasAnyTitle = false;
			for (int i = 0; i < a.KnownTitles.Length; i++)
				if (a.KnownTitles[i].HasValue) { hasAnyTitle = true; break; }
			if (hasAnyTitle)
			{
				knownTitlesCount = 6;
				for (int i = 0; i < 6; i++)
				{
					uint lo = (i * 2 < a.KnownTitles.Length && a.KnownTitles[i * 2].HasValue) ? a.KnownTitles[i * 2].Value : 0;
					uint hi = (i * 2 + 1 < a.KnownTitles.Length && a.KnownTitles[i * 2 + 1].HasValue) ? a.KnownTitles[i * 2 + 1].Value : 0;
					knownTitles64[i] = (ulong)lo | ((ulong)hi << 32);
				}
				SetBit(0); SetBit(3); // dynamic field KnownTitles
			}
		}

		// Pre-compute GlyphSlots/Glyphs from GameSessionData
		// NOTE: Glyphs only change via specific packets, not update fields.
		// Sending them in every Values update is wasteful and may cause format issues.
		// Glyphs are already set correctly in the CREATE path.
		bool hasGlyphChanges = false;

		// ============================================================
		// SET BITS — Block 0 scalar fields (group bit 0, fields 26-37)
		// ============================================================
		if (a.FarsightObject != null) { SetBit(0); SetBit(26); }
		// 27: SummonedBattlePetGUID — not used in WotLK
		if (a.Coinage.HasValue) { SetBit(0); SetBit(28); }
		if (a.XP.HasValue) { SetBit(0); SetBit(29); }
		if (a.NextLevelXP.HasValue) { SetBit(0); SetBit(30); }
		if (a.TrialXP.HasValue) { SetBit(0); SetBit(31); }
		if (a.Skill != null && HasAnySkillChanged(a.Skill)) { SetBit(0); SetBit(32); }
		if (a.CharacterPoints.HasValue) { SetBit(0); SetBit(33); }
		if (a.MaxTalentTiers.HasValue) { SetBit(0); SetBit(34); }
		if (a.TrackCreatureMask.HasValue) { SetBit(0); SetBit(35); }
		if (a.MainhandExpertise.HasValue) { SetBit(0); SetBit(36); }
		if (a.OffhandExpertise.HasValue) { SetBit(0); SetBit(37); }

		// ============================================================
		// SET BITS — Block 38 scalar fields (group bit 38, fields 39-69)
		// ============================================================
		if (a.RangedExpertise.HasValue) { SetBit(38); SetBit(39); }
		if (a.CombatRatingExpertise.HasValue) { SetBit(38); SetBit(40); }
		if (a.BlockPercentage.HasValue) { SetBit(38); SetBit(41); }
		if (a.DodgePercentage.HasValue) { SetBit(38); SetBit(42); }
		if (a.DodgePercentageFromAttribute.HasValue) { SetBit(38); SetBit(43); }
		if (a.ParryPercentage.HasValue) { SetBit(38); SetBit(44); }
		if (a.ParryPercentageFromAttribute.HasValue) { SetBit(38); SetBit(45); }
		if (a.CritPercentage.HasValue) { SetBit(38); SetBit(46); }
		if (a.RangedCritPercentage.HasValue) { SetBit(38); SetBit(47); }
		if (a.OffhandCritPercentage.HasValue) { SetBit(38); SetBit(48); }
		if (a.ShieldBlock.HasValue) { SetBit(38); SetBit(49); }
		// 50: ShieldBlockCritPercentage — no property
		if (a.Mastery.HasValue) { SetBit(38); SetBit(51); }
		if (a.Speed.HasValue) { SetBit(38); SetBit(52); }
		if (a.Avoidance.HasValue) { SetBit(38); SetBit(53); }
		if (a.Sturdiness.HasValue) { SetBit(38); SetBit(54); }
		if (a.Versatility.HasValue) { SetBit(38); SetBit(55); }
		if (a.VersatilityBonus.HasValue) { SetBit(38); SetBit(56); }
		if (a.PvpPowerDamage.HasValue) { SetBit(38); SetBit(57); }
		if (a.PvpPowerHealing.HasValue) { SetBit(38); SetBit(58); }
		if (a.ModHealingDonePos.HasValue) { SetBit(38); SetBit(59); }
		if (a.ModHealingPercent.HasValue) { SetBit(38); SetBit(60); }
		if (a.ModHealingDonePercent.HasValue) { SetBit(38); SetBit(61); }
		if (a.ModPeriodicHealingDonePercent.HasValue) { SetBit(38); SetBit(62); }
		if (a.ModSpellPowerPercent.HasValue) { SetBit(38); SetBit(63); }
		if (a.ModResiliencePercent.HasValue) { SetBit(38); SetBit(64); }
		if (a.OverrideSpellPowerByAPPercent.HasValue) { SetBit(38); SetBit(65); }
		if (a.OverrideAPBySpellPowerPercent.HasValue) { SetBit(38); SetBit(66); }
		if (a.ModTargetResistance.HasValue) { SetBit(38); SetBit(67); }
		if (a.ModTargetPhysicalResistance.HasValue) { SetBit(38); SetBit(68); }
		if (a.LocalFlags.HasValue) { SetBit(38); SetBit(69); }

		// ============================================================
		// SET BITS — Block 70 scalar fields (group bit 70, fields 71-101)
		// ============================================================
		if (a.GrantableLevels.HasValue) { SetBit(70); SetBit(71); }
		if (a.MultiActionBars.HasValue) { SetBit(70); SetBit(72); }
		if (a.LifetimeMaxRank.HasValue) { SetBit(70); SetBit(73); }
		if (a.NumRespecs.HasValue) { SetBit(70); SetBit(74); }
		if (a.AmmoID.HasValue) { SetBit(70); SetBit(75); }
		if (a.PvpMedals.HasValue) { SetBit(70); SetBit(76); }
		if (a.TodayHonorableKills.HasValue) { SetBit(70); SetBit(77); }
		if (a.TodayDishonorableKills.HasValue) { SetBit(70); SetBit(78); }
		if (a.YesterdayHonorableKills.HasValue) { SetBit(70); SetBit(79); }
		if (a.YesterdayDishonorableKills.HasValue) { SetBit(70); SetBit(80); }
		if (a.LastWeekHonorableKills.HasValue) { SetBit(70); SetBit(81); }
		if (a.LastWeekDishonorableKills.HasValue) { SetBit(70); SetBit(82); }
		if (a.ThisWeekHonorableKills.HasValue) { SetBit(70); SetBit(83); }
		if (a.ThisWeekDishonorableKills.HasValue) { SetBit(70); SetBit(84); }
		if (a.ThisWeekContribution.HasValue) { SetBit(70); SetBit(85); }
		if (a.LifetimeHonorableKills.HasValue) { SetBit(70); SetBit(86); }
		if (a.LifetimeDishonorableKills.HasValue) { SetBit(70); SetBit(87); }
		// 88: Field_F24 — unused
		if (a.YesterdayContribution.HasValue) { SetBit(70); SetBit(89); }
		if (a.LastWeekContribution.HasValue) { SetBit(70); SetBit(90); }
		if (a.LastWeekRank.HasValue) { SetBit(70); SetBit(91); }
		if (a.WatchedFactionIndex.HasValue) { SetBit(70); SetBit(92); }
		if (a.MaxLevel.HasValue) { SetBit(70); SetBit(93); }
		if (a.ScalingPlayerLevelDelta.HasValue) { SetBit(70); SetBit(94); }
		if (a.MaxCreatureScalingLevel.HasValue) { SetBit(70); SetBit(95); }
		if (a.PetSpellPower.HasValue) { SetBit(70); SetBit(96); }
		if (a.UiHitModifier.HasValue) { SetBit(70); SetBit(97); }
		if (a.UiSpellHitModifier.HasValue) { SetBit(70); SetBit(98); }
		if (a.HomeRealmTimeOffset.HasValue) { SetBit(70); SetBit(99); }
		if (a.ModPetHaste.HasValue) { SetBit(70); SetBit(100); }
		if (a.LocalRegenFlags.HasValue) { SetBit(70); SetBit(101); }

		// ============================================================
		// SET BITS — Block 102 scalar fields (group bit 102, fields 103-123)
		// ============================================================
		if (a.AuraVision.HasValue) { SetBit(102); SetBit(103); }
		if (a.NumBackpackSlots.HasValue) { SetBit(102); SetBit(104); }
		if (a.OverrideSpellsID.HasValue) { SetBit(102); SetBit(105); }
		if (a.LfgBonusFactionID.HasValue) { SetBit(102); SetBit(106); }
		if (a.LootSpecID.HasValue) { SetBit(102); SetBit(107); }
		if (a.OverrideZonePVPType.HasValue) { SetBit(102); SetBit(108); }
		if (a.Honor.HasValue) { SetBit(102); SetBit(109); }
		if (a.HonorNextLevel.HasValue) { SetBit(102); SetBit(110); }
		// 111: Field_F74 — unused
		if (a.PvPTierMaxFromWins.HasValue) { SetBit(102); SetBit(112); }
		if (a.PvPLastWeeksTierMaxFromWins.HasValue) { SetBit(102); SetBit(113); }
		if (a.PvPRankProgress.HasValue) { SetBit(102); SetBit(114); }
		// 115-123: GlyphsEnabled (120) set in create path only
		// Sending GlyphsEnabled in every Values update adds block 102 + FlushBits overhead

		// ============================================================
		// SET BITS — Array fields
		// ============================================================

		// InvSlots (header 124, elements 125-265)
		int invSlotsChanged = 0;
		for (int i = 0; i < 141; i++)
		{
			if (GetModernInvSlot(a, i) != null)
			{
				SetBit(124);
				SetBit(125 + i);
				invSlotsChanged++;
			}
		}

		// TrackResourceMask (header 266, elements 267-268)
		if (a.TrackResourceMask != null)
			for (int i = 0; i < 2; i++)
				if (a.TrackResourceMask[i].HasValue) { SetBit(266); SetBit(267 + i); }

		// Shared header 269: SpellCritPercentage (270-276), ModDamageDonePos (277-283),
		// ModDamageDoneNeg (284-290), ModDamageDonePercent (291-297)
		if (a.SpellCritPercentage != null)
			for (int i = 0; i < 7; i++)
				if (a.SpellCritPercentage[i].HasValue) { SetBit(269); SetBit(270 + i); }
		if (a.ModDamageDonePos != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDonePos[i].HasValue) { SetBit(269); SetBit(277 + i); }
		if (a.ModDamageDoneNeg != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDoneNeg[i].HasValue) { SetBit(269); SetBit(284 + i); }
		if (a.ModDamageDonePercent != null)
			for (int i = 0; i < 7; i++)
				if (a.ModDamageDonePercent[i].HasValue) { SetBit(269); SetBit(291 + i); }

		// ExploredZones (header 298, elements 299-538)
		if (a.ExploredZones != null)
			for (int i = 0; i < 240; i++)
				if (a.ExploredZones[i].HasValue) { SetBit(298); SetBit(299 + i); }

		// RestInfo (header 539, elements 540-541)
		if (a.RestInfo != null)
			for (int i = 0; i < 2; i++)
				if (a.RestInfo[i] != null && (a.RestInfo[i].Threshold.HasValue || a.RestInfo[i].StateID.HasValue))
				{ SetBit(539); SetBit(540 + i); }

		// Shared header 542: WeaponDmgMultipliers (543-545), WeaponAtkSpeedMultipliers (546-548)
		if (a.WeaponDmgMultipliers != null)
			for (int i = 0; i < 3; i++)
				if (a.WeaponDmgMultipliers[i].HasValue) { SetBit(542); SetBit(543 + i); }
		if (a.WeaponAtkSpeedMultipliers != null)
			for (int i = 0; i < 3; i++)
				if (a.WeaponAtkSpeedMultipliers[i].HasValue) { SetBit(542); SetBit(546 + i); }

		// Shared header 549: BuybackPrice (550-561), BuybackTimestamp (562-573)
		if (a.BuybackPrice != null)
			for (int i = 0; i < 12; i++)
				if (a.BuybackPrice[i].HasValue) { SetBit(549); SetBit(550 + i); }
		if (a.BuybackTimestamp != null)
			for (int i = 0; i < 12; i++)
				if (a.BuybackTimestamp[i].HasValue) { SetBit(549); SetBit(562 + i); }

		// CombatRatings (header 574, elements 575-606)
		if (a.CombatRatings != null)
			for (int i = 0; i < 32; i++)
				if (a.CombatRatings[i].HasValue) { SetBit(574); SetBit(575 + i); }

		// NoReagentCostMask (header 615, elements 616-619)
		if (a.NoReagentCostMask != null)
			for (int i = 0; i < 4; i++)
				if (a.NoReagentCostMask[i].HasValue) { SetBit(615); SetBit(616 + i); }

		// ProfessionSkillLine (header 620, elements 621-622)
		if (a.ProfessionSkillLine != null)
			for (int i = 0; i < 2; i++)
				if (a.ProfessionSkillLine[i].HasValue) { SetBit(620); SetBit(621 + i); }

		// BagSlotFlags (header 623, elements 624-627)
		if (a.BagSlotFlags != null)
			for (int i = 0; i < 4; i++)
				if (a.BagSlotFlags[i].HasValue) { SetBit(623); SetBit(624 + i); }

		// BankBagSlotFlags (header 628, elements 629-635)
		if (a.BankBagSlotFlags != null)
			for (int i = 0; i < 7; i++)
				if (a.BankBagSlotFlags[i].HasValue) { SetBit(628); SetBit(629 + i); }

		// QuestCompleted (header 636, elements 637-1511)
		if (a.QuestCompleted != null)
			for (int i = 0; i < 875; i++)
				if (a.QuestCompleted[i].HasValue) { SetBit(636); SetBit(637 + i); }

		// PvpInfo (header 607, elements 608-614)
		if (a.PvpInfo != null)
			for (int i = 0; i < Math.Min(a.PvpInfo.Length, 7); i++)
				if (a.PvpInfo[i] != null && (a.PvpInfo[i].Rating != 0 || a.PvpInfo[i].SeasonPlayed != 0 || a.PvpInfo[i].Disqualified))
				{ SetBit(607); SetBit(608 + i); }

		// GlyphSlots (header 1512, elements 1513-1518) + Glyphs (header 1512, elements 1519-1524)
		if (hasGlyphChanges)
		{
			SetBit(1512); // shared header
			uint[] glyphSlotIds = { 21, 22, 23, 24, 25, 26 };
			for (int i = 0; i < 6; i++)
			{
				SetBit(1513 + i); // GlyphSlots[i]
				SetBit(1519 + i); // Glyphs[i]
			}
		}

		// ============================================================
		// DEBUG LOG
		// ============================================================
		int setBlockCount = 0;
		System.Text.StringBuilder dbgBlocks = new System.Text.StringBuilder();
		for (int b = 0; b < 48; b++)
		{
			if (blocks[b] != 0)
			{
				setBlockCount++;
				dbgBlocks.Append($" blk{b}=0x{blocks[b]:X8}");
			}
		}
		Log.Print(LogType.Debug, $"[ActivePlayerUpdate] {setBlockCount} blocks set, InvSlots={invSlotsChanged}{dbgBlocks}", "WriteUpdateActivePlayerData", "");

		// ============================================================
		// WRITE BLOCK MASKS
		// ============================================================
		uint blocksMask0 = 0;
		for (int b = 0; b < 32; b++)
			if (blocks[b] != 0) blocksMask0 |= (1u << b);
		uint blocksMask1 = 0;
		for (int b = 32; b < 48; b++)
			if (blocks[b] != 0) blocksMask1 |= (1u << (b - 32));

		data.WriteUInt32(blocksMask0);
		data.WriteBits(blocksMask1, 16);

		for (int b = 0; b < 48; b++)
		{
			bool blockSet = (b < 32) ? ((blocksMask0 & (1u << b)) != 0) : ((blocksMask1 & (1u << (b - 32))) != 0);
			if (blockSet)
				data.WriteBits(blocks[b], 32);
		}

		// Dynamic field masks (TC343 order: bits 1, 2, 3, 20-25, 4-19)
		// Bit 1: SortBagsRightToLeft — not used
		// Bit 2: InsertItemsLeftToRight — not used
		if (IsBitSet(3)) // KnownTitles
		{
			data.WriteBits((uint)knownTitlesCount, 32); // array element count
			for (int i = 0; i < knownTitlesCount; i++)
				data.WriteBit(true); // all elements changed
		}
		// Bits 4-25: other dynamic fields — not used
		data.FlushBits(); // end of dynamic mask section

		// Dynamic field data (TC343 order: Research data, then KnownTitles, then others)
		if (IsBitSet(3)) // KnownTitles data
		{
			for (int i = 0; i < knownTitlesCount; i++)
				data.WriteUInt64(knownTitles64[i]);
		}

		// ============================================================
		// WRITE SCALAR DATA — Block 0 (bits 26-37)
		// ============================================================
		if (IsBitSet(0))
		{
			if (IsBitSet(26)) data.WritePackedGuid128(a.FarsightObject);
			// 27: SummonedBattlePetGUID skipped
			if (IsBitSet(28)) data.WriteUInt64(a.Coinage.Value);
			if (IsBitSet(29)) data.WriteInt32(a.XP.Value);
			if (IsBitSet(30)) data.WriteInt32(a.NextLevelXP.Value);
			if (IsBitSet(31)) data.WriteInt32(a.TrialXP.Value);
			if (IsBitSet(32)) WriteUpdateSkillInfo(data, a.Skill);
			if (IsBitSet(33)) data.WriteInt32(a.CharacterPoints.Value);
			if (IsBitSet(34)) data.WriteInt32(a.MaxTalentTiers.Value);
			if (IsBitSet(35)) data.WriteUInt32(a.TrackCreatureMask.Value);
			if (IsBitSet(36)) data.WriteFloat(a.MainhandExpertise.Value);
			if (IsBitSet(37)) data.WriteFloat(a.OffhandExpertise.Value);
		}

		// ============================================================
		// WRITE SCALAR DATA — Block 38 (bits 39-69)
		// ============================================================
		if (IsBitSet(38))
		{
			if (IsBitSet(39)) data.WriteFloat(a.RangedExpertise.Value);
			if (IsBitSet(40)) data.WriteFloat(a.CombatRatingExpertise.Value);
			if (IsBitSet(41)) data.WriteFloat(a.BlockPercentage.Value);
			if (IsBitSet(42)) data.WriteFloat(a.DodgePercentage.Value);
			if (IsBitSet(43)) data.WriteFloat(a.DodgePercentageFromAttribute.Value);
			if (IsBitSet(44)) data.WriteFloat(a.ParryPercentage.Value);
			if (IsBitSet(45)) data.WriteFloat(a.ParryPercentageFromAttribute.Value);
			if (IsBitSet(46)) data.WriteFloat(a.CritPercentage.Value);
			if (IsBitSet(47)) data.WriteFloat(a.RangedCritPercentage.Value);
			if (IsBitSet(48)) data.WriteFloat(a.OffhandCritPercentage.Value);
			if (IsBitSet(49)) data.WriteInt32(a.ShieldBlock.Value);
			// 50: ShieldBlockCritPercentage skipped
			if (IsBitSet(51)) data.WriteFloat(a.Mastery.Value);
			if (IsBitSet(52)) data.WriteFloat(a.Speed.Value);
			if (IsBitSet(53)) data.WriteFloat(a.Avoidance.Value);
			if (IsBitSet(54)) data.WriteFloat(a.Sturdiness.Value);
			if (IsBitSet(55)) data.WriteInt32(a.Versatility.Value);
			if (IsBitSet(56)) data.WriteFloat(a.VersatilityBonus.Value);
			if (IsBitSet(57)) data.WriteFloat(a.PvpPowerDamage.Value);
			if (IsBitSet(58)) data.WriteFloat(a.PvpPowerHealing.Value);
			if (IsBitSet(59)) data.WriteInt32(a.ModHealingDonePos.Value);
			if (IsBitSet(60)) data.WriteFloat(a.ModHealingPercent.Value);
			if (IsBitSet(61)) data.WriteFloat(a.ModHealingDonePercent.Value);
			if (IsBitSet(62)) data.WriteFloat(a.ModPeriodicHealingDonePercent.Value);
			if (IsBitSet(63)) data.WriteFloat(a.ModSpellPowerPercent.Value);
			if (IsBitSet(64)) data.WriteFloat(a.ModResiliencePercent.Value);
			if (IsBitSet(65)) data.WriteFloat(a.OverrideSpellPowerByAPPercent.Value);
			if (IsBitSet(66)) data.WriteFloat(a.OverrideAPBySpellPowerPercent.Value);
			if (IsBitSet(67)) data.WriteInt32(a.ModTargetResistance.Value);
			if (IsBitSet(68)) data.WriteInt32(a.ModTargetPhysicalResistance.Value);
			if (IsBitSet(69)) data.WriteUInt32(a.LocalFlags.Value);
		}

		// ============================================================
		// WRITE SCALAR DATA — Block 70 (bits 71-101)
		// ============================================================
		if (IsBitSet(70))
		{
			if (IsBitSet(71)) data.WriteUInt8(a.GrantableLevels.Value);
			if (IsBitSet(72)) data.WriteUInt8(a.MultiActionBars.Value);
			if (IsBitSet(73)) data.WriteUInt8(a.LifetimeMaxRank.Value);
			if (IsBitSet(74)) data.WriteUInt8(a.NumRespecs.Value);
			if (IsBitSet(75)) data.WriteInt32((int)a.AmmoID.Value);
			if (IsBitSet(76)) data.WriteUInt32(a.PvpMedals.Value);
			if (IsBitSet(77)) data.WriteUInt16(a.TodayHonorableKills.Value);
			if (IsBitSet(78)) data.WriteUInt16(a.TodayDishonorableKills.Value);
			if (IsBitSet(79)) data.WriteUInt16(a.YesterdayHonorableKills.Value);
			if (IsBitSet(80)) data.WriteUInt16(a.YesterdayDishonorableKills.Value);
			if (IsBitSet(81)) data.WriteUInt16(a.LastWeekHonorableKills.Value);
			if (IsBitSet(82)) data.WriteUInt16(a.LastWeekDishonorableKills.Value);
			if (IsBitSet(83)) data.WriteUInt16(a.ThisWeekHonorableKills.Value);
			if (IsBitSet(84)) data.WriteUInt16(a.ThisWeekDishonorableKills.Value);
			if (IsBitSet(85)) data.WriteUInt32(a.ThisWeekContribution.Value);
			if (IsBitSet(86)) data.WriteUInt32(a.LifetimeHonorableKills.Value);
			if (IsBitSet(87)) data.WriteUInt32(a.LifetimeDishonorableKills.Value);
			// 88: Field_F24 skipped
			if (IsBitSet(89)) data.WriteUInt32(a.YesterdayContribution.Value);
			if (IsBitSet(90)) data.WriteUInt32(a.LastWeekContribution.Value);
			if (IsBitSet(91)) data.WriteUInt32(a.LastWeekRank.Value);
			if (IsBitSet(92)) data.WriteInt32(a.WatchedFactionIndex.Value);
			if (IsBitSet(93)) data.WriteInt32(a.MaxLevel.Value);
			if (IsBitSet(94)) data.WriteInt32(a.ScalingPlayerLevelDelta.Value);
			if (IsBitSet(95)) data.WriteInt32(a.MaxCreatureScalingLevel.Value);
			if (IsBitSet(96)) data.WriteInt32(a.PetSpellPower.Value);
			if (IsBitSet(97)) data.WriteFloat(a.UiHitModifier.Value);
			if (IsBitSet(98)) data.WriteFloat(a.UiSpellHitModifier.Value);
			if (IsBitSet(99)) data.WriteInt32(a.HomeRealmTimeOffset.Value);
			if (IsBitSet(100)) data.WriteFloat(a.ModPetHaste.Value);
			if (IsBitSet(101)) data.WriteUInt8(a.LocalRegenFlags.Value);
		}

		// ============================================================
		// WRITE SCALAR DATA — Block 102 (bits 103-123)
		// ============================================================
		if (IsBitSet(102))
		{
			if (IsBitSet(103)) data.WriteUInt8(a.AuraVision.Value);
			if (IsBitSet(104)) data.WriteUInt8(a.NumBackpackSlots.Value);
			if (IsBitSet(105)) data.WriteInt32(a.OverrideSpellsID.Value);
			if (IsBitSet(106)) data.WriteInt32(a.LfgBonusFactionID.Value);
			if (IsBitSet(107)) data.WriteUInt16((ushort)a.LootSpecID.Value);
			if (IsBitSet(108)) data.WriteUInt32(a.OverrideZonePVPType.Value);
			if (IsBitSet(109)) data.WriteInt32(a.Honor.Value);
			if (IsBitSet(110)) data.WriteInt32(a.HonorNextLevel.Value);
			// 111: Field_F74 skipped
			if (IsBitSet(112)) data.WriteInt32((int)a.PvPTierMaxFromWins.Value);
			if (IsBitSet(113)) data.WriteInt32((int)a.PvPLastWeeksTierMaxFromWins.Value);
			if (IsBitSet(114)) data.WriteUInt8(a.PvPRankProgress.Value);
			// 115-119 skipped
			if (IsBitSet(120)) data.WriteUInt8(this.m_gameState.GlyphsEnabled);
			// 121-123 skipped
			data.FlushBits(); // TC343 flushes here before complex struct fields (116/117/122)
		}

		// ============================================================
		// WRITE ARRAY DATA — TC343 write order
		// ============================================================

		// InvSlots (header 124, elements 125-265)
		if (IsBitSet(124))
		{
			for (int i = 0; i < 141; i++)
			{
				if (IsBitSet(125 + i))
				{
					WowGuid128 guid = GetModernInvSlot(a, i) ?? WowGuid128.Empty;
					data.WritePackedGuid128(guid);
				}
			}
		}

		// TrackResourceMask (header 266, elements 267-268)
		if (IsBitSet(266))
		{
			for (int i = 0; i < 2; i++)
				if (IsBitSet(267 + i))
					data.WriteUInt32(a.TrackResourceMask[i].Value);
		}

		// SpellCritPercentage (header 269, elements 270-276)
		// ModDamageDonePos (header 269, elements 277-283)
		// ModDamageDoneNeg (header 269, elements 284-290)
		// ModDamageDonePercent (header 269, elements 291-297)
		if (IsBitSet(269))
		{
			for (int i = 0; i < 7; i++)
				if (IsBitSet(270 + i))
					data.WriteFloat(a.SpellCritPercentage[i].Value);
			for (int i = 0; i < 7; i++)
				if (IsBitSet(277 + i))
					data.WriteInt32(a.ModDamageDonePos[i].Value);
			for (int i = 0; i < 7; i++)
				if (IsBitSet(284 + i))
					data.WriteInt32(a.ModDamageDoneNeg[i].Value);
			for (int i = 0; i < 7; i++)
				if (IsBitSet(291 + i))
					data.WriteFloat(a.ModDamageDonePercent[i].Value);
		}

		// ExploredZones (header 298, elements 299-538)
		if (IsBitSet(298))
		{
			for (int i = 0; i < 240; i++)
				if (IsBitSet(299 + i))
					data.WriteUInt64(a.ExploredZones[i].Value);
		}

		// RestInfo (header 539, elements 540-541) — nested struct HasChangesMask<3>
		if (IsBitSet(539))
		{
			for (int i = 0; i < 2; i++)
			{
				if (IsBitSet(540 + i))
				{
					var ri = a.RestInfo[i];
					uint restMask = 0;
					if (ri != null && ri.Threshold.HasValue) restMask |= 2;
					if (ri != null && ri.StateID.HasValue) restMask |= 4;
					if (restMask != 0) restMask |= 1; // group bit
					data.WriteBits(restMask, 3);
					data.FlushBits();
					if ((restMask & 2) != 0) data.WriteUInt32(ri.Threshold.Value);
					if ((restMask & 4) != 0) data.WriteUInt8((byte)ri.StateID.Value);
				}
			}
		}

		// WeaponDmgMultipliers (header 542, elements 543-545)
		// WeaponAtkSpeedMultipliers (header 542, elements 546-548)
		if (IsBitSet(542))
		{
			for (int i = 0; i < 3; i++)
				if (IsBitSet(543 + i))
					data.WriteFloat(a.WeaponDmgMultipliers[i].Value);
			for (int i = 0; i < 3; i++)
				if (IsBitSet(546 + i))
					data.WriteFloat(a.WeaponAtkSpeedMultipliers[i].Value);
		}

		// BuybackPrice (header 549, elements 550-561)
		// BuybackTimestamp (header 549, elements 562-573)
		if (IsBitSet(549))
		{
			for (int i = 0; i < 12; i++)
				if (IsBitSet(550 + i))
					data.WriteUInt32(a.BuybackPrice[i].Value);
			for (int i = 0; i < 12; i++)
				if (IsBitSet(562 + i))
					data.WriteInt64((long)a.BuybackTimestamp[i].Value);
		}

		// CombatRatings (header 574, elements 575-606)
		if (IsBitSet(574))
		{
			for (int i = 0; i < 32; i++)
				if (IsBitSet(575 + i))
					data.WriteInt32(a.CombatRatings[i].Value);
		}

		// NoReagentCostMask (header 615, elements 616-619)
		if (IsBitSet(615))
		{
			for (int i = 0; i < 4; i++)
				if (IsBitSet(616 + i))
					data.WriteUInt32(a.NoReagentCostMask[i].Value);
		}

		// ProfessionSkillLine (header 620, elements 621-622)
		if (IsBitSet(620))
		{
			for (int i = 0; i < 2; i++)
				if (IsBitSet(621 + i))
					data.WriteInt32(a.ProfessionSkillLine[i].Value);
		}

		// BagSlotFlags (header 623, elements 624-627)
		if (IsBitSet(623))
		{
			for (int i = 0; i < 4; i++)
				if (IsBitSet(624 + i))
					data.WriteUInt32(a.BagSlotFlags[i].Value);
		}

		// BankBagSlotFlags (header 628, elements 629-635)
		if (IsBitSet(628))
		{
			for (int i = 0; i < 7; i++)
				if (IsBitSet(629 + i))
					data.WriteUInt32(a.BankBagSlotFlags[i].Value);
		}

		// QuestCompleted (header 636, elements 637-1511)
		if (IsBitSet(636))
		{
			for (int i = 0; i < 875; i++)
				if (IsBitSet(637 + i))
					data.WriteUInt64(a.QuestCompleted[i].Value);
		}

		// GlyphSlots (header 1512, elements 1513-1518) + Glyphs (elements 1519-1524)
		if (IsBitSet(1512))
		{
			uint[] glyphSlotIds = { 21, 22, 23, 24, 25, 26 };
			for (int i = 0; i < 6; i++)
				if (IsBitSet(1513 + i))
					data.WriteUInt32(glyphSlotIds[i]);
			for (int i = 0; i < 6; i++)
				if (IsBitSet(1519 + i))
					data.WriteUInt32((uint)(this.m_gameState.ActiveGlyphs[i]));
		}

		// PvpInfo (header 607, elements 608-614) — nested struct HasChangesMask<19>
		if (IsBitSet(607))
		{
			for (int i = 0; i < 7; i++)
			{
				if (IsBitSet(608 + i))
				{
					PVPInfo pi = (a.PvpInfo != null && i < a.PvpInfo.Length) ? a.PvpInfo[i] : null;
					// Build 19-bit changesMask for this PvpInfo entry
					uint pvpMask = 0;
					if (pi != null)
					{
						// Bit 1: Disqualified, 2: Bracket, 3: PvpRatingID
						// 4: WeeklyPlayed, 5: WeeklyWon, 6: SeasonPlayed, 7: SeasonWon
						// 8: Rating, 9: WeeklyBestRating, 10: SeasonBestRating
						// 11: PvpTierID, 12: WeeklyBestWinPvpTierID, 13: Field_28, 14: Field_2C
						// 15-18: Round stats (not in HermesProxy PVPInfo)
						if (pi.Disqualified) pvpMask |= (1u << 1);
						if (pi.WeeklyPlayed != 0) pvpMask |= (1u << 4);
						if (pi.WeeklyWon != 0) pvpMask |= (1u << 5);
						if (pi.SeasonPlayed != 0) pvpMask |= (1u << 6);
						if (pi.SeasonWon != 0) pvpMask |= (1u << 7);
						if (pi.Rating != 0) pvpMask |= (1u << 8);
						if (pi.WeeklyBestRating != 0) pvpMask |= (1u << 9);
						if (pi.SeasonBestRating != 0) pvpMask |= (1u << 10);
						if (pi.PvpTierID != 0) pvpMask |= (1u << 11);
						if (pi.WeeklyBestWinPvpTierID != 0) pvpMask |= (1u << 12);
						if (pi.Field_28 != 0) pvpMask |= (1u << 13);
						if (pi.Field_2C != 0) pvpMask |= (1u << 14);
					}
					if (pvpMask != 0) pvpMask |= 1; // group bit

					// Write 19-bit mask then data
					data.WriteBits(pvpMask, 19);
					if ((pvpMask & (1u << 1)) != 0) data.WriteBit(pi.Disqualified);
					data.FlushBits();
					if ((pvpMask & 1) != 0)
					{
						// Bit 2: Bracket (int8) — not in HermesProxy, write 0
						// Bit 3: PvpRatingID (int32) — not in HermesProxy, write 0
						if ((pvpMask & (1u << 4)) != 0) data.WriteUInt32(pi.WeeklyPlayed);
						if ((pvpMask & (1u << 5)) != 0) data.WriteUInt32(pi.WeeklyWon);
						if ((pvpMask & (1u << 6)) != 0) data.WriteUInt32(pi.SeasonPlayed);
						if ((pvpMask & (1u << 7)) != 0) data.WriteUInt32(pi.SeasonWon);
						if ((pvpMask & (1u << 8)) != 0) data.WriteUInt32(pi.Rating);
						if ((pvpMask & (1u << 9)) != 0) data.WriteUInt32(pi.WeeklyBestRating);
						if ((pvpMask & (1u << 10)) != 0) data.WriteUInt32(pi.SeasonBestRating);
						if ((pvpMask & (1u << 11)) != 0) data.WriteUInt32(pi.PvpTierID);
						if ((pvpMask & (1u << 12)) != 0) data.WriteUInt32(pi.WeeklyBestWinPvpTierID);
						if ((pvpMask & (1u << 13)) != 0) data.WriteUInt32(pi.Field_28);
						if ((pvpMask & (1u << 14)) != 0) data.WriteUInt32(pi.Field_2C);
					}
				}
			}
		}

		data.FlushBits();
	}

	private bool HasAnyUnitFieldSet()
	{
		UnitData u = this.m_updateData.UnitData;
		if (u == null) return false;
		if (u.Health.HasValue || u.MaxHealth.HasValue || u.DisplayID.HasValue) return true;
		if (u.Charm != null || u.Summon != null || u.CharmedBy != null) return true;
		if (u.SummonedBy != null || u.CreatedBy != null || u.Target != null) return true;
		if (u.ChannelData != null) return true;
		if (u.RaceId.HasValue || u.ClassId.HasValue || u.SexId.HasValue) return true;
		if (u.Level.HasValue || u.EffectiveLevel.HasValue || u.DisplayPower.HasValue) return true;
		if (u.FactionTemplate.HasValue || u.Flags.HasValue || u.Flags2.HasValue || u.Flags3.HasValue) return true;
		if (u.AuraState.HasValue || u.OverrideDisplayPowerID.HasValue) return true;
		if (u.BoundingRadius.HasValue || u.CombatReach.HasValue) return true;
		if (u.DisplayScale.HasValue || u.NativeXDisplayScale.HasValue) return true;
		if (u.NativeDisplayID.HasValue || u.MountDisplayID.HasValue) return true;
		if (u.HoverHeight.HasValue || u.GuildGUID != null) return true;
		if (u.NpcFlags != null)
			for (int i = 0; i < u.NpcFlags.Length; i++)
				if (u.NpcFlags[i].HasValue && u.NpcFlags[i] != 0) return true;
		if (u.Power != null)
			for (int i = 0; i < u.Power.Length; i++)
				if (u.Power[i].HasValue) return true;
		if (u.MaxPower != null)
			for (int i = 0; i < u.MaxPower.Length; i++)
				if (u.MaxPower[i].HasValue) return true;
		// Block 1 continued + Block 2 combat stats
		if (u.MinDamage.HasValue || u.MaxDamage.HasValue || u.MinOffHandDamage.HasValue || u.MaxOffHandDamage.HasValue) return true;
		if (u.StandState.HasValue || u.VisFlags.HasValue || u.AnimTier.HasValue) return true;
		if (u.ModCastSpeed.HasValue || u.ModCastHaste.HasValue || u.EmoteState.HasValue) return true;
		if (u.SheatheState.HasValue || u.ShapeshiftForm.HasValue) return true;
		if (u.AttackPower.HasValue || u.AttackPowerModPos.HasValue || u.AttackPowerModNeg.HasValue) return true;
		if (u.RangedAttackPower.HasValue || u.BaseMana.HasValue || u.BaseHealth.HasValue) return true;
		// Block 5: Stats
		if (u.Stats != null)
			for (int i = 0; i < u.Stats.Length; i++)
				if (u.Stats[i].HasValue) return true;
		if (u.StatPosBuff != null)
			for (int i = 0; i < u.StatPosBuff.Length; i++)
				if (u.StatPosBuff[i].HasValue) return true;
		if (u.StatNegBuff != null)
			for (int i = 0; i < u.StatNegBuff.Length; i++)
				if (u.StatNegBuff[i].HasValue) return true;
		// Blocks 5-6: Resistances
		if (u.Resistances != null)
			for (int i = 0; i < 7; i++)
				if (u.Resistances[i].HasValue) return true;
		if (u.ResistanceBuffModsPositive != null)
			for (int i = 0; i < 7; i++)
				if (u.ResistanceBuffModsPositive[i].HasValue) return true;
		if (u.ResistanceBuffModsNegative != null)
			for (int i = 0; i < 7; i++)
				if (u.ResistanceBuffModsNegative[i].HasValue) return true;
		if (u.AttackRoundBaseTime != null)
			for (int i = 0; i < u.AttackRoundBaseTime.Length; i++)
				if (u.AttackRoundBaseTime[i].HasValue) return true;
		return false;
	}

	/// <summary>
	/// Returns true if any field in the SkillInfo has a value set.
	/// </summary>
	private static bool HasAnySkillChanged(SkillInfo s)
	{
		for (int i = 0; i < 256; i++)
		{
			if (s.SkillLineID[i].HasValue) return true;
			if (s.SkillRank[i].HasValue) return true;
			if (s.SkillMaxRank[i].HasValue) return true;
		}
		return false;
	}

	/// <summary>
	/// Writes SkillInfo nested update using TC343 format.
	/// HasChangesMask&lt;1793&gt; = 57 blocks of 32 bits.
	/// Bit 0: root flag, Bits 1-256: SkillLineID[0-255],
	/// Bits 257-512: SkillStep, Bits 513-768: SkillRank,
	/// Bits 769-1024: SkillStartingRank, Bits 1025-1280: SkillMaxRank,
	/// Bits 1281-1536: SkillTempBonus, Bits 1537-1792: SkillPermBonus.
	///
	/// Mask encoding:
	///   1. WriteUInt32(blocksMask0) — which of blocks 0-31 have changes
	///   2. WriteBits(blocksMask1, 25) — which of blocks 32-56 have changes
	///   3. For each set block: WriteBits(block[b], 32)
	///   4. FlushBits
	///   5. Data: per-skill interleaved (all 7 fields for skill i before skill i+1)
	/// </summary>
	private static void WriteUpdateSkillInfo(WorldPacket data, SkillInfo s)
	{
		if (s == null)
		{
			// Empty skill update — write empty masks
			data.WriteUInt32(0); // blocksMask0
			data.WriteBits(0, 25); // blocksMask1
			data.FlushBits();
			return;
		}

		// Build 1793-bit changesMask (57 blocks of 32)
		uint[] skillBlocks = new uint[57];
		void SB(int bit) { skillBlocks[bit / 32] |= (1u << (bit % 32)); }

		bool anyChanged = false;
		for (int i = 0; i < 256; i++)
		{
			if (s.SkillLineID[i].HasValue) { SB(1 + i); anyChanged = true; }
			if (s.SkillStep[i].HasValue) { SB(257 + i); anyChanged = true; }
			if (s.SkillRank[i].HasValue) { SB(513 + i); anyChanged = true; }
			if (s.SkillStartingRank[i].HasValue) { SB(769 + i); anyChanged = true; }
			if (s.SkillMaxRank[i].HasValue) { SB(1025 + i); anyChanged = true; }
			if (s.SkillTempBonus[i].HasValue) { SB(1281 + i); anyChanged = true; }
			if (s.SkillPermBonus[i].HasValue) { SB(1537 + i); anyChanged = true; }
		}

		if (anyChanged)
			SB(0); // root bit

		// Compute blocksMask0 (which of blocks 0-31 have non-zero masks)
		uint blocksMask0 = 0;
		for (int b = 0; b < 32 && b < 57; b++)
			if (skillBlocks[b] != 0) blocksMask0 |= (1u << b);

		// Compute blocksMask1 (which of blocks 32-56 have non-zero masks) — 25 bits
		uint blocksMask1 = 0;
		for (int b = 32; b < 57; b++)
			if (skillBlocks[b] != 0) blocksMask1 |= (1u << (b - 32));

		// Write masks
		data.WriteUInt32(blocksMask0);
		data.WriteBits(blocksMask1, 25);

		// Write each set block's 32-bit mask
		for (int b = 0; b < 57; b++)
		{
			bool blockSet = (b < 32) ? ((blocksMask0 & (1u << b)) != 0) : ((blocksMask1 & (1u << (b - 32))) != 0);
			if (blockSet)
				data.WriteBits(skillBlocks[b], 32);
		}

		data.FlushBits();

		// Write data — per-skill interleaved (TC343 order)
		if ((skillBlocks[0] & 1) != 0) // root bit set
		{
			for (int i = 0; i < 256; i++)
			{
				int lineIdBit = 1 + i;
				if ((skillBlocks[lineIdBit / 32] & (1u << (lineIdBit % 32))) != 0)
					data.WriteUInt16(s.SkillLineID[i].Value);

				int stepBit = 257 + i;
				if ((skillBlocks[stepBit / 32] & (1u << (stepBit % 32))) != 0)
					data.WriteUInt16(s.SkillStep[i].Value);

				int rankBit = 513 + i;
				if ((skillBlocks[rankBit / 32] & (1u << (rankBit % 32))) != 0)
					data.WriteUInt16(s.SkillRank[i].Value);

				int startBit = 769 + i;
				if ((skillBlocks[startBit / 32] & (1u << (startBit % 32))) != 0)
					data.WriteUInt16(s.SkillStartingRank[i].Value);

				int maxBit = 1025 + i;
				if ((skillBlocks[maxBit / 32] & (1u << (maxBit % 32))) != 0)
					data.WriteUInt16(s.SkillMaxRank[i].Value);

				int tempBit = 1281 + i;
				if ((skillBlocks[tempBit / 32] & (1u << (tempBit % 32))) != 0)
					data.WriteInt16(s.SkillTempBonus[i].Value);

				int permBit = 1537 + i;
				if ((skillBlocks[permBit / 32] & (1u << (permBit % 32))) != 0)
					data.WriteUInt16(s.SkillPermBonus[i].Value);
			}
		}
	}

	private void WriteValuesUpdate(WorldPacket data)
	{
		uint changedMask = 0u;
		bool hasObjectChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Object) && this.m_updateData.ObjectData != null && (this.m_updateData.ObjectData.EntryID.HasValue || this.m_updateData.ObjectData.DynamicFlags.HasValue || this.m_updateData.ObjectData.Scale.HasValue);
		bool hasUnitChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit) && this.m_updateData.UnitData != null && this.HasAnyUnitFieldSet();
		bool hasItemChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Item) && this.m_updateData.ItemData != null;

		bool hasActivePlayerChanges = this.HasActivePlayerChanges();

		if (hasObjectChanges)
		{
			changedMask |= 1;
		}
		if (hasItemChanges)
		{
			changedMask |= 2;
		}
		if (hasUnitChanges)
		{
			changedMask |= 0x20;
		}
		// Only set Player block when there are actual PlayerData changes (matching TC343)
		bool hasPlayerChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Player) && this.HasAnyPlayerFieldSet();
		if (hasPlayerChanges)
		{
			changedMask |= 0x40;
		}
		if (hasActivePlayerChanges)
		{
			changedMask |= 0x80;
		}
		bool hasGameObjectChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.GameObject) && this.m_updateData.GameObjectData != null && this.HasAnyGameObjectFieldSet();
		if (hasGameObjectChanges)
		{
			changedMask |= 0x100;
		}
		// Safety: if changedMask is 0, nothing to write
		if (changedMask == 0)
			return;
		data.WriteUInt32(changedMask);
		if (hasObjectChanges)
		{
			this.WriteUpdateObjectData(data);
		}
		if (hasItemChanges)
		{
			this.WriteUpdateItemData(data);
		}
		if (hasUnitChanges)
		{
			this.WriteUpdateUnitData(data);
		}
		if (hasPlayerChanges)
		{
			this.WriteUpdatePlayerData(data);
		}
		if (hasActivePlayerChanges)
		{
			this.WriteUpdateActivePlayerData(data);
		}
		if (hasGameObjectChanges)
		{
			this.WriteUpdateGameObjectData(data);
		}
	}

	private void WriteCreateObjectData(WorldPacket data)
	{
		ObjectData obj = this.m_updateData.ObjectData;
		data.WriteInt32(obj.EntryID.GetValueOrDefault());
		data.WriteUInt32(obj.DynamicFlags.GetValueOrDefault());
		data.WriteFloat(obj.Scale ?? 1f);
	}

	private void WriteUpdateObjectData(WorldPacket data)
	{
		ObjectData obj = this.m_updateData.ObjectData;
		uint mask = 0u;
		if (obj.EntryID.HasValue)
		{
			mask |= 2;
		}
		if (obj.DynamicFlags.HasValue)
		{
			mask |= 4;
		}
		if (obj.Scale.HasValue)
		{
			mask |= 8;
		}
		if (mask != 0)
		{
			mask |= 1;
		}
		data.WriteBits(mask, 4);
		data.FlushBits();
		if ((mask & 1) != 0)
		{
			if (obj.EntryID.HasValue)
			{
				data.WriteInt32(obj.EntryID.Value);
			}
			if (obj.DynamicFlags.HasValue)
			{
				data.WriteUInt32(obj.DynamicFlags.Value);
			}
			if (obj.Scale.HasValue)
			{
				data.WriteFloat(obj.Scale.Value);
			}
		}
	}

	private void WriteCreateItemData(WorldPacket data)
	{
		ItemData item = this.m_updateData.ItemData;
		if (item == null)
		{
			this.WriteEmptyItemCreate(data);
			return;
		}
		data.WritePackedGuid128(item.Owner ?? WowGuid128.Empty);
		data.WritePackedGuid128(item.ContainedIn ?? WowGuid128.Empty);
		data.WritePackedGuid128(item.Creator ?? WowGuid128.Empty);
		data.WritePackedGuid128(item.GiftCreator ?? WowGuid128.Empty);
		if (this.IsOwner)
		{
			data.WriteUInt32(item.StackCount.GetValueOrDefault());
			data.WriteUInt32(item.Duration.GetValueOrDefault());
			for (int i = 0; i < 5; i++)
			{
				data.WriteInt32(item.SpellCharges[i].GetValueOrDefault());
			}
		}
		data.WriteUInt32(item.Flags.GetValueOrDefault());
		for (int j = 0; j < 13; j++)
		{
			if (item.Enchantment[j] != null)
			{
				data.WriteInt32(item.Enchantment[j].ID.GetValueOrDefault());
				data.WriteUInt32(item.Enchantment[j].Duration.GetValueOrDefault());
				data.WriteInt16((short)item.Enchantment[j].Charges.GetValueOrDefault());
				data.WriteUInt16(item.Enchantment[j].Inactive.GetValueOrDefault());
			}
			else
			{
				data.WriteInt32(0);
				data.WriteUInt32(0u);
				data.WriteInt16(0);
				data.WriteUInt16(0);
			}
		}
		data.WriteInt32((int)item.PropertySeed.GetValueOrDefault());
		data.WriteInt32((int)item.RandomProperty.GetValueOrDefault());
		if (this.IsOwner)
		{
			data.WriteUInt32(item.Durability.GetValueOrDefault());
			data.WriteUInt32(item.MaxDurability.GetValueOrDefault());
		}
		data.WriteUInt32(item.CreatePlayedTime.GetValueOrDefault());
		data.WriteInt32(0);
		data.WriteInt64(0L);
		if (this.IsOwner)
		{
			data.WriteUInt64(0uL);
			data.WriteUInt8(0);
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			data.WriteUInt32(0u);
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			data.WriteUInt16(0);
		}
		data.WriteInt32(0);
	}

	private void WriteEmptyItemCreate(WorldPacket data)
	{
		for (int i = 0; i < 4; i++)
		{
			data.WritePackedGuid128(WowGuid128.Empty);
		}
		if (this.IsOwner)
		{
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			for (int j = 0; j < 5; j++)
			{
				data.WriteInt32(0);
			}
		}
		data.WriteUInt32(0u);
		for (int k = 0; k < 13; k++)
		{
			data.WriteInt32(0);
			data.WriteUInt32(0u);
			data.WriteInt16(0);
			data.WriteUInt16(0);
		}
		data.WriteInt32(0);
		data.WriteInt32(0);
		if (this.IsOwner)
		{
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
		}
		data.WriteUInt32(0u);
		data.WriteInt32(0);
		data.WriteInt64(0L);
		if (this.IsOwner)
		{
			data.WriteUInt64(0uL);
			data.WriteUInt8(0);
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			data.WriteUInt32(0u);
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			data.WriteUInt16(0);
		}
		data.WriteInt32(0);
	}

	private void WriteUpdateItemData(WorldPacket data)
	{
		ItemData item = this.m_updateData.ItemData;
		if (item == null)
		{
			data.WriteBits(0, 2);
			data.FlushBits();
			return;
		}

		// ItemData changesMask: 43 bits = 2 blocks of 32
		// TC343 bit layout:
		//   0: group bit for bits 1-22
		//   1: ArtifactPowers (dynamic), 2: Gems (dynamic)
		//   3: Owner, 4: ContainedIn, 5: Creator, 6: GiftCreator
		//   7: StackCount, 8: Expiration/Duration, 9: DynamicFlags/Flags
		//  10: PropertySeed, 11: RandomPropertiesID, 12: Durability, 13: MaxDurability
		//  14: CreatePlayedTime, 15: Context, 16: CreateTime, 17: ArtifactXP
		//  18: ItemAppearanceModID, 19: Modifiers, 20: DynamicFlags2, 21: ItemBonusKey
		//  22: DEBUGItemLevel
		//  23: group bit for SpellCharges[5] (bits 24-28)
		//  29: group bit for Enchantment[13] (bits 30-42)
		uint[] blocks = new uint[2];
		void SetBit(int bit) { blocks[bit / 32] |= (1u << (bit % 32)); }

		if (item.Owner != null) { SetBit(0); SetBit(3); }
		if (item.ContainedIn != null) { SetBit(0); SetBit(4); }
		if (item.Creator != null) { SetBit(0); SetBit(5); }
		if (item.GiftCreator != null) { SetBit(0); SetBit(6); }
		if (item.StackCount.HasValue) { SetBit(0); SetBit(7); }
		if (item.Duration.HasValue) { SetBit(0); SetBit(8); }
		if (item.Flags.HasValue) { SetBit(0); SetBit(9); }
		if (item.PropertySeed.HasValue) { SetBit(0); SetBit(10); }
		if (item.RandomProperty.HasValue) { SetBit(0); SetBit(11); }
		if (item.Durability.HasValue) { SetBit(0); SetBit(12); }
		if (item.MaxDurability.HasValue) { SetBit(0); SetBit(13); }
		if (item.CreatePlayedTime.HasValue) { SetBit(0); SetBit(14); }
		if (item.Context.HasValue) { SetBit(0); SetBit(15); }
		if (item.ArtifactXP.HasValue) { SetBit(0); SetBit(17); }
		if (item.ItemAppearanceModID.HasValue) { SetBit(0); SetBit(18); }
		for (int i = 0; i < 5; i++)
			if (item.SpellCharges[i].HasValue) { SetBit(23); SetBit(24 + i); }
		for (int i = 0; i < 13; i++)
			if (item.Enchantment[i] != null) { SetBit(29); SetBit(30 + i); }

		// Write blocksMask (2 bits) then each set block (32 bits)
		byte blocksMask = 0;
		if (blocks[0] != 0) blocksMask |= 1;
		if (blocks[1] != 0) blocksMask |= 2;
		data.WriteBits(blocksMask, 2);
		for (int b = 0; b < 2; b++)
			if ((blocksMask & (1 << b)) != 0)
				data.WriteBits(blocks[b], 32);

		// No dynamic fields (ArtifactPowers/Gems not used)
		data.FlushBits();

		// Group 0 scalar fields (bits 3-22)
		if ((blocks[0] & 1) != 0)
		{
			if (item.Owner != null) data.WritePackedGuid128(item.Owner);
			if (item.ContainedIn != null) data.WritePackedGuid128(item.ContainedIn);
			if (item.Creator != null) data.WritePackedGuid128(item.Creator);
			if (item.GiftCreator != null) data.WritePackedGuid128(item.GiftCreator);
			if (item.StackCount.HasValue) data.WriteUInt32(item.StackCount.Value);
			if (item.Duration.HasValue) data.WriteUInt32(item.Duration.Value);
			if (item.Flags.HasValue) data.WriteUInt32(item.Flags.Value);
			if (item.PropertySeed.HasValue) data.WriteInt32((int)item.PropertySeed.Value);
			if (item.RandomProperty.HasValue) data.WriteInt32((int)item.RandomProperty.Value);
			if (item.Durability.HasValue) data.WriteUInt32(item.Durability.Value);
			if (item.MaxDurability.HasValue) data.WriteUInt32(item.MaxDurability.Value);
			if (item.CreatePlayedTime.HasValue) data.WriteUInt32(item.CreatePlayedTime.Value);
			if (item.Context.HasValue) data.WriteInt32(item.Context.Value);
			if (item.ArtifactXP.HasValue) data.WriteUInt64(item.ArtifactXP.Value);
			if (item.ItemAppearanceModID.HasValue) data.WriteUInt8((byte)item.ItemAppearanceModID.Value);
		}

		// SpellCharges array (group bit 23, entries 24-28)
		if ((blocks[0] & (1u << 23)) != 0)
		{
			for (int i = 0; i < 5; i++)
				if (item.SpellCharges[i].HasValue)
					data.WriteInt32(item.SpellCharges[i].Value);
		}

		// Enchantment array (group bit 29, entries 30-42)
		if ((blocks[0] & (1u << 29)) != 0)
		{
			for (int i = 0; i < 13; i++)
			{
				if (item.Enchantment[i] != null)
				{
					// ItemEnchantment WriteUpdate: 4-bit mask + fields
					uint enchMask = 0;
					if (item.Enchantment[i].ID.HasValue) enchMask |= 2;
					if (item.Enchantment[i].Duration.HasValue) enchMask |= 4;
					if (item.Enchantment[i].Charges.HasValue) enchMask |= 8;
					if (enchMask != 0) enchMask |= 1;
					data.WriteBits(enchMask, 4);
					data.FlushBits();
					if (item.Enchantment[i].ID.HasValue) data.WriteInt32(item.Enchantment[i].ID.Value);
					if (item.Enchantment[i].Duration.HasValue) data.WriteUInt32(item.Enchantment[i].Duration.Value);
					if (item.Enchantment[i].Charges.HasValue) data.WriteUInt16(item.Enchantment[i].Charges.Value);
				}
			}
		}
	}

	private void WriteCreateContainerData(WorldPacket data)
	{
		ContainerData container = this.m_updateData.ContainerData;
		for (int i = 0; i < 36; i++)
		{
			data.WritePackedGuid128(((container != null) ? container.Slots[i] : null) ?? WowGuid128.Empty);
		}
		data.WriteUInt32((container?.NumSlots).GetValueOrDefault());
	}

	private void WriteCreateUnitData(WorldPacket data)
	{
		UnitData unit = this.m_updateData.UnitData ?? new UnitData();
		ObjectData obj = this.m_updateData.ObjectData;
		if (this.IsOwner)
		{
			Log.Print(LogType.Debug, $"[PlayerUnitData] DisplayID={unit.DisplayID} NativeDisplayID={unit.NativeDisplayID} Race={unit.RaceId} Class={unit.ClassId} Sex={unit.SexId} Health={unit.Health}/{unit.MaxHealth} Level={unit.Level}", "WriteCreateUnitData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		data.WriteInt64(unit.Health.GetValueOrDefault());
		data.WriteInt64(unit.MaxHealth.GetValueOrDefault());
		data.WriteInt32(unit.DisplayID.GetValueOrDefault());
		for (int i = 0; i < 2; i++)
		{
			uint?[] npcFlags = unit.NpcFlags;
			data.WriteUInt32(((npcFlags != null) ? npcFlags[i] : ((uint?)null)).GetValueOrDefault());
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WritePackedGuid128(unit.Charm ?? WowGuid128.Empty);
		data.WritePackedGuid128(unit.Summon ?? WowGuid128.Empty);
		if (this.IsOwner)
		{
			data.WritePackedGuid128(unit.Critter ?? WowGuid128.Empty);
		}
		data.WritePackedGuid128(unit.CharmedBy ?? WowGuid128.Empty);
		data.WritePackedGuid128(unit.SummonedBy ?? WowGuid128.Empty);
		data.WritePackedGuid128(unit.CreatedBy ?? WowGuid128.Empty);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WritePackedGuid128(unit.Target ?? WowGuid128.Empty);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WriteUInt64(0uL);
		data.WriteInt32(unit.ChannelData?.SpellID ?? 0);
		data.WriteInt32(unit.ChannelData?.SpellXSpellVisualID ?? 0);
		data.WriteUInt32(0u);
		data.WriteUInt8(unit.RaceId.GetValueOrDefault());
		data.WriteUInt8(unit.ClassId.GetValueOrDefault());
		data.WriteUInt8(unit.PlayerClassId.GetValueOrDefault());
		data.WriteUInt8(unit.SexId.GetValueOrDefault());
		data.WriteUInt8(0);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			for (int j = 0; j < 10; j++)
			{
				data.WriteFloat(0f);
				data.WriteFloat(0f);
			}
		}
		for (int k = 0; k < 10; k++)
		{
			data.WriteInt32((k < 7) ? unit.Power[k].GetValueOrDefault() : 0);
			data.WriteInt32((k < 7) ? unit.MaxPower[k].GetValueOrDefault() : 0);
			data.WriteFloat(0f);
		}
		data.WriteInt32(unit.Level.GetValueOrDefault());
		data.WriteInt32(unit.EffectiveLevel ?? unit.Level.GetValueOrDefault());
		data.WriteInt32(unit.ContentTuningID.GetValueOrDefault());
		data.WriteInt32(unit.ScalingLevelMin.GetValueOrDefault());
		data.WriteInt32(unit.ScalingLevelMax.GetValueOrDefault());
		data.WriteInt32(unit.ScalingLevelDelta.GetValueOrDefault());
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(unit.FactionTemplate.GetValueOrDefault());
		for (int l = 0; l < 3; l++)
		{
			VisibleItem[] virtualItems = unit.VirtualItems;
			int vItemId = (((virtualItems != null) ? virtualItems[l] : null) != null) ? unit.VirtualItems[l].ItemID : 0;
			// For players, VirtualItems may not be set by the server (players use PLAYER_VISIBLE_ITEM).
			// Populate from PlayerData.VisibleItems: slot 0=mainhand(15), 1=offhand(16), 2=ranged(17)
			if (vItemId == 0 && this.IsOwner && this.m_updateData.PlayerData?.VisibleItems != null)
			{
				int playerSlot = 15 + l; // mainhand=15, offhand=16, ranged=17
				if (playerSlot < this.m_updateData.PlayerData.VisibleItems.Length)
				{
					var pv = this.m_updateData.PlayerData.VisibleItems[playerSlot];
					if (pv != null && pv.ItemID != 0)
					{
						vItemId = pv.ItemID;
					}
				}
			}
			data.WriteInt32(vItemId);
			data.WriteUInt16(0);
			data.WriteUInt16(0);
		}
		data.WriteUInt32(unit.Flags.GetValueOrDefault());
		data.WriteUInt32(unit.Flags2.GetValueOrDefault());
		data.WriteUInt32(0u);
		data.WriteUInt32(unit.AuraState.GetValueOrDefault());
		for (int m = 0; m < 2; m++)
		{
			uint?[] attackRoundBaseTime = unit.AttackRoundBaseTime;
			data.WriteUInt32(((attackRoundBaseTime != null) ? attackRoundBaseTime[m] : ((uint?)null)).GetValueOrDefault());
		}
		if (this.IsOwner)
		{
			uint rangedTime = unit.RangedAttackRoundBaseTime.GetValueOrDefault();
			// If server didn't send ranged attack time but player has a ranged weapon visible,
			// default to 2300ms (standard bow speed) so the client enables Auto Shot
			if (rangedTime == 0 && this.m_updateData.PlayerData?.VisibleItems != null)
			{
				var rangedVisible = this.m_updateData.PlayerData.VisibleItems.Length > 17 ? this.m_updateData.PlayerData.VisibleItems[17] : null;
				if (rangedVisible != null && rangedVisible.ItemID != 0)
					rangedTime = 2300;
			}
			data.WriteUInt32(rangedTime);
		}
		data.WriteFloat(unit.BoundingRadius ?? 0.389f);
		data.WriteFloat(unit.CombatReach ?? 1.5f);
		data.WriteFloat(1f);
		data.WriteInt32(unit.NativeDisplayID.GetValueOrDefault());
		data.WriteFloat(1f);
		data.WriteInt32(unit.MountDisplayID.GetValueOrDefault());
		if ((this.IsOwner || this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit)) && this.IsOwner)
		{
			data.WriteFloat(unit.MinDamage.GetValueOrDefault());
			data.WriteFloat(unit.MaxDamage.GetValueOrDefault());
			data.WriteFloat(unit.MinOffHandDamage.GetValueOrDefault());
			data.WriteFloat(unit.MaxOffHandDamage.GetValueOrDefault());
		}
		data.WriteUInt8(unit.StandState.GetValueOrDefault());
		data.WriteUInt8(unit.PetLoyaltyIndex.GetValueOrDefault());
		data.WriteUInt8(unit.VisFlags.GetValueOrDefault());
		data.WriteUInt8(unit.AnimTier.GetValueOrDefault());
		data.WriteUInt32(unit.PetNumber.GetValueOrDefault());
		data.WriteUInt32(unit.PetNameTimestamp.GetValueOrDefault());
		data.WriteUInt32(unit.PetExperience.GetValueOrDefault());
		data.WriteUInt32(unit.PetNextLevelExperience.GetValueOrDefault());
		data.WriteFloat(unit.ModCastSpeed ?? 1f);
		data.WriteFloat(unit.ModCastHaste ?? 1f);
		data.WriteFloat(1f);
		data.WriteFloat(1f);
		data.WriteFloat(1f);
		data.WriteFloat(1f);
		data.WriteInt32(unit.CreatedBySpell.GetValueOrDefault());
		data.WriteInt32(unit.EmoteState.GetValueOrDefault());
		data.WriteInt16(0);
		data.WriteInt16(0);
		if (this.IsOwner)
		{
			for (int n = 0; n < 5; n++)
			{
				int?[] stats = unit.Stats;
				data.WriteInt32(((stats != null) ? stats[n] : ((int?)null)).GetValueOrDefault());
				int?[] statPosBuff = unit.StatPosBuff;
				data.WriteInt32(((statPosBuff != null) ? statPosBuff[n] : ((int?)null)).GetValueOrDefault());
				int?[] statNegBuff = unit.StatNegBuff;
				data.WriteInt32(((statNegBuff != null) ? statNegBuff[n] : ((int?)null)).GetValueOrDefault());
			}
		}
		if (this.IsOwner)
		{
			for (int num = 0; num < 7; num++)
			{
				int?[] resistances = unit.Resistances;
				data.WriteInt32(((resistances != null) ? resistances[num] : ((int?)null)).GetValueOrDefault());
			}
		}
		if (this.IsOwner)
		{
			for (int num2 = 0; num2 < 7; num2++)
			{
				int?[] powerCostModifier = unit.PowerCostModifier;
				data.WriteInt32(((powerCostModifier != null) ? powerCostModifier[num2] : ((int?)null)).GetValueOrDefault());
				float?[] powerCostMultiplier = unit.PowerCostMultiplier;
				data.WriteFloat(((powerCostMultiplier != null) ? powerCostMultiplier[num2] : ((float?)null)).GetValueOrDefault());
			}
		}
		for (int num3 = 0; num3 < 7; num3++)
		{
			int?[] resistanceBuffModsPositive = unit.ResistanceBuffModsPositive;
			data.WriteInt32(((resistanceBuffModsPositive != null) ? resistanceBuffModsPositive[num3] : ((int?)null)).GetValueOrDefault());
			int?[] resistanceBuffModsNegative = unit.ResistanceBuffModsNegative;
			data.WriteInt32(((resistanceBuffModsNegative != null) ? resistanceBuffModsNegative[num3] : ((int?)null)).GetValueOrDefault());
		}
		data.WriteInt32(unit.BaseMana.GetValueOrDefault());
		if (this.IsOwner)
		{
			data.WriteInt32(unit.BaseHealth.GetValueOrDefault());
		}
		data.WriteUInt8(unit.SheatheState.GetValueOrDefault());
		data.WriteUInt8(unit.PvpFlags.GetValueOrDefault());
		data.WriteUInt8(unit.PetFlags.GetValueOrDefault());
		data.WriteUInt8(unit.ShapeshiftForm.GetValueOrDefault());
		if (this.IsOwner)
		{
			data.WriteInt32(unit.AttackPower.GetValueOrDefault());
			data.WriteInt32(unit.AttackPowerModPos.GetValueOrDefault());
			data.WriteInt32(unit.AttackPowerModNeg.GetValueOrDefault());
			data.WriteFloat(unit.AttackPowerMultiplier.GetValueOrDefault());
			data.WriteInt32(unit.RangedAttackPower.GetValueOrDefault());
			data.WriteInt32(unit.RangedAttackPowerModPos.GetValueOrDefault());
			data.WriteInt32(unit.RangedAttackPowerModNeg.GetValueOrDefault());
			data.WriteFloat(unit.RangedAttackPowerMultiplier.GetValueOrDefault());
			data.WriteInt32(0);
			data.WriteFloat(0f);
			data.WriteFloat(unit.MinRangedDamage.GetValueOrDefault());
			data.WriteFloat(unit.MaxRangedDamage.GetValueOrDefault());
			data.WriteFloat(unit.MaxHealthModifier ?? 1f);
		}
		data.WriteFloat(unit.HoverHeight.GetValueOrDefault());
		data.WriteInt32(unit.MinItemLevelCutoff.GetValueOrDefault());
		data.WriteInt32(unit.MinItemLevel.GetValueOrDefault());
		data.WriteInt32(unit.MaxItemLevel.GetValueOrDefault());
		data.WriteInt32(unit.WildBattlePetLevel.GetValueOrDefault());
		data.WriteUInt32(0u);
		data.WriteInt32(unit.InteractSpellID.GetValueOrDefault());
		data.WriteInt32(0);
		data.WriteInt32(unit.LooksLikeMountID.GetValueOrDefault());
		data.WriteInt32(unit.LooksLikeCreatureID.GetValueOrDefault());
		data.WriteInt32(unit.LookAtControllerID.GetValueOrDefault());
		data.WriteInt32(0);
		data.WritePackedGuid128(unit.GuildGUID ?? WowGuid128.Empty);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WriteInt32(0);
		data.WriteFloat(0f);
		data.WriteUInt32(0u);
		if (this.IsOwner)
		{
			data.WritePackedGuid128(WowGuid128.Empty);
		}
	}

	private void WriteUpdateUnitData(WorldPacket data)
	{
		UnitData unit = this.m_updateData.UnitData;
		if (unit == null)
		{
			data.WriteBits(0, 8);
			data.FlushBits();
			data.FlushBits();
			return;
		}
		uint[] blockMasks = new uint[8];
		if (unit.Health.HasValue)
		{
			SetBit(5);
		}
		if (unit.MaxHealth.HasValue)
		{
			SetBit(6);
		}
		if (unit.DisplayID.HasValue)
		{
			SetBit(7);
		}
		if (unit.Charm != null)
		{
			SetBit(11);
		}
		if (unit.Summon != null)
		{
			SetBit(12);
		}
		if (unit.CharmedBy != null)
		{
			SetBit(14);
		}
		if (unit.SummonedBy != null)
		{
			SetBit(15);
		}
		if (unit.CreatedBy != null)
		{
			SetBit(16);
		}
		if (unit.Target != null)
		{
			SetBit(19);
		}
		if (unit.ChannelData != null)
		{
			SetBit(22);
		}
		if (unit.RaceId.HasValue)
		{
			SetBit(24);
		}
		if (unit.ClassId.HasValue)
		{
			SetBit(25);
		}
		if (unit.SexId.HasValue)
		{
			SetBit(27);
		}
		if (unit.DisplayPower.HasValue) SetBit(28);
		if (unit.Level.HasValue)
		{
			SetBit(30);
		}
		if (unit.EffectiveLevel.HasValue)
		{
			SetBit(31);
		}
		if (unit.FactionTemplate.HasValue)
		{
			SetBit(40);
		}
		if (unit.Flags.HasValue)
		{
			SetBit(41);
		}
		if (unit.Flags2.HasValue)
		{
			SetBit(42);
		}
		if (unit.Flags3.HasValue) SetBit(43);
		if (unit.AuraState.HasValue)
		{
			SetBit(44);
		}
		if (unit.OverrideDisplayPowerID.HasValue) SetBit(45);
		if (unit.BoundingRadius.HasValue)
		{
			SetBit(46);
		}
		if (unit.CombatReach.HasValue)
		{
			SetBit(47);
		}
		if (unit.DisplayScale.HasValue) SetBit(48);
		if (unit.NativeDisplayID.HasValue)
		{
			SetBit(49);
		}
		if (unit.NativeXDisplayScale.HasValue) SetBit(50);
		if (unit.MountDisplayID.HasValue)
		{
			SetBit(51);
		}
		// Block 1 continued: damage, stance bytes, pet fields
		if (unit.MinDamage.HasValue) SetBit(52);
		if (unit.MaxDamage.HasValue) SetBit(53);
		if (unit.MinOffHandDamage.HasValue) SetBit(54);
		if (unit.MaxOffHandDamage.HasValue) SetBit(55);
		if (unit.StandState.HasValue) SetBit(56);
		// 57 = PetTalentPoints (not in UnitData)
		if (unit.VisFlags.HasValue) SetBit(58);
		if (unit.AnimTier.HasValue) SetBit(59);
		if (unit.PetNumber.HasValue) SetBit(60);
		if (unit.PetNameTimestamp.HasValue) SetBit(61);
		if (unit.PetExperience.HasValue) SetBit(62);
		if (unit.PetNextLevelExperience.HasValue) SetBit(63);
		// Block 2: ModCast/Haste, combat stats, attack power
		if (unit.ModCastSpeed.HasValue) SetBit(65);
		if (unit.ModCastHaste.HasValue) SetBit(66);
		if (unit.ModHaste.HasValue) SetBit(67);
		if (unit.ModRangedHaste.HasValue) SetBit(68);
		if (unit.ModHasteRegen.HasValue) SetBit(69);
		if (unit.ModTimeRate.HasValue) SetBit(70);
		if (unit.CreatedBySpell.HasValue) SetBit(71);
		if (unit.EmoteState.HasValue) SetBit(72);
		if (unit.TrainingPointsUsed.HasValue) SetBit(73);
		if (unit.TrainingPointsTotal.HasValue) SetBit(74);
		if (unit.BaseMana.HasValue) SetBit(75);
		if (unit.BaseHealth.HasValue) SetBit(76);
		if (unit.SheatheState.HasValue) SetBit(77);
		if (unit.PvpFlags.HasValue) SetBit(78);
		if (unit.PetFlags.HasValue) SetBit(79);
		if (unit.ShapeshiftForm.HasValue) SetBit(80);
		if (unit.AttackPower.HasValue) SetBit(81);
		if (unit.AttackPowerModPos.HasValue) SetBit(82);
		if (unit.AttackPowerModNeg.HasValue) SetBit(83);
		if (unit.AttackPowerMultiplier.HasValue) SetBit(84);
		if (unit.RangedAttackPower.HasValue) SetBit(85);
		if (unit.RangedAttackPowerModPos.HasValue) SetBit(86);
		if (unit.RangedAttackPowerModNeg.HasValue) SetBit(87);
		if (unit.RangedAttackPowerMultiplier.HasValue) SetBit(88);
		if (unit.AttackSpeedAura.HasValue) SetBit(89);
		if (unit.Lifesteal.HasValue) SetBit(90);
		if (unit.MinRangedDamage.HasValue) SetBit(91);
		if (unit.MaxRangedDamage.HasValue) SetBit(92);
		if (unit.MaxHealthModifier.HasValue) SetBit(93);
		if (unit.HoverHeight.HasValue)
		{
			SetBit(94);
		}
		if (unit.MinItemLevelCutoff.HasValue) SetBit(95);
		// Block 3: MinItemLevel..GuildGUID
		if (unit.MinItemLevel.HasValue) SetBit(97);
		if (unit.MaxItemLevel.HasValue) SetBit(98);
		if (unit.WildBattlePetLevel.HasValue) SetBit(99);
		// 100 = BattlePetCompanionNameTimestamp (not tracked)
		if (unit.InteractSpellID.HasValue) SetBit(101);
		if (unit.ScaleDuration.HasValue) SetBit(102);
		if (unit.LooksLikeMountID.HasValue) SetBit(103);
		if (unit.LooksLikeCreatureID.HasValue) SetBit(104);
		if (unit.LookAtControllerID.HasValue) SetBit(105);
		// 106 = PerksVendorItemID (not tracked)
		if (unit.GuildGUID != null)
		{
			SetBit(107);
		}
		if (unit.NpcFlags != null)
		{
			bool hasAnyNpcFlag = false;
			for (int i = 0; i < unit.NpcFlags.Length; i++)
			{
				if (unit.NpcFlags[i].HasValue && unit.NpcFlags[i] != 0)
				{
					SetBit(114 + i);
					hasAnyNpcFlag = true;
				}
			}
			if (hasAnyNpcFlag)
				SetBit(113); // parent bit for NpcFlags array
		}
		bool hasAnyPowerGroup = false;
		if (unit.Power != null)
		{
			for (int j = 0; j < unit.Power.Length; j++)
			{
				if (unit.Power[j].HasValue)
				{
					SetBit(137 + j);
					hasAnyPowerGroup = true;
				}
			}
		}
		if (unit.MaxPower != null)
		{
			for (int k = 0; k < unit.MaxPower.Length; k++)
			{
				if (unit.MaxPower[k].HasValue)
				{
					SetBit(147 + k);
					hasAnyPowerGroup = true;
				}
			}
		}
		if (unit.ModPowerRegen != null)
		{
			for (int j2 = 0; j2 < unit.ModPowerRegen.Length; j2++)
			{
				if (unit.ModPowerRegen[j2].HasValue)
				{
					SetBit(157 + j2);
					hasAnyPowerGroup = true;
				}
			}
		}
		if (hasAnyPowerGroup)
			SetBit(116); // parent bit for Power/MaxPower/Regen arrays
		// VirtualItems array (parent bit 167, elements 168-170)
		if (unit.VirtualItems != null)
		{
			bool hasAnyVI = false;
			for (int i = 0; i < 3; i++)
			{
				if (unit.VirtualItems[i] != null && unit.VirtualItems[i].ItemID != 0)
				{
					SetBit(168 + i);
					hasAnyVI = true;
				}
			}
			if (hasAnyVI) SetBit(167);
		}
		// RangedAttackRoundBaseTime (bit 170 — shares VirtualItems parent 167? No, separate)
		// Actually TC343 has RangedAttackRoundBaseTime at a different position. Check:
		// AttackRoundBaseTime array (parent bit 171, elements 172-173)
		if (unit.AttackRoundBaseTime != null)
		{
			bool hasAnyART = false;
			for (int i = 0; i < unit.AttackRoundBaseTime.Length && i < 2; i++)
			{
				if (unit.AttackRoundBaseTime[i].HasValue)
				{
					SetBit(172 + i);
					hasAnyART = true;
				}
			}
			if (hasAnyART) SetBit(171);
		}
		// Stats/StatPosBuff/StatNegBuff array (parent bit 174, elements 175-189)
		bool hasAnyStatsGroup = false;
		if (unit.Stats != null)
		{
			for (int i = 0; i < 5; i++)
			{
				if (unit.Stats[i].HasValue) { SetBit(175 + i); hasAnyStatsGroup = true; }
			}
		}
		if (unit.StatPosBuff != null)
		{
			for (int i = 0; i < 5; i++)
			{
				if (unit.StatPosBuff[i].HasValue) { SetBit(180 + i); hasAnyStatsGroup = true; }
			}
		}
		if (unit.StatNegBuff != null)
		{
			for (int i = 0; i < 5; i++)
			{
				if (unit.StatNegBuff[i].HasValue) { SetBit(185 + i); hasAnyStatsGroup = true; }
			}
		}
		if (hasAnyStatsGroup) SetBit(174);
		// Resistances/PowerCostModifier/PowerCostMultiplier array (parent bit 190, elements 191-211)
		bool hasAnyResistGroup = false;
		if (unit.Resistances != null)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.Resistances[i].HasValue) { SetBit(191 + i); hasAnyResistGroup = true; }
			}
		}
		if (unit.PowerCostModifier != null)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.PowerCostModifier[i].HasValue) { SetBit(198 + i); hasAnyResistGroup = true; }
			}
		}
		if (unit.PowerCostMultiplier != null)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.PowerCostMultiplier[i].HasValue) { SetBit(205 + i); hasAnyResistGroup = true; }
			}
		}
		if (hasAnyResistGroup) SetBit(190);
		// ResistanceBuffMods array (parent bit 212, elements 213-226)
		bool hasAnyResBuffGroup = false;
		if (unit.ResistanceBuffModsPositive != null)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.ResistanceBuffModsPositive[i].HasValue) { SetBit(213 + i); hasAnyResBuffGroup = true; }
			}
		}
		if (unit.ResistanceBuffModsNegative != null)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.ResistanceBuffModsNegative[i].HasValue) { SetBit(220 + i); hasAnyResBuffGroup = true; }
			}
		}
		if (hasAnyResBuffGroup) SetBit(212);
		for (int bi = 0; bi < 8; bi++)
		{
			if (blockMasks[bi] != 0)
			{
				blockMasks[bi] |= 1u;
			}
		}
		byte blocksMask = 0;
		for (int l = 0; l < 8; l++)
		{
			if (blockMasks[l] != 0)
			{
				blocksMask |= (byte)(1 << l);
			}
		}
		data.WriteBits(blocksMask, 8);
		for (int m = 0; m < 8; m++)
		{
			if ((blocksMask & (1 << m)) != 0)
			{
				data.WriteBits(blockMasks[m], 32);
			}
		}
		if ((blockMasks[0] & 1) != 0)
		{
		}
		data.FlushBits();
		data.FlushBits();
		if ((blockMasks[0] & 1) != 0)
		{
			if (unit.Health.HasValue)
			{
				data.WriteInt64(unit.Health.Value);
			}
			if (unit.MaxHealth.HasValue)
			{
				data.WriteInt64(unit.MaxHealth.Value);
			}
			if (unit.DisplayID.HasValue)
			{
				data.WriteInt32(unit.DisplayID.Value);
			}
			if (unit.Charm != null)
			{
				data.WritePackedGuid128(unit.Charm);
			}
			if (unit.Summon != null)
			{
				data.WritePackedGuid128(unit.Summon);
			}
			if (unit.CharmedBy != null)
			{
				data.WritePackedGuid128(unit.CharmedBy);
			}
			if (unit.SummonedBy != null)
			{
				data.WritePackedGuid128(unit.SummonedBy);
			}
			if (unit.CreatedBy != null)
			{
				data.WritePackedGuid128(unit.CreatedBy);
			}
			if (unit.Target != null)
			{
				data.WritePackedGuid128(unit.Target);
			}
			if (unit.ChannelData != null)
			{
				bool hasChannelObject = unit.ChannelObject != null && !unit.ChannelObject.IsEmpty();
				data.WriteBits(hasChannelObject ? 7 : 3, 4);
				data.FlushBits();
				data.WriteInt32(unit.ChannelData.SpellID);
				data.WriteInt32(unit.ChannelData.SpellXSpellVisualID);
				if (hasChannelObject)
				{
					data.WritePackedGuid128(unit.ChannelObject);
				}
			}
			if (unit.RaceId.HasValue)
			{
				data.WriteUInt8(unit.RaceId.Value);
			}
			if (unit.ClassId.HasValue)
			{
				data.WriteUInt8(unit.ClassId.Value);
			}
			if (unit.SexId.HasValue)
			{
				data.WriteUInt8(unit.SexId.Value);
			}
			if (unit.DisplayPower.HasValue) data.WriteUInt32(unit.DisplayPower.Value);
			if (unit.Level.HasValue)
			{
				data.WriteInt32(unit.Level.Value);
			}
			if (unit.EffectiveLevel.HasValue)
			{
				data.WriteInt32(unit.EffectiveLevel.Value);
			}
		}
		if ((blocksMask & 2) != 0)
		{
			if (unit.FactionTemplate.HasValue)
			{
				data.WriteInt32(unit.FactionTemplate.Value);
			}
			if (unit.Flags.HasValue)
			{
				data.WriteUInt32(unit.Flags.Value);
			}
			if (unit.Flags2.HasValue)
			{
				data.WriteUInt32(unit.Flags2.Value);
			}
			if (unit.Flags3.HasValue) data.WriteUInt32(unit.Flags3.Value);
			if (unit.AuraState.HasValue)
			{
				data.WriteUInt32(unit.AuraState.Value);
			}
			if (unit.OverrideDisplayPowerID.HasValue) data.WriteUInt32(unit.OverrideDisplayPowerID.Value);
			if (unit.BoundingRadius.HasValue)
			{
				data.WriteFloat(unit.BoundingRadius.Value);
			}
			if (unit.CombatReach.HasValue)
			{
				data.WriteFloat(unit.CombatReach.Value);
			}
			if (unit.DisplayScale.HasValue) data.WriteFloat(unit.DisplayScale.Value);
			if (unit.NativeDisplayID.HasValue)
			{
				data.WriteInt32(unit.NativeDisplayID.Value);
			}
			if (unit.NativeXDisplayScale.HasValue) data.WriteFloat(unit.NativeXDisplayScale.Value);
			if (unit.MountDisplayID.HasValue)
			{
				data.WriteInt32(unit.MountDisplayID.Value);
			}
			// Block 1 continued: damage, stance, pet
			if (unit.MinDamage.HasValue) data.WriteFloat(unit.MinDamage.Value);
			if (unit.MaxDamage.HasValue) data.WriteFloat(unit.MaxDamage.Value);
			if (unit.MinOffHandDamage.HasValue) data.WriteFloat(unit.MinOffHandDamage.Value);
			if (unit.MaxOffHandDamage.HasValue) data.WriteFloat(unit.MaxOffHandDamage.Value);
			if (unit.StandState.HasValue) data.WriteUInt8(unit.StandState.Value);
			if (unit.VisFlags.HasValue) data.WriteUInt8(unit.VisFlags.Value);
			if (unit.AnimTier.HasValue) data.WriteUInt8(unit.AnimTier.Value);
			if (unit.PetNumber.HasValue) data.WriteUInt32(unit.PetNumber.Value);
			if (unit.PetNameTimestamp.HasValue) data.WriteUInt32(unit.PetNameTimestamp.Value);
			if (unit.PetExperience.HasValue) data.WriteUInt32(unit.PetExperience.Value);
			if (unit.PetNextLevelExperience.HasValue) data.WriteUInt32(unit.PetNextLevelExperience.Value);
		}
		// Block 2 (bits 64-95): ModCast/Haste, combat stats, attack power
		if ((blocksMask & 4) != 0)
		{
			if (unit.ModCastSpeed.HasValue) data.WriteFloat(unit.ModCastSpeed.Value);
			if (unit.ModCastHaste.HasValue) data.WriteFloat(unit.ModCastHaste.Value);
			if (unit.ModHaste.HasValue) data.WriteFloat(unit.ModHaste.Value);
			if (unit.ModRangedHaste.HasValue) data.WriteFloat(unit.ModRangedHaste.Value);
			if (unit.ModHasteRegen.HasValue) data.WriteFloat(unit.ModHasteRegen.Value);
			if (unit.ModTimeRate.HasValue) data.WriteFloat(unit.ModTimeRate.Value);
			if (unit.CreatedBySpell.HasValue) data.WriteInt32(unit.CreatedBySpell.Value);
			if (unit.EmoteState.HasValue) data.WriteInt32(unit.EmoteState.Value);
			if (unit.TrainingPointsUsed.HasValue) data.WriteUInt16(unit.TrainingPointsUsed.Value);
			if (unit.TrainingPointsTotal.HasValue) data.WriteUInt16(unit.TrainingPointsTotal.Value);
			if (unit.BaseMana.HasValue) data.WriteInt32(unit.BaseMana.Value);
			if (unit.BaseHealth.HasValue) data.WriteInt32(unit.BaseHealth.Value);
			if (unit.SheatheState.HasValue) data.WriteUInt8(unit.SheatheState.Value);
			if (unit.PvpFlags.HasValue) data.WriteUInt8(unit.PvpFlags.Value);
			if (unit.PetFlags.HasValue) data.WriteUInt8(unit.PetFlags.Value);
			if (unit.ShapeshiftForm.HasValue) data.WriteUInt8(unit.ShapeshiftForm.Value);
			if (unit.AttackPower.HasValue) data.WriteInt32(unit.AttackPower.Value);
			if (unit.AttackPowerModPos.HasValue) data.WriteInt32(unit.AttackPowerModPos.Value);
			if (unit.AttackPowerModNeg.HasValue) data.WriteInt32(unit.AttackPowerModNeg.Value);
			if (unit.AttackPowerMultiplier.HasValue) data.WriteFloat(unit.AttackPowerMultiplier.Value);
			if (unit.RangedAttackPower.HasValue) data.WriteInt32(unit.RangedAttackPower.Value);
			if (unit.RangedAttackPowerModPos.HasValue) data.WriteInt32(unit.RangedAttackPowerModPos.Value);
			if (unit.RangedAttackPowerModNeg.HasValue) data.WriteInt32(unit.RangedAttackPowerModNeg.Value);
			if (unit.RangedAttackPowerMultiplier.HasValue) data.WriteFloat(unit.RangedAttackPowerMultiplier.Value);
			if (unit.AttackSpeedAura.HasValue) data.WriteInt32(unit.AttackSpeedAura.Value);
			if (unit.Lifesteal.HasValue) data.WriteFloat(unit.Lifesteal.Value);
			if (unit.MinRangedDamage.HasValue) data.WriteFloat(unit.MinRangedDamage.Value);
			if (unit.MaxRangedDamage.HasValue) data.WriteFloat(unit.MaxRangedDamage.Value);
			if (unit.MaxHealthModifier.HasValue) data.WriteFloat(unit.MaxHealthModifier.Value);
			if (unit.HoverHeight.HasValue) data.WriteFloat(unit.HoverHeight.Value);
			if (unit.MinItemLevelCutoff.HasValue) data.WriteInt32(unit.MinItemLevelCutoff.Value);
		}
		// Block 3 (bits 96-127): MinItemLevel..ComboTarget, GuildGUID, NpcFlags
		if ((blocksMask & 8) != 0)
		{
			if (unit.MinItemLevel.HasValue) data.WriteInt32(unit.MinItemLevel.Value);
			if (unit.MaxItemLevel.HasValue) data.WriteInt32(unit.MaxItemLevel.Value);
			if (unit.WildBattlePetLevel.HasValue) data.WriteInt32(unit.WildBattlePetLevel.Value);
			// 100 = BattlePetCompanionNameTimestamp not tracked
			if (unit.InteractSpellID.HasValue) data.WriteInt32(unit.InteractSpellID.Value);
			if (unit.ScaleDuration.HasValue) data.WriteInt32(unit.ScaleDuration.Value);
			if (unit.LooksLikeMountID.HasValue) data.WriteInt32(unit.LooksLikeMountID.Value);
			if (unit.LooksLikeCreatureID.HasValue) data.WriteInt32(unit.LooksLikeCreatureID.Value);
			if (unit.LookAtControllerID.HasValue) data.WriteInt32(unit.LookAtControllerID.Value);
			// 106 = PerksVendorItemID not tracked
			if (unit.GuildGUID != null)
			{
				data.WritePackedGuid128(unit.GuildGUID);
			}
			// NpcFlags array (parent bit 113, elements 114-115) — gated by block 3
			if (unit.NpcFlags != null)
			{
				for (int n = 0; n < unit.NpcFlags.Length; n++)
				{
					if (unit.NpcFlags[n].HasValue && unit.NpcFlags[n] != 0)
					{
						data.WriteUInt32(unit.NpcFlags[n].Value);
					}
				}
			}
		}
		// Power group (parent bit 116) — TC343 interleaves all power arrays per-index
		if ((blocksMask & 0x10) != 0)
		{
			int maxLen = 0;
			if (unit.Power != null && unit.Power.Length > maxLen) maxLen = unit.Power.Length;
			if (unit.MaxPower != null && unit.MaxPower.Length > maxLen) maxLen = unit.MaxPower.Length;
			for (int pi = 0; pi < maxLen; pi++)
			{
				// PowerRegenFlatModifier[pi] (bits 117+) — not tracked, skip
				// PowerRegenInterruptedFlatModifier[pi] (bits 127+) — not tracked, skip
				if (unit.Power != null && pi < unit.Power.Length && unit.Power[pi].HasValue)
				{
					data.WriteInt32(unit.Power[pi].Value);
				}
				if (unit.MaxPower != null && pi < unit.MaxPower.Length && unit.MaxPower[pi].HasValue)
				{
					data.WriteInt32(unit.MaxPower[pi].Value);
				}
				if (unit.ModPowerRegen != null && pi < unit.ModPowerRegen.Length && unit.ModPowerRegen[pi].HasValue)
				{
					data.WriteFloat(unit.ModPowerRegen[pi].Value);
				}
			}
		}
		// VirtualItems (parent bit 167, elements 168-170)
		if ((blockMasks[167 / 32] & (1u << (167 % 32))) != 0)
		{
			for (int vi = 0; vi < 3; vi++)
			{
				if ((blockMasks[(168 + vi) / 32] & (1u << ((168 + vi) % 32))) != 0)
				{
					var vItem = unit.VirtualItems[vi];
					// VirtualItem::WriteUpdate: 4-bit mask + fields
					// Bit 0=hasAny, 1=ItemID, 2=ItemAppearanceModID, 3=ItemVisual
					data.WriteBits(0x03u, 4); // bits 0+1 set (hasAny + ItemID)
					data.FlushBits();
					data.WriteInt32(vItem != null ? vItem.ItemID : 0);
				}
			}
		}
		// AttackRoundBaseTime array (parent bit 171, elements 172-173) — block 5
		if ((blockMasks[171 / 32] & (1u << (171 % 32))) != 0)
		{
			if (unit.AttackRoundBaseTime != null)
			{
				for (int i = 0; i < unit.AttackRoundBaseTime.Length && i < 2; i++)
				{
					if (unit.AttackRoundBaseTime[i].HasValue)
						data.WriteUInt32(unit.AttackRoundBaseTime[i].Value);
				}
			}
		}
		// Stats/StatPosBuff/StatNegBuff (parent bit 174, interleaved per TC343) — block 5
		if ((blockMasks[174 / 32] & (1u << (174 % 32))) != 0)
		{
			for (int i = 0; i < 5; i++)
			{
				if (unit.Stats != null && unit.Stats[i].HasValue) data.WriteInt32(unit.Stats[i].Value);
				if (unit.StatPosBuff != null && unit.StatPosBuff[i].HasValue) data.WriteInt32(unit.StatPosBuff[i].Value);
				if (unit.StatNegBuff != null && unit.StatNegBuff[i].HasValue) data.WriteInt32(unit.StatNegBuff[i].Value);
			}
		}
		// Resistances/PowerCostModifier/PowerCostMultiplier (parent bit 190, spans blocks 5-6)
		if ((blockMasks[190 / 32] & (1u << (190 % 32))) != 0)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.Resistances != null && unit.Resistances[i].HasValue) data.WriteInt32(unit.Resistances[i].Value);
				if (unit.PowerCostModifier != null && unit.PowerCostModifier[i].HasValue) data.WriteInt32(unit.PowerCostModifier[i].Value);
				if (unit.PowerCostMultiplier != null && unit.PowerCostMultiplier[i].HasValue) data.WriteFloat(unit.PowerCostMultiplier[i].Value);
			}
		}
		// ResistanceBuffMods (parent bit 212, elements 213-226, spans blocks 6-7)
		if ((blockMasks[212 / 32] & (1u << (212 % 32))) != 0)
		{
			for (int i = 0; i < 7; i++)
			{
				if (unit.ResistanceBuffModsPositive != null && unit.ResistanceBuffModsPositive[i].HasValue) data.WriteInt32(unit.ResistanceBuffModsPositive[i].Value);
				if (unit.ResistanceBuffModsNegative != null && unit.ResistanceBuffModsNegative[i].HasValue) data.WriteInt32(unit.ResistanceBuffModsNegative[i].Value);
			}
		}
		void SetBit(int idx)
		{
			blockMasks[idx / 32] |= (uint)(1 << idx % 32);
		}
	}

	private void WriteCreatePlayerData(WorldPacket data)
	{
		PlayerData player = this.m_updateData.PlayerData ?? new PlayerData();
		Log.Print(LogType.Debug, $"[PlayerDataCreate] PlayerFlags=0x{player.PlayerFlags.GetValueOrDefault():X8} FlagsEx=0x{player.PlayerFlagsEx.GetValueOrDefault():X8} DuelArbiter={player.DuelArbiter} WowAccount={player.WowAccount} LootTarget={player.LootTargetGUID}", "WriteCreatePlayerData", "");
		data.WritePackedGuid128(player.DuelArbiter ?? WowGuid128.Empty);
		data.WritePackedGuid128(player.WowAccount ?? WowGuid128.Empty);
		data.WritePackedGuid128(player.LootTargetGUID ?? WowGuid128.Empty);
		data.WriteUInt32(player.PlayerFlags.GetValueOrDefault());
		data.WriteUInt32(player.PlayerFlagsEx.GetValueOrDefault());
		data.WriteUInt32(player.GuildRankID.GetValueOrDefault());
		data.WriteUInt32(player.GuildDeleteDate.GetValueOrDefault());
		data.WriteInt32(player.GuildLevel.GetValueOrDefault());
		int customizationCount = 0;
		for (int i = 0; i < player.Customizations.Length; i++)
		{
			if (player.Customizations[i] != null)
			{
				customizationCount++;
				if (this.IsOwner)
				{
					Log.Print(LogType.Debug, $"[Customization] Option={player.Customizations[i].ChrCustomizationOptionID} Choice={player.Customizations[i].ChrCustomizationChoiceID}", "WriteCreatePlayerData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
				}
			}
		}
		if (this.IsOwner)
		{
			Log.Print(LogType.Debug, $"[Customization] Total count={customizationCount}", "WriteCreatePlayerData", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
		}
		data.WriteUInt32((uint)customizationCount);
		data.WriteUInt8(player.PartyType.GetValueOrDefault());
		data.WriteUInt8(0);
		data.WriteUInt8(player.NumBankSlots.GetValueOrDefault());
		data.WriteUInt8(player.NativeSex.GetValueOrDefault());
		data.WriteUInt8(player.Inebriation.GetValueOrDefault());
		data.WriteUInt8(player.PvpTitle.GetValueOrDefault());
		data.WriteUInt8(player.ArenaFaction.GetValueOrDefault());
		data.WriteUInt8(player.PvPRank.GetValueOrDefault());
		data.WriteInt32(0);
		data.WriteUInt32(player.DuelTeam.GetValueOrDefault());
		data.WriteInt32(player.GuildTimeStamp.GetValueOrDefault());
		// QuestLog[25] - gated by PartyMember flag (0x02) in TC343
		if (this.IsOwner)
		{
			int questCount = 0;
			for (int q = 0; q < 25; q++)
			{
				QuestLog quest = (player.QuestLog != null && q < player.QuestLog.Length) ? player.QuestLog[q] : null;
				if (quest != null && quest.QuestID.HasValue && quest.QuestID.Value != 0)
					questCount++;
				data.WriteInt64(quest?.EndTime ?? 0);
				data.WriteInt32(quest?.QuestID ?? 0);
				data.WriteUInt32(quest?.StateFlags ?? 0);
				for (int obj = 0; obj < 24; obj++)
				{
					data.WriteUInt16((ushort)(quest?.ObjectiveProgress[obj] ?? 0));
				}
			}
			if (questCount > 0)
			{
				string questSlots = "";
				for (int qi = 0; qi < 25; qi++)
				{
					QuestLog qe = (player.QuestLog != null && qi < player.QuestLog.Length) ? player.QuestLog[qi] : null;
					if (qe != null && qe.QuestID.HasValue && qe.QuestID.Value != 0)
						questSlots += $" slot{qi}=QID:{qe.QuestID.Value}";
				}
				Log.Print(LogType.Debug, $"[QuestLogCreate] Writing {questCount} quests:{questSlots}", "WriteCreatePlayerData", "");
			}
		}
		for (int j = 0; j < 19; j++)
		{
			if (player.VisibleItems != null && j < player.VisibleItems.Length && player.VisibleItems[j] != null)
			{
				data.WriteInt32(player.VisibleItems[j].ItemID);
				data.WriteUInt16(player.VisibleItems[j].ItemAppearanceModID);
				data.WriteUInt16(player.VisibleItems[j].ItemVisual);
			}
			else
			{
				data.WriteInt32(0);
				data.WriteUInt16(0);
				data.WriteUInt16(0);
			}
		}
		data.WriteInt32(player.ChosenTitle.GetValueOrDefault());
		data.WriteInt32(0);
		data.WriteUInt32(player.VirtualPlayerRealm.GetValueOrDefault());
		data.WriteUInt32(player.CurrentSpecID.GetValueOrDefault());
		data.WriteInt32(0);
		for (int k = 0; k < 6; k++)
		{
			data.WriteFloat(0f);
		}
		data.WriteUInt8(0);
		data.WriteInt32(player.HonorLevel.GetValueOrDefault());
		data.WriteInt64(0L);
		data.WriteUInt32(0u);
		data.WriteInt32(0);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WriteUInt32(0u);
		for (int l = 0; l < 19; l++)
		{
			data.WriteUInt32(0u);
		}
		for (int m = 0; m < player.Customizations.Length; m++)
		{
			if (player.Customizations[m] != null)
			{
				data.WriteUInt32(player.Customizations[m].ChrCustomizationOptionID);
				data.WriteUInt32(player.Customizations[m].ChrCustomizationChoiceID);
			}
		}
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteUInt32(0u);
	}

	private bool HasAnyPlayerFieldSet()
	{
		PlayerData p = this.m_updateData.PlayerData;
		if (p == null) return false;
		// Scalar fields (bits 4-31)
		if (p.DuelArbiter != null || p.WowAccount != null || p.LootTargetGUID != null) return true;
		if (p.PlayerFlags.HasValue || p.PlayerFlagsEx.HasValue) return true;
		if (p.GuildRankID.HasValue || p.GuildDeleteDate.HasValue || p.GuildLevel.HasValue) return true;
		if (p.NumBankSlots.HasValue || p.NativeSex.HasValue || p.Inebriation.HasValue) return true;
		if (p.PvpTitle.HasValue || p.ArenaFaction.HasValue || p.PvPRank.HasValue) return true;
		if (p.DuelTeam.HasValue || p.GuildTimeStamp.HasValue || p.ChosenTitle.HasValue) return true;
		if (p.FakeInebriation.HasValue || p.VirtualPlayerRealm.HasValue || p.CurrentSpecID.HasValue) return true;
		if (p.HonorLevel.HasValue) return true;
		// Quest log (bits 35-60)
		if (p.QuestLog != null)
			for (int i = 0; i < p.QuestLog.Length; i++)
				if (p.QuestLog[i] != null && p.QuestLog[i].QuestID.HasValue) return true;
		// Visible items (bits 61-80)
		if (p.VisibleItems != null)
			for (int i = 0; i < p.VisibleItems.Length; i++)
				if (p.VisibleItems[i] != null) return true;
		return false;
	}

	/// <summary>
	/// Writes PlayerData update using TC343 bitmask format.
	/// HasChangesMask&lt;108&gt; = 4 blocks of 32 bits.
	/// TC343 bit assignments (all BlockBit=0):
	///   1=Customizations(dyn), 2=ArenaCooldowns(dyn), 3=VisualItemReplacements(dyn)
	///   4=DuelArbiter, 5=WowAccount, 6=LootTargetGUID, 7=PlayerFlags, 8=PlayerFlagsEx
	///   9=GuildRankID, 10=GuildDeleteDate, 11=GuildLevel, 12=NumBankSlots, 13=NativeSex
	///   14=Inebriation, 15=PvpTitle, 16=ArenaFaction, 17=PvpRank, 18=Field_88
	///   19=DuelTeam, 20=GuildTimeStamp, 21=PlayerTitle/ChosenTitle, 22=FakeInebriation
	///   23=VirtualPlayerRealm, 24=CurrentSpecID, 25=TaxiMountAnimKitID
	///   26=CurrentBattlePetBreedQuality, 27=HonorLevel, 28=LogoutTime
	///   32-33=PartyType[2], 35-60=QuestLog[25], 61-80=VisibleItems[19]
	/// </summary>
	private void WriteUpdatePlayerData(WorldPacket data)
	{
		PlayerData p = this.m_updateData.PlayerData ?? new PlayerData();

		uint[] blocks = new uint[4];
		void SetBit(int bit) { blocks[bit / 32] |= (1u << (bit % 32)); }
		bool IsBitSet(int bit) { return (blocks[bit / 32] & (1u << (bit % 32))) != 0; }

		// Block 0: scalar fields (bits 4-31)
		if (p.DuelArbiter != null) { SetBit(0); SetBit(4); }
		if (p.WowAccount != null) { SetBit(0); SetBit(5); }
		if (p.LootTargetGUID != null) { SetBit(0); SetBit(6); }
		if (p.PlayerFlags.HasValue) { SetBit(0); SetBit(7); }
		if (p.PlayerFlagsEx.HasValue) { SetBit(0); SetBit(8); }
		if (p.GuildRankID.HasValue) { SetBit(0); SetBit(9); }
		if (p.GuildDeleteDate.HasValue) { SetBit(0); SetBit(10); }
		if (p.GuildLevel.HasValue) { SetBit(0); SetBit(11); }
		if (p.NumBankSlots.HasValue) { SetBit(0); SetBit(12); }
		if (p.NativeSex.HasValue) { SetBit(0); SetBit(13); }
		if (p.Inebriation.HasValue) { SetBit(0); SetBit(14); }
		if (p.PvpTitle.HasValue) { SetBit(0); SetBit(15); }
		if (p.ArenaFaction.HasValue) { SetBit(0); SetBit(16); }
		if (p.PvPRank.HasValue) { SetBit(0); SetBit(17); }
		// 18: Field_88 — unused
		if (p.DuelTeam.HasValue) { SetBit(0); SetBit(19); }
		if (p.GuildTimeStamp.HasValue) { SetBit(0); SetBit(20); }
		if (p.ChosenTitle.HasValue) { SetBit(0); SetBit(21); }
		if (p.FakeInebriation.HasValue) { SetBit(0); SetBit(22); }
		if (p.VirtualPlayerRealm.HasValue) { SetBit(0); SetBit(23); }
		if (p.CurrentSpecID.HasValue) { SetBit(0); SetBit(24); }
		// 25: TaxiMountAnimKitID — not tracked
		// 26: CurrentBattlePetBreedQuality — not tracked
		if (p.HonorLevel.HasValue) { SetBit(0); SetBit(27); }
		// 28-31: LogoutTime, CurrentBattlePetSpeciesID, BnetAccount, DungeonScore — not tracked

		// QuestLog (header 35, elements 36-60)
		bool hasAnyQuestLog = false;
		for (int i = 0; i < 25; i++)
		{
			if (p.QuestLog[i] != null && p.QuestLog[i].QuestID.HasValue)
			{
				SetBit(35);
				SetBit(36 + i);
				hasAnyQuestLog = true;
			}
		}

		// VisibleItems (header 61, elements 62-80)
		bool hasAnyVisibleItem = false;
		for (int i = 0; i < 19; i++)
		{
			if (p.VisibleItems != null && i < p.VisibleItems.Length && p.VisibleItems[i] != null)
			{
				SetBit(61);
				SetBit(62 + i);
				hasAnyVisibleItem = true;
			}
		}

		Log.Print(LogType.Debug, $"[PlayerDataUpdate] blocks=[0x{blocks[0]:X8},0x{blocks[1]:X8},0x{blocks[2]:X8},0x{blocks[3]:X8}]", "WriteUpdatePlayerData", "");

		// Write blocksMask (4 bits)
		byte blocksMask = 0;
		for (int i = 0; i < 4; i++)
			if (blocks[i] != 0) blocksMask |= (byte)(1 << i);

		data.WriteBits(blocksMask, 4);
		for (int i = 0; i < 4; i++)
			if ((blocksMask & (1 << i)) != 0)
				data.WriteBits(blocks[i], 32);

		// IsQuestLogChangesMaskSkipped = true → use WriteCreate format for quest entries
		data.WriteBit(true);

		// No dynamic fields (bits 1-3 not set)
		data.FlushBits();

		// Block 0: scalar field values in TC343 bit order (4-31)
		if (IsBitSet(0))
		{
			if (IsBitSet(4)) data.WritePackedGuid128(p.DuelArbiter);
			if (IsBitSet(5)) data.WritePackedGuid128(p.WowAccount);
			if (IsBitSet(6)) data.WritePackedGuid128(p.LootTargetGUID);
			if (IsBitSet(7)) data.WriteUInt32(p.PlayerFlags.Value);
			if (IsBitSet(8)) data.WriteUInt32(p.PlayerFlagsEx.Value);
			if (IsBitSet(9)) data.WriteUInt32(p.GuildRankID.Value);
			if (IsBitSet(10)) data.WriteUInt32(p.GuildDeleteDate.Value);
			if (IsBitSet(11)) data.WriteInt32(p.GuildLevel.Value);
			if (IsBitSet(12)) data.WriteUInt8(p.NumBankSlots.Value);
			if (IsBitSet(13)) data.WriteUInt8(p.NativeSex.Value);
			if (IsBitSet(14)) data.WriteUInt8(p.Inebriation.Value);
			if (IsBitSet(15)) data.WriteUInt8(p.PvpTitle.Value);
			if (IsBitSet(16)) data.WriteUInt8(p.ArenaFaction.Value);
			if (IsBitSet(17)) data.WriteUInt8(p.PvPRank.Value);
			// 18: Field_88 skipped
			if (IsBitSet(19)) data.WriteUInt32(p.DuelTeam.Value);
			if (IsBitSet(20)) data.WriteInt32(p.GuildTimeStamp.Value);
			if (IsBitSet(21)) data.WriteInt32(p.ChosenTitle.Value);
			if (IsBitSet(22)) data.WriteInt32(p.FakeInebriation.Value);
			if (IsBitSet(23)) data.WriteUInt32(p.VirtualPlayerRealm.Value);
			if (IsBitSet(24)) data.WriteUInt32(p.CurrentSpecID.Value);
			// 25-26 skipped
			if (IsBitSet(27)) data.WriteInt32(p.HonorLevel.Value);
		}

		// QuestLog entries (bits 35-60) — WriteCreate format
		if (hasAnyQuestLog)
		{
			for (int i = 0; i < 25; i++)
			{
				if (IsBitSet(36 + i))
				{
					QuestLog quest = p.QuestLog[i];
					data.WriteInt64(quest?.EndTime ?? 0);
					data.WriteInt32(quest?.QuestID ?? 0);
					data.WriteUInt32(quest?.StateFlags ?? 0);
					for (int obj = 0; obj < 24; obj++)
						data.WriteUInt16((ushort)(quest?.ObjectiveProgress[obj] ?? 0));
				}
			}
		}

		// VisibleItems (bits 61-80) — TC343 VisibleItem::WriteUpdate uses WriteBits(mask, 4)
		if (hasAnyVisibleItem)
		{
			for (int i = 0; i < 19; i++)
			{
				if (IsBitSet(62 + i))
				{
					VisibleItem item = p.VisibleItems[i];
					// VisibleItem has HasChangesMask<4>: bit 0=hasAny, 1=ItemID, 2=AppearanceModID, 3=ItemVisual
					data.WriteBits(0x0F, 4); // all 4 bits set
					data.FlushBits();
					data.WriteInt32(item.ItemID);
					data.WriteUInt16(item.ItemAppearanceModID);
					data.WriteUInt16(item.ItemVisual);
				}
			}
		}
	}

	private void WriteEmptyQuestLog(WorldPacket data)
	{
		data.WriteInt64(0L);
		data.WriteInt32(0);
		data.WriteUInt32(0u);
		for (int i = 0; i < 24; i++)
		{
			data.WriteUInt16(0);
		}
	}

	private void WriteCreateActivePlayerData(WorldPacket data)
	{
		ActivePlayerData active = this.m_updateData.ActivePlayerData ?? new ActivePlayerData();
		// Modern 3.4.3 InvSlots[141] - mapped from legacy arrays via GetModernInvSlot
		for (int i = 0; i < 141; i++)
		{
			data.WritePackedGuid128(GetModernInvSlot(active, i) ?? WowGuid128.Empty);
		}
		data.WritePackedGuid128(active.FarsightObject ?? WowGuid128.Empty);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WriteUInt32(0u);
		data.WriteUInt64(active.Coinage.GetValueOrDefault());
		data.WriteInt32(active.XP.GetValueOrDefault());
		data.WriteInt32(active.NextLevelXP.GetValueOrDefault());
		data.WriteInt32(0);
		SkillInfo skill = active.Skill;
		for (int j = 0; j < 256; j++)
		{
			data.WriteUInt16(((skill != null) ? skill.SkillLineID[j] : ((ushort?)null)).GetValueOrDefault());
			data.WriteUInt16(((skill != null) ? skill.SkillStep[j] : ((ushort?)null)).GetValueOrDefault());
			data.WriteUInt16(((skill != null) ? skill.SkillRank[j] : ((ushort?)null)).GetValueOrDefault());
			data.WriteUInt16(((skill != null) ? skill.SkillStartingRank[j] : ((ushort?)null)).GetValueOrDefault());
			data.WriteUInt16(((skill != null) ? skill.SkillMaxRank[j] : ((ushort?)null)).GetValueOrDefault());
			data.WriteUInt16((ushort)((skill != null) ? skill.SkillTempBonus[j] : ((short?)null)).GetValueOrDefault());
			data.WriteUInt16(((skill != null) ? skill.SkillPermBonus[j] : ((ushort?)null)).GetValueOrDefault());
		}
		data.WriteInt32(active.CharacterPoints.GetValueOrDefault()); // CharacterPoints (unspent talent points)
		data.WriteInt32(active.MaxTalentTiers.GetValueOrDefault());  // MaxTalentTiers (total talent points)
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		for (int k = 0; k < 7; k++)
		{
			data.WriteFloat(0f);
			data.WriteInt32(0);
			data.WriteInt32(0);
			data.WriteFloat(0f);
		}
		data.WriteInt32(0);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteInt32(0);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		for (int l = 0; l < 240; l++)
		{
			data.WriteUInt64(0uL);
		}
		data.WriteUInt32(0u);
		data.WriteUInt8(1);
		data.WriteUInt32(0u);
		data.WriteUInt8(1);
		data.WriteInt32(0);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		for (int m = 0; m < 3; m++)
		{
			data.WriteFloat(1f);
			data.WriteFloat(1f);
		}
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteUInt32(0u);
		data.WriteUInt8(0);
		data.WriteUInt8(0);
		data.WriteUInt8(0);
		data.WriteUInt8(0);
		data.WriteInt32(0);
		data.WriteUInt32(0u);
		for (int n = 0; n < 12; n++)
		{
			data.WriteUInt32(0u);
			data.WriteInt64(0L);
		}
		for (int num = 0; num < 8; num++)
		{
			data.WriteUInt16(0);
		}
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteInt32(active.WatchedFactionIndex ?? (-1));
		for (int num2 = 0; num2 < 32; num2++)
		{
			int?[] combatRatings = active.CombatRatings;
			data.WriteInt32(((combatRatings != null) ? combatRatings[num2] : ((int?)null)).GetValueOrDefault());
		}
		data.WriteInt32(active.MaxLevel ?? LegacyVersion.GetMaxLevel());
		data.WriteInt32(0);
		data.WriteInt32(0);
		for (int num3 = 0; num3 < 4; num3++)
		{
			data.WriteUInt32(0u);
		}
		data.WriteInt32(active.PetSpellPower.GetValueOrDefault());
		for (int num4 = 0; num4 < 2; num4++)
		{
			int?[] professionSkillLine = active.ProfessionSkillLine;
			data.WriteInt32(((professionSkillLine != null) ? professionSkillLine[num4] : ((int?)null)).GetValueOrDefault());
		}
		data.WriteFloat(0f);
		data.WriteFloat(0f);
		data.WriteInt32(0);
		data.WriteFloat(active.ModPetHaste ?? 1f);
		data.WriteUInt8(0);
		data.WriteUInt8(0);
		data.WriteUInt8(active.NumBackpackSlots ?? 16);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteUInt16(0);
		data.WriteUInt32(0u);
		for (int num5 = 0; num5 < 4; num5++)
		{
			data.WriteUInt32(0u);
		}
		for (int num6 = 0; num6 < 7; num6++)
		{
			data.WriteUInt32(0u);
		}
		for (int num7 = 0; num7 < 875; num7++)
		{
			data.WriteUInt64(0uL);
		}
		data.WriteInt32(active.Honor.GetValueOrDefault());
		data.WriteInt32(active.HonorNextLevel ?? 5500);
		data.WriteInt32(0);
		data.WriteInt32(((int?)active.PvPTierMaxFromWins) ?? (-1));
		data.WriteInt32(((int?)active.PvPLastWeeksTierMaxFromWins) ?? (-1));
		data.WriteUInt8(0);
		data.WriteInt32(0);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteInt32(0);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		// GlyphSlots[6] and Glyphs[6] - interleaved
		uint[] glyphSlotIds = { 21, 22, 23, 24, 25, 26 }; // WotLK GlyphSlot.db2 IDs
		for (int num8 = 0; num8 < 6; num8++)
		{
			data.WriteUInt32(glyphSlotIds[num8]); // GlyphSlots[i]
			data.WriteUInt32((uint)(this.m_gameState.ActiveGlyphs[num8])); // Glyphs[i]
		}
		data.WriteUInt8(this.m_gameState.GlyphsEnabled); // GlyphsEnabled
		data.WriteUInt8(0); // LfgRoles
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		data.WriteUInt8(0);
		for (int num9 = 0; num9 < 7; num9++)
		{
			data.WriteInt8(0);
			data.WriteInt32(0);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
			data.WriteBit(bit: false);
			data.FlushBits();
		}
		data.FlushBits();
		data.WriteBit(bit: false);
		data.WriteBit(bit: false);
		data.WriteBit(bit: false);
		data.FlushBits();
		data.WriteUInt32(0u);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt32(0);
		data.WriteInt64(0L);
		data.WriteBit(bit: false);
		data.FlushBits();
		data.FlushBits();
	}

	private void WriteCreateGameObjectData(WorldPacket data)
	{
		GameObjectData go = this.m_updateData.GameObjectData ?? new GameObjectData();
		if (this.m_updateData.ObjectData?.EntryID == 35591)
		{
			Log.Print(LogType.Debug, $"[BobberCreate] DisplayID={go.DisplayID} Flags=0x{go.Flags:X} State={go.State} TypeID={go.TypeID} ArtKit={go.ArtKit} PercentHealth={go.PercentHealth} CreatedBy={go.CreatedBy} Level={go.Level} DynFlags=0x{this.m_updateData.ObjectData?.DynamicFlags:X}", "WriteCreateGameObjectData", "");
		}
		data.WriteInt32(go.DisplayID.GetValueOrDefault());
		data.WriteUInt32(go.SpellVisualID.GetValueOrDefault());
		data.WriteUInt32(go.StateSpellVisualID.GetValueOrDefault());
		data.WriteUInt32(go.StateAnimID.GetValueOrDefault());
		data.WriteUInt32(go.StateAnimKitID.GetValueOrDefault());
		data.WriteUInt32(0u);
		data.WritePackedGuid128(go.CreatedBy ?? WowGuid128.Empty);
		data.WritePackedGuid128(WowGuid128.Empty);
		data.WriteUInt32(go.Flags.GetValueOrDefault());
		CreateObjectData createData = this.m_updateData.CreateData;
		if (createData != null && (createData.MoveInfo?.Rotation).HasValue)
		{
			Quaternion rot = this.m_updateData.CreateData.MoveInfo.Rotation;
			data.WriteFloat(rot.X);
			data.WriteFloat(rot.Y);
			data.WriteFloat(rot.Z);
			data.WriteFloat(rot.W);
		}
		else
		{
			data.WriteFloat(0f);
			data.WriteFloat(0f);
			data.WriteFloat(0f);
			data.WriteFloat(1f);
		}
		data.WriteInt32(go.FactionTemplate.GetValueOrDefault());
		data.WriteInt32(go.Level.GetValueOrDefault());
		data.WriteInt8(go.State.GetValueOrDefault());
		data.WriteInt8(go.TypeID.GetValueOrDefault());
		data.WriteUInt8(go.PercentHealth ?? 100);
		data.WriteUInt32(go.ArtKit.GetValueOrDefault());
		data.WriteUInt32(0u);
		data.WriteUInt32(go.CustomParam.GetValueOrDefault());
		data.WriteUInt32(0u);
	}

	private bool HasAnyGameObjectFieldSet()
	{
		GameObjectData go = this.m_updateData.GameObjectData;
		if (go == null) return false;
		if (go.DisplayID.HasValue || go.SpellVisualID.HasValue || go.StateSpellVisualID.HasValue) return true;
		if (go.StateAnimID.HasValue || go.StateAnimKitID.HasValue) return true;
		if (go.CreatedBy != null || go.GuildGUID != null) return true;
		if (go.Flags.HasValue || go.FactionTemplate.HasValue || go.Level.HasValue) return true;
		if (go.State.HasValue || go.TypeID.HasValue || go.PercentHealth.HasValue) return true;
		if (go.ArtKit.HasValue || go.CustomParam.HasValue) return true;
		if (go.ParentRotation != null)
			for (int i = 0; i < 4; i++)
				if (go.ParentRotation[i].HasValue) return true;
		return false;
	}

	/// <summary>
	/// Writes GameObjectData update using TC343 bitmask format.
	/// HasChangesMask&lt;20&gt; = 1 block of 32 bits.
	/// TC343 bit assignments (all BlockBit=0):
	///   1=StateWorldEffectIDs, 4=DisplayID, 5=SpellVisualID, 6=StateSpellVisualID,
	///   7=StateAnimID, 8=StateAnimKitID, 9=CreatedBy, 10=GuildGUID, 11=Flags,
	///   12=ParentRotation, 13=FactionTemplate, 14=Level, 15=State, 16=TypeID,
	///   17=PercentHealth, 18=ArtKit, 19=CustomParam
	/// </summary>
	private void WriteUpdateGameObjectData(WorldPacket data)
	{
		GameObjectData go = this.m_updateData.GameObjectData ?? new GameObjectData();

		uint mask = 0;
		void SetBit(int bit) { mask |= (1u << bit); }
		bool IsBitSet(int bit) { return (mask & (1u << bit)) != 0; }

		// Set bits for changed fields
		if (go.DisplayID.HasValue) { SetBit(0); SetBit(4); }
		if (go.SpellVisualID.HasValue) { SetBit(0); SetBit(5); }
		if (go.StateSpellVisualID.HasValue) { SetBit(0); SetBit(6); }
		if (go.StateAnimID.HasValue) { SetBit(0); SetBit(7); }
		if (go.StateAnimKitID.HasValue) { SetBit(0); SetBit(8); }
		if (go.CreatedBy != null) { SetBit(0); SetBit(9); }
		if (go.GuildGUID != null) { SetBit(0); SetBit(10); }
		if (go.Flags.HasValue) { SetBit(0); SetBit(11); }
		bool hasRotation = false;
		if (go.ParentRotation != null)
			for (int i = 0; i < 4; i++)
				if (go.ParentRotation[i].HasValue) hasRotation = true;
		if (hasRotation) { SetBit(0); SetBit(12); }
		if (go.FactionTemplate.HasValue) { SetBit(0); SetBit(13); }
		if (go.Level.HasValue) { SetBit(0); SetBit(14); }
		if (go.State.HasValue) { SetBit(0); SetBit(15); }
		if (go.TypeID.HasValue) { SetBit(0); SetBit(16); }
		if (go.PercentHealth.HasValue) { SetBit(0); SetBit(17); }
		if (go.ArtKit.HasValue) { SetBit(0); SetBit(18); }
		if (go.CustomParam.HasValue) { SetBit(0); SetBit(19); }

		// 1 block, 20 bits — write blocksMask (1 bit) then block mask
		byte blocksMask = (mask != 0) ? (byte)1 : (byte)0;
		data.WriteBits(blocksMask, 1);
		if (blocksMask != 0)
			data.WriteBits(mask, 20);

		// No dynamic fields
		data.FlushBits();

		// Write field values in TC343 order (bits 4-19)
		if (IsBitSet(0))
		{
			if (IsBitSet(4)) data.WriteInt32(go.DisplayID.Value);
			if (IsBitSet(5)) data.WriteUInt32(go.SpellVisualID.Value);
			if (IsBitSet(6)) data.WriteUInt32(go.StateSpellVisualID.Value);
			if (IsBitSet(7)) data.WriteUInt32(go.StateAnimID.Value);
			if (IsBitSet(8)) data.WriteUInt32(go.StateAnimKitID.Value);
			if (IsBitSet(9)) data.WritePackedGuid128(go.CreatedBy);
			if (IsBitSet(10)) data.WritePackedGuid128(go.GuildGUID);
			if (IsBitSet(11)) data.WriteUInt32(go.Flags.Value);
			if (IsBitSet(12))
			{
				data.WriteFloat(go.ParentRotation[0] ?? 0f);
				data.WriteFloat(go.ParentRotation[1] ?? 0f);
				data.WriteFloat(go.ParentRotation[2] ?? 0f);
				data.WriteFloat(go.ParentRotation[3] ?? 1f);
			}
			if (IsBitSet(13)) data.WriteInt32(go.FactionTemplate.Value);
			if (IsBitSet(14)) data.WriteInt32(go.Level.Value);
			if (IsBitSet(15)) data.WriteInt8(go.State.Value);
			if (IsBitSet(16)) data.WriteInt8(go.TypeID.Value);
			if (IsBitSet(17)) data.WriteUInt8(go.PercentHealth.Value);
			if (IsBitSet(18)) data.WriteUInt32(go.ArtKit.Value);
			if (IsBitSet(19)) data.WriteUInt32(go.CustomParam.Value);
		}
	}

	private void WriteCreateDynamicObjectData(WorldPacket data)
	{
		DynamicObjectData dyn = this.m_updateData.DynamicObjectData ?? new DynamicObjectData();
		data.WritePackedGuid128(dyn.Caster ?? WowGuid128.Empty);
		data.WriteUInt8(0);
		data.WriteInt32(0);
		data.WriteInt32(dyn.SpellID.GetValueOrDefault());
		data.WriteFloat(dyn.Radius.GetValueOrDefault());
		data.WriteUInt32(dyn.CastTime.GetValueOrDefault());
	}

	private void WriteCreateCorpseData(WorldPacket data)
	{
		CorpseData corpse = this.m_updateData.CorpseData ?? new CorpseData();
		// TC343 field order: DynamicFlags FIRST, then Owner, Party, Guild, etc.
		data.WriteUInt32(corpse.DynamicFlags.GetValueOrDefault());
		data.WritePackedGuid128(corpse.Owner ?? WowGuid128.Empty);
		data.WritePackedGuid128(corpse.PartyGUID ?? WowGuid128.Empty);
		data.WritePackedGuid128(corpse.GuildGUID ?? WowGuid128.Empty);
		data.WriteUInt32(corpse.DisplayID.GetValueOrDefault());
		for (int i = 0; i < 19; i++)
		{
			uint?[] items = corpse.Items;
			data.WriteUInt32(((items != null) ? items[i] : ((uint?)null)).GetValueOrDefault());
		}
		data.WriteUInt8(corpse.RaceId.GetValueOrDefault());
		data.WriteUInt8(corpse.SexId.GetValueOrDefault());
		data.WriteUInt8(corpse.ClassId.GetValueOrDefault());
		data.WriteUInt32(0u); // Customizations.size() = 0
		data.WriteUInt32(corpse.Flags.GetValueOrDefault());
		data.WriteInt32(corpse.FactionTemplate.GetValueOrDefault());
		// Customizations loop would go here if size > 0
	}

	public void SetCreateObjectBits()
	{
		this.m_createBits.Clear();
		this.m_createBits.PlayHoverAnim = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_updateData.CreateData.MoveInfo.Hover;
		this.m_createBits.MovementUpdate = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit);
		// Never set MovementTransport for 3.4.3: old-style transports (elevators/boats) are
		// filtered out entirely, and sending passengers with a transport reference causes a
		// client crash because the referenced transport object was never sent.
		this.m_createBits.MovementTransport = false;
		this.m_createBits.Stationary = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && !this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit);
		this.m_createBits.ServerTime = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && (this.m_updateData.Guid.GetHighType() == HighGuidType.Transport || this.m_updateData.Guid.GetHighType() == HighGuidType.MOTransport);
		this.m_createBits.CombatVictim = this.m_updateData.CreateData != null && this.m_updateData.CreateData.AutoAttackVictim != null;
		this.m_createBits.Vehicle = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_updateData.CreateData.MoveInfo.VehicleId != 0;
		this.m_createBits.Rotation = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_objectType == ObjectTypeBCC.GameObject;
		this.m_createBits.GameObject = this.m_objectType == ObjectTypeBCC.GameObject;
		this.m_createBits.ThisIsYou = (this.m_createBits.ActivePlayer = this.m_objectType == ObjectTypeBCC.ActivePlayer);
	}

	public void BuildMovementUpdate(WorldPacket data)
	{
		int PauseTimesCount = 0;
		data.WriteBit(this.m_createBits.NoBirthAnim);
		data.WriteBit(this.m_createBits.EnablePortals);
		data.WriteBit(this.m_createBits.PlayHoverAnim);
		data.WriteBit(this.m_createBits.MovementUpdate);
		data.WriteBit(this.m_createBits.MovementTransport);
		data.WriteBit(this.m_createBits.Stationary);
		data.WriteBit(this.m_createBits.CombatVictim);
		data.WriteBit(this.m_createBits.ServerTime);
		data.WriteBit(this.m_createBits.Vehicle);
		data.WriteBit(this.m_createBits.AnimKit);
		data.WriteBit(this.m_createBits.Rotation);
		data.WriteBit(this.m_createBits.AreaTrigger);
		data.WriteBit(this.m_createBits.GameObject);
		data.WriteBit(this.m_createBits.SmoothPhasing);
		data.WriteBit(this.m_createBits.ThisIsYou);
		data.WriteBit(this.m_createBits.SceneObject);
		data.WriteBit(this.m_createBits.ActivePlayer);
		data.WriteBit(this.m_createBits.Conversation);
		data.FlushBits();
		if (this.m_createBits.MovementUpdate)
		{
			MovementInfo moveInfo = this.m_updateData.CreateData.MoveInfo;
			bool hasSpline = this.m_updateData.CreateData.MoveSpline != null;
			int beforeMove = data.GetData().Length;
			moveInfo.WriteMovementInfoModern(data, this.m_updateData.Guid);
			int afterMoveInfo = data.GetData().Length;
			Log.Print(LogType.Debug, $"[Movement] MoveInfo={afterMoveInfo - beforeMove}b Speeds: Walk={moveInfo.WalkSpeed} Run={moveInfo.RunSpeed} Swim={moveInfo.SwimSpeed} Flight={moveInfo.FlightSpeed} Turn={moveInfo.TurnRate} Pitch={moveInfo.PitchRate}", "BuildMovementUpdate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
			data.WriteFloat(moveInfo.WalkSpeed);
			data.WriteFloat(moveInfo.RunSpeed);
			data.WriteFloat(moveInfo.RunBackSpeed);
			data.WriteFloat(moveInfo.SwimSpeed);
			data.WriteFloat(moveInfo.SwimBackSpeed);
			data.WriteFloat(moveInfo.FlightSpeed);
			data.WriteFloat(moveInfo.FlightBackSpeed);
			data.WriteFloat(moveInfo.TurnRate);
			data.WriteFloat(moveInfo.PitchRate);
			data.WriteUInt32(0u);
			data.WriteFloat(1f);
			data.WriteFloat(2f);
			data.WriteFloat(65f);
			data.WriteFloat(1f);
			data.WriteFloat(3f);
			data.WriteFloat(10f);
			data.WriteFloat(100f);
			data.WriteFloat(90f);
			data.WriteFloat(140f);
			data.WriteFloat(180f);
			data.WriteFloat(360f);
			data.WriteFloat(90f);
			data.WriteFloat(270f);
			data.WriteFloat(30f);
			data.WriteFloat(80f);
			data.WriteFloat(2.75f);
			data.WriteFloat(7f);
			data.WriteFloat(0.4f);
			data.WriteBit(hasSpline);
			data.FlushBits();
			if (hasSpline)
			{
				ObjectUpdateBuilder.WriteCreateObjectSplineDataBlock(this.m_updateData.CreateData.MoveSpline, data);
			}
		}
		data.WriteInt32(PauseTimesCount);
		if (this.m_createBits.Stationary)
		{
			data.WriteFloat(this.m_updateData.CreateData.MoveInfo.Position.X);
			data.WriteFloat(this.m_updateData.CreateData.MoveInfo.Position.Y);
			data.WriteFloat(this.m_updateData.CreateData.MoveInfo.Position.Z);
			data.WriteFloat(this.m_updateData.CreateData.MoveInfo.Orientation);
		}
		if (this.m_createBits.CombatVictim)
		{
			data.WritePackedGuid128(this.m_updateData.CreateData.AutoAttackVictim);
		}
		if (this.m_createBits.ServerTime)
		{
			// TC343 writes GameTime::GetGameTimeMS() = server uptime in ms
			// Legacy 3.3.5a sends PathProgress (transport-specific counter), NOT game time
			// The 3.4.3 client expects server uptime for transport animation sync
			data.WriteUInt32((uint)Environment.TickCount);
		}
		if (this.m_createBits.Vehicle)
		{
			data.WriteUInt32(this.m_updateData.CreateData.MoveInfo.VehicleId);
			data.WriteFloat(this.m_updateData.CreateData.MoveInfo.VehicleOrientation);
		}
		if (this.m_createBits.AnimKit)
		{
			data.WriteUInt16(0);
			data.WriteUInt16(0);
			data.WriteUInt16(0);
		}
		if (this.m_createBits.Rotation)
		{
			data.WriteInt64(this.m_updateData.CreateData.MoveInfo.Rotation.GetPackedRotation());
		}
		for (int i = 0; i < PauseTimesCount; i++)
		{
			data.WriteUInt32(0u);
		}
		if (this.m_createBits.MovementTransport)
		{
			this.m_updateData.CreateData.MoveInfo.WriteTransportInfoModern(data);
		}
		if (this.m_createBits.GameObject)
		{
			data.WriteUInt32(0u);
			data.WriteBit(bit: false);
			data.FlushBits();
		}
		if (this.m_createBits.ActivePlayer)
		{
			bool hasSceneInstanceIDs = false;
			bool hasRuneState = false;
			bool hasActionButtons = true;
			data.WriteBit(hasSceneInstanceIDs);
			data.WriteBit(hasRuneState);
			data.WriteBit(hasActionButtons);
			data.FlushBits();
			for (int j = 0; j < 180; j++)
			{
				data.WriteInt32((j < this.m_gameState.ActionButtons.Count) ? this.m_gameState.ActionButtons[j] : 0);
			}
		}
	}

	public static void WriteCreateObjectSplineDataBlock(ServerSideMovement moveSpline, WorldPacket data)
	{
		data.WriteUInt32(moveSpline.SplineId);
		if (!moveSpline.SplineFlags.HasAnyFlag(SplineFlagModern.Cyclic))
		{
			data.WriteVector3(moveSpline.EndPosition);
		}
		else
		{
			data.WriteVector3(Vector3.Zero);
		}
		bool hasSplineMove = data.WriteBit(moveSpline.SplineCount != 0);
		data.FlushBits();
		if (!hasSplineMove)
		{
			return;
		}
		data.WriteUInt32((uint)moveSpline.SplineFlags);
		data.WriteUInt32(moveSpline.SplineTime);
		data.WriteUInt32(moveSpline.SplineTimeFull);
		data.WriteFloat(1f);
		data.WriteFloat(1f);
		data.WriteBits((byte)moveSpline.SplineType, 2);
		bool hasFadeObjectTime = data.WriteBit(bit: false);
		data.WriteBits(moveSpline.SplineCount, 16);
		data.WriteBit(bit: false);
		data.WriteBit(bit: false);
		data.WriteBit(bit: false);
		data.WriteBit(bit: false);
		data.FlushBits();
		switch (moveSpline.SplineType)
		{
		case SplineTypeModern.FacingSpot:
			data.WriteVector3(moveSpline.FinalFacingSpot);
			break;
		case SplineTypeModern.FacingTarget:
			data.WriteFloat(moveSpline.FinalOrientation);
			data.WritePackedGuid128(moveSpline.FinalFacingGuid);
			break;
		case SplineTypeModern.FacingAngle:
			data.WriteFloat(moveSpline.FinalOrientation);
			break;
		}
		if (hasFadeObjectTime)
		{
			data.WriteInt32(0);
		}
		foreach (Vector3 vec in moveSpline.SplinePoints)
		{
			data.WriteVector3(vec);
		}
	}
}
