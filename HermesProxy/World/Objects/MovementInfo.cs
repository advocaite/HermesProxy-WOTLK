using System;
using Framework.GameMath;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Objects;

public sealed class MovementInfo
{
	public const float DEFAULT_WALK_SPEED = 2.5f;

	public const float DEFAULT_RUN_SPEED = 7f;

	public const float DEFAULT_RUN_BACK_SPEED = 4.5f;

	public const float DEFAULT_SWIM_SPEED = 4.72222f;

	public const float DEFAULT_SWIM_BACK_SPEED = 2.5f;

	public const float DEFAULT_FLY_SPEED = 7f;

	public const float DEFAULT_FLY_BACK_SPEED = 4.5f;

	public const float DEFAULT_TURN_RATE = 3.141593f;

	public const float DEFAULT_PITCH_RATE = 3.141593f;

	public uint Flags;

	public uint FlagsExtra;

	public uint FlagsExtra2;

	public uint MoveTime;

	public float SwimPitch;

	public uint FallTime;

	public float JumpHorizontalSpeed;

	public float JumpVerticalSpeed;

	public float JumpCosAngle;

	public float JumpSinAngle;

	public float SplineElevation;

	public bool HasSplineData;

	public Vector3 Position;

	public float Orientation;

	public float CorpseOrientation;

	public WowGuid128 TransportGuid;

	public Vector3 TransportOffset;

	public float TransportOrientation;

	public uint TransportTime;

	public uint TransportTime2;

	public sbyte TransportSeat = -1;

	public Quaternion Rotation;

	public float WalkSpeed;

	public float RunSpeed;

	public float RunBackSpeed;

	public float SwimSpeed;

	public float SwimBackSpeed;

	public float FlightSpeed;

	public float FlightBackSpeed;

	public float TurnRate;

	public float PitchRate;

	public bool Hover;

	public float VehicleOrientation;

	public uint VehicleId;

	public uint TransportPathTimer;

	public MovementInfo CopyFromMe()
	{
		MovementInfo copy = new MovementInfo();
		copy.Flags = this.Flags;
		copy.FlagsExtra = this.FlagsExtra;
		copy.SwimPitch = this.SwimPitch;
		copy.FallTime = this.FallTime;
		copy.JumpHorizontalSpeed = this.JumpHorizontalSpeed;
		copy.JumpVerticalSpeed = this.JumpVerticalSpeed;
		copy.JumpCosAngle = this.JumpCosAngle;
		copy.JumpSinAngle = this.JumpSinAngle;
		copy.SplineElevation = this.SplineElevation;
		copy.HasSplineData = this.HasSplineData;
		copy.Position = this.Position;
		copy.Orientation = this.Orientation;
		copy.CorpseOrientation = this.CorpseOrientation;
		copy.TransportGuid = this.TransportGuid;
		copy.TransportOffset = this.TransportOffset;
		copy.TransportOrientation = this.TransportOrientation;
		copy.TransportTime = this.TransportTime;
		copy.TransportTime2 = this.TransportTime2;
		copy.TransportSeat = this.TransportSeat;
		copy.Rotation = this.Rotation;
		copy.WalkSpeed = this.WalkSpeed;
		copy.RunSpeed = this.RunSpeed;
		copy.RunBackSpeed = this.RunBackSpeed;
		copy.SwimSpeed = this.SwimSpeed;
		copy.SwimBackSpeed = this.SwimBackSpeed;
		copy.FlightSpeed = this.FlightSpeed;
		copy.FlightBackSpeed = this.FlightBackSpeed;
		copy.TurnRate = this.TurnRate;
		copy.PitchRate = this.PitchRate;
		copy.Hover = this.Hover;
		copy.VehicleId = this.VehicleId;
		copy.VehicleOrientation = this.VehicleOrientation;
		copy.TransportPathTimer = this.TransportPathTimer;
		return copy;
	}

	public void SetMovementFlags(MovementFlagModern f)
	{
		this.Flags = (uint)f;
	}

	public void AddMovementFlag(MovementFlagModern f)
	{
		this.Flags |= (uint)f;
	}

	public void RemoveMovementFlag(MovementFlagModern f)
	{
		this.Flags &= (uint)(~f);
	}

	public bool HasMovementFlag(MovementFlagModern f)
	{
		return (this.Flags & (uint)f) != 0;
	}

