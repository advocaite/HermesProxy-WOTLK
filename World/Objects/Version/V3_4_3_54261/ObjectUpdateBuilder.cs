using Framework.GameMath;
using Framework.IO;
using Framework.Logging;
using HermesProxy.World.Enums;
using HermesProxy.World.Enums.V3_4_3_54261;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Objects.Version.V3_4_3_54261
{
    // WotLK Classic 3.4.3 uses the MODERN update fields format:
    //   - CreateObject: uint32 size + uint32 typeFlags + sequential field writes (no bitmask)
    //   - Values update: uint32 size + uint32 typeFlags + per-type changesMask + changed values
    // This is different from TBC Classic 2.5.3 which uses the legacy flat bitmask format.
    public class ObjectUpdateBuilder
    {
        public ObjectUpdateBuilder(ObjectUpdate updateData, GameSessionData gameState)
        {
            m_updateData = updateData;
            m_gameState = gameState;

            Enums.ObjectType objectType = updateData.Guid.GetObjectType();
            if (updateData.CreateData != null)
            {
                objectType = updateData.CreateData.ObjectType;
                if (updateData.CreateData.ThisIsYou)
                    objectType = Enums.ObjectType.ActivePlayer;
            }
            if (objectType == Enums.ObjectType.Player && m_gameState.CurrentPlayerGuid == updateData.Guid)
                objectType = Enums.ObjectType.ActivePlayer;
            m_objectType = ObjectTypeConverter.ConvertToBCC(objectType);
            m_realObjectType = m_objectType;
            m_objectTypeMask = Enums.ObjectTypeMask.Object;

            switch (m_objectType)
            {
                case Enums.ObjectTypeBCC.Item:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Item;
                    break;
                case Enums.ObjectTypeBCC.Container:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Item | Enums.ObjectTypeMask.Container;
                    break;
                case Enums.ObjectTypeBCC.Unit:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Unit;
                    break;
                case Enums.ObjectTypeBCC.Player:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Unit | Enums.ObjectTypeMask.Player;
                    break;
                case Enums.ObjectTypeBCC.ActivePlayer:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Unit | Enums.ObjectTypeMask.Player | Enums.ObjectTypeMask.ActivePlayer;
                    break;
                case Enums.ObjectTypeBCC.GameObject:
                    m_objectTypeMask |= Enums.ObjectTypeMask.GameObject;
                    break;
                case Enums.ObjectTypeBCC.DynamicObject:
                    m_objectTypeMask |= Enums.ObjectTypeMask.DynamicObject;
                    break;
                case Enums.ObjectTypeBCC.Corpse:
                    m_objectTypeMask |= Enums.ObjectTypeMask.Corpse;
                    break;
            }
        }

        protected ObjectUpdate m_updateData;
        protected Enums.ObjectTypeBCC m_objectType;
        protected Enums.ObjectTypeMask m_objectTypeMask;
        protected CreateObjectBits m_createBits;
        protected GameSessionData m_gameState;

        // Whether this object belongs to the player (gets Owner visibility flags)
        private Enums.ObjectTypeBCC m_realObjectType;
        private bool IsOwner => m_realObjectType == Enums.ObjectTypeBCC.ActivePlayer ||
                                m_realObjectType == Enums.ObjectTypeBCC.Item ||
                                m_realObjectType == Enums.ObjectTypeBCC.Container;

        // Convert HermesProxy internal ObjectTypeMask to 3.4.3 client TypeMask
        // HermesProxy uses legacy bit positions (no Azerite gaps)
        // 3.4.3 client expects modern bit positions (with Azerite gaps)
        private static uint ConvertTypeMask(Enums.ObjectTypeMask mask)
        {
            uint result = 0;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Object))       result |= 0x0001;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Item))         result |= 0x0002;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Container))    result |= 0x0004;
            // 0x0008 = AzeriteEmpoweredItem (not used)
            // 0x0010 = AzeriteItem (not used)
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Unit))         result |= 0x0020;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Player))       result |= 0x0040;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.ActivePlayer)) result |= 0x0080;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.GameObject))   result |= 0x0100;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.DynamicObject))result |= 0x0200;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Corpse))       result |= 0x0400;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.AreaTrigger))  result |= 0x0800;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Sceneobject))  result |= 0x1000;
            if (mask.HasAnyFlag(Enums.ObjectTypeMask.Conversation)) result |= 0x2000;
            return result;
        }

        // Convert HermesProxy internal ObjectTypeBCC to 3.4.3 client TypeID
        private static byte ConvertTypeId(Enums.ObjectTypeBCC type)
        {
            return type switch
            {
                Enums.ObjectTypeBCC.Object => 0,
                Enums.ObjectTypeBCC.Item => 1,
                Enums.ObjectTypeBCC.Container => 2,
                // 3 = AzeriteEmpoweredItem, 4 = AzeriteItem (not used)
                Enums.ObjectTypeBCC.Unit => 5,
                Enums.ObjectTypeBCC.Player => 6,
                Enums.ObjectTypeBCC.ActivePlayer => 7,
                Enums.ObjectTypeBCC.GameObject => 8,
                Enums.ObjectTypeBCC.DynamicObject => 9,
                Enums.ObjectTypeBCC.Corpse => 10,
                Enums.ObjectTypeBCC.AreaTrigger => 11,
                Enums.ObjectTypeBCC.SceneObject => 12,
                _ => 0,
            };
        }

        public void WriteToPacket(WorldPacket packet)
        {
            Log.Print(LogType.Debug, $"[UpdateBuilder] Writing {m_updateData.Type} for {m_updateData.Guid} objType={m_objectType} typeMask=0x{ConvertTypeMask(m_objectTypeMask):X4}");
            packet.WriteUInt8((byte)m_updateData.Type);
            packet.WritePackedGuid128(m_updateData.Guid);

            if (m_updateData.Type != Enums.UpdateTypeModern.Values)
            {
                var headerType = m_objectType;
                // TC 3.4.3: only writes uint8 objectType, NO typeMask
                packet.WriteUInt8(ConvertTypeId(headerType));

                SetCreateObjectBits();
                Log.Print(LogType.Debug, $"[UpdateBuilder] CreateBits: Move={m_createBits.MovementUpdate} Stationary={m_createBits.Stationary} Vehicle={m_createBits.Vehicle} ActivePlayer={m_createBits.ActivePlayer} Transport={m_createBits.MovementTransport} Rotation={m_createBits.Rotation}");
                BuildMovementUpdate(packet);
            }

            // Modern format: uint32 size + uint32 typeFlags + field data
            WriteValuesModern(packet);
        }

        private void WriteValuesModern(WorldPacket packet)
        {
            var valuesBuffer = new WorldPacket();

            if (m_updateData.Type == Enums.UpdateTypeModern.Values)
            {
                // VALUES UPDATE: write changesMask per type
                WriteValuesUpdate(valuesBuffer);
            }
            else
            {
                // CREATE: write all fields sequentially per type
                WriteValuesCreate(valuesBuffer);
            }

            var valuesData = valuesBuffer.GetData();
            Log.Print(LogType.Debug, $"[ValuesBlock] type={m_objectType} totalValuesSize={valuesData.Length} fieldFlags=0x{(valuesData.Length >= 1 ? valuesData[0] : 0):X2}");
            // Write size prefix + data
            packet.WriteUInt32((uint)valuesData.Length);
            packet.WriteBytes(valuesData);
        }

        // DEBUG: Set to true to skip Player/ActivePlayer data (isolate crash source)
        // DEBUG: Strip Player+ActivePlayer from VALUES only (keep correct TypeID/TypeMask in header)
        private static readonly int DEBUG_STRIP_LEVEL = 0; // 0=full data
        public static readonly bool DEBUG_SKIP_GAMEOBJECTS = false;
        public static readonly bool DEBUG_SKIP_ALL_UPDATES = false;
        // Skip entire player object from the update
        public static readonly bool DEBUG_SKIP_PLAYER_OBJECT = false;

        private void WriteValuesCreate(WorldPacket data)
        {
            var effectiveMask = m_objectTypeMask;

            // Debug mode: strip sections from values only (header keeps correct type)
            if (IsOwner && DEBUG_STRIP_LEVEL == 1)
                effectiveMask &= ~(Enums.ObjectTypeMask.Player | Enums.ObjectTypeMask.ActivePlayer);
            else if (IsOwner && DEBUG_STRIP_LEVEL == 2)
                effectiveMask &= ~Enums.ObjectTypeMask.ActivePlayer;

            // TC 3.4.3: WriteValuesCreate writes uint8 UpdateFieldFlags
            // DEBUG: Try Owner (0x01) for player
            byte updateFieldFlags = IsOwner ? (byte)0x01 : (byte)0x00;
            Log.Print(LogType.Debug, $"[ValuesCreate] type={m_objectType} flags=0x{updateFieldFlags:X2} IsOwner={IsOwner}");
            data.WriteUInt8(updateFieldFlags);
            int sectionStart = data.GetData().Length;

            // Object data (always present)
            WriteCreateObjectData(data);
            int afterObj = data.GetData().Length;
            Log.Print(LogType.Debug, $"[Sizes] ObjectData={afterObj - sectionStart} bytes");

            if (effectiveMask.HasAnyFlag(Enums.ObjectTypeMask.Item))
            {
                WriteCreateItemData(data);
                int afterItem = data.GetData().Length;
                Log.Print(LogType.Debug, $"[Sizes] ItemData={afterItem - afterObj} bytes");
            }

            if (effectiveMask.HasAnyFlag(Enums.ObjectTypeMask.Container))
                WriteCreateContainerData(data);

            if (effectiveMask.HasAnyFlag(Enums.ObjectTypeMask.Unit))
            {
                int beforeUnit = data.GetData().Length;
                WriteCreateUnitData(data);
                int afterUnit = data.GetData().Length;
                Log.Print(LogType.Debug, $"[Sizes] UnitData={afterUnit - beforeUnit} bytes");
            }

            if (effectiveMask.HasAnyFlag(Enums.ObjectTypeMask.Player))
            {
                int beforePlayer = data.GetData().Length;
                WriteCreatePlayerData(data);
                int afterPlayer = data.GetData().Length;
                Log.Print(LogType.Debug, $"[Sizes] PlayerData={afterPlayer - beforePlayer} bytes");
            }

            if (effectiveMask.HasAnyFlag(Enums.ObjectTypeMask.ActivePlayer))
            {
                int beforeActive = data.GetData().Length;
                WriteCreateActivePlayerData(data);
                int afterActive = data.GetData().Length;
                Log.Print(LogType.Debug, $"[Sizes] ActivePlayerData={afterActive - beforeActive} bytes");
            }

            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.GameObject))
                WriteCreateGameObjectData(data);

            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.DynamicObject))
                WriteCreateDynamicObjectData(data);

            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Corpse))
                WriteCreateCorpseData(data);
        }

        private void WriteValuesUpdate(WorldPacket data)
        {
            // TC 3.4.3 VALUES update: uint32 changedTypeMask + per-type change masks
            // changedTypeMask bits indicate which type sections follow
            data.WriteUInt32(ConvertTypeMask(m_objectTypeMask));

            // ObjectData: WriteBits(block, 4) + FlushBits
            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Object))
                WriteUpdateObjectData(data);

            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Item))
                WriteUpdateItemData(data);

            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit))
                WriteUpdateUnitData(data);

            // PlayerData: WriteBits(blocksMask, 4) - 4 blocks
            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Player))
            {
                data.WriteBits(0, 4); // no blocks changed
                data.FlushBits();
            }

            // ActivePlayerData: uint32(blocksMask[0]) + WriteBits(blocksMask[1], 16) - 48 blocks
            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.ActivePlayer))
            {
                data.WriteUInt32(0); // blocksMask[0] = no changes
                data.WriteBits(0, 16); // blocksMask[1] = no changes
                data.FlushBits();
            }

            // GameObjectData: WriteBits(block, 20) - single block of 20 bits
            if (m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.GameObject))
            {
                data.WriteBits(0, 20); // no fields changed
                data.FlushBits();
            }
        }

        #region ObjectData
        private void WriteCreateObjectData(WorldPacket data)
        {
            ObjectData obj = m_updateData.ObjectData;
            data.WriteInt32(obj.EntryID ?? 0);
            data.WriteUInt32((uint)(obj.DynamicFlags ?? 0));
            data.WriteFloat((float)(obj.Scale ?? 1.0f));
        }

        private void WriteUpdateObjectData(WorldPacket data)
        {
            ObjectData obj = m_updateData.ObjectData;
            // 4 bits: bit0=hasAny, bit1=EntryID, bit2=DynamicFlags, bit3=Scale
            uint mask = 0;
            if (obj.EntryID != null) mask |= 2;
            if (obj.DynamicFlags != null) mask |= 4;
            if (obj.Scale != null) mask |= 8;
            if (mask != 0) mask |= 1; // set "has any" bit
            data.WriteBits(mask, 4);
            data.FlushBits();

            if ((mask & 1) != 0)
            {
                if (obj.EntryID != null) data.WriteInt32((int)obj.EntryID);
                if (obj.DynamicFlags != null) data.WriteUInt32((uint)obj.DynamicFlags);
                if (obj.Scale != null) data.WriteFloat((float)obj.Scale);
            }
        }
        #endregion

        #region ItemData
        private void WriteCreateItemData(WorldPacket data)
        {
            ItemData item = m_updateData.ItemData;
            if (item == null) { WriteEmptyItemCreate(data); return; }

            data.WritePackedGuid128(item.Owner ?? WowGuid128.Empty);
            data.WritePackedGuid128(item.ContainedIn ?? WowGuid128.Empty);
            data.WritePackedGuid128(item.Creator ?? WowGuid128.Empty);
            data.WritePackedGuid128(item.GiftCreator ?? WowGuid128.Empty);

            if (IsOwner)
            {
                data.WriteUInt32((uint)(item.StackCount ?? 0));
                data.WriteUInt32((uint)(item.Duration ?? 0));
                for (int i = 0; i < 5; i++)
                    data.WriteInt32((int)(item.SpellCharges[i] ?? 0));
            }
            data.WriteUInt32((uint)(item.Flags ?? 0));

            for (int i = 0; i < 13; i++)
            {
                // ItemEnchantment: ID(int32), Duration(uint32), Charges(int16), Inactive(uint16)
                if (item.Enchantment[i] != null)
                {
                    data.WriteInt32((int)(item.Enchantment[i].ID ?? 0));
                    data.WriteUInt32((uint)(item.Enchantment[i].Duration ?? 0));
                    data.WriteInt16((short)(item.Enchantment[i].Charges ?? 0));
                    data.WriteUInt16((ushort)(item.Enchantment[i].Inactive ?? 0));
                }
                else
                {
                    data.WriteInt32(0);
                    data.WriteUInt32(0);
                    data.WriteInt16(0);
                    data.WriteUInt16(0);
                }
            }
            data.WriteInt32((int)(item.PropertySeed ?? 0));
            data.WriteInt32((int)(item.RandomProperty ?? 0));

            if (IsOwner)
            {
                data.WriteUInt32((uint)(item.Durability ?? 0));
                data.WriteUInt32((uint)(item.MaxDurability ?? 0));
            }
            data.WriteUInt32((uint)(item.CreatePlayedTime ?? 0));
            data.WriteInt32(0); // Context
            data.WriteInt64(0); // CreateTime
            if (IsOwner)
            {
                data.WriteUInt64(0); // ArtifactXP
                data.WriteUInt8(0); // ItemAppearanceModID
            }
            data.WriteUInt32(0); // ArtifactPowers.size()
            data.WriteUInt32(0); // Gems.size()
            if (IsOwner)
                data.WriteUInt32(0); // DynamicFlags2

            // ItemBonusKey (empty)
            data.WriteUInt32(0); // ItemID
            data.WriteUInt32(0); // ItemBonusListIDsSize
            data.WriteUInt32(0); // Context
            // No bonus list IDs

            if (IsOwner)
                data.WriteUInt16(0); // DEBUGItemLevel

            // Empty dynamic arrays (ArtifactPowers, Gems)
            // Modifiers (empty)
            data.WriteInt32(0); // Modifiers size
        }

        private void WriteEmptyItemCreate(WorldPacket data)
        {
            // Write minimal empty item data
            for (int i = 0; i < 4; i++) data.WritePackedGuid128(WowGuid128.Empty); // Owner, Contained, Creator, Gift
            if (IsOwner)
            {
                data.WriteUInt32(0); data.WriteUInt32(0); // Stack, Duration
                for (int i = 0; i < 5; i++) data.WriteInt32(0); // SpellCharges
            }
            data.WriteUInt32(0); // DynamicFlags
            for (int i = 0; i < 13; i++) { data.WriteInt32(0); data.WriteUInt32(0); data.WriteInt16(0); data.WriteUInt16(0); } // Enchantments
            data.WriteInt32(0); data.WriteInt32(0); // Seed, Random
            if (IsOwner) { data.WriteUInt32(0); data.WriteUInt32(0); } // Durability
            data.WriteUInt32(0); data.WriteInt32(0); data.WriteInt64(0); // PlayedTime, Context, CreateTime
            if (IsOwner) { data.WriteUInt64(0); data.WriteUInt8(0); } // ArtifactXP, AppearanceMod
            data.WriteUInt32(0); data.WriteUInt32(0); // ArtifactPowers, Gems sizes
            if (IsOwner) data.WriteUInt32(0); // DynamicFlags2
            data.WriteUInt32(0); data.WriteUInt32(0); data.WriteUInt32(0); // ItemBonusKey
            if (IsOwner) data.WriteUInt16(0); // DEBUGItemLevel
            data.WriteInt32(0); // Modifiers size
        }

        private void WriteUpdateItemData(WorldPacket data)
        {
            // TODO: Implement item update with changesMask
            // For now write "no changes" mask
            data.WriteBits(0, 2); // 2 block flags for ItemData (43 fields / 32 = 2 blocks)
            data.FlushBits();
        }
        #endregion

        #region ContainerData
        private void WriteCreateContainerData(WorldPacket data)
        {
            ContainerData container = m_updateData.ContainerData;
            for (int i = 0; i < 36; i++)
                data.WritePackedGuid128(container?.Slots[i] ?? WowGuid128.Empty);
            data.WriteUInt32((uint)(container?.NumSlots ?? 0));
        }
        #endregion

        #region UnitData
        private void WriteCreateUnitData(WorldPacket data)
        {
            UnitData unit = m_updateData.UnitData ?? new UnitData();
            ObjectData obj = m_updateData.ObjectData;

            if (IsOwner)
                Log.Print(LogType.Debug, $"[PlayerUnitData] DisplayID={unit.DisplayID} NativeDisplayID={unit.NativeDisplayID} Race={unit.RaceId} Class={unit.ClassId} Sex={unit.SexId} Health={unit.Health}/{unit.MaxHealth} Level={unit.Level}");

            // Field order matches TrinityCore UnitData::WriteCreate exactly
            data.WriteInt64((long)(unit.Health ?? 0));           // Health (int64)
            data.WriteInt64((long)(unit.MaxHealth ?? 0));        // MaxHealth (int64)
            data.WriteInt32((int)(unit.DisplayID ?? 0));         // DisplayID
            for (int i = 0; i < 2; i++)
                data.WriteUInt32((uint)(unit.NpcFlags?[i] ?? 0)); // NpcFlags[2]
            data.WriteUInt32(0);  // StateSpellVisualID
            data.WriteUInt32(0);  // StateAnimID
            data.WriteUInt32(0);  // StateAnimKitID
            data.WriteUInt32(0);  // StateWorldEffectIDs.size()
            // (no StateWorldEffectIDs elements since size=0)

            data.WritePackedGuid128(unit.Charm ?? WowGuid128.Empty);
            data.WritePackedGuid128(unit.Summon ?? WowGuid128.Empty);
            if (IsOwner)
                data.WritePackedGuid128(unit.Critter ?? WowGuid128.Empty);
            data.WritePackedGuid128(unit.CharmedBy ?? WowGuid128.Empty);
            data.WritePackedGuid128(unit.SummonedBy ?? WowGuid128.Empty);
            data.WritePackedGuid128(unit.CreatedBy ?? WowGuid128.Empty);
            data.WritePackedGuid128(WowGuid128.Empty); // DemonCreator
            data.WritePackedGuid128(WowGuid128.Empty); // LookAtControllerTarget
            data.WritePackedGuid128(unit.Target ?? WowGuid128.Empty);
            data.WritePackedGuid128(WowGuid128.Empty); // BattlePetCompanionGUID
            data.WriteUInt64(0); // BattlePetDBID

            // ChannelData (SpellID + SpellXSpellVisualID)
            data.WriteInt32(unit.ChannelData?.SpellID ?? 0);
            data.WriteInt32(unit.ChannelData?.SpellXSpellVisualID ?? 0);

            data.WriteUInt32(0); // SummonedByHomeRealm
            data.WriteUInt8((byte)(unit.RaceId ?? 0));
            data.WriteUInt8((byte)(unit.ClassId ?? 0));
            data.WriteUInt8((byte)(unit.PlayerClassId ?? 0));
            data.WriteUInt8((byte)(unit.SexId ?? 0));
            data.WriteUInt8(0); // DisplayPower
            data.WriteUInt32(0); // OverrideDisplayPowerID

            // Owner-only: PowerRegenFlatModifier[10] + PowerRegenInterruptedFlatModifier[10]
            if (IsOwner)
            {
                for (int i = 0; i < 10; i++)
                {
                    data.WriteFloat(0); // PowerRegenFlatModifier
                    data.WriteFloat(0); // PowerRegenInterruptedFlatModifier
                }
            }

            // Power[10], MaxPower[10], ModPowerRegen[10] - interleaved
            for (int i = 0; i < 10; i++)
            {
                data.WriteInt32(i < 7 ? (int)(unit.Power[i] ?? 0) : 0);
                data.WriteInt32(i < 7 ? (int)(unit.MaxPower[i] ?? 0) : 0);
                data.WriteFloat(0); // ModPowerRegen
            }

            data.WriteInt32((int)(unit.Level ?? 0));
            data.WriteInt32(0); // EffectiveLevel
            data.WriteInt32(0); // ContentTuningID
            data.WriteInt32(0); // ScalingLevelMin
            data.WriteInt32(0); // ScalingLevelMax
            data.WriteInt32(0); // ScalingLevelDelta
            data.WriteInt32(0); // ScalingFactionGroup
            data.WriteInt32(0); // ScalingHealthItemLevelCurveID
            data.WriteInt32(0); // ScalingDamageItemLevelCurveID
            data.WriteInt32((int)(unit.FactionTemplate ?? 0));

            // VirtualItems[3] - each is {ItemID (int32), ItemAppearanceModID (uint16), ItemVisual (uint16)}
            for (int i = 0; i < 3; i++)
            {
                data.WriteInt32(unit.VirtualItems?[i] != null ? (int)unit.VirtualItems[i].ItemID : 0);
                data.WriteUInt16(0); // AppearanceModID
                data.WriteUInt16(0); // ItemVisual
            }

            data.WriteUInt32((uint)(unit.Flags ?? 0));
            data.WriteUInt32((uint)(unit.Flags2 ?? 0));
            data.WriteUInt32(0); // Flags3
            data.WriteUInt32((uint)(unit.AuraState ?? 0));
            for (int i = 0; i < 2; i++)
                data.WriteUInt32((uint)(unit.AttackRoundBaseTime?[i] ?? 0));

            if (IsOwner)
                data.WriteUInt32((uint)(unit.RangedAttackRoundBaseTime ?? 0));

            data.WriteFloat((float)(unit.BoundingRadius ?? 0.389f));
            data.WriteFloat((float)(unit.CombatReach ?? 1.5f));
            data.WriteFloat(1.0f); // DisplayScale
            data.WriteInt32((int)(unit.NativeDisplayID ?? 0));
            data.WriteFloat(1.0f); // NativeXDisplayScale
            data.WriteInt32((int)(unit.MountDisplayID ?? 0));

            if (IsOwner || m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit)) // Empath check simplified
            {
                if (IsOwner)
                {
                    data.WriteFloat((float)(unit.MinDamage ?? 0));
                    data.WriteFloat((float)(unit.MaxDamage ?? 0));
                    data.WriteFloat((float)(unit.MinOffHandDamage ?? 0));
                    data.WriteFloat((float)(unit.MaxOffHandDamage ?? 0));
                }
            }

            data.WriteUInt8((byte)(unit.StandState ?? 0));
            data.WriteUInt8((byte)(unit.PetLoyaltyIndex ?? 0)); // PetTalentPoints
            data.WriteUInt8((byte)(unit.VisFlags ?? 0));
            data.WriteUInt8((byte)(unit.AnimTier ?? 0));
            data.WriteUInt32((uint)(unit.PetNumber ?? 0));
            data.WriteUInt32((uint)(unit.PetNameTimestamp ?? 0));
            data.WriteUInt32((uint)(unit.PetExperience ?? 0));
            data.WriteUInt32((uint)(unit.PetNextLevelExperience ?? 0));
            data.WriteFloat((float)(unit.ModCastSpeed ?? 1.0f));
            data.WriteFloat((float)(unit.ModCastHaste ?? 1.0f));
            data.WriteFloat(1.0f); // ModHaste
            data.WriteFloat(1.0f); // ModRangedHaste
            data.WriteFloat(1.0f); // ModHasteRegen
            data.WriteFloat(1.0f); // ModTimeRate
            data.WriteInt32((int)(unit.CreatedBySpell ?? 0));
            data.WriteInt32((int)(unit.EmoteState ?? 0));

            // TrainingPoints (int16 + int16 packed)
            data.WriteInt16(0); // TrainingPointsUsed
            data.WriteInt16(0); // TrainingPointsTotal

            if (IsOwner)
            {
                for (int i = 0; i < 5; i++)
                {
                    data.WriteInt32((int)(unit.Stats?[i] ?? 0));
                    data.WriteInt32((int)(unit.StatPosBuff?[i] ?? 0));
                    data.WriteInt32((int)(unit.StatNegBuff?[i] ?? 0));
                }
            }

            if (IsOwner)
            {
                for (int i = 0; i < 7; i++)
                    data.WriteInt32((int)(unit.Resistances?[i] ?? 0));
            }

            if (IsOwner)
            {
                for (int i = 0; i < 7; i++)
                {
                    data.WriteInt32((int)(unit.PowerCostModifier?[i] ?? 0));
                    data.WriteFloat((float)(unit.PowerCostMultiplier?[i] ?? 0));
                }
            }

            // ResistanceBuffMods (always written)
            for (int i = 0; i < 7; i++)
            {
                data.WriteInt32(0); // ResistanceBuffModsPositive
                data.WriteInt32(0); // ResistanceBuffModsNegative
            }

            data.WriteInt32((int)(unit.BaseMana ?? 0));
            if (IsOwner)
                data.WriteInt32((int)(unit.BaseHealth ?? 0));

            data.WriteUInt8((byte)(unit.SheatheState ?? 0));
            data.WriteUInt8((byte)(unit.PvpFlags ?? 0));
            data.WriteUInt8((byte)(unit.PetFlags ?? 0));
            data.WriteUInt8((byte)(unit.ShapeshiftForm ?? 0));

            if (IsOwner)
            {
                data.WriteInt32((int)(unit.AttackPower ?? 0));
                data.WriteInt32((int)(unit.AttackPowerModPos ?? 0));
                data.WriteInt32((int)(unit.AttackPowerModNeg ?? 0));
                data.WriteFloat((float)(unit.AttackPowerMultiplier ?? 0));
                data.WriteInt32((int)(unit.RangedAttackPower ?? 0));
                data.WriteInt32((int)(unit.RangedAttackPowerModPos ?? 0));
                data.WriteInt32((int)(unit.RangedAttackPowerModNeg ?? 0));
                data.WriteFloat((float)(unit.RangedAttackPowerMultiplier ?? 0));
                data.WriteInt32(0); // SetAttackSpeedAura
                data.WriteFloat(0); // Lifesteal
                data.WriteFloat((float)(unit.MinRangedDamage ?? 0));
                data.WriteFloat((float)(unit.MaxRangedDamage ?? 0));
                data.WriteFloat((float)(unit.MaxHealthModifier ?? 1.0f));
            }

            data.WriteFloat((float)(unit.HoverHeight ?? 0));
            data.WriteInt32(0); // MinItemLevelCutoff
            data.WriteInt32(0); // MinItemLevel
            data.WriteInt32(0); // MaxItemLevel
            data.WriteInt32(0); // WildBattlePetLevel
            data.WriteUInt32(0); // BattlePetCompanionNameTimestamp
            data.WriteInt32(0); // InteractSpellID
            data.WriteInt32(0); // ScaleDuration
            data.WriteInt32(0); // LooksLikeMountID
            data.WriteInt32(0); // LooksLikeCreatureID
            data.WriteInt32(0); // LookAtControllerID
            data.WriteInt32(0); // PerksVendorItemID
            data.WritePackedGuid128(unit.GuildGUID ?? WowGuid128.Empty); // GuildGUID

            // Dynamic field sizes
            data.WriteUInt32(0); // PassiveSpells.size()
            data.WriteUInt32(0); // WorldEffects.size()
            data.WriteUInt32(0); // ChannelObjects.size()

            data.WritePackedGuid128(WowGuid128.Empty); // SkinningOwnerGUID
            data.WriteInt32(0); // FlightCapabilityID
            data.WriteFloat(0); // GlideEventSpeedDivisor
            data.WriteUInt32(0); // CurrentAreaID

            if (IsOwner)
                data.WritePackedGuid128(WowGuid128.Empty); // ComboTarget

            // Dynamic arrays (all empty)
        }

        private void WriteUpdateUnitData(WorldPacket data)
        {
            // UnitData has 229 fields → ceil(229/32) = 8 blocks
            // Block mask: 8 bits indicating which blocks have changes
            // For now, write "no changes"
            data.WriteBits(0, 8);
            data.FlushBits();
        }
        #endregion

        #region PlayerData
        private void WriteCreatePlayerData(WorldPacket data)
        {
            PlayerData player = m_updateData.PlayerData ?? new PlayerData();

            // Exact field order from TrinityCore PlayerData::WriteCreate
            data.WritePackedGuid128(player.DuelArbiter ?? WowGuid128.Empty);          // DuelArbiter
            data.WritePackedGuid128(player.WowAccount ?? WowGuid128.Empty);           // WowAccount
            data.WritePackedGuid128(player.LootTargetGUID ?? WowGuid128.Empty);       // LootTargetGUID
            data.WriteUInt32((uint)(player.PlayerFlags ?? 0));                         // PlayerFlags
            data.WriteUInt32((uint)(player.PlayerFlagsEx ?? 0));                       // PlayerFlagsEx
            data.WriteUInt32((uint)(player.GuildRankID ?? 0));                         // GuildRankID
            data.WriteUInt32((uint)(player.GuildDeleteDate ?? 0));                     // GuildDeleteDate
            data.WriteInt32((int)(player.GuildLevel ?? 0));                            // GuildLevel
            // Count non-null customizations
            int customizationCount = 0;
            for (int i = 0; i < player.Customizations.Length; i++)
            {
                if (player.Customizations[i] != null)
                {
                    customizationCount++;
                    if (IsOwner)
                        Log.Print(LogType.Debug, $"[Customization] Option={player.Customizations[i].ChrCustomizationOptionID} Choice={player.Customizations[i].ChrCustomizationChoiceID}");
                }
            }
            if (IsOwner)
                Log.Print(LogType.Debug, $"[Customization] Total count={customizationCount}");
            data.WriteUInt32((uint)customizationCount);                                // Customizations.size()
            data.WriteUInt8((byte)(player.PartyType ?? 0));                            // PartyType[0]
            data.WriteUInt8(0);                                                        // PartyType[1]
            data.WriteUInt8((byte)(player.NumBankSlots ?? 0));                         // NumBankSlots
            data.WriteUInt8((byte)(player.NativeSex ?? 0));                            // NativeSex
            data.WriteUInt8((byte)(player.Inebriation ?? 0));                          // Inebriation
            data.WriteUInt8((byte)(player.PvpTitle ?? 0));                             // PvpTitle
            data.WriteUInt8((byte)(player.ArenaFaction ?? 0));                         // ArenaFaction
            data.WriteUInt8((byte)(player.PvPRank ?? 0));                              // PvpRank
            data.WriteInt32(0);                                                        // Field_88
            data.WriteUInt32((uint)(player.DuelTeam ?? 0));                            // DuelTeam
            data.WriteInt32((int)(player.GuildTimeStamp ?? 0));                        // GuildTimeStamp

            // QuestLog[25] - TC only writes if PartyMember flag is set (in party/raid)
            // Solo player does NOT get QuestLog in PlayerData - it's PartyMember-only visibility
            // DO NOT write QuestLog here for solo player!

            // VisibleItems[19]
            for (int i = 0; i < 19; i++)
            {
                if (player.VisibleItems != null && i < player.VisibleItems.Length && player.VisibleItems[i] != null)
                {
                    data.WriteInt32((int)player.VisibleItems[i].ItemID);
                    data.WriteUInt16(player.VisibleItems[i].ItemAppearanceModID);
                    data.WriteUInt16(player.VisibleItems[i].ItemVisual);
                }
                else
                {
                    data.WriteInt32(0);
                    data.WriteUInt16(0);
                    data.WriteUInt16(0);
                }
            }

            data.WriteInt32((int)(player.ChosenTitle ?? 0));                           // PlayerTitle
            data.WriteInt32(0);                                                        // FakeInebriation
            data.WriteUInt32((uint)(player.VirtualPlayerRealm ?? 0));                  // VirtualPlayerRealm
            data.WriteUInt32((uint)(player.CurrentSpecID ?? 0));                       // CurrentSpecID
            data.WriteInt32(0);                                                        // TaxiMountAnimKitID
            for (int i = 0; i < 6; i++)
                data.WriteFloat(0);                                                    // AvgItemLevel[6]
            data.WriteUInt8(0);                                                        // CurrentBattlePetBreedQuality
            data.WriteInt32((int)(player.HonorLevel ?? 0));                            // HonorLevel
            data.WriteInt64(0);                                                        // LogoutTime
            data.WriteUInt32(0);                                                       // ArenaCooldowns.size()
            data.WriteInt32(0);                                                        // CurrentBattlePetSpeciesID
            data.WritePackedGuid128(WowGuid128.Empty);                                 // BnetAccount
            data.WriteUInt32(0);                                                       // VisualItemReplacements.size()
            for (int i = 0; i < 19; i++)
                data.WriteUInt32(0);                                                   // Field_3120[19]

            // Dynamic array elements: Customizations
            for (int i = 0; i < player.Customizations.Length; i++)
            {
                if (player.Customizations[i] != null)
                {
                    data.WriteUInt32(player.Customizations[i].ChrCustomizationOptionID);
                    data.WriteUInt32(player.Customizations[i].ChrCustomizationChoiceID);
                }
            }
            // ArenaCooldowns: 0 entries
            // VisualItemReplacements: 0 entries

            // DungeonScoreSummary
            data.WriteFloat(0);                                                        // OverallScoreCurrentSeason
            data.WriteFloat(0);                                                        // LadderScoreCurrentSeason
            data.WriteUInt32(0);                                                       // Runs.size() = 0
        }

        private void WriteEmptyQuestLog(WorldPacket data)
        {
            // QuestLog::WriteCreate: EndTime(int64) + QuestID(int32) + StateFlags(uint32) + ObjectiveProgress[24](uint16)
            data.WriteInt64(0);  // EndTime
            data.WriteInt32(0);  // QuestID
            data.WriteUInt32(0); // StateFlags
            for (int i = 0; i < 24; i++)
                data.WriteUInt16(0); // ObjectiveProgress[24]
        }
        #endregion

        #region ActivePlayerData
        private void WriteCreateActivePlayerData(WorldPacket data)
        {
            ActivePlayerData active = m_updateData.ActivePlayerData ?? new ActivePlayerData();

            // InvSlots[141] - combine all inventory slot arrays
            for (int i = 0; i < 141; i++)
            {
                WowGuid128 slot = null;
                if (active.InvSlots != null && i < active.InvSlots.Length)
                    slot = active.InvSlots[i];
                data.WritePackedGuid128(slot ?? WowGuid128.Empty);
            }

            data.WritePackedGuid128(active.FarsightObject ?? WowGuid128.Empty);
            data.WritePackedGuid128(WowGuid128.Empty); // SummonedBattlePetGUID
            data.WriteUInt32(0); // KnownTitles.size()
            data.WriteUInt64((ulong)(active.Coinage ?? 0));
            data.WriteInt32((int)(active.XP ?? 0));
            data.WriteInt32((int)(active.NextLevelXP ?? 0));
            data.WriteInt32(0); // TrialXP

            // Skill: TC writes INTERLEAVED per-skill (all 7 values for each skill index)
            var skill = active.Skill;
            for (int i = 0; i < 256; i++)
            {
                data.WriteUInt16(skill?.SkillLineID[i] ?? 0);
                data.WriteUInt16(skill?.SkillStep[i] ?? 0);
                data.WriteUInt16(skill?.SkillRank[i] ?? 0);
                data.WriteUInt16(skill?.SkillStartingRank[i] ?? 0);
                data.WriteUInt16(skill?.SkillMaxRank[i] ?? 0);
                data.WriteUInt16((ushort)(skill?.SkillTempBonus[i] ?? 0));
                data.WriteUInt16(skill?.SkillPermBonus[i] ?? 0);
            }

            data.WriteInt32(0); // CharacterPoints
            data.WriteInt32(0); // MaxTalentTiers
            data.WriteUInt32(0); // TrackCreatureMask
            data.WriteUInt32(0); data.WriteUInt32(0); // TrackResourceMask[2]
            data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); // Expertise
            data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); // Block/Dodge
            data.WriteFloat(0); data.WriteFloat(0); // Parry
            data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); // Crit
            for (int i = 0; i < 7; i++)
            {
                data.WriteFloat(0); // SpellCritPercentage
                data.WriteInt32(0); // ModDamageDonePos
                data.WriteInt32(0); // ModDamageDoneNeg
                data.WriteFloat(0); // ModDamageDonePercent
            }
            data.WriteInt32(0); // ShieldBlock
            data.WriteFloat(0); // ShieldBlockCritPercentage
            data.WriteFloat(0); // Mastery
            data.WriteFloat(0); // Speed
            data.WriteFloat(0); // Avoidance
            data.WriteFloat(0); // Sturdiness
            data.WriteInt32(0); // Versatility
            data.WriteFloat(0); // VersatilityBonus
            data.WriteFloat(0); data.WriteFloat(0); // PvpPower

            // ExploredZones[240] (uint64 each)
            for (int i = 0; i < 240; i++)
                data.WriteUInt64(0);

            // RestInfo[2] (Threshold + StateID each)
            // StateID: 1=Rested, 2=Normal. 0 is INVALID and causes LUA crash!
            data.WriteUInt32(0);  // RestInfo[0].Threshold
            data.WriteUInt8(1);   // RestInfo[0].StateID = Rested
            data.WriteUInt32(0);  // RestInfo[1].Threshold
            data.WriteUInt8(1);   // RestInfo[1].StateID = Rested

            data.WriteInt32(0); // ModHealingDonePos
            data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); // Healing mods
            for (int i = 0; i < 3; i++) { data.WriteFloat(1.0f); data.WriteFloat(1.0f); } // WeaponDmg/AtkSpeed multipliers
            data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); // SpellPower/Resilience/Override
            data.WriteInt32(0); data.WriteInt32(0); // ModTargetResistance
            data.WriteUInt32(0); // LocalFlags
            data.WriteUInt8(0); data.WriteUInt8(0); data.WriteUInt8(0); data.WriteUInt8(0); // Bytes
            data.WriteInt32(0); // AmmoID
            data.WriteUInt32(0); // PvpMedals
            for (int i = 0; i < 12; i++) { data.WriteUInt32(0); data.WriteInt64(0); } // Buyback Price+Timestamp
            for (int i = 0; i < 8; i++) data.WriteUInt16(0); // Honor kills (8 x uint16)
            data.WriteUInt32(0); data.WriteUInt32(0); data.WriteUInt32(0); data.WriteUInt32(0); // Contribution/kills
            data.WriteUInt32(0); data.WriteUInt32(0); data.WriteUInt32(0); // More contribution
            data.WriteInt32((int)(active.WatchedFactionIndex ?? -1));
            for (int i = 0; i < 32; i++) data.WriteInt32(0); // CombatRatings
            data.WriteInt32(0); data.WriteInt32(0); data.WriteInt32(0); // MaxLevel, Scaling
            for (int i = 0; i < 4; i++) data.WriteUInt32(0); // NoReagentCostMask
            data.WriteInt32(0); // PetSpellPower
            data.WriteInt32(0); data.WriteInt32(0); // ProfessionSkillLine
            data.WriteFloat(0); data.WriteFloat(0); // UiHitModifier
            data.WriteInt32(0); data.WriteFloat(0); // HomeRealmTimeOffset, ModPetHaste
            data.WriteUInt8(0); data.WriteUInt8(0); data.WriteUInt8(0); // LocalRegenFlags, AuraVision, NumBackpackSlots
            data.WriteInt32(0); data.WriteInt32(0); // OverrideSpellsID, LfgBonusFactionID
            data.WriteUInt16(0); // LootSpecID
            data.WriteUInt32(0); // OverrideZonePVPType
            for (int i = 0; i < 4; i++) data.WriteUInt32(0); // BagSlotFlags
            for (int i = 0; i < 7; i++) data.WriteUInt32(0); // BankBagSlotFlags

            // QuestCompleted[875] (uint64 each)
            for (int i = 0; i < 875; i++)
                data.WriteUInt64(0);

            data.WriteInt32(0); data.WriteInt32(0); // Honor, HonorNextLevel
            data.WriteInt32(0); // Field_F74
            data.WriteInt32(0); // PvpTierMaxFromWins
            data.WriteInt32(0); // PvpLastWeeksTierMaxFromWins
            data.WriteUInt8(0); // PvpRankProgress
            data.WriteInt32(0); // PerksProgramCurrency

            // ResearchSites/Progress/Research (1 iteration, all empty)
            data.WriteUInt32(0); // ResearchSites[0].size
            data.WriteUInt32(0); // ResearchSiteProgress[0].size
            data.WriteUInt32(0); // Research[0].size
            // (no elements since all sizes=0)

            // Dynamic array sizes
            data.WriteUInt32(0); // DailyQuestsCompleted.size
            data.WriteUInt32(0); // AvailableQuestLineXQuestIDs.size
            data.WriteUInt32(0); // Field_1000.size
            data.WriteUInt32(0); // Heirlooms.size
            data.WriteUInt32(0); // HeirloomFlags.size
            data.WriteUInt32(0); // Toys.size
            data.WriteUInt32(0); // Transmog.size
            data.WriteUInt32(0); // ConditionalTransmog.size
            data.WriteUInt32(0); // SelfResSpells.size
            data.WriteUInt32(0); // CharacterRestrictions.size
            data.WriteUInt32(0); // SpellPctModByLabel.size
            data.WriteUInt32(0); // SpellFlatModByLabel.size
            data.WriteUInt32(0); // TaskQuests.size

            data.WriteInt32(0);  // TransportServerTime
            data.WriteUInt32(0); // TraitConfigs.size
            data.WriteUInt32(0); // ActiveCombatTraitConfigID

            // GlyphSlots[6] + Glyphs[6] - interleaved per TC
            for (int i = 0; i < 6; i++)
            {
                data.WriteUInt32(0); // GlyphSlots[i]
                data.WriteUInt32(0); // Glyphs[i]
            }
            data.WriteUInt8(0); // GlyphsEnabled
            data.WriteUInt8(0); // LfgRoles

            data.WriteUInt32(0); // CategoryCooldownMods.size
            data.WriteUInt32(0); // WeeklySpellUses.size

            data.WriteUInt8(0); // NumStableSlots

            // Dynamic array elements (KnownTitles already wrote size=0, so no elements)
            // All other dynamic arrays have size=0, no elements to write

            // PVPInfo[7] - exact TC PVPInfo::WriteCreate format
            for (int i = 0; i < 7; i++)
            {
                data.WriteInt8(0);   // Bracket
                data.WriteInt32(0);  // PvpRatingID
                data.WriteUInt32(0); // WeeklyPlayed
                data.WriteUInt32(0); // WeeklyWon
                data.WriteUInt32(0); // SeasonPlayed
                data.WriteUInt32(0); // SeasonWon
                data.WriteUInt32(0); // Rating
                data.WriteUInt32(0); // WeeklyBestRating
                data.WriteUInt32(0); // SeasonBestRating
                data.WriteUInt32(0); // PvpTierID
                data.WriteUInt32(0); // WeeklyBestWinPvpTierID
                data.WriteUInt32(0); // Field_28
                data.WriteUInt32(0); // Field_2C
                data.WriteUInt32(0); // WeeklyRoundsPlayed
                data.WriteUInt32(0); // WeeklyRoundsWon
                data.WriteUInt32(0); // SeasonRoundsPlayed
                data.WriteUInt32(0); // SeasonRoundsWon
                data.WriteBit(false); // Disqualified
                data.FlushBits();
            }

            // End bits
            data.FlushBits();
            data.WriteBit(false); // SortBagsRightToLeft
            data.WriteBit(false); // InsertItemsLeftToRight
            data.WriteBit(false); // PetStable.has_value()
            data.FlushBits();

            // ResearchHistory::WriteCreate (empty)
            data.WriteUInt32(0); // CompletedProjects.size

            // FrozenPerksVendorItem - exact TC format
            data.WriteInt32(0);  // VendorItemID
            data.WriteInt32(0);  // MountID
            data.WriteInt32(0);  // BattlePetSpeciesID
            data.WriteInt32(0);  // TransmogSetID
            data.WriteInt32(0);  // ItemModifiedAppearanceID
            data.WriteInt32(0);  // Field_14
            data.WriteInt32(0);  // Field_18
            data.WriteInt32(0);  // Price
            data.WriteInt64(0);  // AvailableUntil (Timestamp)
            data.WriteBit(false); // Disabled
            data.FlushBits();

            // CharacterRestrictions elements (size=0, none)
            // TraitConfigs elements (size=0, none)
            // PetStable (has_value=false, not written)

            data.FlushBits();
        }
        #endregion

        #region GameObjectData
        private void WriteCreateGameObjectData(WorldPacket data)
        {
            GameObjectData go = m_updateData.GameObjectData ?? new GameObjectData();

            // Exact TC GameObjectData::WriteCreate field order
            data.WriteInt32((int)(go.DisplayID ?? 0));                      // DisplayID
            data.WriteUInt32((uint)(go.SpellVisualID ?? 0));                // SpellVisualID
            data.WriteUInt32((uint)(go.StateSpellVisualID ?? 0));           // StateSpellVisualID
            data.WriteUInt32((uint)(go.StateAnimID ?? 0));                  // SpawnTrackingStateAnimID
            data.WriteUInt32((uint)(go.StateAnimKitID ?? 0));               // SpawnTrackingStateAnimKitID
            data.WriteUInt32(0);                                            // StateWorldEffectIDs.size()
            // (no elements since size=0)

            data.WritePackedGuid128(go.CreatedBy ?? WowGuid128.Empty);      // CreatedBy
            data.WritePackedGuid128(WowGuid128.Empty);                      // GuildGUID
            data.WriteUInt32((uint)(go.Flags ?? 0));                        // Flags

            // ParentRotation (4 floats)
            if (m_updateData.CreateData?.MoveInfo?.Rotation != null)
            {
                var rot = m_updateData.CreateData.MoveInfo.Rotation;
                data.WriteFloat(rot.X);
                data.WriteFloat(rot.Y);
                data.WriteFloat(rot.Z);
                data.WriteFloat(rot.W);
            }
            else
            {
                data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(0); data.WriteFloat(1.0f);
            }

            data.WriteInt32((int)(go.FactionTemplate ?? 0));                // FactionTemplate
            data.WriteInt32((int)(go.Level ?? 0));                          // Level
            data.WriteInt8((sbyte)(go.State ?? 0));                         // State (int8)
            data.WriteInt8((sbyte)(go.TypeID ?? 0));                        // TypeID (int8)
            data.WriteUInt8((byte)(go.PercentHealth ?? 100));               // PercentHealth (uint8)
            data.WriteUInt32((uint)(go.ArtKit ?? 0));                       // ArtKit (uint32, NOT packed byte!)
            data.WriteUInt32(0);                                            // EnableDoodadSets.size()
            data.WriteUInt32((uint)(go.CustomParam ?? 0));                  // CustomParam
            data.WriteUInt32(0);                                            // WorldEffects.size()
            // (no elements for EnableDoodadSets or WorldEffects since size=0)
        }
        #endregion

        #region DynamicObjectData
        private void WriteCreateDynamicObjectData(WorldPacket data)
        {
            DynamicObjectData dyn = m_updateData.DynamicObjectData ?? new DynamicObjectData();

            data.WritePackedGuid128(dyn.Caster ?? WowGuid128.Empty);
            data.WriteUInt8(0); // Type
            data.WriteInt32(0); // SpellXSpellVisualID
            data.WriteInt32((int)(dyn.SpellID ?? 0));
            data.WriteFloat((float)(dyn.Radius ?? 0));
            data.WriteUInt32((uint)(dyn.CastTime ?? 0));
        }
        #endregion

        #region CorpseData
        private void WriteCreateCorpseData(WorldPacket data)
        {
            CorpseData corpse = m_updateData.CorpseData ?? new CorpseData();

            data.WritePackedGuid128(corpse.Owner ?? WowGuid128.Empty);
            data.WritePackedGuid128(corpse.PartyGUID ?? WowGuid128.Empty);
            data.WritePackedGuid128(corpse.GuildGUID ?? WowGuid128.Empty);
            data.WriteUInt32((uint)(corpse.DisplayID ?? 0));
            for (int i = 0; i < 19; i++)
                data.WriteUInt32((uint)(corpse.Items?[i] ?? 0));
            data.WriteUInt8((byte)(corpse.RaceId ?? 0));
            data.WriteUInt8((byte)(corpse.SexId ?? 0));
            data.WriteUInt8((byte)(corpse.ClassId ?? 0));
            data.WriteUInt8(0); // Padding
            data.WriteUInt32((uint)(corpse.Flags ?? 0));
            data.WriteUInt32((uint)(corpse.DynamicFlags ?? 0));
            data.WriteInt32((int)(corpse.FactionTemplate ?? 0));
            data.WriteUInt32(0); // Customizations count
        }
        #endregion

        #region Movement
        public void SetCreateObjectBits()
        {
            m_createBits.Clear();
            m_createBits.PlayHoverAnim = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && m_updateData.CreateData.MoveInfo.Hover;
            m_createBits.MovementUpdate = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit);
            m_createBits.Stationary = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && !m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit);
            // TC 3.4.3: MovementTransport is for objects RIDING on a transport (not for transport objects themselves)
            // GameObjects that ARE transports use ServerTime flag instead
            m_createBits.MovementTransport = false; // GameObjects don't ride on transports
            m_createBits.Stationary = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && !m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit);
            m_createBits.ServerTime = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && m_updateData.Guid.GetHighType() == Enums.HighGuidType.Transport;
            m_createBits.CombatVictim = m_updateData.CreateData != null && m_updateData.CreateData.AutoAttackVictim != null;
            m_createBits.Vehicle = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && m_updateData.CreateData.MoveInfo.VehicleId != 0;
            m_createBits.Rotation = m_updateData.CreateData != null & m_updateData.CreateData.MoveInfo != null && m_objectType == Enums.ObjectTypeBCC.GameObject;
            m_createBits.GameObject = m_objectType == Enums.ObjectTypeBCC.GameObject;
            m_createBits.ThisIsYou = m_createBits.ActivePlayer = m_objectType == Enums.ObjectTypeBCC.ActivePlayer;
        }

        public void BuildMovementUpdate(WorldPacket data)
        {
            int PauseTimesCount = 0;

            data.WriteBit(m_createBits.NoBirthAnim);
            data.WriteBit(m_createBits.EnablePortals);
            data.WriteBit(m_createBits.PlayHoverAnim);
            data.WriteBit(m_createBits.MovementUpdate);
            data.WriteBit(m_createBits.MovementTransport);
            data.WriteBit(m_createBits.Stationary);
            data.WriteBit(m_createBits.CombatVictim);
            data.WriteBit(m_createBits.ServerTime);
            data.WriteBit(m_createBits.Vehicle);
            data.WriteBit(m_createBits.AnimKit);
            data.WriteBit(m_createBits.Rotation);
            data.WriteBit(m_createBits.AreaTrigger);
            data.WriteBit(m_createBits.GameObject);
            data.WriteBit(m_createBits.SmoothPhasing);
            data.WriteBit(m_createBits.ThisIsYou);
            data.WriteBit(m_createBits.SceneObject);
            data.WriteBit(m_createBits.ActivePlayer);
            data.WriteBit(m_createBits.Conversation);
            data.FlushBits();

            if (m_createBits.MovementUpdate)
            {
                MovementInfo moveInfo = m_updateData.CreateData.MoveInfo;
                bool hasSpline = m_updateData.CreateData.MoveSpline != null;

                int beforeMove = data.GetData().Length;
                moveInfo.WriteMovementInfoModern(data, m_updateData.Guid);
                int afterMoveInfo = data.GetData().Length;
                Log.Print(LogType.Debug, $"[Movement] MoveInfo={afterMoveInfo-beforeMove}b Speeds: Walk={moveInfo.WalkSpeed} Run={moveInfo.RunSpeed} Swim={moveInfo.SwimSpeed} Flight={moveInfo.FlightSpeed} Turn={moveInfo.TurnRate} Pitch={moveInfo.PitchRate}");

                data.WriteFloat(moveInfo.WalkSpeed);
                data.WriteFloat(moveInfo.RunSpeed);
                data.WriteFloat(moveInfo.RunBackSpeed);
                data.WriteFloat(moveInfo.SwimSpeed);
                data.WriteFloat(moveInfo.SwimBackSpeed);
                data.WriteFloat(moveInfo.FlightSpeed);
                data.WriteFloat(moveInfo.FlightBackSpeed);
                data.WriteFloat(moveInfo.TurnRate);
                data.WriteFloat(moveInfo.PitchRate);

                data.WriteUInt32(0);     // MovementForces count
                data.WriteFloat(1.0f);   // MovementForcesModMagnitude

                // 17 advanced flying parameter floats - TC default values (MUST be non-zero!)
                data.WriteFloat(2.0f);     // AdvFlyingAirFriction
                data.WriteFloat(65.0f);    // AdvFlyingMaxVel
                data.WriteFloat(1.0f);     // AdvFlyingLiftCoefficient
                data.WriteFloat(3.0f);     // AdvFlyingDoubleJumpVelMod
                data.WriteFloat(10.0f);    // AdvFlyingGlideStartMinHeight
                data.WriteFloat(100.0f);   // AdvFlyingAddImpulseMaxSpeed
                data.WriteFloat(90.0f);    // AdvFlyingMinBankingRate
                data.WriteFloat(140.0f);   // AdvFlyingMaxBankingRate
                data.WriteFloat(180.0f);   // AdvFlyingMinPitchingRateDown
                data.WriteFloat(360.0f);   // AdvFlyingMaxPitchingRateDown
                data.WriteFloat(90.0f);    // AdvFlyingMinPitchingRateUp
                data.WriteFloat(270.0f);   // AdvFlyingMaxPitchingRateUp
                data.WriteFloat(30.0f);    // AdvFlyingMinTurnVelocityThreshold
                data.WriteFloat(80.0f);    // AdvFlyingMaxTurnVelocityThreshold
                data.WriteFloat(2.75f);    // AdvFlyingSurfaceFriction
                data.WriteFloat(7.0f);     // AdvFlyingOverMaxDeceleration
                data.WriteFloat(0.4f);     // AdvFlyingLaunchSpeedCoefficient

                data.WriteBit(hasSpline);
                data.FlushBits();

                if (hasSpline)
                    WriteCreateObjectSplineDataBlock(m_updateData.CreateData.MoveSpline, data);
            }

            data.WriteInt32(PauseTimesCount);

            if (m_createBits.Stationary)
            {
                data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.X);
                data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.Y);
                data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.Z);
                data.WriteFloat(m_updateData.CreateData.MoveInfo.Orientation);
            }

            if (m_createBits.CombatVictim)
                data.WritePackedGuid128(m_updateData.CreateData.AutoAttackVictim);

            if (m_createBits.ServerTime)
            {
                if (m_updateData.CreateData.MoveInfo.TransportPathTimer != 0)
                    data.WriteUInt32(m_updateData.CreateData.MoveInfo.TransportPathTimer);
                else
                    data.WriteUInt32((uint)Time.UnixTime);
            }

            if (m_createBits.Vehicle)
            {
                data.WriteUInt32(m_updateData.CreateData.MoveInfo.VehicleId);
                data.WriteFloat(m_updateData.CreateData.MoveInfo.VehicleOrientation);
            }

            if (m_createBits.AnimKit)
            {
                data.WriteUInt16(0);
                data.WriteUInt16(0);
                data.WriteUInt16(0);
            }

            if (m_createBits.Rotation)
                data.WriteInt64(m_updateData.CreateData.MoveInfo.Rotation.GetPackedRotation());

            for (int i = 0; i < PauseTimesCount; ++i)
                data.WriteUInt32(0);

            if (m_createBits.MovementTransport)
                m_updateData.CreateData.MoveInfo.WriteTransportInfoModern(data);

            if (m_createBits.GameObject)
            {
                data.WriteUInt32(0); // WorldEffectID
                data.WriteBit(false);
                data.FlushBits();
            }

            if (m_createBits.ActivePlayer)
            {
                bool hasSceneInstanceIDs = false;
                bool hasRuneState = false;
                bool hasActionButtons = true; // TC always sends action buttons for player

                data.WriteBit(hasSceneInstanceIDs);
                data.WriteBit(hasRuneState);
                data.WriteBit(hasActionButtons);
                data.FlushBits();

                // Always write 180 action buttons (TC always does)
                for (int i = 0; i < 180; i++)
                    data.WriteInt32(i < m_gameState.ActionButtons.Count ? m_gameState.ActionButtons[i] : 0);
            }
        }

        public static void WriteCreateObjectSplineDataBlock(ServerSideMovement moveSpline, WorldPacket data)
        {
            data.WriteUInt32(moveSpline.SplineId);

            if (!moveSpline.SplineFlags.HasAnyFlag(Enums.SplineFlagModern.Cyclic))
                data.WriteVector3(moveSpline.EndPosition);
            else
                data.WriteVector3(Vector3.Zero);

            bool hasSplineMove = data.WriteBit(moveSpline.SplineCount != 0);
            data.FlushBits();

            if (hasSplineMove)
            {
                data.WriteUInt32((uint)moveSpline.SplineFlags);
                data.WriteUInt32(moveSpline.SplineTime);
                data.WriteUInt32(moveSpline.SplineTimeFull);
                data.WriteFloat(1.0f);
                data.WriteFloat(1.0f);
                data.WriteBits((byte)moveSpline.SplineType, 2);
                bool hasFadeObjectTime = data.WriteBit(false);
                data.WriteBits(moveSpline.SplineCount, 16);
                data.WriteBit(false); // HasSplineFilter
                data.WriteBit(false); // HasSpellEffectExtraData
                data.WriteBit(false); // HasJumpExtraData
                data.WriteBit(false); // HasAnimationTierTransition
                data.FlushBits();

                switch (moveSpline.SplineType)
                {
                    case Enums.SplineTypeModern.FacingSpot:
                        data.WriteVector3(moveSpline.FinalFacingSpot);
                        break;
                    case Enums.SplineTypeModern.FacingTarget:
                        data.WriteFloat(moveSpline.FinalOrientation);
                        data.WritePackedGuid128(moveSpline.FinalFacingGuid);
                        break;
                    case Enums.SplineTypeModern.FacingAngle:
                        data.WriteFloat(moveSpline.FinalOrientation);
                        break;
                }

                if (hasFadeObjectTime)
                    data.WriteInt32(0);

                foreach (var vec in moveSpline.SplinePoints)
                    data.WriteVector3(vec);
            }
        }
        #endregion
    }
}
