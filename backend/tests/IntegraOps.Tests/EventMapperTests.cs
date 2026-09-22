using System.Text.Json;
using IntegraOps.Application;
using Xunit;

namespace IntegraOps.Tests;

public sealed class EventMapperTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Maps_paths_constants_fallbacks_and_derives_ids()
    {
        var template = EventMapper.Parse(Json("""{"flow":"f","transactionId":"$.order.no","step":"invoice-created","occurredAt":"$.at","group":"$.class|standard"}"""));
        var events = EventMapper.Map(template, Json("""{"order":{"no":"SO-1"},"at":"2026-09-22T10:00:00+03:00"}"""));
        var e = Assert.Single(events);
        Assert.Null(e.Problem);
        Assert.Equal(("f", "SO-1", "invoice-created", "completed", "standard"), (e.Event.Flow, e.Event.TransactionId, e.Event.Step, e.Event.Status, e.Event.Group));
        // Same fact → same id; a different fact → different id.
        Assert.Equal(e.Event.EventId, Assert.Single(EventMapper.Map(template, Json("""{"order":{"no":"SO-1"},"at":"2026-09-22T10:00:00+03:00"}"""))).Event.EventId);
        Assert.NotEqual(e.Event.EventId, Assert.Single(EventMapper.Map(template, Json("""{"order":{"no":"SO-2"},"at":"2026-09-22T10:00:00+03:00"}"""))).Event.EventId);
        Assert.Equal(5, int.Parse(e.Event.EventId.ToString()[14..15]));
    }

    [Fact]
    public void Derived_ids_match_the_sdk_algorithm()
    {
        // uuid5(namespace, "x") computed with Python's uuid module for the same namespace.
        Assert.Equal(Guid.Parse("144d2318-4031-5d31-a0e0-712528b987b7"), EventMapper.Derive("x"));
    }

    [Fact]
    public void Records_path_arrays_root_reference_and_unix_times()
    {
        var template = EventMapper.Parse(Json("""{"flow":"f","transactionId":"$.id","step":"$.type","occurredAt":"$.created_at","eventId":"$.uid","group":"$$.shop","records":"$.events"}"""));
        var events = EventMapper.Map(template, Json("""{"shop":"tr","events":[{"uid":"8f1c0d3e-1a2b-4c3d-9e8f-0a1b2c3d4e5f","id":"SO-1","type":"a","created_at":1790073600},{"id":"SO-1","type":"b","created_at":"2026-09-22 10:15:00"}]}"""));
        Assert.Equal(2, events.Count);
        Assert.Equal("tr", events[0].Event.Group);
        Assert.Equal(Guid.Parse("8f1c0d3e-1a2b-4c3d-9e8f-0a1b2c3d4e5f"), events[0].Event.EventId);
        Assert.Equal("2026-09-22T10:40:00.0000000+00:00", events[0].Event.OccurredAt);
        Assert.Equal("2026-09-22T10:15:00.0000000+00:00", events[1].Event.OccurredAt);
        Assert.Null(events[1].Problem); // no source id → derived from the business fact
        Assert.NotEqual(events[0].Event.EventId, events[1].Event.EventId);
    }

    [Fact]
    public void Missing_values_are_reported_per_record_and_unknown_fields_rejected()
    {
        var template = EventMapper.Parse(Json("""{"flow":"f","transactionId":"$.no","step":"s","occurredAt":"$.at"}"""));
        var events = EventMapper.Map(template, Json("""[{"no":"A","at":"2026-09-22T10:00:00Z"},{"at":"2026-09-22T10:00:00Z"}]"""));
        Assert.Null(events[0].Problem);
        Assert.Contains("transactionId", events[1].Problem);
        Assert.Throws<RequestValidationException>(() => EventMapper.Parse(Json("""{"flow":"f","transactionId":"$.no","step":"s","occurredAt":"$.at","colour":"x"}""")));
        Assert.Throws<RequestValidationException>(() => EventMapper.Parse(Json("""{"flow":"f"}""")));
    }

    [Fact]
    public void Catalog_samples_map_without_problems()
    {
        foreach (var entry in EventMapper.Catalog)
        {
            var events = EventMapper.Map(entry.Template, Json(entry.Sample));
            Assert.NotEmpty(events);
            Assert.All(events, e => Assert.Null(e.Problem));
        }
    }
}
