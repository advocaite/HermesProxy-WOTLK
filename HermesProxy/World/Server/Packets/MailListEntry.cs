using System;
using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

public class MailListEntry
{
	public int MailID;

	public MailType SenderType;

	public WowGuid128 SenderCharacter;

	public uint? AltSenderID;

	public ulong Cod;

	public int StationeryID;

	public ulong SentMoney;

	public uint Flags;

	public float DaysLeft;

	public int MailTemplateID;

	public string Subject = "";

	public string Body = "";

	public uint ItemTextId;

	public List<MailAttachedItem> Attachments = new List<MailAttachedItem>();

	/// <summary>
	/// TC343 MailListEntry write format:
	///   uint64 MailID, uint32 SenderType, uint64 Cod, int32 StationeryID,
	///   uint64 SentMoney, int32 Flags, float DaysLeft, int32 MailTemplateID,
	///   uint32 AttachmentCount,
	///   THEN based on SenderType: PackedGuid128 OR int32 AltSenderID,
	///   WriteBits Subject(8), WriteBits Body(13), FlushBits,
	///   Attachments[], Subject string, Body string
	/// </summary>
	public void Write(WorldPacket data)
	{
		data.WriteUInt64((ulong)this.MailID);
		data.WriteUInt32((uint)this.SenderType);
		data.WriteUInt64(this.Cod);
		data.WriteInt32(this.StationeryID);
		data.WriteUInt64(this.SentMoney);
		data.WriteInt32((int)this.Flags);
		data.WriteFloat(this.DaysLeft);
		data.WriteInt32(this.MailTemplateID);
		data.WriteInt32(this.Attachments.Count);

		// TC343: sender written unconditionally based on type (not optional bits)
		switch (this.SenderType)
		{
		case MailType.Normal:
			data.WritePackedGuid128(this.SenderCharacter ?? WowGuid128.Empty);
			break;
		case MailType.Auction:
		case MailType.Item:
		case MailType.Creature:
		case MailType.GameObject:
			data.WriteInt32((int)this.AltSenderID.GetValueOrDefault());
			break;
		default:
			break;
		}

		data.WriteBits(this.Subject.GetByteCount(), 8);
		data.WriteBits(this.Body.GetByteCount(), 13);
		data.FlushBits();
		this.Attachments.ForEach(delegate(MailAttachedItem p)
		{
			p.Write(data);
		});
		data.WriteString(this.Subject);
		data.WriteString(this.Body);
	}
}
