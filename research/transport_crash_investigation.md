# Transport Crash Investigation

## Problem
Transport objects (boats, zeppelins, elevators) crash the 3.4.3 client on login. 
Currently filtered out in UpdateObject.cs with debug logging.

## What We Know

### Transport Data From Server (logged 2026-04-06)
```
[Transport] Full: 0x18000300000000000000000000000000 Transport/0 R0/S0 Map: 6144 Low: 12
  Type=CreateObject1
  GO: DisplayID=7546 TypeID=null State=null Flags=40
  MoveInfo: Pos=(1372.0128, -3851.5461, 154.4312) Rot=(1, 0, 0, 0) PathTimer=28299391 TransportGuid=null
```

### Issues Found
1. **TypeID = null** — Not populated from legacy server. Must be 11 (GAMEOBJECT_TYPE_TRANSPORT) or 15 (MO_TRANSPORT). Written as 0 (int8) which means DOOR type — client misinterprets the object.
2. **State = null** — Not populated. Should be GO_STATE_READY (0) or GO_STATE_ACTIVE (1).  
3. **Rotation = (1, 0, 0, 0)** — X=1 instead of W=1. Identity quaternion should be (0, 0, 0, 1). Component order is wrong in the legacy→modern conversion.
4. **TransportGuid = null** — This is correct (the object IS the transport, not riding one).

### Code Locations

#### Filter + logging (to remove once fixed):
- `UpdateObject.cs:~100` — `RemoveAll` filter + `[Transport]` debug log

#### SetCreateObjectBits (already fixed):
- `ObjectUpdateBuilder.cs:2165` — MovementTransport now checks TransportGuid + GameObject type
- `ObjectUpdateBuilder.cs:2167` — ServerTime now checks both HighGuidType.Transport and MOTransport

#### GameObjectData WriteCreate:
- `ObjectUpdateBuilder.cs:2087` — WriteCreateGameObjectData — format matches TC343 field-by-field
- TC343 reference: `UpdateFields.cpp:4272` — GameObjectData::WriteCreate

#### Legacy GameObject field reading (WHERE THE FIX IS NEEDED):
- `WorldClient.cs` — ReadValuesUpdateBlock for GameObjects, search for `GAMEOBJECT_TYPE_ID`, `GAMEOBJECT_STATE`, `GAMEOBJECT_ROTATION`
- Need to find where GameObjectData.TypeID, State, and Rotation are populated from legacy 3.3.5a fields

#### Legacy field definitions:
- `Enums/V3_3_5a_12340/` — look for GameObjectField enum, find GAMEOBJECT_TYPE_ID, GAMEOBJECT_FLAGS, GAMEOBJECT_STATE (likely in GAMEOBJECT_BYTES_1)
- AzerothCore: `F:/Ampps/azerothcore-wotlk-master/src/server/game/Entities/GameObject/GameObject.cpp` — search for GAMEOBJECT_BYTES_1 packing format
- In 3.3.5a, GAMEOBJECT_BYTES_1 packs: State (byte 0), TypeID (byte 1), ArtKit (byte 2), AnimProgress (byte 3)

#### Rotation quaternion:
- Legacy sends rotation as int64 packed rotation (GAMEOBJECT_ROTATION field) 
- Also has GAMEOBJECT_PARENTROTATION (4 floats) for transport parent rotation
- Need to verify component order: legacy might be (X,Y,Z,W) but stored/read as (W,X,Y,Z) or similar
- TC343 ParentRotation in WriteCreate is (X,Y,Z,W) order
- MoveInfo.Rotation is a Quaternion — check if X/Y/Z/W mapping matches between legacy read and modern write

### AzerothCore References
- `F:/Ampps/azerothcore-wotlk-master/src/server/game/Entities/Transport/Transport.cpp` — MotionTransport/StaticTransport constructors
- `F:/Ampps/azerothcore-wotlk-master/src/server/game/Entities/Object/Object.cpp:~458` — UPDATEFLAG_TRANSPORT writes uint32 PathProgress
- m_updateFlag for transports = UPDATEFLAG_TRANSPORT | UPDATEFLAG_LOWGUID | UPDATEFLAG_STATIONARY_POSITION | UPDATEFLAG_ROTATION

### TC343 References  
- `f:/Ampps/TC343/src/server/game/Entities/Transport/Transport.cpp:92` — m_updateFlag.ServerTime = true
- `f:/Ampps/TC343/src/server/game/Entities/Object/Object.cpp:410` — ServerTime writes GameTime::GetGameTimeMS()
- `f:/Ampps/TC343/src/server/game/Entities/Object/Updates/UpdateFields.cpp:4272` — GameObjectData::WriteCreate

## Fix Plan
1. Find where legacy GAMEOBJECT_BYTES_1 is read — extract TypeID (byte 1) and State (byte 0) into GameObjectData
2. Fix Rotation quaternion component order (likely just swap X↔W or reorder)
3. Remove transport filter from UpdateObject.cs
4. Test login near a transport (boat dock, zeppelin tower, elevator)
