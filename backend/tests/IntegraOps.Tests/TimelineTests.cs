using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class TimelineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
    private static readonly FlowDefinition Flow = new(Guid.NewGuid(), Guid.NewGuid(), "order-flow", "Sipariş",
        [new("order-created", "Sipariş", 0, null), new("erp-completed", "ERP", 1, 300)]);

    private static ObservedEvent Event(string step, EventStatus status, int minute) =>
        new(Guid.NewGuid(), step, status, Start.AddMinutes(minute), Start.AddMinutes(20), null);

    [Fact]
    public void Reverse_delivery_order_does_not_change_completed_transaction()
    {
        var first = Event("order-created", EventStatus.Completed, 0);
        var second = Event("erp-completed", EventStatus.Completed, 1);
        var result = TimelineProjector.Project(Flow, [second, first]);
        Assert.Equal(TransactionStatus.Completed, result.Status);
        Assert.Equal(Start, result.Steps[0].OccurredAt);
    }

    [Fact]
    public void Missing_event_is_not_evidence_of_failed_business_step()
    {
        var result = TimelineProjector.Project(Flow, [Event("order-created", EventStatus.Completed, 0)]);
        Assert.Equal(TransactionStatus.InProgress, result.Status);
        Assert.Equal("not_observed", result.Steps[1].Observation);
        Assert.Null(result.Steps[1].Error);
    }

    [Fact]
    public void Late_delivery_of_old_failure_does_not_override_later_success()
    {
        var result = TimelineProjector.Project(Flow,
            [Event("order-created", EventStatus.Completed, 0),
             Event("erp-completed", EventStatus.Completed, 3),
             Event("erp-completed", EventStatus.Failed, 1)]);
        Assert.Equal(TransactionStatus.Completed, result.Status);
        Assert.Equal(Start.AddMinutes(3), result.Steps[1].OccurredAt);
    }

    [Fact]
    public void A_newer_failure_overrides_an_older_success()
    {
        var result = TimelineProjector.Project(Flow,
            [Event("order-created", EventStatus.Completed, 0),
             Event("erp-completed", EventStatus.Failed, 3),
             Event("erp-completed", EventStatus.Completed, 1)]);
        Assert.Equal(TransactionStatus.Failed, result.Status);
    }

    [Fact]
    public void Accepted_but_unprocessed_transaction_remains_pending()
    {
        var result = TimelineProjector.Project(Flow, []);
        Assert.Equal(TransactionStatus.Pending, result.Status);
        Assert.All(result.Steps, s => Assert.Equal("not_observed", s.Observation));
    }

    [Fact]
    public void Identical_business_timestamps_have_a_stable_tie_breaker()
    {
        var failure = Event("erp-completed", EventStatus.Failed, 1) with
            { EventId = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var success = Event("erp-completed", EventStatus.Completed, 1) with
            { EventId = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        Assert.Equal(TimelineProjector.Project(Flow, [failure, success]).Steps[1],
            TimelineProjector.Project(Flow, [success, failure]).Steps[1]);
    }
}
