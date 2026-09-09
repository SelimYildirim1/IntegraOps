namespace IntegraOps.Domain;

// Gecikme kontrolünün sonucunu taşır.
public sealed record DelayAssessment(
    string State,
    string? Step,
    DateTimeOffset? StartedAt,
    DateTimeOffset? DeadlineAt
);

public static class DelayEvaluator
{
    public static DelayAssessment Evaluate(
        Projection projection,
        DateTimeOffset now)
    {
        var none = new DelayAssessment("none", null, null, null);

        // Yalnız devam eden işlemlerde beklenen adımı kontrol ediyoruz.
        if (projection.Status != TransactionStatus.InProgress)
            return none;

        var steps = projection.Steps
            .OrderBy(step => step.Position)
            .ToArray();

        // Sıradaki ilk tamamlanmamış adımı bul.
        var nextIndex = Array.FindIndex(
            steps,
            step => step.Observation != "completed"
        );

        // İlk adım için saati başlatacak önceki bir adım yok.
        // -1 ise tamamlanmamış adım bulunamadı.
        if (nextIndex <= 0)
            return none;

        var next = steps[nextIndex];
        var previous = steps[nextIndex - 1];

        if (next.Observation != "not_observed"
            || previous.Observation != "completed"
            || previous.OccurredAt is not { } startedAt
            || next.MaxDelaySeconds is not { } seconds)
        {
            return none;
        }

        var deadlineAt = startedAt.AddSeconds(seconds);

        var state = now >= deadlineAt
            ? "overdue"
            : "waiting";

        return new DelayAssessment(
            state,
            next.Code,
            startedAt,
            deadlineAt
        );
    }
}