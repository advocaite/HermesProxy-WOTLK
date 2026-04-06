using System;
using System.Collections.Generic;
using System.IO;
using Framework.Constants;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects.Version.V1_14_0_40237;
using HermesProxy.World.Objects.Version.V1_14_1_40688;
using HermesProxy.World.Objects.Version.V2_5_2_39570;
using HermesProxy.World.Objects.Version.V2_5_3_41750;
using HermesProxy.World.Objects.Version.V3_4_3_54261;

namespace HermesProxy.World.Server.Packets;

public class UpdateObject : ServerPacket
{
	[ThreadStatic]
	private static List<ObjectUpdate> _pendingLoginUpdates;

	[ThreadStatic]
	private static List<WowGuid128> _pendingLoginDestroys;

	[ThreadStatic]
	private static bool _playerObjectSent;

	public static bool IsPlayerObjectSent => _playerObjectSent;

	private GameSessionData _gameState;

	public uint NumObjUpdates;

	public ushort MapID;

	public byte[] Data;

	public List<WowGuid128> OutOfRangeGuids = new List<WowGuid128>();

	public List<WowGuid128> DestroyedGuids = new List<WowGuid128>();

	public List<ObjectUpdate> ObjectUpdates = new List<ObjectUpdate>();

	public UpdateObject(GameSessionData gameState)
		: base(Opcode.SMSG_UPDATE_OBJECT, ConnectionType.Instance)
	{
		this._gameState = gameState;
	}

	public static void ResetLoginBuffer()
	{
		UpdateObject._pendingLoginUpdates = new List<ObjectUpdate>();
		UpdateObject._pendingLoginDestroys = new List<WowGuid128>();
		UpdateObject._playerObjectSent = false;
	}

