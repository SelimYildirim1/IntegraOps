using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class AlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static Projection Waiting() => new(TransactionStatus.InProgress,
        [new("erp-created", "ERP", 0, null, "completed", Now.AddSeconds(-31), null),
         new("invoice-created", "Fatura", 1, 30, "not_observed", null, null)]);

    [Fact]
    public void Overdue_does_not_claim_a_failed_invoice()
    {
        var projection = Waiting();
        var alarm = Assert.Single(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("overdue", alarm.Kind); Assert.Equal("invoice-created", alarm.Step);
        Assert.Null(alarm.Error); Assert.Equal(TransactionStatus.InProgress, projection.Status);
    }
    [Fact]
    public void Late_completion_removes_condition_and_explains_resolution()
    {
        var projection = new Projection(TransactionStatus.Completed,
            [Waiting().Steps[0], Waiting().Steps[1] with { Observation = "completed", OccurredAt = Now }]);
        Assert.Empty(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("step_completed", AlarmRules.Resolution(projection, "invoice-created"));
    }
    [Fact]
    public void Failure_elsewhere_does_not_claim_missing_invoice_completed()
    {
        var projection = new Projection(TransactionStatus.Failed,
            [Waiting().Steps[0] with { Observation = "failed", Error = "ERP failure" }, Waiting().Steps[1]]);
        var alarm = Assert.Single(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("failed", alarm.Kind); Assert.Equal("erp-created", alarm.Step);
        Assert.Equal("tracking_changed", AlarmRules.Resolution(projection, "invoice-created"));
    }
    [Fact]
    public void Before_deadline_there_is_no_alarm()
    {
        var projection = Waiting();
        Assert.Empty(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now.AddSeconds(-2))));
    }
}
