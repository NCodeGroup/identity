// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.GlobalDirectory;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public class AuditRecordHandlerTests
{
    private sealed record TestAuditEvent : AuditEvent
    {
        public override string EventName => "token.issued";

        public override string EventCategory => AuditEventCategories.Token;
    }

    private static IOptions<CellOptions> CellOptionsAccessor { get; } =
        Options.Create(new CellOptions { Name = "cell-1" });

    private static TestAuditEvent CreateEvent() =>
        new()
        {
            EventId = Guid.CreateVersion7(),
            EventTime = DateTimeOffset.UnixEpoch,
            EventSource = AuditEventSources.OpenId,
            Outcome = AuditOutcome.Success,
        };

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenEnabled_QueuesTheEventsRecord()
    {
        var options = Options.Create(new AuditRecordOptions());
        var queue = new AuditRecordQueue(options);
        var handler = new AuditRecordHandler(options, CellOptionsAccessor, queue);
        var auditEvent = CreateEvent();

        await handler.HandleAsync(auditEvent, CancellationToken.None);

        var drained =
            new List<NCode.Identity.OpenId.Persistence.DataContracts.PersistedAuditRecord>();
        queue.Drain(drained, limit: 10);
        var record = Assert.Single(drained);
        Assert.Equal(auditEvent.EventId, record.EventId);
        Assert.Equal("cell-1", record.Cell);
    }

    [Fact]
    public async Task HandleAsync_WhenDisabled_QueuesNothing()
    {
        var options = Options.Create(new AuditRecordOptions { Enabled = false });
        var queue = new AuditRecordQueue(options);
        var handler = new AuditRecordHandler(options, CellOptionsAccessor, queue);

        await handler.HandleAsync(CreateEvent(), CancellationToken.None);

        Assert.Equal(0, queue.Count);
    }

    #endregion
}
