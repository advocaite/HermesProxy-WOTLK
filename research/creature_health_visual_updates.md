# Creature Health Visual Update Research

## Problem
- Creatures die server-side (become unattackable) but health bars don't visually update on 3.4.3 client
- Works for first 1-2 kills after login, then stops working
- Pre-existing issue in the decompiled build, not introduced by our changes

## How Creature Health Updates Flow

### Step 1: AzerothCore sends Health change
- `Unit::SetHealth()` modifies `UNIT_FIELD_HEALTH` update field
- This marks the field as changed in the update mask
- On next update tick, server sends `SMSG_UPDATE_OBJECT` or `SMSG_COMPRESSED_UPDATE_OBJECT`
- The packet contains a Values update with the creature's GUID and changed fields

### Step 2: HermesProxy reads the legacy Values update
- `WorldClient.HandleUpdateObject()` (WorldClient.cs:8710)
- For `UpdateTypeLegacy.Values`: reads GUID, creates `ObjectUpdate` with `UpdateTypeModern.Values`
- `ReadValuesUpdateBlock()` reads the update mask and field values
- For UNIT_FIELD_HEALTH: sets `updateData.UnitData.Health = value` (WorldClient.cs:10100)
- The ObjectUpdate is added to `updateObject.ObjectUpdates`

### Step 3: HermesProxy writes the modern Values update
- `ObjectUpdateBuilder.WriteValuesUpdate()` (ObjectUpdateBuilder.cs:258)
- `hasUnitChanges = UnitData != null` — **ALWAYS true** for creatures (constructor creates UnitData)
- `changedMask |= 0x20` (Unit) — always set for creatures
- `WriteUpdateUnitData()` writes the Unit block with Health at bit 5

### Step 4: Client receives and processes
- Client reads changedMask, sees Unit bit (0x20)
- Reads Unit block, finds Health field, updates the health bar

## CONFIRMED: hasAny Bit Bug (FIXED)

The original decompiled code only set the hasAny bit (bit 0) for blocks 0 and 1 in `WriteUpdateUnitData`:
```csharp
if (blockMasks[0] != 0) blockMasks[0] |= 1u;
if (blockMasks[1] != 0) blockMasks[1] |= 1u;
// MISSING: blocks 2-7!
```

**Fix:** Loop all 8 blocks:
```csharp
for (int bi = 0; bi < 8; bi++)
    if (blockMasks[bi] != 0) blockMasks[bi] |= 1u;
```

This fixed creature death and player combat (Health in block 0, Power in block 4).

## CONFIRMED: Player ObjectData.DynamicFlags Crashes on Loot

Sending ObjectData (DynamicFlags) for the player during loot causes DC. Emptying ObjectData for the player prevents this. The server sends DynamicFlags=0 for the player during loot state changes.

## Current Working State
- Player Unit data (Health, Power, Flags): works with hasAny fix
- Player ObjectData: must be emptied (crashes on loot)
- Player PlayerData: must be nulled (format not implemented)
- Player ActivePlayerData: must be nulled (crashes)
- Creature Object+Unit: fully works

## Remaining Issues
- XP, gold, bags need ActivePlayerData/PlayerData
- Intermittent loot DC (first body works, second sometimes DCs)
- Block 1 (Flags/Flags2/AuraState) for the player may have legacy values that crash

## Root Cause Theory: Player Values Updates Corrupt Client State

The ObjectUpdate constructor creates `UnitData = new UnitData()` for BOTH creatures AND the player (ObjectType.Player/ActivePlayer). This means:

For EVERY player Values update from the server:
1. `hasUnitChanges = true` (UnitData is not null)
2. `changedMask |= 0x20` (Unit block included)
3. WriteUpdateUnitData writes a Unit block

But the player's UnitData may have NO actual fields set (the server only sent quest log or DynamicFlags changes). The Unit block writes `blocksMask = 0` (empty). The client receives a Unit update for the player with no data.

**After the first player Values update goes through with an empty Unit block, the client may enter a corrupted state** where it stops processing creature Unit updates correctly. This explains the intermittent behavior: first kills work (before any player Values update), then after a player state change triggers a Values update, creature health stops updating.

## Evidence
- First 2 wolves die (before any player Values update triggered)
- Third wolf doesn't die (after combat XP/loot/quest triggers a player Values update)
- When player Values updates were filtered entirely, creatures died but player health didn't update
- The decompiled code always had this issue — `UnitData != null` always sends Unit blocks for player Values updates

## Fix Options

### Option A: Don't write Unit block when no actual unit fields changed
Change `hasUnitChanges` to check for actual field values, not just UnitData existence.
This prevents empty Unit blocks for both players and creatures.
Risk: creatures with ONLY non-checked unit field changes would lose those changes.

### Option B: Filter player Values updates entirely
Skip adding player Values updates to the UpdateObject.
Pro: prevents any player-related corruption
Con: player health/power bars don't update from Values updates (but SMSG_POWER_UPDATE exists)

### Option C: Only write Unit block for player when there are actual unit changes
Keep `UnitData != null` for creatures but use a field check for the player GUID.
Most surgical approach.

## Key Files
- ObjectUpdateBuilder.cs:258 — WriteValuesUpdate
- ObjectUpdateBuilder.cs:684 — WriteUpdateUnitData (Health at bit 5)
- ObjectUpdate.cs:52-54 — UnitData created for all Unit types
- WorldClient.cs:8710 — HandleUpdateObject
- WorldClient.cs:10097-10101 — Health field read from legacy update
- UpdateObject.cs:56-87 — Login buffering system (not related to this issue)

## Key Bit Positions in WriteUpdateUnitData
- Bit 5: Health (int64)
- Bit 6: MaxHealth (int64)
- Bit 7: DisplayID (int32)
- Bit 19: Target (PackedGuid128)
- Bit 22: ChannelData
- Bit 24: RaceId (uint8)
- Bit 30: Level (int32)
- Bit 40: FactionTemplate (int32)
- Bit 41: Flags (uint32)
