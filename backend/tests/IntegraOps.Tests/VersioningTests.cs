using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class VersioningTests
{
    // Monday–Friday 09:00–18:00 in Istanbul (UTC+3), 29 October is a holiday.
    private static readonly BusinessCalendar Office = new(Guid.NewGuid(), "Ofis", "Europe/Istanbul",
        BusinessCalendar.Days([1, 2, 3, 4, 5]), new TimeOnly(9, 0), new TimeOnly(18, 0), new HashSet<DateOnly> { new(2026, 10, 29) });

    private static DateTimeOffset Istanbul(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Working_time_inside_a_day_is_plain_addition()
    {
        var deadline = Office.AddWorkingSeconds(Istanbul(2026, 9, 22, 10), 3600);
        Assert.Equal(Istanbul(2026, 9, 22, 11), deadline);
    }

    [Fact]
    public void Working_time_rolls_over_evening_and_weekend()
    {
        // Friday 17:00 + 2 hours of work: 1 h until close, 1 h on Monday morning.
        var deadline = Office.AddWorkingSeconds(Istanbul(2026, 9, 25, 17), 7200);
        Assert.Equal(Istanbul(2026, 9, 28, 10), deadline);
    }

    [Fact]
    public void Event_outside_hours_starts_at_next_opening_and_skips_holidays()
    {
        // Wednesday 28 Oct 22:00 → Thursday is a holiday → Friday 09:00 + 30 min.
        var deadline = Office.AddWorkingSeconds(Istanbul(2026, 10, 28, 22), 1800);
        Assert.Equal(Istanbul(2026, 10, 30, 9, 30), deadline);
    }

    [Fact]
    public void Elapsed_working_seconds_ignore_nights()
    {
        var elapsed = Office.WorkingSecondsBetween(Istanbul(2026, 9, 22, 17), Istanbul(2026, 9, 23, 10));
        Assert.Equal(7200, elapsed);
        Assert.Equal(0, Office.WorkingSecondsBetween(Istanbul(2026, 9, 26, 10), Istanbul(2026, 9, 27, 10)));
    }

    private static FlowDefinition Flow(BusinessCalendar? calendar = null) => new(Guid.NewGuid(), Guid.NewGuid(), "f", "F", [
        new("start", "Start", 0, null),
        new("finish", "Finish", 1, 3600, new Dictionary<string, int> { ["vip"] = 600 })], Guid.NewGuid(), 2, calendar);

    private static ObservedEvent Done(string step, DateTimeOffset at) => new(Guid.NewGuid(), step, EventStatus.Completed, at, at.AddSeconds(1), null);

    [Fact]
    public void Group_override_shortens_the_allowance()
    {
        var start = Istanbul(2026, 9, 22, 10);
        var standard = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(), [Done("start", start)]), start.AddMinutes(20), Flow());
        var vip = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(), [Done("start", start)], "vip"), start.AddMinutes(20), Flow());
        Assert.Equal("waiting", standard.State);
        Assert.Equal("overdue", vip.State);
        Assert.Equal(start.AddMinutes(10), vip.DeadlineAt);
    }

    [Fact]
    public void Calendar_moves_the_deadline_to_working_time()
    {
        var flow = Flow(Office);
        var start = Istanbul(2026, 9, 22, 17, 30); // 30 min left today, 30 min tomorrow morning
        var result = DelayEvaluator.Evaluate(TimelineProjector.Project(flow, [Done("start", start)]), start.AddHours(1), flow);
        Assert.Equal("waiting", result.State);
        Assert.Equal(Istanbul(2026, 9, 23, 9, 30), result.DeadlineAt);
    }

    [Fact]
    public void Step_stats_measure_working_time_and_flag_breaches()
    {
        var flow = Flow(Office);
        var start = Istanbul(2026, 9, 22, 17);
        var projection = TimelineProjector.Project(flow, [Done("start", start), Done("finish", Istanbul(2026, 9, 23, 10))]);
        var stat = Assert.Single(StepStats.Compute(flow, projection));
        Assert.Equal(7200, stat.ElapsedSeconds);
        Assert.True(stat.Breached);
        var quick = TimelineProjector.Project(flow, [Done("start", start), Done("finish", start.AddMinutes(30))]);
        Assert.False(Assert.Single(StepStats.Compute(flow, quick)).Breached);
    }

    [Fact]
    public void Diff_lists_added_removed_changed_and_calendar()
    {
        var v1 = new FlowDefinition(Guid.NewGuid(), Guid.NewGuid(), "f", "F", [
            new("a", "A", 0, null), new("b", "B", 1, 60), new("c", "C", 2, 60)]);
        var v2 = new FlowDefinition(v1.Id, v1.ProjectId, "f", "F", [
            new("a", "A", 0, null), new("c", "C2", 1, 120, new Dictionary<string, int> { ["vip"] = 30 }), new("d", "D", 2, 10)], Calendar: Office);
        var changes = FlowDiff.Compare(v1, v2);
        Assert.Contains(changes, x => x.Kind == "moved" && x.Code == "c");
        Assert.Contains(changes, x => x.Kind == "renamed" && x.Code == "c");
        Assert.Contains(changes, x => x.Kind == "allowance" && x.Code == "c");
        Assert.Contains(changes, x => x.Kind == "override_added" && x.Code == "c");
        Assert.Contains(changes, x => x.Kind == "added" && x.Code == "d");
        Assert.Contains(changes, x => x.Kind == "removed" && x.Code == "b");
        Assert.Contains(changes, x => x.Kind == "calendar");
    }
}
