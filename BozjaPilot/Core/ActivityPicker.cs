namespace BozjaPilot.Core;

public enum PickReason
{
    None,
    CriticalEngagementRegistration,
    CriticalEngagementBattle,
    Skirmish
}

public sealed record PickResult(Activity? Activity, PickReason Reason, string Explanation);

/// <summary>
///     Pure decision logic - no game access, no side effects. Keeping this separate makes the
///     "why did it pick that" question answerable, which is exactly what the UI shows.
/// </summary>
public sealed class ActivityPicker(Configuration config)
{
    private const float CeBonus = 900f;

    public PickResult Pick(IReadOnlyList<Activity> activities, bool registeredToCe)
    {
        if (registeredToCe)
        {
            var mine = activities.FirstOrDefault(a => a.RegisteredByMe);
            if (mine != null)
                return new PickResult(mine, mine.IsCriticalEngagement
                    ? PickReason.CriticalEngagementBattle
                    : PickReason.Skirmish, "已报名，留在当前活动");
        }

        var registration = BestRegistration(activities);
        if (registration != null)
            return new PickResult(registration, PickReason.CriticalEngagementRegistration,
                $"CE 报名中，{registration.Eta:F0}s 可到，剩余 {registration.SecondsLeft:F0}s");

        var running = BestRunning(activities);
        if (running != null)
            return new PickResult(running,
                running.IsCriticalEngagement ? PickReason.CriticalEngagementBattle : PickReason.Skirmish,
                $"评分 {running.Score:F0}，剩余 {running.SecondsLeft:F0}s / 路程 {running.Eta:F0}s");

        return new PickResult(null, PickReason.None, "没有可参与的活动");
    }

    /// <summary>
    ///     CEs still in their registration window, soonest reachable first. A CE we cannot
    ///     physically reach before registration closes is worthless and gets dropped.
    /// </summary>
    public Activity? BestRegistration(IReadOnlyList<Activity> activities)
    {
        if (!config.JoinCriticalEngagements) return null;

        var now = DateTime.UtcNow;
        return activities
            .Where(a => a.IsRegistrationOpen)
            .Where(a => !a.RegisteredByMe)
            .Where(a => !a.Blacklisted || a.BlacklistedUntil < now)
            .Where(a => a.Eta + config.RegisterSafetyMargin <= a.SecondsLeft)
            .OrderBy(a => a.Eta)
            .FirstOrDefault();
    }

    public Activity? BestRunning(IReadOnlyList<Activity> activities)
    {
        var now = DateTime.UtcNow;

        var candidates = activities
            .Where(a => a.IsFighting)
            .Where(a => !a.RegisteredByMe)
            .Where(a => !a.Blacklisted || a.BlacklistedUntil < now)
            .Where(a => a.IsCriticalEngagement ? config.JoinCriticalEngagements : config.JoinSkirmishes)
            // A CE we never registered for cannot be joined once the battle started.
            .Where(a => !a.IsCriticalEngagement || a.RegisteredByMe)
            .Where(a => a.Distance <= config.MaxTravelDistance)
            .Where(a => a.RemainingAfterArrival >= config.MinTimeOnArrival)
            .ToList();

        foreach (var candidate in candidates)
            candidate.Score = Score(candidate);

        return candidates.OrderByDescending(a => a.Score).FirstOrDefault();
    }

    /// <summary>
    ///     Remaining useful time dominates, travel time is a soft penalty, and progress is a
    ///     small tie-breaker (a nearly-finished event is risky, not attractive).
    /// </summary>
    public float Score(Activity activity)
    {
        var remaining = Math.Clamp(activity.RemainingAfterArrival, 0f, 300f);
        var score = remaining
                    - activity.Eta * 0.6f
                    - activity.Progress * 0.15f
                    + activity.Participants * 0.5f;

        if (activity.IsCriticalEngagement) score += CeBonus;

        return score;
    }
}
