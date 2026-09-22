using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class ComplexFlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    // order (stage 0) → payment ∥ stock (stage 1) → customs only for "export" (stage 2) → shipped (stage 3); gift-wrap is optional.
    private static readonly FlowDefinition Flow = new(Guid.NewGuid(), Guid.NewGuid(), "order", "Sipariş", [
        new("order", "Sipariş", 0, null, Stage: 0),
        new("payment", "Ödeme", 1, 600, Stage: 1),
        new("stock", "Stok", 2, 1800, Stage: 1),
        new("gift-wrap", "Hediye paketi", 3, null, Stage: 1, Kind: StepKind.Optional),
        new("customs", "Gümrük", 4, 3600, Stage: 2, Kind: StepKind.Conditional, Groups: ["export"]),
        new("shipped", "Kargo", 5, 7200, Stage: 3)]);

    private static ObservedEvent Ev(string step, int minutes, EventStatus status = EventStatus.Completed) =>
        new(Guid.NewGuid(), step, status, T0.AddMinutes(minutes), T0.AddMinutes(minutes + 1), status == EventStatus.Failed ? "hata" : null);

    [Fact]
    public void Parallel_branches_are_timed_from_the_same_start_and_both_can_be_overdue()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0)]);
        var d = DelayEvaluator.Evaluate(p, T0.AddMinutes(45), Flow);
        Assert.Equal("overdue", d.State);
        Assert.Equal("payment", d.Step); // the most urgent branch
        Assert.Equal(2, d.PendingSteps.Count);
        var alarms = AlarmRules.Current(p, d, T0.AddMinutes(45));
        Assert.Equal(["payment", "stock"], alarms.Select(a => a.Step).Order());
        // At 15 minutes only payment is late.
        var early = DelayEvaluator.Evaluate(p, T0.AddMinutes(15), Flow);
        Assert.Single(AlarmRules.Current(p, early, T0.AddMinutes(15)));
    }

    [Fact]
    public void Next_stage_waits_for_all_branches_and_starts_from_the_last_one()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5)]);
        Assert.Equal(TransactionStatus.InProgress, p.Status);
        Assert.Equal((1, T0), p.CurrentStage());
        var both = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5), Ev("stock", 20)]);
        var d = DelayEvaluator.Evaluate(both, T0.AddMinutes(30), Flow);
        Assert.Equal("shipped", d.Step); // customs does not apply without the export group
        Assert.Equal(T0.AddMinutes(20), d.StartedAt);
    }

    [Fact]
    public void Conditional_step_applies_only_to_its_groups()
    {
        var events = new[] { Ev("order", 0), Ev("payment", 5), Ev("stock", 6), Ev("shipped", 10) };
        var domestic = TimelineProjector.Project(Flow, events);
        Assert.Equal(TransactionStatus.Completed, domestic.Status);
        Assert.Equal("not_applicable", domestic.Steps.Single(s => s.Code == "customs").Observation);
        var export = TimelineProjector.Project(Flow, events, "export");
        Assert.Equal(TransactionStatus.InProgress, export.Status);
        var d = DelayEvaluator.Evaluate(export, T0.AddMinutes(30), Flow);
        Assert.Equal("customs", d.Step);
        Assert.Equal(T0.AddMinutes(6), d.StartedAt);
    }

    [Fact]
    public void Optional_step_is_never_expected_and_never_timed()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5), Ev("stock", 6), Ev("shipped", 10)]);
        Assert.Equal(TransactionStatus.Completed, p.Status);
        Assert.Equal("not_observed", p.Steps.Single(s => s.Code == "gift-wrap").Observation);
        var withWrap = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("gift-wrap", 3)]);
        var d = DelayEvaluator.Evaluate(withWrap, T0.AddMinutes(4), Flow);
        Assert.DoesNotContain(d.PendingSteps, s => s.Step == "gift-wrap");
    }

    [Fact]
    public void Explicit_skip_counts_as_done_and_closes_the_wait()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5), Ev("stock", 6, EventStatus.Skipped), Ev("shipped", 10)]);
        Assert.Equal(TransactionStatus.Completed, p.Status);
        Assert.Equal("skipped", p.Steps.Single(s => s.Code == "stock").Observation);
        Assert.Equal("step_completed", AlarmRules.Resolution(p, "stock"));
        Assert.DoesNotContain(StepStats.Compute(Flow, p), s => s.Code == "stock"); // skipped steps are not measured
    }

    [Fact]
    public void Cancellation_ends_tracking_and_resolves_alarms()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5, EventStatus.Failed), Ev("stock", 30, EventStatus.Cancelled)]);
        Assert.Equal(TransactionStatus.Cancelled, p.Status);
        Assert.Equal("none", DelayEvaluator.Evaluate(p, T0.AddHours(2), Flow).State);
        Assert.DoesNotContain(AlarmRules.Current(p, DelayEvaluator.Evaluate(p, T0.AddHours(2), Flow), T0.AddHours(2)), a => a.Kind == "overdue");
        Assert.Equal("cancelled", AlarmRules.Resolution(p, "payment"));
    }

    [Fact]
    public void Step_stats_measure_from_the_end_of_the_previous_stage()
    {
        var p = TimelineProjector.Project(Flow, [Ev("order", 0), Ev("payment", 5), Ev("stock", 20), Ev("shipped", 50)]);
        var stats = StepStats.Compute(Flow, p).ToDictionary(s => s.Code);
        Assert.Equal(300, stats["payment"].ElapsedSeconds);
        Assert.Equal(1200, stats["stock"].ElapsedSeconds);
        Assert.Equal(1800, stats["shipped"].ElapsedSeconds); // from stock at 20, not from payment
        Assert.False(stats["shipped"].Breached);
    }

    [Fact]
    public void Linear_flows_keep_their_behaviour()
    {
        var linear = new FlowDefinition(Guid.NewGuid(), Guid.NewGuid(), "f", "F", [new("a", "A", 0, null), new("b", "B", 1, 60), new("c", "C", 2, 60)]);
        var p = TimelineProjector.Project(linear, [Ev("a", 0), Ev("b", 1)]);
        var d = DelayEvaluator.Evaluate(p, T0.AddMinutes(3), linear);
        Assert.Equal(("overdue", "c"), (d.State, d.Step));
        Assert.Equal(T0.AddMinutes(1), d.StartedAt);
    }
}
