# Death/Revive Visual Investigation (2026-04-08)

## Current Working State
- Ghost wisp model appears on death (from aura 8326) ✓
- Wisp clears on revive (aura removal at slot 0) ✓
- Health/Power restore on revive ✓
- Grey ghost overlay does NOT appear (PlayerFlags.Ghost 0x10 stripped)
- Grey ghost overlay CANNOT be cleared via Values updates — only relog fixes it

## What Controls What
- **Aura 8326** → ghost wisp character model. SET/REMOVE works correctly.
- **PlayerFlags.Ghost (0x10)** → grey ghost world overlay. SET works, but CLEAR via Values update does NOT work. Only clears on relog (CreateObject from scratch).
- **PRE_RESSURECT packet** → death animation/UI. Works independently.
- **ActivePlayerData.LocalFlags RELEASE_TIMER (0x08)** → death release timer UI. 3.4.3 specific, 3.3.5a server doesn't set it.
- **PLAYER_FLAGS_IS_OUT_OF_BOUNDS (0x4000)** → void death only (falling off map). Prevents all movement. NOT for normal ghost state.

## What Was Tested (all failed to clear grey overlay)
1. ❌ Sending PlayerFlags=0 on revive (Values update)
2. ❌ Full aura clear (UpdateAll=true, empty aura list)
3. ❌ Aura 8326 removal (slot 0 REMOVE) — removal IS sent correctly
4. ❌ SMSG_PRE_RESSURECT suppression
5. ❌ ActivePlayerData.LocalFlags = 0 on revive
6. ❌ IS_OUT_OF_BOUNDS (0x4000) — locks movement completely
7. ❌ LoginVerifyWorld on revive
8. ❌ DestroyObject + MSG_MOVE_WORLDPORT_ACK — broke server state
9. ❌ ObjectData DynamicFlags pass-through
10. ❌ No field stripping at all
11. ❌ Disabling player/creature packet splitting

## What Works
- ✓ Relog (full disconnect/reconnect) — CreateObject rebuilds player from scratch
- ✓ Stripping PlayerFlags.Ghost (0x10) — prevents grey overlay from being set

## Root Cause Theory
The 3.4.3 client has an internal ghost state machine that enters grey overlay mode when PlayerFlags.Ghost is set. This state can only exit when the player object is fully re-created (CreateObject), not via incremental Values updates. The client's PlayerFlags change handler for Ghost doesn't trigger visual re-evaluation.

## Possible Future Fix
Force a player object DestroyObject + CreateObject2 on revive. This requires building a full CreateObject from cached state including:
- Movement data (position, speeds, flags)
- Full UnitData, PlayerData, ActivePlayerData
- Object type and create bits

This is complex but would mimic the relog behavior.

## Key Files
- WorldClient.cs: HandleDeathReleaseLoc, HandlePreResurrect, ReadSingleAura, PlayerFlags conversion
- PreRessurect.cs: SMSG_PRE_RESSURECT packet class
- V3_4_3_54261/Opcode.cs: SMSG_PRE_RESSURECT = 0x276B
- AzerothCore Player.cpp: BuildPlayerRepop (death), ResurrectPlayer (revive)
- TC343 Player.cpp: ResurrectPlayer, PLAYER_FLAGS_IS_OUT_OF_BOUNDS