	public void ReadMovementInfoLegacy(WorldPacket packet, GameSessionData gameState)
	{
		bool hasPitch;
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			MovementFlagWotLK flags = (MovementFlagWotLK)(this.Flags = packet.ReadUInt32());
			this.FlagsExtra = packet.ReadUInt16();
			hasPitch = flags.HasAnyFlag(MovementFlagWotLK.Swimming | MovementFlagWotLK.Flying) || this.FlagsExtra.HasAnyFlag(MovementFlagExtra.AlwaysAllowPitching);
		}
		else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			MovementFlagTBC flags2 = (MovementFlagTBC)packet.ReadUInt32();
			this.Flags = (uint)flags2.CastFlags<MovementFlagWotLK>();
			this.FlagsExtra = packet.ReadUInt8();
			hasPitch = flags2.HasAnyFlag(MovementFlagTBC.Swimming | MovementFlagTBC.Flying2);
		}
		else
		{
			MovementFlagVanilla flags3 = (MovementFlagVanilla)packet.ReadUInt32();
			this.Flags = (uint)flags3.CastFlags<MovementFlagWotLK>();
			hasPitch = flags3.HasAnyFlag(MovementFlagVanilla.Swimming);
			this.Hover = flags3.HasAnyFlag(MovementFlagVanilla.FixedZ);
		}
		this.MoveTime = packet.ReadUInt32();
		this.Position = packet.ReadVector3();
		this.Orientation = packet.ReadFloat();
		if (this.Flags.HasAnyFlag(MovementFlagWotLK.OnTransport))
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
			{
				this.TransportGuid = packet.ReadPackedGuid().To128(gameState);
			}
			else
			{
				this.TransportGuid = packet.ReadGuid().To128(gameState);
			}
			this.TransportOffset = packet.ReadVector3();
			this.TransportOrientation = packet.ReadFloat();
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				this.TransportTime = packet.ReadUInt32();
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				this.TransportSeat = packet.ReadInt8();
			}
			if (this.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateMove))
			{
				this.TransportTime2 = packet.ReadUInt32();
			}
		}
		if (hasPitch)
		{
			this.SwimPitch = packet.ReadFloat();
		}
		this.FallTime = packet.ReadUInt32();
		if (this.Flags.HasAnyFlag(MovementFlagWotLK.Falling))
		{
			this.JumpVerticalSpeed = packet.ReadFloat();
			this.JumpSinAngle = packet.ReadFloat();
			this.JumpCosAngle = packet.ReadFloat();
			this.JumpHorizontalSpeed = packet.ReadFloat();
		}
		if (this.Flags.HasAnyFlag(MovementFlagWotLK.SplineElevation))
		{
			this.SplineElevation = packet.ReadFloat();
		}
	}

	public void WriteMovementInfoLegacy(WorldPacket data)
	{
		uint flags = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? ((uint)((MovementFlagModern)this.Flags).CastFlags<MovementFlagWotLK>()) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? ((uint)((MovementFlagModern)this.Flags).CastFlags<MovementFlagVanilla>()) : ((uint)((MovementFlagModern)this.Flags).CastFlags<MovementFlagTBC>())));
		if (this.TransportGuid != null)
		{
			flags = (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? (flags | 0x200) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? (flags | 0x2000000) : (flags | 0x200)));
		}
		data.WriteUInt32(flags);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
		{
			data.WriteUInt16((ushort)this.FlagsExtra);
		}
		else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
		{
			data.WriteUInt8((byte)this.FlagsExtra);
		}
		data.WriteUInt32(this.MoveTime);
		data.WriteVector3(this.Position);
		data.WriteFloat(this.Orientation);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? flags.HasAnyFlag(MovementFlagWotLK.OnTransport) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? flags.HasAnyFlag(MovementFlagVanilla.OnTransport) : flags.HasAnyFlag(MovementFlagTBC.OnTransport)))
		{
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
			{
				data.WritePackedGuid(this.TransportGuid.To64());
			}
			else
			{
				data.WriteGuid(this.TransportGuid.To64());
			}
			data.WriteVector3(this.TransportOffset);
			data.WriteFloat(this.TransportOrientation);
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
			{
				data.WriteUInt32(this.TransportTime);
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
			{
				data.WriteInt8(this.TransportSeat);
			}
			if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) && this.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateMove))
			{
				data.WriteUInt32(this.TransportTime2);
			}
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? (flags.HasAnyFlag(MovementFlagWotLK.Swimming | MovementFlagWotLK.Flying) || this.FlagsExtra.HasAnyFlag(MovementFlagExtra.AlwaysAllowPitching)) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? flags.HasAnyFlag(MovementFlagVanilla.Swimming) : flags.HasAnyFlag(MovementFlagTBC.Swimming | MovementFlagTBC.Flying2)))
		{
			data.WriteFloat(this.SwimPitch);
		}
		data.WriteUInt32(this.FallTime);
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? flags.HasAnyFlag(MovementFlagWotLK.Falling) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? flags.HasAnyFlag(MovementFlagVanilla.Falling) : flags.HasAnyFlag(MovementFlagTBC.Falling)))
		{
			data.WriteFloat(this.JumpVerticalSpeed);
			data.WriteFloat(this.JumpSinAngle);
			data.WriteFloat(this.JumpCosAngle);
			data.WriteFloat(this.JumpHorizontalSpeed);
		}
		if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) ? flags.HasAnyFlag(MovementFlagWotLK.SplineElevation) : ((!LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180)) ? flags.HasAnyFlag(MovementFlagVanilla.SplineElevation) : flags.HasAnyFlag(MovementFlagTBC.SplineElevation)))
		{
			data.WriteFloat(this.SplineElevation);
		}
	}

	public void ReadMovementInfoModern(WorldPacket data)
	{
		if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
		{
			this.Flags = data.ReadUInt32();
			this.FlagsExtra = data.ReadUInt32();
			this.FlagsExtra2 = data.ReadUInt32();
		}
		this.MoveTime = data.ReadUInt32();
		this.Position = data.ReadVector3();
		this.Orientation = data.ReadFloat();
		this.SwimPitch = data.ReadFloat();
		this.SplineElevation = data.ReadFloat();
		uint removeMovementForcesCount = data.ReadUInt32();
		uint moveIndex = data.ReadUInt32();
		for (uint i = 0u; i < removeMovementForcesCount; i++)
		{
			data.ReadPackedGuid128();
		}
		if (!ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
		{
			this.Flags = data.ReadBits<uint>(30);
			this.FlagsExtra = data.ReadBits<uint>(18);
		}
		bool hasStandingOnGameObjectGUID = ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3) && data.HasBit();
		bool hasTransport = data.HasBit();
		bool hasFall = data.HasBit();
		bool hasSpline = data.HasBit();
		data.ReadBit(); // HeightChangeFailed
		data.ReadBit(); // RemoteTimeValid
		bool hasInertia = ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3) && data.HasBit();
		bool hasAdvFlying = ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3) && data.HasBit();
		if (hasTransport)
		{
			this.ReadTransportInfoModern(data);
		}
		if (hasStandingOnGameObjectGUID)
		{
			data.ReadPackedGuid128(); // StandingOnGameObjectGUID
		}
		if (hasInertia)
		{
			data.ReadPackedGuid128();
			data.ReadVector3();
			data.ReadUInt32();
		}
		if (hasAdvFlying)
		{
			data.ReadFloat(); // forwardVelocity
			data.ReadFloat(); // upVelocity
		}
		if (hasFall)
		{
			this.FallTime = data.ReadUInt32();
			this.JumpVerticalSpeed = data.ReadFloat();
			if (data.HasBit())
			{
				this.JumpSinAngle = data.ReadFloat();
				this.JumpCosAngle = data.ReadFloat();
				this.JumpHorizontalSpeed = data.ReadFloat();
			}
		}
	}

	public void ReadTransportInfoModern(WorldPacket data)
	{
		this.TransportGuid = data.ReadPackedGuid128();
		this.TransportOffset = data.ReadVector3();
		this.TransportOrientation = data.ReadFloat();
		this.TransportSeat = data.ReadInt8();
		this.TransportTime = data.ReadUInt32();
		bool hasPrevTime = data.HasBit();
		bool hasVehicleId = data.HasBit();
		if (hasPrevTime)
		{
			this.TransportTime2 = data.ReadUInt32();
		}
		if (hasVehicleId)
		{
			this.VehicleId = data.ReadUInt32();
		}
	}

	public void WriteMovementInfoModern(WorldPacket data, WowGuid128 guid)
	{
		bool hasFallDirection = this.Flags.HasAnyFlag(MovementFlagModern.Falling | MovementFlagModern.FallingFar);
		bool hasFall = hasFallDirection || this.FallTime != 0;
		data.WritePackedGuid128(guid);
		if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
		{
			data.WriteUInt32(this.Flags);
			data.WriteUInt32(this.FlagsExtra);
			data.WriteUInt32(this.FlagsExtra2);
		}
		data.WriteUInt32(this.MoveTime);
		data.WriteFloat(this.Position.X);
		data.WriteFloat(this.Position.Y);
		data.WriteFloat(this.Position.Z);
		data.WriteFloat(this.Orientation);
		data.WriteFloat(this.SwimPitch);
		data.WriteFloat(this.SplineElevation);
		data.WriteUInt32(0u);
		data.WriteUInt32(0u);
		if (!ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
		{
			data.WriteBits(this.Flags, 30);
			data.WriteBits(this.FlagsExtra, 18);
		}
		if (ModernVersion.ExpansionVersion >= 3)
		{
			data.WriteBit(bit: false);
			data.WriteBit(this.TransportGuid != null);
			data.WriteBit(hasFall);
			data.WriteBit(this.HasSplineData);
			data.WriteBit(bit: false);
			data.WriteBit(bit: false);
			data.WriteBit(bit: false);
			data.WriteBit(bit: false);
		}
		else
		{
			data.WriteBit(this.TransportGuid != null);
			data.WriteBit(hasFall);
			data.WriteBit(this.HasSplineData);
			data.WriteBit(bit: false);
			data.WriteBit(bit: false);
			if (ModernVersion.AddedInVersion(9, 2, 0, 1, 14, 1, 2, 5, 3))
			{
				data.WriteBit(bit: false);
			}
		}
		data.FlushBits();
		if (this.TransportGuid != null)
		{
			this.WriteTransportInfoModern(data);
		}
		if (hasFall)
		{
			data.WriteUInt32(this.FallTime);
			data.WriteFloat(this.JumpVerticalSpeed);
			data.WriteBit(hasFallDirection);
			data.FlushBits();
			if (hasFallDirection)
			{
				data.WriteFloat(this.JumpSinAngle);
				data.WriteFloat(this.JumpCosAngle);
				data.WriteFloat(this.JumpHorizontalSpeed);
			}
		}
	}

	public void WriteTransportInfoModern(WorldPacket data)
	{
		bool hasPrevTime = false;
		bool hasVehicleId = this.VehicleId != 0;
		data.WritePackedGuid128(this.TransportGuid);
		data.WriteFloat(this.TransportOffset.X);
		data.WriteFloat(this.TransportOffset.Y);
		data.WriteFloat(this.TransportOffset.Z);
		data.WriteFloat(this.TransportOrientation);
		data.WriteInt8(this.TransportSeat);
		data.WriteUInt32(this.TransportTime);
		data.WriteBit(hasPrevTime);
		data.WriteBit(hasVehicleId);
		data.FlushBits();
		if (hasPrevTime)
		{
			data.WriteUInt32(0u);
		}
		if (hasVehicleId)
		{
			data.WriteUInt32(this.VehicleId);
		}
	}

	public static void ClampOrientation(ref float orientation)
	{
		while (orientation < 0f)
		{
			orientation += (float)Math.PI * 2f;
		}
		while (orientation > (float)Math.PI * 2f)
		{
			orientation -= (float)Math.PI * 2f;
		}
	}

	public void ValidateMovementInfo()
	{
		MovementInfo.ClampOrientation(ref this.Orientation);
		MovementInfo.ClampOrientation(ref this.TransportOrientation);
		Action<bool, MovementFlagModern> RemoveViolatingFlags = delegate(bool check, MovementFlagModern maskToRemove)
		{
			if (check)
			{
				Log.Print(LogType.Error, $"Violation of MovementFlags found ({check}). MovementFlags: {this.Flags}, MovementFlags2: {this.FlagsExtra}. Mask {maskToRemove} will be removed.", "ValidateMovementInfo", "F:\\Ampps\\HermesProxy-master\\HermesProxy\\World\\Objects\\MovementInfo.cs");
				this.RemoveMovementFlag(maskToRemove);
			}
		};
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.Root) && this.HasMovementFlag(MovementFlagModern.MaskMoving), MovementFlagModern.MaskMoving);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.Ascending) && this.HasMovementFlag(MovementFlagModern.Descending), MovementFlagModern.Ascending | MovementFlagModern.Descending);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.TurnLeft) && this.HasMovementFlag(MovementFlagModern.TurnRight), MovementFlagModern.TurnLeft | MovementFlagModern.TurnRight);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.StrafeLeft) && this.HasMovementFlag(MovementFlagModern.StrafeRight), MovementFlagModern.StrafeLeft | MovementFlagModern.StrafeRight);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.PitchUp) && this.HasMovementFlag(MovementFlagModern.PitchDown), MovementFlagModern.PitchUp | MovementFlagModern.PitchDown);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.Forward) && this.HasMovementFlag(MovementFlagModern.Backward), MovementFlagModern.Forward | MovementFlagModern.Backward);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.DisableGravity | MovementFlagModern.CanFly) && this.HasMovementFlag(MovementFlagModern.Falling), MovementFlagModern.Falling);
		RemoveViolatingFlags(this.HasMovementFlag(MovementFlagModern.SplineElevation) && MathFunctions.fuzzyEq(this.SplineElevation, 0f), MovementFlagModern.SplineElevation);
		if (MathFunctions.fuzzyNe(this.SplineElevation, 0f))
		{
			this.AddMovementFlag(MovementFlagModern.SplineElevation);
		}
	}
}
