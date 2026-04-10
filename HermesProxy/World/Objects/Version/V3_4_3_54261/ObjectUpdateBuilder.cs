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
		if (a.Coinage.HasValue || a.XP.HasValue || a.NextLevelXP.HasValue) return true;
		if (a.LocalFlags.HasValue) return true;
		// Check if any InvSlots changed
		for (int i = 0; i < 141; i++)
		{
			WowGuid128 slot = GetModernInvSlot(a, i);
			if (slot != null) return true;
		}
		return false;
	}

	/// <summary>
	/// Writes ActivePlayerData update using TC343 bitmask format.
	/// The changesMask has 48 blocks of 32 bits each (1536 total bits).
	/// Key bit indices from TC343 UpdateFields.h:
	///   Bit 0: group bit for fields 1-37
	///   Bit 28: Coinage, Bit 29: XP, Bit 30: NextLevelXP
	///   Bit 124: InvSlots group, Bits 125-265: InvSlots[0-140]
	/// </summary>
	private void WriteUpdateActivePlayerData(WorldPacket data)
	{
		ActivePlayerData a = this.m_updateData.ActivePlayerData ?? new ActivePlayerData();

		// Build changesMask (1536 bits = 48 blocks of 32)
		uint[] blocks = new uint[48];

		// Helper to set a bit in the changesMask
		void SetBit(int bit)
		{
			blocks[bit / 32] |= (1u << (bit % 32));
		}

		// Group 0 scalar fields (bits 26-37)
		if (a.Coinage.HasValue) { SetBit(0); SetBit(28); }
		if (a.XP.HasValue) { SetBit(0); SetBit(29); }
		if (a.NextLevelXP.HasValue) { SetBit(0); SetBit(30); }
		// Group 1 scalar fields (bits 38-69)
		if (a.LocalFlags.HasValue) { SetBit(38); SetBit(69); }

		// InvSlots: bit 124 = group bit, bits 125-265 = individual slots
		int invSlotsChanged = 0;
		for (int i = 0; i < 141; i++)
		{
			WowGuid128 slot = GetModernInvSlot(a, i);
			if (slot != null)
			{
				SetBit(124); // group bit
				SetBit(125 + i);
				invSlotsChanged++;
			}
		}

		// Log what we're writing
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		sb.Append($"[ActivePlayerUpdate] Coinage={a.Coinage.HasValue} XP={a.XP.HasValue} NextLevelXP={a.NextLevelXP.HasValue} InvSlots={invSlotsChanged}");
		if (invSlotsChanged > 0)
		{
			sb.Append(" slots:[");
			for (int i = 0; i < 141; i++)
			{
				int bit = 125 + i;
				if ((blocks[bit / 32] & (1u << (bit % 32))) != 0)
					sb.Append($"{i},");
			}
			sb.Append("]");
		}
		for (int b = 0; b < 48; b++)
			if (blocks[b] != 0)
				sb.Append($" blk{b}=0x{blocks[b]:X8}");
		Log.Print(LogType.Debug, sb.ToString(), "WriteUpdateActivePlayerData", "");

		// Write blocksMask: first uint32 for blocks 0-31, then 16 bits for blocks 32-47
		uint blocksMask0 = 0;
		for (int b = 0; b < 32; b++)
			if (blocks[b] != 0) blocksMask0 |= (1u << b);
		uint blocksMask1 = 0;
		for (int b = 32; b < 48; b++)
			if (blocks[b] != 0) blocksMask1 |= (1u << (b - 32));

		data.WriteUInt32(blocksMask0);
		data.WriteBits(blocksMask1, 16);

		// Write each set block's 32-bit mask
		for (int b = 0; b < 48; b++)
		{
			bool blockSet = (b < 32) ? ((blocksMask0 & (1u << b)) != 0) : ((blocksMask1 & (1u << (b - 32))) != 0);
			if (blockSet)
				data.WriteBits(blocks[b], 32);
		}

		// No dynamic fields used - two FlushBits for the two dynamic mask sections
		data.FlushBits();
		data.FlushBits();

		// Write actual field values in TC343 order

		// Group 0 scalar fields
		if (blocks[0] != 0)
		{
			if ((blocks[0] & (1u << 28)) != 0)
				data.WriteUInt64(a.Coinage.Value);
			if ((blocks[0] & (1u << 29)) != 0)
				data.WriteInt32(a.XP.Value);
			if ((blocks[0] & (1u << 30)) != 0)
				data.WriteInt32(a.NextLevelXP.Value);
		}

		// Group 1 scalar fields (bits 38-69)
		if (a.LocalFlags.HasValue && (blocks[69 / 32] & (1u << (69 % 32))) != 0)
			data.WriteUInt32(a.LocalFlags.Value);

		// InvSlots (after all scalar groups, per TC343 write order)
		if ((blocks[124 / 32] & (1u << (124 % 32))) != 0)
		{
			for (int i = 0; i < 141; i++)
			{
				int bit = 125 + i;
				if ((blocks[bit / 32] & (1u << (bit % 32))) != 0)
				{
					WowGuid128 guid = GetModernInvSlot(a, i) ?? WowGuid128.Empty;
					Log.Print(LogType.Debug, $"[ActivePlayerUpdate] InvSlot[{i}] = {guid}", "WriteUpdateActivePlayerData", "");
					data.WritePackedGuid128(guid);
				}
			}
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
		// Block 1 continued: damage, stance bytes, pet fields
		if (unit.MinDamage.HasValue) SetBit(52);
		if (unit.MaxDamage.HasValue) SetBit(53);
		if (unit.MinOffHandDamage.HasValue) SetBit(54);
		if (unit.MaxOffHandDamage.HasValue) SetBit(55);
		if (unit.StandState.HasValue) SetBit(56);
		// 57 = PetTalentPoints (not tracked)
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
		// 73-74 = TrainingPointsUsed/Total (not tracked individually)
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
		if (hasAnyPowerGroup)
			SetBit(116); // parent bit for Power/MaxPower/Regen arrays
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
			// 73-74: TrainingPointsUsed/Total not tracked
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
				// ModPowerRegen[pi] (bits 157+) — not tracked, skip
			}
		}
		// VirtualItems (parent bit 167, elements 168-170) — not tracked in updates, skip
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
		// Check quest log entries
		if (p.QuestLog != null)
			for (int i = 0; i < p.QuestLog.Length; i++)
				if (p.QuestLog[i] != null && p.QuestLog[i].QuestID.HasValue) return true;
		// Check scalar fields
		if (p.PlayerFlags.HasValue || p.PlayerFlagsEx.HasValue) return true;
		if (p.ChosenTitle.HasValue) return true;
		if (p.GuildTimeStamp.HasValue) return true;
		// Check visible items
		if (p.VisibleItems != null)
			for (int i = 0; i < p.VisibleItems.Length; i++)
				if (p.VisibleItems[i] != null) return true;
		return false;
	}

	private void WriteUpdatePlayerData(WorldPacket data)
	{
		PlayerData p = this.m_updateData.PlayerData ?? new PlayerData();

		// TC343 PlayerData: HasChangesMask<108>, 4 blocks of 32 bits
		// Block 0 (bits 0-31): dynamic[1-3] + scalar[4-31]
		// Block 1 (bits 32-63): PartyType[33-34], QuestLog hasAny[35], QuestLog[36-60], VisibleItems hasAny[61], VisibleItems[62-63]
		// Block 2 (bits 64-95): VisibleItems[64-80], AvgItemLevel hasAny[81], AvgItemLevel[82-87], Field_3120 hasAny[88], Field_3120[89-95]
		// Block 3 (bits 96-107): Field_3120[96-107]
		uint[] blocks = new uint[4];

		// Block 0: scalar fields
		if (p.PlayerFlags.HasValue) { blocks[0] |= (1u << 0) | (1u << 7); }
		if (p.PlayerFlagsEx.HasValue) { blocks[0] |= (1u << 0) | (1u << 8); }
		if (p.GuildTimeStamp.HasValue) { blocks[0] |= (1u << 0) | (1u << 20); }
		if (p.ChosenTitle.HasValue) { blocks[0] |= (1u << 0) | (1u << 21); }

		// Block 1-2: QuestLog (bits 35-60) and VisibleItems (bits 61-80)
		bool hasAnyQuestLog = false;
		for (int i = 0; i < 25; i++)
		{
			if (p.QuestLog[i] != null && p.QuestLog[i].QuestID.HasValue)
			{
				int bit = 36 + i;
				blocks[bit / 32] |= (1u << (bit % 32));
				blocks[35 / 32] |= (1u << (35 % 32)); // hasAny flag
				hasAnyQuestLog = true;
			}
		}
		bool hasAnyVisibleItem = false;
		for (int i = 0; i < 19; i++)
		{
			if (p.VisibleItems != null && i < p.VisibleItems.Length && p.VisibleItems[i] != null)
			{
				int bit = 62 + i;
				blocks[bit / 32] |= (1u << (bit % 32));
				blocks[61 / 32] |= (1u << (61 % 32)); // hasAny flag
				hasAnyVisibleItem = true;
			}
		}

		// Log which fields are set
		Log.Print(LogType.Debug, $"[PlayerDataUpdate] Flags={p.PlayerFlags.HasValue} FlagsEx={p.PlayerFlagsEx.HasValue} GuildTS={p.GuildTimeStamp.HasValue} Title={p.ChosenTitle.HasValue} QuestLog={hasAnyQuestLog} VisItems={hasAnyVisibleItem} blocks=[0x{blocks[0]:X8},0x{blocks[1]:X8},0x{blocks[2]:X8},0x{blocks[3]:X8}]", "WriteUpdatePlayerData", "");

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

		// No dynamic fields (bits 1-3 of block 0 not set, so no masks needed)
		data.FlushBits();

		// Block 0: scalar field values in bit order
		if ((blocks[0] & (1u << 0)) != 0)
		{
			if ((blocks[0] & (1u << 7)) != 0)
				data.WriteUInt32(p.PlayerFlags.Value);
			if ((blocks[0] & (1u << 8)) != 0)
				data.WriteUInt32(p.PlayerFlagsEx.Value);
			if ((blocks[0] & (1u << 20)) != 0)
				data.WriteInt32(p.GuildTimeStamp.Value);
			if ((blocks[0] & (1u << 21)) != 0)
				data.WriteInt32(p.ChosenTitle.Value);
		}

		// QuestLog entries (bits 35-60) — WriteCreate format
		if (hasAnyQuestLog)
		{
			for (int i = 0; i < 25; i++)
			{
				int bit = 36 + i;
				if ((blocks[bit / 32] & (1u << (bit % 32))) != 0)
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
				int bit = 62 + i;
				if ((blocks[bit / 32] & (1u << (bit % 32))) != 0)
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
		this.m_createBits.MovementTransport = ((this.m_updateData.CreateData != null) & (this.m_updateData.CreateData.MoveInfo != null)) && this.m_updateData.CreateData.MoveInfo.TransportGuid != null && this.m_objectType == ObjectTypeBCC.GameObject;
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
