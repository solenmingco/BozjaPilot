using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System.Numerics;

namespace BozjaPilot.Core;

public enum AutomationState
{
    Disabled,
    OutOfZone,
    Busy,
    Dead,
    Idle,
    Travelling,
    Registering,
    Fighting,
    Recovering,
    Stuck
}

/// <summary>
///     The brain. One tick = "look at the world, decide, act". Every decision is recorded in
///     <see cref="StatusMessage" /> so the window can explain itself instead of looking frozen.
/// </summary>
public sealed class AutomationEngine
{
    private readonly ActivityPicker picker;
    private readonly Configuration config;

    private DateTime idleSince = DateTime.UtcNow;
    private DateTime lastScan = DateTime.MinValue;
    private DateTime deadSince = DateTime.MinValue;
    private DateTime lastReturnAttempt = DateTime.MinValue;

    private ushort committedId;
    private uint mettleAtEnable;

    public AutomationEngine(Configuration configuration)
    {
        config = configuration;
        Scanner = new ActivityScanner(configuration);
        picker = new ActivityPicker(configuration);
        Movement = new MovementService(configuration);
        Combat = new CombatService(configuration);
    }

    public ActivityScanner Scanner { get; }

    public MovementService Movement { get; }

    public CombatService Combat { get; }

    public AutomationState State { get; private set; } = AutomationState.Disabled;

    public string StatusMessage { get; private set; } = "未启动";

    public Activity? Target { get; private set; }

    public bool Enabled => config.Enabled;

    public int ActivitiesJoined { get; private set; }

    public int CriticalEngagementsJoined { get; private set; }

    public uint SessionMettleGain
    {
        get
        {
            var mettle = GameState.Mettle;
            return mettle > mettleAtEnable ? mettle - mettleAtEnable : 0;
        }
    }

    public float MettlePerHour
    {
        get
        {
            var elapsed = (DateTime.UtcNow - enabledAt).TotalHours;
            return elapsed < 0.01 ? 0f : (float)(SessionMettleGain / elapsed);
        }
    }

    private DateTime enabledAt = DateTime.UtcNow;

    public void Toggle()
    {
        if (config.Enabled) Disable();
        else Enable();
    }

    public void Enable()
    {
        if (config.Enabled) return;

        config.Enabled = true;
        config.Save();

        mettleAtEnable = GameState.Mettle;
        enabledAt = DateTime.UtcNow;
        ActivitiesJoined = 0;
        CriticalEngagementsJoined = 0;
        committedId = 0;

        Scanner.Clear();
        Movement.ResetStuck();
        Services.Print("已启动。");
    }

    public void Disable()
    {
        if (!config.Enabled) return;

        config.Enabled = false;
        config.Save();

        StopEverything();
        State = AutomationState.Disabled;
        StatusMessage = "已停止";
        Services.Print("已停止。");
    }

    private void StopEverything()
    {
        committedId = 0;
        Target = null;
        Movement.Stop();
        Combat.Disengage();
    }

    public void Tick(DateTime now)
    {
        if (!config.Enabled)
        {
            State = AutomationState.Disabled;
            StatusMessage = "未启动";
            return;
        }

        if (!Services.ClientState.IsLoggedIn)
        {
            State = AutomationState.OutOfZone;
            StatusMessage = "未登录";
            return;
        }

        if (!GameState.IsInBozja)
        {
            if (State != AutomationState.OutOfZone)
            {
                StopEverything();
                Scanner.Clear();
            }

            State = AutomationState.OutOfZone;
            StatusMessage = "不在博兹雅 / 天佑女王";
            return;
        }

        LearnTerritory();

        if (GameState.IsBusy)
        {
            State = AutomationState.Busy;
            StatusMessage = "过场 / 切换区域中";
            Movement.Stop();
            return;
        }

        if (GameState.IsDead)
        {
            HandleDeath(now);
            return;
        }

        deadSince = DateTime.MinValue;

        if ((now - lastScan).TotalMilliseconds >= 200)
        {
            Scanner.Scan(now);
            Scanner.ApplyKnownIds();
            lastScan = now;
        }

        var activities = Scanner.Current;
        var registeredId = GameState.RegisteredEventId;
        var registeredActivity = registeredId == 0
            ? null
            : activities.FirstOrDefault(a => a.Id == registeredId);

        var pick = picker.Pick(activities, registeredActivity != null);
        var chosen = ChooseWithCommitment(pick, activities, registeredActivity, now);

        if (chosen == null)
        {
            Idle(now);
            return;
        }

        Target = chosen;

        if (chosen.IsRegistrationOpen && !chosen.RegisteredByMe)
        {
            RunRegistration(chosen);
            return;
        }

        if (chosen.IsFighting)
        {
            RunFight(chosen);
            return;
        }

        // Registered and waiting (registration window or warm-up): hold position, the game
        // teleports us in when the battle starts.
        if (chosen.State is DynamicEventState.Register or DynamicEventState.Warmup)
        {
            if (chosen.RegisteredByMe) CountActivity(chosen);
            RunWarmup(chosen);
            return;
        }

        Idle(now);
    }

