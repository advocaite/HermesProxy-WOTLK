using System;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;

namespace HermesProxy.World.Server.Packets;

public class ObjectUpdate
{
	public UpdateTypeModern Type;

	public WowGuid128 Guid;

	public GlobalSessionData GlobalSession;

	public CreateObjectData CreateData;

	public ObjectData ObjectData;

	public ItemData ItemData;

	public ContainerData ContainerData;

	public UnitData UnitData;

	public PlayerData PlayerData;

	public ActivePlayerData ActivePlayerData;

	public GameObjectData GameObjectData;

	public DynamicObjectData DynamicObjectData;

	public CorpseData CorpseData;

	public ObjectUpdate(WowGuid128 guid, UpdateTypeModern type, GlobalSessionData globalSession)
	{
		this.Type = type;
		this.Guid = guid;
		this.GlobalSession = globalSession;
		this.ObjectData = new ObjectData();
		if ((uint)(type - 1) <= 1u)
		{
			this.CreateData = new CreateObjectData();
		}
		switch (guid.GetObjectType())
		{
		case ObjectType.Item:
		case ObjectType.Container:
			this.ItemData = new ItemData();
			this.ContainerData = new ContainerData();
			break;
		case ObjectType.Unit:
			this.UnitData = new UnitData();
			break;
		case ObjectType.Player:
		case ObjectType.ActivePlayer:
			this.UnitData = new UnitData();
			this.PlayerData = new PlayerData();
			this.ActivePlayerData = new ActivePlayerData();
			break;
		case ObjectType.GameObject:
			this.GameObjectData = new GameObjectData();
			break;
		case ObjectType.DynamicObject:
			this.DynamicObjectData = new DynamicObjectData();
			break;
		case ObjectType.Corpse:
			this.CorpseData = new CorpseData();
			break;
		case ObjectType.AzeriteEmpoweredItem:
		case ObjectType.AzeriteItem:
			break;
		}
	}

