using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class DelayTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static FlowDefinition Flow(int invoiceSeconds = 600) => new(Guid.NewGuid(), Guid.NewGuid(),
        "invoice-flow", "Fatura akışı", [
            new("erp-completed", "ERP", 0, null),
            new("approval-completed", "Onay", 1, 3600),
            new("invoice-created", "Fatura", 2, invoiceSeconds),
            new("cargo-created", "Kargo", 3, 7200)]);

    private static ObservedEvent Event(string step, int seconds = 0, EventStatus status = EventStatus.Completed) =>
        new(Guid.NewGuid(), step, status, Start.AddSeconds(seconds), Start.AddSeconds(seconds + 2),
            status == EventStatus.Failed ? "Dış sistem hata bildirdi." : null);

    private static DelayAssessment Check(int nowSeconds, params ObservedEvent[] events) =>
        DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(), events), Start.AddSeconds(nowSeconds));

    private static ObservedEvent Delivered(int occurredSeconds, int receivedSeconds) =>
        new(Guid.NewGuid(), "erp-completed", EventStatus.Completed, Start.AddSeconds(occurredSeconds), Start.AddSeconds(receivedSeconds), null);

    [Theory]
    [InlineData(0, 2, 10, false)]        // delivered on time: alarms are never held
    [InlineData(0, 299, 300, false)]     // just under the late threshold
    [InlineData(0, 3600, 3610, true)]    // an hour old on arrival: hold while the source catches up
    [InlineData(0, 3600, 3719, true)]
    [InlineData(0, 3600, 3720, false)]   // grace is over; alarms open normally
    public void Late_delivery_holds_new_alarms_only_during_the_grace(int occurred, int received, int now, bool held)
    {
        var until = CatchUp.Until([Delivered(occurred, received)], Start.AddSeconds(now));
        Assert.Equal(held ? Start.AddSeconds(received) + CatchUp.Grace : null, until);
    }

    [Fact]
    public void Catch_up_follows_the_most_recently_received_event()
    {
        // The replayed event arrived first; a live event received afterwards ends the hold.
        Assert.Null(CatchUp.Until([Delivered(0, 3600), Delivered(3605, 3607)], Start.AddSeconds(3610)));
        Assert.NotNull(CatchUp.Until([Delivered(3605, 3607), Delivered(0, 3650)], Start.AddSeconds(3660)));
        Assert.Null(CatchUp.Until([], Start));
    }

    [Fact]
    public void Approval_uses_its_own_hour_allowance()
    {
        var result = Check(600, Event("erp-completed"));
        Assert.Equal("waiting", result.State);
        Assert.Equal("approval-completed", result.Step);
        Assert.Equal(Start.AddHours(1), result.DeadlineAt);
    }

    [Theory]
    [InlineData(899, "waiting")]
    [InlineData(900, "overdue")]
    [InlineData(901, "overdue")]
    public void Invoice_boundary_starts_at_approval_not_erp(int nowSeconds, string state)
    {
        var result = Check(nowSeconds, Event("erp-completed"), Event("approval-completed", 300));
        Assert.Equal(state, result.State);
        Assert.Equal("invoice-created", result.Step);
        Assert.Equal(Start.AddMinutes(5), result.StartedAt);
        Assert.Equal(Start.AddMinutes(15), result.DeadlineAt);
    }

    [Fact]
    public void Different_flows_can_allow_different_invoice_times()
    {
        ObservedEvent[] events = [Event("erp-completed"), Event("approval-completed", 300)];
        var now = Start.AddMinutes(20);
        var fast = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(600), events), now);
        var slow = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(7200), events), now);
        Assert.Equal("overdue", fast.State);
        Assert.Equal("waiting", slow.State);
    }

    [Fact]
    public void No_events_has_no_timer() => Assert.Equal("none", Check(9999).State);

    [Fact]
    public void Missing_first_step_has_no_invented_start_time() =>
        Assert.Equal("none", Check(9999, Event("invoice-created", 600)).State);

    [Fact]
    public void Later_step_does_not_hide_an_earlier_gap()
    {
        var result = Check(3600, Event("erp-completed"), Event("invoice-created", 600));
        Assert.Equal("overdue", result.State);
        Assert.Equal("approval-completed", result.Step);
    }

    [Fact]
    public void Explicit_failure_is_not_classified_as_delay()
    {
        var events = new[] { Event("erp-completed"), Event("approval-completed", 20, EventStatus.Failed) };
        var projection = TimelineProjector.Project(Flow(), events);
        Assert.Equal(TransactionStatus.Failed, projection.Status);
        Assert.Equal("none", DelayEvaluator.Evaluate(projection, Start.AddHours(2)).State);
    }

    [Fact]
    public void Late_invoice_moves_expectation_to_cargo()
    {
        var result = Check(1300, Event("erp-completed"), Event("approval-completed", 300),
            Event("invoice-created", 1200));
        Assert.Equal("waiting", result.State);
        Assert.Equal("cargo-created", result.Step);
        Assert.Equal(Start.AddSeconds(8400), result.DeadlineAt);
    }

    [Fact]
    public void Complete_flow_has_no_current_delay_even_if_completion_was_late()
    {
        var result = Check(10000, Event("cargo-created", 9500), Event("invoice-created", 1200),
            Event("approval-completed", 300), Event("erp-completed"));
        Assert.Equal(new DelayAssessment("none", null, null, null), result);
    }

    [Fact]
    public void Receipt_time_does_not_postpone_the_business_deadline()
    {
        var approval = Event("approval-completed", 300) with { ReceivedAt = Start.AddHours(3) };
        var result = Check(10800, Event("erp-completed"), approval);
        Assert.Equal("overdue", result.State);
        Assert.Equal(Start.AddSeconds(900), result.DeadlineAt);
    }

    [Fact]
    public void Older_failure_does_not_reset_a_later_successful_anchor()
    {
        var result = Check(900, Event("erp-completed"), Event("approval-completed", 300),
            Event("approval-completed", 100, EventStatus.Failed));
        Assert.Equal("overdue", result.State);
        Assert.Equal("invoice-created", result.Step);
    }

    [Fact]
    public void Delay_does_not_change_the_business_status()
    {
        var projection = TimelineProjector.Project(Flow(), [Event("erp-completed")]);
        Assert.Equal("overdue", DelayEvaluator.Evaluate(projection, Start.AddHours(2)).State);
        Assert.Equal(TransactionStatus.InProgress, projection.Status);
    }
}