    private Activity? ChooseWithCommitment(PickResult pick, IReadOnlyList<Activity> activities,
        Activity? registeredActivity, DateTime now)
    {
        if (registeredActivity != null) return registeredActivity;

        var committed = committedId == 0 ? null : activities.FirstOrDefault(a => a.Id == committedId);
        var committedUseful = committed != null && IsStillUseful(committed, now);

        // A CE that just opened registration always beats whatever skirmish we are doing.
        if (committedUseful && pick.Reason == PickReason.CriticalEngagementRegistration
                            && (committed!.Id != pick.Activity!.Id)
                            && (!committed.IsCriticalEngagement || !committed.RegisteredByMe))
        {
            if (config.LeaveSkirmishForCe) return Adopt(pick.Activity!, now);
            return committed;
        }

        if (committedUseful && committed!.BlacklistedUntil < now) return committed;

        if (pick.Activity == null) return null;

        // Avoid oscillating between two equally good targets: only switch if clearly better.
        if (committedUseful && committed!.Id != pick.Activity.Id)
        {
            var better = pick.Activity.Score > committed.Score + 120f
                         || pick.Reason == PickReason.CriticalEngagementRegistration;
            if (!better) return committed;
        }

        return Adopt(pick.Activity, now);
    }

    private Activity Adopt(Activity activity, DateTime now)
    {
        if (committedId == activity.Id) return activity;

        if (committedId != 0)
            Services.Log.Debug($"[BozjaPilot] switching target {committedId} -> {activity.Id} ({activity.Name})");

        committedId = activity.Id;
        Movement.ResetStuck();
        idleSince = now;
        return activity;
    }

    private bool IsStillUseful(Activity activity, DateTime now)
    {
        if (activity.Blacklisted && activity.BlacklistedUntil > now) return false;
        if (activity.State == DynamicEventState.Inactive)
            return false;
        if (activity.IsRegistrationOpen) return activity.Eta + config.RegisterSafetyMargin <= activity.SecondsLeft;
        return activity.RemainingAfterArrival >= config.MinTimeOnArrival || activity.Distance < 40f;
    }

    // ------------------------------------------------------------------ states

    private void RunRegistration(Activity activity)
    {
        State = AutomationState.Registering;

        var distance = Vec.Distance2D(GameState.PlayerPosition, activity.Position);

        StatusMessage = $"赶往 CE 报名：{activity.Name}（{distance:F0} 米，剩 {activity.SecondsLeft:F0}s）";

        // Already deep inside the circle: registration is automatic, just hold position.
        if (distance <= 4f)
        {
            Movement.Stop();
            Movement.HandleMountAndSprint(0f);
            return;
        }

        Travel(activity.Position, distance, activity);
    }

    private void RunWarmup(Activity activity)
    {
        State = AutomationState.Registering;
        StatusMessage = $"已报名 {activity.Name}，等待开战";

        var distance = Vec.Distance2D(GameState.PlayerPosition, activity.Position);
        var radius = MathF.Max(activity.Radius, 18f);
        if (distance > radius) Travel(activity.Position, distance, activity);
        else Movement.Stop();

        Combat.Disengage();
    }

    private void RunFight(Activity activity)
    {
        State = AutomationState.Fighting;
        CountActivity(activity);

        var distance = Vec.Distance2D(GameState.PlayerPosition, activity.Position);
        var nearestEnemy = NearestHostileDistance(config.TargetSearchRadius);

        StatusMessage = activity.IsCriticalEngagement
            ? $"紧急任务中：{activity.Name}（剩 {activity.SecondsLeft:F0}s）"
            : $"遭遇战中：{activity.Name}（剩 {activity.SecondsLeft:F0}s，进度 {activity.Progress}%）";

        Combat.Engage(activity);

        if (nearestEnemy < config.CombatStopDistance)
        {
            if (config.StopMovingInCombat) Movement.Stop();
            Movement.HandleMountAndSprint(0f);
            return;
        }

        // Nothing to hit nearby - close the distance to the fight, mounting if it is a long way.
        if (distance > 6f)
        {
            Travel(activity.Position, distance, activity);
            return;
        }

        Movement.Stop();
    }

