namespace IntegraOps.Domain;

/// <summary>
/// Working-time calendar: allowances count only working seconds inside the given hours on
/// working days that are not holidays, in the calendar's time zone. Null calendar means 24/7.
/// </summary>
public sealed record BusinessCalendar(
    Guid Id, string Name, string TimeZoneId,
    IReadOnlySet<DayOfWeek> WorkDays, TimeOnly Start, TimeOnly End,
    IReadOnlySet<DateOnly> Holidays)
{
    private TimeZoneInfo? zone;
    private TimeZoneInfo Zone => zone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public bool IsWorkingDay(DateOnly day) => WorkDays.Contains(day.DayOfWeek) && !Holidays.Contains(day);

    /// <summary>Adds <paramref name="seconds"/> of working time to <paramref name="start"/>.</summary>
    public DateTimeOffset AddWorkingSeconds(DateTimeOffset start, int seconds)
    {
        if (seconds <= 0) return start;
        var local = TimeZoneInfo.ConvertTime(start, Zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        var remaining = (double)seconds;
        // Guard against calendars with no working days at all.
        for (var guard = 0; guard < 3660; guard++, day = day.AddDays(1))
        {
            if (!IsWorkingDay(day)) continue;
            var open = At(day, Start);
            var close = At(day, End);
            var cursor = local > open ? local : open;
            if (cursor >= close) continue;
            var available = (close - cursor).TotalSeconds;
            if (remaining <= available) return cursor.AddSeconds(remaining).ToUniversalTime();
            remaining -= available;
            local = close; // the next day starts from its opening time
        }
        throw new InvalidOperationException("Takvimde çalışma günü bulunamadı.");
    }

    /// <summary>Working seconds elapsed between two instants; zero when <paramref name="to"/> is not later.</summary>
    public long WorkingSecondsBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) return 0;
        var a = TimeZoneInfo.ConvertTime(from, Zone);
        var b = TimeZoneInfo.ConvertTime(to, Zone);
        var day = DateOnly.FromDateTime(a.DateTime);
        var last = DateOnly.FromDateTime(b.DateTime);
        double total = 0;
        for (; day <= last; day = day.AddDays(1))
        {
            if (!IsWorkingDay(day)) continue;
            var open = At(day, Start);
            var close = At(day, End);
            var lo = a > open ? a : open;
            var hi = b < close ? b : close;
            if (hi > lo) total += (hi - lo).TotalSeconds;
        }
        return (long)Math.Round(total);
    }

    private DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var unspecified = day.ToDateTime(time);
        // Inside a DST gap the wall-clock time does not exist; take the offset after the transition.
        var offset = Zone.IsInvalidTime(unspecified) ? Zone.GetUtcOffset(unspecified.AddHours(1)) : Zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    public static IReadOnlySet<DayOfWeek> Days(IEnumerable<int> isoDays) =>
        isoDays.Select(d => d == 7 ? DayOfWeek.Sunday : (DayOfWeek)d).ToHashSet();
}
