using IntegraOps.Application;
using Xunit;

namespace IntegraOps.Tests;

public sealed class ValidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
    private static SubmitEventRequest Valid() => new(Guid.NewGuid(), "order-flow", "ORDER-123",
        "order-created", "completed", "2026-09-07T12:59:00+03:00");

    [Fact]
    public void Explicit_timezone_is_normalized_to_utc()
    {
        var result = InputValidation.Event(Valid(), Now);
        Assert.Equal(TimeSpan.Zero, result.OccurredAt.Offset);
        Assert.Equal(Now.AddMinutes(-1), result.OccurredAt);
    }

    [Theory]
    [InlineData("2026-09-07T10:00:00")]
    [InlineData("07/09/2026 10:00:00")]
    [InlineData("2026-09-07T10:06:00Z")]
    public void Ambiguous_or_excessively_future_time_is_rejected(string time)
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { OccurredAt = time }, Now));
        Assert.Contains("occurredAt", exception.Errors.Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("success")]
    [InlineData("stuck")]
    public void Producer_cannot_invent_statuses(string status)
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { Status = status }, Now));
        Assert.Contains("status", exception.Errors.Keys);
    }

    [Fact]
    public void Missing_event_identity_is_rejected()
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { EventId = Guid.Empty }, Now));
        Assert.Contains("eventId", exception.Errors.Keys);
    }

    [Fact]
    public void Repeating_step_codes_would_make_a_flow_ambiguous()
    {
        var request = new CreateFlowRequest("order-flow", "Sipariş",
            [new("created", "İlk", null), new("created", "İkinci", 300)]);
        Assert.Throws<RequestValidationException>(() => InputValidation.Flow(request));
    }

    [Fact]
    public void Subsequent_step_requires_a_positive_deadline()
    {
        var request = new CreateFlowRequest("order-flow", "Sipariş",
            [new("created", "Sipariş", null), new("paid", "Ödeme", null)]);
        var exception = Assert.Throws<RequestValidationException>(() => InputValidation.Flow(request));
        Assert.Contains("steps[1].maxDelaySeconds", exception.Errors.Keys);
    }
}
