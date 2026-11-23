// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using NCode.Identity.OpenId.Persistence.DataContracts;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public class AuditStreamPayloadWriterTests
{
    private const string TenantId = "tenant-1";

    private static DateTimeOffset Timestamp { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement ParseRecord(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    private static PersistedAuditRecord CreateFullRecord(Guid eventId) =>
        new()
        {
            EventId = eventId,
            EventTime = new DateTimeOffset(2026, 11, 22, 11, 59, 30, TimeSpan.Zero),
            EventName = "token.issued",
            EventCategory = "token",
            Outcome = "success",
            Severity = "info",
            Cell = "cell-1",
            TenantId = TenantId,
            SubjectId = "subject-1",
            ClientId = "client-1",
            ActorType = "client",
            ActorId = "actor-1",
            TraceId = "trace-1",
            SourceIp = "203.0.113.7",
            Resources =
            [
                new PersistedAuditResource { Type = "client", Id = "client-1" },
                new PersistedAuditResource { Type = "session" },
            ],
            Record = ParseRecord("{\"eventVersion\":\"1\",\"request\":{\"scope\":\"openid\"}}"),
        };

    private static PersistedAuditRecord CreateMinimalRecord(Guid eventId) =>
        new()
        {
            EventId = eventId,
            EventTime = new DateTimeOffset(2026, 11, 22, 11, 59, 30, TimeSpan.Zero),
            EventName = "token.issued",
            EventCategory = "token",
            Outcome = "success",
            Severity = "info",
            Cell = "cell-1",
            ActorType = "anonymous",
            Resources = [],
            Record = ParseRecord("{}"),
        };

    private static JsonElement WriteAndParse(PersistedAuditRecord record)
    {
        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);
        AuditStreamPayloadWriter.Write(buffer, TenantId, [record], Timestamp);
        return JsonSerializer.Deserialize<JsonElement>(buffer.WrittenSpan);
    }

    #region Write Tests

    [Fact]
    public void Write_Always_WritesTheEnvelopeAndTheLookupsFields()
    {
        var eventId = Guid.CreateVersion7();

        var envelope = WriteAndParse(CreateFullRecord(eventId));

        Assert.Equal("audit_records", envelope.GetProperty("type").GetString());
        Assert.Equal(Timestamp.UtcDateTime, envelope.GetProperty("timestamp").GetDateTime());
        var data = envelope.GetProperty("data");
        Assert.Equal(TenantId, data.GetProperty("tenantId").GetString());
        var record = Assert.Single(data.GetProperty("records").EnumerateArray());
        Assert.Equal(eventId, record.GetProperty("eventId").GetGuid());
        Assert.Equal(
            new DateTime(2026, 11, 22, 11, 59, 30, DateTimeKind.Utc),
            record.GetProperty("eventTime").GetDateTime()
        );
        Assert.Equal("token.issued", record.GetProperty("eventName").GetString());
        Assert.Equal("token", record.GetProperty("eventCategory").GetString());
        Assert.Equal("success", record.GetProperty("outcome").GetString());
        Assert.Equal("info", record.GetProperty("severity").GetString());
        Assert.Equal("subject-1", record.GetProperty("subjectId").GetString());
        Assert.Equal("client-1", record.GetProperty("clientId").GetString());
        Assert.Equal("client", record.GetProperty("actorType").GetString());
        Assert.Equal("actor-1", record.GetProperty("actorId").GetString());
        Assert.Equal("trace-1", record.GetProperty("traceId").GetString());
        Assert.Equal("203.0.113.7", record.GetProperty("sourceIp").GetString());
        var resources = record.GetProperty("resources").EnumerateArray().ToList();
        Assert.Equal(2, resources.Count);
        Assert.Equal("client", resources[0].GetProperty("type").GetString());
        Assert.Equal("client-1", resources[0].GetProperty("id").GetString());
        Assert.Equal("session", resources[1].GetProperty("type").GetString());
        Assert.False(resources[1].TryGetProperty("id", out _));
        Assert.Equal(
            "{\"eventVersion\":\"1\",\"request\":{\"scope\":\"openid\"}}",
            record.GetProperty("record").GetRawText()
        );
    }

    [Fact]
    public void Write_WithoutOptionalFields_OmitsThem()
    {
        var record = Assert.Single(
            WriteAndParse(CreateMinimalRecord(Guid.CreateVersion7()))
                .GetProperty("data")
                .GetProperty("records")
                .EnumerateArray()
        );

        Assert.False(record.TryGetProperty("subjectId", out _));
        Assert.False(record.TryGetProperty("clientId", out _));
        Assert.False(record.TryGetProperty("actorId", out _));
        Assert.False(record.TryGetProperty("traceId", out _));
        Assert.False(record.TryGetProperty("sourceIp", out _));
        Assert.Empty(record.GetProperty("resources").EnumerateArray());
        Assert.Equal("{}", record.GetProperty("record").GetRawText());
    }

    #endregion
}
