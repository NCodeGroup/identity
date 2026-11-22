// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Writes the body of a delivery to an audit stream: an envelope with a type and a timestamp around the tenant's batch
/// of audit records, each with its envelope fields and the stored record as it is.
/// </summary>
// See ADR-1534.
internal static class AuditStreamPayloadWriter
{
    /// <summary>
    /// The type of the envelope that carries a batch of audit records.
    /// </summary>
    internal const string EnvelopeType = "audit_records";

    /// <summary>
    /// Writes the body of a delivery as UTF-8 JSON.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant whose records are delivered.</param>
    /// <param name="records">The records to deliver, in the order the store stored them.</param>
    /// <param name="timestamp">The time of the delivery.</param>
    /// <returns>The UTF-8 bytes of the body.</returns>
    public static byte[] Write(
        string tenantId,
        IReadOnlyList<PersistedAuditRecord> records,
        DateTimeOffset timestamp
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentNullException.ThrowIfNull(records);

        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", EnvelopeType);
            writer.WriteString("timestamp", timestamp.UtcDateTime);

            writer.WriteStartObject("data");
            writer.WriteString("tenantId", tenantId);

            writer.WriteStartArray("records");
            foreach (var record in records)
            {
                WriteRecord(writer, record);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteRecord(Utf8JsonWriter writer, PersistedAuditRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("eventId", record.EventId);
        writer.WriteString("eventTime", record.EventTime.UtcDateTime);
        writer.WriteString("eventName", record.EventName);
        writer.WriteString("eventCategory", record.EventCategory);
        writer.WriteString("outcome", record.Outcome);
        writer.WriteString("severity", record.Severity);
        WriteIfSet(writer, "subjectId", record.SubjectId);
        WriteIfSet(writer, "clientId", record.ClientId);
        writer.WriteString("actorType", record.ActorType);
        WriteIfSet(writer, "actorId", record.ActorId);
        WriteIfSet(writer, "traceId", record.TraceId);
        WriteIfSet(writer, "sourceIp", record.SourceIp);

        writer.WriteStartArray("resources");
        foreach (var resource in record.Resources)
        {
            writer.WriteStartObject();
            writer.WriteString("type", resource.Type);
            WriteIfSet(writer, "id", resource.Id);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("record");
        record.Record.WriteTo(writer);

        writer.WriteEndObject();
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
            writer.WriteString(propertyName, value);
    }
}
