# Player Values Update Crash Investigation

## Problem
Player Unit blocks 0+1 in Values updates crash the 3.4.3 client (reason 7 DC).
Block 4 alone (Power) works fine for the player.
Blocks 0+1 work fine for creatures.

## What We Know

### Working Configurations
- Player: changedMask=0x20, Unit blocksMask=0x10 (block 4 only — Power) ✓
- Player: changedMask=0x00 (empty update, all nulled) ✓  
- Creature: changedMask=0x21, Unit blocksMask=0x03 (blocks 0+1 — Health+Flags) ✓
- Creature: changedMask=0x01, Object only (DynamicFlags) ✓

### Crashing Configurations
- Player: changedMask=0x21, Unit blocksMask=0x13 (blocks 0+1+4) ✗
- Player: changedMask=0x21, Object+Unit any data ✗
- Player: changedMask=0x20, Unit blocksMask=0x13 (blocks 0+1+4) ✗

### Key Finding: hasAny bit fix
Original decompiled code only set hasAny (bit 0) for blocks 0 and 1.
Fix: set hasAny for ALL 8 blocks. This fixed creature death and player block 4.
But blocks 0+1 for the player still crash.

## TC343 Analysis

### UnitData::WriteUpdate format (TC343 UpdateFields.cpp:876)
1. WriteBits(blocksMask, 8) — which blocks have data
2. For each set block: WriteBits(blockMask[i], 32)
3. if (changesMask[0]): check bit 1 (StateWorldEffectIDs) — write size+data if set
4. FlushBits
5. if (changesMask[0]): check bits 2-4 (PassiveSpells/WorldEffects/ChannelObjects) — write update masks
6. FlushBits
7. Write scalar fields for each block

### Our Code
- Steps 3 and 5 are EMPTY (no dynamic field support)
- Two FlushBits at same position
- Should produce same output when bits 1-4 aren't set

### Bit Positions Verified Against TC343
- Health=5, MaxHealth=6, DisplayID=7 (block 0)
- Flags=41, Flags2=42, AuraState=44 (block 1)
- Power=137+, MaxPower=147+ (block 4)
All match TC343.

## Theories

### Theory 1: Client validates player updates differently
The client knows the GUID is a Player and may use a different code path for parsing.
Maybe it expects PlayerData (0x40) to ALWAYS be present in the changedMask for player objects.

### Theory 2: Bit packing difference
WriteBits in HermesProxy uses MSB-first. TC343 might use a different order.
This would only matter when the total bits aren't byte-aligned, causing FlushBits padding differences.
But 8 + 3×32 = 104 bits = 13 bytes (aligned), so this shouldn't matter for blocks 0+1+4.

### Theory 3: Missing dynamic fields cause offset shift
TC343's block 0 checks bits 1-4 for dynamic fields. Even though we don't set those bits,
maybe the CLIENT expects some minimum structure when block 0's hasAny is set.
Unlikely since bits 1-4 being 0 means "no dynamic data."

### Theory 4: Object block for player has wrong DynamicFlags
DynamicFlags=0 sent for player during loot clears important flags.
Emptying ObjectData prevents this crash. But block 0+1 Unit data alone also crashes.

### Theory 5: Bag/inventory state not initialized properly
If ActivePlayerData inventory slots are wrong on login, any subsequent update
that interacts with inventory (loot) could crash. The initial create DOES populate
InvSlots from 3.3.5a data, so this seems unlikely but needs verification.

## Next Steps
1. Binary dump comparison: capture exact bytes for creature blocks 0+1 vs player blocks 0+1
2. Check if TC343 WriteBits uses same bit ordering as HermesProxy
3. Try adding a minimal PlayerData block (empty, with IsQuestLogChangesMaskSkipped bit) when block 0+1 are present for the player
4. Check if the client expects changedMask to include 0x40 (Player) for any player Values update

## Current Workaround
Strip ObjectData, PlayerData, ActivePlayerData for the player.
Keep UnitData (block 4 Power works, blocks 0+1 stripped).
Health comes from combat packets, not Values updates.

## Files
- ObjectUpdateBuilder.cs:258 — WriteValuesUpdate  
- ObjectUpdateBuilder.cs:684 — WriteUpdateUnitData
- ObjectUpdateBuilder.cs:855 — hasAny fix (all 8 blocks)
- WorldClient.cs:8713 — player Values update stripping
- TC343 UpdateFields.cpp:876 — UnitData::WriteUpdate reference