	public override void Write()
	{
		if (ModernVersion.ExpansionVersion >= 3 && !UpdateObject._playerObjectSent)
		{
			Log.Print(LogType.Debug, $"[UpdateObject] _playerObjectSent=false, checking for player in {this.ObjectUpdates.Count} updates", "Write", "");
		}
		if (ModernVersion.ExpansionVersion >= 3 && !UpdateObject._playerObjectSent)
		{
			if (UpdateObject._pendingLoginUpdates == null)
			{
				UpdateObject.ResetLoginBuffer();
			}
			bool hasPlayer = false;
			foreach (ObjectUpdate update in this.ObjectUpdates)
			{
				if (update.Guid == this._gameState.CurrentPlayerGuid)
				{
					hasPlayer = true;
					break;
				}
			}
			if (!hasPlayer && this.ObjectUpdates.Count > 0)
			{
				UpdateObject._pendingLoginUpdates.AddRange(this.ObjectUpdates);
				UpdateObject._pendingLoginDestroys.AddRange(this.DestroyedGuids);
				Log.Print(LogType.Debug, $"[UpdateObject] Buffering {this.ObjectUpdates.Count} updates (total: {UpdateObject._pendingLoginUpdates.Count})", "Write", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\Packets\\UpdatePackets.cs");
				base.SkipSend = true;
				return;
			}
			if (hasPlayer)
			{
				List<ObjectUpdate> merged = new List<ObjectUpdate>(UpdateObject._pendingLoginUpdates);
				merged.AddRange(this.ObjectUpdates);
				this.ObjectUpdates = merged;
				this.DestroyedGuids.AddRange(UpdateObject._pendingLoginDestroys);
				UpdateObject._playerObjectSent = true;
				Log.Print(LogType.Debug, $"[UpdateObject] Merged {UpdateObject._pendingLoginUpdates.Count} buffered + sending {this.ObjectUpdates.Count} total updates", "Write", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\Packets\\UpdatePackets.cs");
			}
		}
		this.MapID = (ushort)this._gameState.CurrentMapId.Value;
		if (HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder.DEBUG_SKIP_GAMEOBJECTS)
		{
			this.ObjectUpdates.RemoveAll((ObjectUpdate u) => u.Guid.GetObjectType() == ObjectType.GameObject);
		}
		// Transport debug logging + selective filter to isolate crash
		if (ModernVersion.ExpansionVersion >= 3)
		{
			foreach (var upd in this.ObjectUpdates)
			{
				if (upd.Guid.GetHighType() == HighGuidType.Transport || upd.Guid.GetHighType() == HighGuidType.MOTransport)
				{
					var go = upd.GameObjectData;
					var mi = upd.CreateData?.MoveInfo;
					var od = upd.ObjectData;
					Log.Print(LogType.Debug, $"[Transport] {upd.Guid} Type={upd.Type} Entry={od?.EntryID} GO: DisplayID={go?.DisplayID} TypeID={go?.TypeID} State={go?.State} Flags={go?.Flags} Level={go?.Level} MoveInfo: Pos={mi?.Position} Rot={mi?.Rotation} PathTimer={mi?.TransportPathTimer}", "Write", "");
				}
			}
			// Allow known elevators + boats with known periods; filter unknown entries
			this.ObjectUpdates.RemoveAll((ObjectUpdate u) =>
			{
				if (u.Guid.GetHighType() != HighGuidType.Transport && u.Guid.GetHighType() != HighGuidType.MOTransport)
					return false;
				sbyte typeId = u.GameObjectData?.TypeID ?? 0;
				uint entry = u.ObjectData?.EntryID.HasValue == true ? (uint)u.ObjectData.EntryID.Value : 0;
				if (typeId == 11 && !GameData.TransportAnimationEntries.Contains(entry))
				{
					Log.Print(LogType.Debug, $"[Transport] FILTERED elevator entry {entry} — not in TransportAnimation DB2", "Write", "");
					return true;
				}
				if (typeId == 15 && GameData.GetTransportPeriod(entry) == 0)
				{
					Log.Print(LogType.Debug, $"[Transport] FILTERED boat entry {entry} — not in Transports CSV", "Write", "");
					return true;
				}
				return false;
			});
		}
		this.NumObjUpdates = (uint)this.ObjectUpdates.Count;
		base._worldPacket.WriteUInt32(this.NumObjUpdates);
		base._worldPacket.WriteUInt16(this.MapID);
		WorldPacket buffer = new WorldPacket();
		if (buffer.WriteBit(!this.OutOfRangeGuids.Empty() || !this.DestroyedGuids.Empty()))
		{
			buffer.WriteUInt16((ushort)this.DestroyedGuids.Count);
			buffer.WriteInt32(this.DestroyedGuids.Count + this.OutOfRangeGuids.Count);
			foreach (WowGuid128 destroyGuid in this.DestroyedGuids)
			{
				buffer.WritePackedGuid128(destroyGuid);
			}
			foreach (WowGuid128 outOfRangeGuid in this.OutOfRangeGuids)
			{
				buffer.WritePackedGuid128(outOfRangeGuid);
			}
		}
		WorldPacket data = new WorldPacket();
		Log.Print(LogType.Debug, $"[UpdateObject] Writing {this.ObjectUpdates.Count} updates, {this.DestroyedGuids.Count} destroyed, {this.OutOfRangeGuids.Count} OOR, map={this.MapID}", "Write", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\Packets\\UpdatePackets.cs");
		foreach (ObjectUpdate update2 in this.ObjectUpdates)
		{
			update2.InitializePlaceholders();
			switch (ModernVersion.GetUpdateFieldsDefiningBuild())
			{
			case ClientVersionBuild.V1_14_0_40237:
			{
				HermesProxy.World.Objects.Version.V1_14_0_40237.ObjectUpdateBuilder builder5 = new HermesProxy.World.Objects.Version.V1_14_0_40237.ObjectUpdateBuilder(update2, this._gameState);
				builder5.WriteToPacket(data);
				break;
			}
			case ClientVersionBuild.V1_14_1_40688:
			{
				HermesProxy.World.Objects.Version.V1_14_1_40688.ObjectUpdateBuilder builder4 = new HermesProxy.World.Objects.Version.V1_14_1_40688.ObjectUpdateBuilder(update2, this._gameState);
				builder4.WriteToPacket(data);
				break;
			}
			case ClientVersionBuild.V2_5_2_39570:
			{
				HermesProxy.World.Objects.Version.V2_5_2_39570.ObjectUpdateBuilder builder3 = new HermesProxy.World.Objects.Version.V2_5_2_39570.ObjectUpdateBuilder(update2, this._gameState);
				builder3.WriteToPacket(data);
				break;
			}
			case ClientVersionBuild.V2_5_3_41750:
			{
				HermesProxy.World.Objects.Version.V2_5_3_41750.ObjectUpdateBuilder builder2 = new HermesProxy.World.Objects.Version.V2_5_3_41750.ObjectUpdateBuilder(update2, this._gameState);
				builder2.WriteToPacket(data);
				break;
			}
			case ClientVersionBuild.V3_4_3_54261:
			{
				HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder builder = new HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder(update2, this._gameState);
				builder.WriteToPacket(data);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException("No object update builder defined for current build.");
			}
		}
		byte[] bytes = data.GetData();
		Log.Print(LogType.Debug, $"[UpdateObject] Data block size={bytes.Length}, first 64 bytes: {BitConverter.ToString(bytes, 0, Math.Min(64, bytes.Length))}", "Write", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\Packets\\UpdatePackets.cs");
		if (bytes.Length > 1000)
		{
			try
			{
				string dumpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updateobject_dump.bin");
				using (FileStream fs = new FileStream(dumpPath, FileMode.Create))
				{
					fs.Write(BitConverter.GetBytes(this.NumObjUpdates), 0, 4);
					fs.Write(BitConverter.GetBytes(this.MapID), 0, 2);
					byte[] bufferData = buffer.GetData();
					fs.WriteByte(0);
					fs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
					fs.Write(bytes, 0, bytes.Length);
				}
				Log.Print(LogType.Debug, $"[UpdateObject] Dumped {bytes.Length} bytes to {dumpPath}", "Write", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Server\\Packets\\UpdatePackets.cs");
			}
			catch
			{
			}
		}
		buffer.WriteInt32(bytes.Length);
		buffer.WriteBytes(bytes);
		this.Data = buffer.GetData();
		base._worldPacket.WriteBytes(this.Data);
	}
}
