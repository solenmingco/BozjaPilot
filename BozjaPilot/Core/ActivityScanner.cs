using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaPilot.Core;

/// <summary>
///     Reads <c>PublicContentBozja.DynamicEventContainer</c> and turns the raw events into
///     <see cref="Activity" /> snapshots with travel estimates attached.
/// </summary>
public sealed class ActivityScanner(Configuration config)
{
    private const float MountedSpeed = 18f;
    private const float FootSpeed = 7.5f;
    private const float TerrainPenalty = 1.18f;

    private readonly Dictionary<ushort, Activity> history = [];

    public IReadOnlyList<Activity> Current { get; private set; } = [];

    public DateTime LastScan { get; private set; } = DateTime.MinValue;

    public static float TravelSpeed(bool mounted) => (mounted ? MountedSpeed : FootSpeed) / TerrainPenalty;

    /// <summary>
    ///     Refresh the snapshot. Scans are throttled by the caller; this method always scans.
    /// </summary>
    public unsafe IReadOnlyList<Activity> Scan(DateTime now)
    {
        var result = new List<Activity>();
        var player = GameState.PlayerPosition;
        var mounted = GameState.IsMounted;
        var speed = TravelSpeed(mounted);

        var container = GameState.Events;
        if (container != null)
        {
            var events = container->Events;
            var registeredId = GameState.RegisteredEventId;

            foreach (var ev in events)
            {
                if (ev.State == DynamicEventState.Inactive) continue;
                if (ev.DynamicEventId == 0) continue;

                if (!history.TryGetValue(ev.DynamicEventId, out var activity))
                {
                    activity = new Activity
                    {
                        Id = ev.DynamicEventId,
                        FirstSeen = now
                    };
                    history[ev.DynamicEventId] = activity;
                }

                activity.Name = ev.Name.ToString();
                activity.Position = ev.MapMarker.Position;
                activity.Radius = ev.MapMarker.Radius;
                activity.State = ev.State;
                activity.SecondsLeft = ev.SecondsLeft;
                activity.Duration = ev.SecondsDuration;
                activity.Participants = ev.Participants;
                activity.Progress = ev.Progress;
                activity.LastSeen = now;
                activity.RegisteredByMe = registeredId != 0 && registeredId == ev.DynamicEventId;

                // Sticky CE classification - see Activity.IsCriticalEngagement.
                if (ev.State == DynamicEventState.Register && !activity.IsCriticalEngagement)
                {
                    activity.IsCriticalEngagement = true;
                    RememberCriticalEngagement(ev.DynamicEventId);
                }

                if (activity.Position.IsZero())
                {
                    Services.Log.Debug($"[BozjaPilot] event {ev.DynamicEventId} has no map marker position, skipping");
                    continue;
                }

                activity.Distance = Vec.Distance2D(player, activity.Position);
                activity.Eta = activity.Distance / speed;
                result.Add(activity);
            }
        }

        // Drop events that vanished (finished or left the instance).
        foreach (var stale in history.Values.Where(a => (now - a.LastSeen).TotalSeconds > 1.5).ToList())
        {
            history.Remove(stale.Id);
        }

        Current = result;
        LastScan = now;
        return result;
    }

    public Activity? Find(ushort id) => history.GetValueOrDefault(id);

    public void Clear()
    {
        history.Clear();
        Current = [];
    }

    private void RememberCriticalEngagement(ushort id)
    {
        if (config.KnownCriticalEngagementIds.Contains(id)) return;
        config.KnownCriticalEngagementIds.Add(id);
        config.Save();
        Services.Log.Debug($"[BozjaPilot] learned CE id {id}");
    }

    /// <summary>
    ///     Seed the CE flag from the ids we learned in previous sessions, so a CE that we first
    ///     see during its battle phase is still treated as a CE.
    /// </summary>
    public void ApplyKnownIds()
    {
        foreach (var activity in Current)
        {
            if (!activity.IsCriticalEngagement && config.KnownCriticalEngagementIds.Contains(activity.Id))
                activity.IsCriticalEngagement = true;
        }
    }
}