    private void Idle(DateTime now)
    {
        if (State != AutomationState.Idle) idleSince = now;

        State = AutomationState.Idle;
        StatusMessage = "待机：暂无可用活动";

        Combat.Disengage();
        Movement.Stop();

        if (config.ReturnWhenIdleSeconds <= 0) return;

        var idleFor = (now - idleSince).TotalSeconds;
        StatusMessage = $"待机 {idleFor:F0}s：暂无可用活动";

        if (idleFor < config.ReturnWhenIdleSeconds || (now - lastReturnAttempt).TotalMinutes < 2) return;

        lastReturnAttempt = now;
        GameState.ReturnToCamp();
        StatusMessage = "长时间无活动，返回营地";
    }

    private void HandleDeath(DateTime now)
    {
        State = AutomationState.Dead;
        Movement.Stop();
        Combat.Disengage();

        if (deadSince == DateTime.MinValue) deadSince = now;

        var deadFor = (now - deadSince).TotalSeconds;

        if (!config.AutoRelease)
        {
            StatusMessage = $"已阵亡 {deadFor:F0}s（自动复活已关闭）";
            return;
        }

        StatusMessage = deadFor < config.WaitForRaiseSeconds
            ? $"已阵亡 {deadFor:F0}s，等待复活"
            : $"已阵亡 {deadFor:F0}s，返回营地";

        if (deadFor >= config.WaitForRaiseSeconds && (now - lastReturnAttempt).TotalSeconds > 5)
        {
            lastReturnAttempt = now;
            GameState.ReturnToCamp();
            deadSince = now;
        }
    }

    private void Travel(Vector3 destination, float distance, Activity activity)
    {
        State = AutomationState.Travelling;
        Movement.MoveTo(destination);
        Movement.HandleMountAndSprint(distance);
        Movement.Tick(DateTime.UtcNow, GameState.PlayerPosition, true);

        if (Movement.StuckThisTick)
        {
            Blacklist(activity, "卡住");
            State = AutomationState.Stuck;
            StatusMessage = $"卡住了，放弃 {activity.Name}";
        }
    }

    public void Blacklist(Activity activity, string reason)
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        activity.Blacklisted = true;
        activity.BlacklistedUntil = until;
        if (committedId == activity.Id) committedId = 0;
        Services.Log.Debug($"[BozjaPilot] blacklisting {activity.Id} ({reason}) for 90s");
    }

    private float NearestHostileDistance(float max)
    {
        var player = GameState.PlayerPosition;
        var best = max;
        foreach (var obj in Services.Objects)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc) continue;
            if (npc.IsDead || !npc.IsTargetable) continue;
            if (npc.SubKind != (byte)Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant) continue;
            var distance = Vec.Distance2D(npc.Position, player);
            if (distance < best) best = distance;
        }

        return best;
    }

    private void LearnTerritory()
    {
        var territory = Services.ClientState.TerritoryType;
        if (territory == 0) return;
        if (config.KnownBozjaTerritories.Contains(territory)) return;

        config.KnownBozjaTerritories.Add(territory);
        config.Save();
        Services.Log.Debug($"[BozjaPilot] learned Bozja territory {territory}");
    }

    private ushort countedActivityId;

    /// <summary>Count each activity once, the first time we actually take part in it.</summary>
    private void CountActivity(Activity activity)
    {
        if (countedActivityId == activity.Id) return;
        countedActivityId = activity.Id;
        ActivitiesJoined++;
        if (activity.IsCriticalEngagement) CriticalEngagementsJoined++;
        idleSince = DateTime.UtcNow;
        Services.Log.Debug($"[BozjaPilot] joined {activity}");
    }

    /// <summary>Manual override from the activity list ("前往" button).</summary>
    public void ForceTarget(Activity activity)
    {
        committedId = activity.Id;
        Movement.ResetStuck();
        idleSince = DateTime.UtcNow;
        Services.Print($"已指定目标：{activity.Name}");
    }
}