	public void InitializePlaceholders()
	{
		if (this.CreateData == null)
		{
			return;
		}
		if (this.CreateData.MoveInfo != null)
		{
			if (this.CreateData.MoveInfo.WalkSpeed == 0f)
			{
				this.CreateData.MoveInfo.WalkSpeed = 2.5f;
			}
			if (this.CreateData.MoveInfo.RunSpeed == 0f)
			{
				this.CreateData.MoveInfo.RunSpeed = 7f;
			}
			if (this.CreateData.MoveInfo.RunBackSpeed == 0f)
			{
				this.CreateData.MoveInfo.RunBackSpeed = 4.5f;
			}
			if (this.CreateData.MoveInfo.SwimSpeed == 0f)
			{
				this.CreateData.MoveInfo.SwimSpeed = 4.722222f;
			}
			if (this.CreateData.MoveInfo.SwimBackSpeed == 0f)
			{
				this.CreateData.MoveInfo.SwimBackSpeed = 2.5f;
			}
			if (this.CreateData.MoveInfo.FlightSpeed == 0f)
			{
				this.CreateData.MoveInfo.FlightSpeed = 7f;
			}
			if (this.CreateData.MoveInfo.FlightBackSpeed == 0f)
			{
				this.CreateData.MoveInfo.FlightBackSpeed = 4.5f;
			}
			if (this.CreateData.MoveInfo.TurnRate == 0f)
			{
				this.CreateData.MoveInfo.TurnRate = 3.141594f;
			}
			if (this.CreateData.MoveInfo.PitchRate == 0f)
			{
				this.CreateData.MoveInfo.PitchRate = this.CreateData.MoveInfo.TurnRate;
			}
			if (this.CreateData.MoveInfo.Flags.HasAnyFlag(MovementFlagModern.WalkMode) && this.CreateData.MoveSpline != null)
			{
				this.CreateData.MoveInfo.Flags &= 4294967039u;
			}
			if (this.CreateData.MoveInfo.FlagsExtra == 0)
			{
				this.CreateData.MoveInfo.FlagsExtra = 512u;
			}
		}
		if (this.CreateData.MoveSpline != null && this.CreateData.MoveSpline.SplineFlags == SplineFlagModern.None)
		{
			this.CreateData.MoveSpline.SplineFlags = SplineFlagModern.Unknown5 | SplineFlagModern.Steering | SplineFlagModern.Unknown10;
		}
		if (this.GameObjectData != null)
		{
			if (!this.GameObjectData.PercentHealth.HasValue && (this.GameObjectData.State.HasValue || this.GameObjectData.TypeID.HasValue || this.GameObjectData.ArtKit.HasValue))
			{
				this.GameObjectData.PercentHealth = (byte)byte.MaxValue;
			}
			if (!this.GameObjectData.ParentRotation[3].HasValue)
			{
				this.GameObjectData.ParentRotation[3] = 1f;
			}
			if (!this.GameObjectData.StateAnimID.HasValue)
			{
				this.GameObjectData.StateAnimID = ModernVersion.GetGameObjectStateAnimId();
			}
			if (this.Guid.GetHighType() == HighGuidType.Transport)
			{
				// Map legacy GOState to 3.4.3 transport states for GAMEOBJECT_TYPE_TRANSPORT (elevators)
				// 3.3.5a: 0=active, 1=ready/stopped
				// 3.4.3:  24=GO_STATE_TRANSPORT_ACTIVE, 25=GO_STATE_TRANSPORT_STOPPED
				if (this.GameObjectData.TypeID.HasValue && this.GameObjectData.TypeID.Value == 11)
				{
					sbyte legacyState = this.GameObjectData.State.GetValueOrDefault();
					if (legacyState == 0)
						this.GameObjectData.State = 24; // GO_STATE_TRANSPORT_ACTIVE
					else if (legacyState == 1)
						this.GameObjectData.State = 25; // GO_STATE_TRANSPORT_STOPPED
				}

				uint entry = this.ObjectData.EntryID.HasValue ? (uint)this.ObjectData.EntryID.Value : 0;
				uint period = GameData.GetTransportPeriod(entry);
				Framework.Logging.Log.Print(Framework.Logging.LogType.Debug, $"[TransportFixup] Entry={entry} Period={period} DynFlagsSet={this.ObjectData.DynamicFlags.HasValue} DynFlags=0x{this.ObjectData.DynamicFlags.GetValueOrDefault():X8} Level={this.GameObjectData.Level} State={this.GameObjectData.State} TypeID={this.GameObjectData.TypeID} HasCreateData={this.CreateData != null} HasMoveInfo={this.CreateData?.MoveInfo != null}");
				if (period != 0)
				{
					if (!this.GameObjectData.Level.HasValue)
					{
						this.GameObjectData.Level = (int)period;
					}
					if (!this.ObjectData.DynamicFlags.HasValue)
					{
						uint pathTimer = this.CreateData.MoveInfo.TransportPathTimer;
						uint progress = pathTimer % period;
						uint dynFlags = (uint)((float)progress / (float)period * 65535f) << 16;
						this.ObjectData.DynamicFlags = dynFlags;
					}
					this.GameObjectData.Flags = 1048616u;
				}
				else if (!this.ObjectData.DynamicFlags.HasValue)
				{
					this.ObjectData.DynamicFlags = 0u;
				}
			}
		}
		if (this.CorpseData != null)
		{
			if (!this.CorpseData.ClassId.HasValue)
			{
				if (this.CorpseData.Owner != null)
				{
					this.CorpseData.ClassId = (byte)this.GlobalSession.GameState.GetUnitClass(this.CorpseData.Owner);
				}
				else
				{
					this.CorpseData.ClassId = (byte)1;
				}
			}
			if (!this.CorpseData.FactionTemplate.HasValue && this.CorpseData.Owner != null)
			{
				int ownerFaction = this.GlobalSession.GameState.GetLegacyFieldValueInt32(this.CorpseData.Owner, UnitField.UNIT_FIELD_FACTIONTEMPLATE);
				if (ownerFaction != 0)
				{
					this.CorpseData.FactionTemplate = ownerFaction;
				}
				else if (this.CorpseData.RaceId.HasValue)
				{
					this.CorpseData.FactionTemplate = (int)GameData.GetFactionForRace(this.CorpseData.RaceId.Value);
				}
			}
		}
		if (this.UnitData != null)
		{
			for (int i = 0; i < 6; i++)
			{
				if (!this.UnitData.ModPowerRegen[i].HasValue)
				{
					this.UnitData.ModPowerRegen[i] = 1f;
				}
			}
			if (!this.UnitData.Flags2.HasValue)
			{
				this.UnitData.Flags2 = 2048u;
			}
			if (!this.UnitData.DisplayScale.HasValue)
			{
				this.UnitData.DisplayScale = 1f;
			}
			if (!this.UnitData.NativeXDisplayScale.HasValue)
			{
				this.UnitData.NativeXDisplayScale = 1f;
			}
			if (!this.UnitData.ModCastHaste.HasValue)
			{
				this.UnitData.ModCastHaste = 1f;
			}
			if (!this.UnitData.ModHaste.HasValue)
			{
				this.UnitData.ModHaste = 1f;
			}
			if (!this.UnitData.ModRangedHaste.HasValue)
			{
				this.UnitData.ModRangedHaste = 1f;
			}
			if (!this.UnitData.ModHasteRegen.HasValue)
			{
				this.UnitData.ModHasteRegen = 1f;
			}
			if (!this.UnitData.ModTimeRate.HasValue)
			{
				this.UnitData.ModTimeRate = 1f;
			}
			if (!this.UnitData.HoverHeight.HasValue)
			{
				this.UnitData.HoverHeight = 1f;
			}
			if (!this.UnitData.ScaleDuration.HasValue)
			{
				this.UnitData.ScaleDuration = 100;
			}
			if (!this.UnitData.LookAtControllerID.HasValue)
			{
				this.UnitData.LookAtControllerID = -1;
			}
			if (this.UnitData.ChannelObject == null && this.Guid == this.GlobalSession.GameState.CurrentPlayerGuid)
			{
				this.UnitData.ChannelObject = WowGuid128.Empty;
			}
		}
		if (this.PlayerData != null)
		{
			if (this.PlayerData.WowAccount == null)
			{
				if (this.CreateData.ThisIsYou)
				{
					this.PlayerData.WowAccount = WowGuid128.Create(HighGuidType703.WowAccount, this.GlobalSession.GameAccountInfo.Id);
				}
				else
				{
					this.PlayerData.WowAccount = WowGuid128.Create(HighGuidType703.WowAccount, this.Guid.GetCounter());
				}
			}
			if (!this.PlayerData.VirtualPlayerRealm.HasValue)
			{
				this.PlayerData.VirtualPlayerRealm = this.GlobalSession.RealmId.GetAddress();
			}
			if (!this.PlayerData.HonorLevel.HasValue)
			{
				this.PlayerData.HonorLevel = 1;
			}
			if (!this.PlayerData.AvgItemLevel[3].HasValue)
			{
				this.PlayerData.AvgItemLevel[3] = 1f;
			}
		}
		if (this.ActivePlayerData == null)
		{
			return;
		}
		if (this.ActivePlayerData.RestInfo[0] == null)
		{
			this.ActivePlayerData.RestInfo[0] = new RestInfo();
		}
		if (!this.ActivePlayerData.RestInfo[0].Threshold.HasValue)
		{
			this.ActivePlayerData.RestInfo[0].Threshold = 1u;
		}
		if (!this.ActivePlayerData.RestInfo[0].StateID.HasValue)
		{
			this.ActivePlayerData.RestInfo[0].StateID = 0u;
		}
		for (int j = 0; j < 7; j++)
		{
			if (!this.ActivePlayerData.ModDamageDonePercent[j].HasValue)
			{
				this.ActivePlayerData.ModDamageDonePercent[j] = 1f;
			}
		}
		if (!this.ActivePlayerData.ModHealingPercent.HasValue)
		{
			this.ActivePlayerData.ModHealingPercent = 1f;
		}
		if (!this.ActivePlayerData.ModHealingDonePercent.HasValue)
		{
			this.ActivePlayerData.ModHealingDonePercent = 1f;
		}
		if (!this.ActivePlayerData.ModPeriodicHealingDonePercent.HasValue)
		{
			this.ActivePlayerData.ModPeriodicHealingDonePercent = 1f;
		}
		for (int k = 0; k < 3; k++)
		{
			if (!this.ActivePlayerData.WeaponDmgMultipliers[k].HasValue)
			{
				this.ActivePlayerData.WeaponDmgMultipliers[k] = 1f;
			}
			if (!this.ActivePlayerData.WeaponAtkSpeedMultipliers[k].HasValue)
			{
				this.ActivePlayerData.WeaponAtkSpeedMultipliers[k] = 1f;
			}
		}
		if (!this.ActivePlayerData.ModSpellPowerPercent.HasValue)
		{
			this.ActivePlayerData.ModSpellPowerPercent = 1f;
		}
		if (!this.ActivePlayerData.NumBackpackSlots.HasValue)
		{
			this.ActivePlayerData.NumBackpackSlots = (byte)16;
		}
		if (!this.ActivePlayerData.MultiActionBars.HasValue)
		{
			this.ActivePlayerData.MultiActionBars = (byte)7;
		}
		if (!this.ActivePlayerData.MaxLevel.HasValue)
		{
			this.ActivePlayerData.MaxLevel = LegacyVersion.GetMaxLevel();
		}
		if (!this.ActivePlayerData.ModPetHaste.HasValue)
		{
			this.ActivePlayerData.ModPetHaste = 1f;
		}
		if (!this.ActivePlayerData.HonorNextLevel.HasValue)
		{
			this.ActivePlayerData.HonorNextLevel = 5500;
		}
		if (!this.ActivePlayerData.PvPTierMaxFromWins.HasValue)
		{
			this.ActivePlayerData.PvPTierMaxFromWins = uint.MaxValue;
		}
		if (!this.ActivePlayerData.PvPLastWeeksTierMaxFromWins.HasValue)
		{
			this.ActivePlayerData.PvPLastWeeksTierMaxFromWins = uint.MaxValue;
		}
	}
}
