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
		byte updateFieldFlags = (byte)(this.IsOwner ? 1 : 0);
		Log.Print(LogType.Debug, $"[ValuesCreate] type={this.m_objectType} flags=0x{updateFieldFlags:X2} IsOwner={this.IsOwner}", "WriteValuesCreate", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\Version\\V3_4_3_54261\\ObjectUpdateBuilder.cs");
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

	private bool HasAnyUnitFieldSet()
	{
		UnitData u = this.m_updateData.UnitData;
		if (u == null) return false;
		if (u.Health.HasValue || u.MaxHealth.HasValue || u.DisplayID.HasValue) return true;
		if (u.Charm != null || u.Summon != null || u.CharmedBy != null) return true;
		if (u.SummonedBy != null || u.CreatedBy != null || u.Target != null) return true;
		if (u.ChannelData != null) return true;
		if (u.RaceId.HasValue || u.ClassId.HasValue || u.SexId.HasValue) return true;
		if (u.Level.HasValue || u.EffectiveLevel.HasValue) return true;
		if (u.FactionTemplate.HasValue || u.Flags.HasValue || u.Flags2.HasValue) return true;
		if (u.AuraState.HasValue) return true;
		if (u.BoundingRadius.HasValue || u.CombatReach.HasValue) return true;
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
		return false;
	}

	private void WriteValuesUpdate(WorldPacket data)
	{
		uint changedMask = 0u;
		bool hasObjectChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Object) && this.m_updateData.ObjectData != null && (this.m_updateData.ObjectData.EntryID.HasValue || this.m_updateData.ObjectData.DynamicFlags.HasValue || this.m_updateData.ObjectData.Scale.HasValue);
		bool hasUnitChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit) && this.m_updateData.UnitData != null && this.HasAnyUnitFieldSet();
		bool hasItemChanges = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Item) && this.m_updateData.ItemData != null;
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
		// For player objects, always include empty PlayerData block (0x40)
		// The 3.4.3 client may require it when Unit data is present
		bool isPlayer = this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Player);
		if (isPlayer && (hasObjectChanges || hasUnitChanges))
		{
			changedMask |= 0x40;
		}
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
		if (isPlayer && (changedMask & 0x40) != 0)
		{
			// Empty PlayerData: blocksMask=0 (4 bits), IsQuestLogChangesMaskSkipped=false (1 bit), FlushBits
			data.WriteBits(0, 4);
			data.WriteBit(false);
			data.FlushBits();
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
		data.WriteBits(0, 2);
		data.FlushBits();
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
			data.WriteInt32((((virtualItems != null) ? virtualItems[l] : null) != null) ? unit.VirtualItems[l].ItemID : 0);
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
			data.WriteUInt32(unit.RangedAttackRoundBaseTime.GetValueOrDefault());
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
		if (unit.AuraState.HasValue)
		{
			SetBit(44);
		}
		if (unit.BoundingRadius.HasValue)
		{
			SetBit(46);
		}
		if (unit.CombatReach.HasValue)
		{
			SetBit(47);
		}
		if (unit.NativeDisplayID.HasValue)
		{
			SetBit(49);
		}
		if (unit.MountDisplayID.HasValue)
		{
			SetBit(51);
		}
		if (unit.HoverHeight.HasValue)
		{
			SetBit(94);
		}
		if (unit.GuildGUID != null)
		{
			SetBit(107);
		}
		if (unit.NpcFlags != null)
		{
			for (int i = 0; i < unit.NpcFlags.Length; i++)
			{
				if (unit.NpcFlags[i].HasValue && unit.NpcFlags[i] != 0)
				{
					SetBit(114 + i);
				}
			}
		}
		if (unit.Power != null)
		{
			for (int j = 0; j < unit.Power.Length; j++)
			{
				if (unit.Power[j].HasValue)
				{
					SetBit(137 + j);
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
				}
			}
		}
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
				data.WriteBits(7, 4);
				data.FlushBits();
				data.WriteInt32(unit.ChannelData.SpellID);
				data.WriteInt32(unit.ChannelData.SpellXSpellVisualID);
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
			if (unit.AuraState.HasValue)
			{
				data.WriteUInt32(unit.AuraState.Value);
			}
			if (unit.BoundingRadius.HasValue)
			{
				data.WriteFloat(unit.BoundingRadius.Value);
			}
			if (unit.CombatReach.HasValue)
			{
				data.WriteFloat(unit.CombatReach.Value);
			}
			if (unit.NativeDisplayID.HasValue)
			{
				data.WriteInt32(unit.NativeDisplayID.Value);
			}
			if (unit.MountDisplayID.HasValue)
			{
				data.WriteInt32(unit.MountDisplayID.Value);
			}
		}
		if ((blocksMask & 4) != 0 && unit.HoverHeight.HasValue)
		{
			data.WriteFloat(unit.HoverHeight.Value);
		}
		if ((blocksMask & 8) != 0 && unit.GuildGUID != null)
		{
			data.WritePackedGuid128(unit.GuildGUID);
		}
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
		if (unit.Power != null)
		{
			for (int num = 0; num < unit.Power.Length; num++)
			{
				if (unit.Power[num].HasValue)
				{
					data.WriteInt32(unit.Power[num].Value);
				}
			}
		}
		if (unit.MaxPower == null)
		{
			return;
		}
		for (int num2 = 0; num2 < unit.MaxPower.Length; num2++)
		{
			if (unit.MaxPower[num2].HasValue)
			{
				data.WriteInt32(unit.MaxPower[num2].Value);
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
		for (int i = 0; i < 141; i++)
		{
			WowGuid128 slot = null;
			if (active.InvSlots != null && i < active.InvSlots.Length)
			{
				slot = active.InvSlots[i];
			}
			data.WritePackedGuid128(slot ?? WowGuid128.Empty);
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
		data.WriteInt32(0);
		data.WriteInt32(0);
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
		for (int num8 = 0; num8 < 6; num8++)
		{
			data.WriteUInt32(0u);
			data.WriteUInt32(0u);
		}
		data.WriteUInt8(0);
		data.WriteUInt8(0);
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
		data.WriteUInt8(0);
		data.WriteUInt32(corpse.Flags.GetValueOrDefault());
		data.WriteUInt32(corpse.DynamicFlags.GetValueOrDefault());
		data.WriteInt32(corpse.FactionTemplate.GetValueOrDefault());
		data.WriteUInt32(0u);
	}

	public void SetCreateObjectBits()
	{
		this.m_createBits.Clear();
		this.m_createBits.PlayHoverAnim = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_updateData.CreateData.MoveInfo.Hover;
		this.m_createBits.MovementUpdate = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit);
		this.m_createBits.Stationary = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && !this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit);
		this.m_createBits.MovementTransport = false;
		this.m_createBits.Stationary = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && !this.m_objectTypeMask.HasAnyFlag(ObjectTypeMask.Unit);
		this.m_createBits.ServerTime = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_updateData.Guid.GetHighType() == HighGuidType.Transport;
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
			if (this.m_updateData.CreateData.MoveInfo.TransportPathTimer != 0)
			{
				data.WriteUInt32(this.m_updateData.CreateData.MoveInfo.TransportPathTimer);
			}
			else
			{
				data.WriteUInt32((uint)Time.UnixTime);
			}
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
