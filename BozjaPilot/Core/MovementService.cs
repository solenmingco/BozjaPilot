using Dalamud.Plugin.Ipc;
using System.Numerics;

namespace BozjaPilot.Core;

/// <summary>
///     Movement, mounting and anti-stuck. Pathing is delegated to vnavmesh over IPC; when
///     vnavmesh is missing we degrade to "walk toward the target" so the plugin still does
///     something useful instead of standing there.
/// </summary>
public sealed class MovementService
{
    private readonly Configuration config;

    private ICallGateSubscriber<Vector3, bool, object>? navMove;
    private ICallGateSubscriber<bool>? navIsRunning;
    private ICallGateSubscriber<object>? navStop;

    private Vector3 lastProgressPosition;
    private DateTime lastProgressTime = DateTime.UtcNow;
    private Vector3 lastCommandTarget;
    private DateTime lastCommandTime = DateTime.MinValue;

    private int stuckAttempts;
    private bool hasBaseline;

    public MovementService(Configuration configuration)
    {
        config = configuration;

        // NOTE: never touch game state here. Dalamud loads plugins off the main thread, and
        // ObjectTable.LocalPlayer throws "Not on main thread!" when called from the ctor,
        // which surfaces to the user as a generic "插件安装失败". The progress baseline is
        // taken lazily on the first Tick(), which always runs on the framework thread.
        SetupIpc();
        lastProgressTime = DateTime.UtcNow;
    }

    public bool NavmeshAvailable => navMove != null;

    public bool IsMovingToDestination { get; private set; }

    public Vector3 Destination { get; private set; }

    public int StuckAttempts => stuckAttempts;

    /// <summary>Set by the game loop when we gave up on the current target because of terrain.</summary>
    public bool StuckThisTick { get; private set; }

    private void SetupIpc()
    {
        try
        {
            var simple = Services.PluginInterface.GetIpcSubscriber<Vector3, bool, object>("vnavmesh.SimpleMove.PathfindAndMoveTo");
            if (simple.HasAction) navMove = simple;
            else
            {
                var legacy = Services.PluginInterface.GetIpcSubscriber<Vector3, bool, object>("vnavmesh.PathfindAndMoveTo");
                if (legacy.HasAction) navMove = legacy;
            }

            var running = Services.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.PathIsRunning");
            if (running.HasFunction) navIsRunning = running;

            var stop = Services.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Stop");
            navStop = stop;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "[BozjaPilot] vnavmesh IPC unavailable");
        }

        if (navMove == null)
            Services.Log.Warning("[BozjaPilot] vnavmesh not found - falling back to direct movement");
    }

    public bool IsPathRunning
    {
        get
        {
            if (navIsRunning == null) return IsMovingToDestination;
            try
            {
                return navIsRunning.InvokeFunc();
            }
            catch
            {
                return IsMovingToDestination;
            }
        }
    }

    public void MoveTo(Vector3 destination, bool force = false)
    {
        Destination = destination;
        IsMovingToDestination = true;

        var now = DateTime.UtcNow;
        var moved = Vec.Distance2D(lastCommandTarget, destination) > 3f;
        if (!force && !moved && (now - lastCommandTime).TotalMilliseconds < 400) return;

        lastCommandTarget = destination;
        lastCommandTime = now;

        if (navMove != null)
        {
            try
            {
                navMove.InvokeAction(destination, false);
                return;
            }
            catch (Exception ex)
            {
                Services.Log.Debug(ex, "[BozjaPilot] vnavmesh move failed, falling back");
            }
        }

        WalkToward(destination);
    }

    public void Stop()
    {
        // Avoid hammering the IPC stop gate every single tick.
        if (!IsMovingToDestination) return;
        IsMovingToDestination = false;
        if (navStop != null)
        {
            try
            {
                navStop.InvokeAction();
            }
            catch
            {
                // ignored - stopping is best effort
            }
        }
    }

    private bool warnedNoNavmesh;

    /// <summary>
    ///     No vnavmesh means no pathfinding. We refuse to fake it (driving the character through
    ///     walls is worse than standing still) and tell the user once per session.
    /// </summary>
    private void WalkToward(Vector3 destination)
    {
        if (warnedNoNavmesh) return;
        warnedNoNavmesh = true;
        Services.Print("未检测到 vnavmesh 寻路插件，无法自动赶路。请安装并启用 vnavmesh。");
    }

    /// <summary>
    ///     Keeps an eye on actual displacement. When we asked for movement but the character is
    ///     not getting anywhere, jump once, re-path once, then report stuck to the engine.
    /// </summary>
    public void Tick(DateTime now, Vector3 currentPosition, bool wantToMove)
    {
        StuckThisTick = false;

        // First tick after (re)load establishes the baseline, so the character is not
        // instantly considered "stuck" or "moving" because of a zero initial position.
        if (!hasBaseline)
        {
            hasBaseline = true;
            lastProgressPosition = currentPosition;
            lastProgressTime = now;
            return;
        }

        if (!wantToMove)
        {
            if (stuckAttempts != 0) stuckAttempts = 0;
            lastProgressPosition = currentPosition;
            lastProgressTime = now;
            return;
        }

        var progressed = Vec.Distance2D(currentPosition, lastProgressPosition) > 0.6f;
        if (progressed || GameState.InCombat)
        {
            lastProgressPosition = currentPosition;
            lastProgressTime = now;
            if (progressed) stuckAttempts = 0;
            return;
        }

        if ((now - lastProgressTime).TotalSeconds < config.StuckTimeout) return;

        stuckAttempts++;
        lastProgressTime = now;
        lastProgressPosition = currentPosition;

        switch (stuckAttempts)
        {
            case 1:
                GameState.Jump();
                MoveTo(Destination, true);
                Services.Log.Debug("[BozjaPilot] stuck: jumping and re-pathing");
                break;
            case 2:
                GameState.Dismount();
                GameState.Mount();
                MoveTo(Destination, true);
                Services.Log.Debug("[BozjaPilot] stuck: remounting and re-pathing");
                break;
            default:
                StuckThisTick = true;
                Stop();
                Services.Log.Debug("[BozjaPilot] stuck: giving up on this destination");
                break;
        }
    }

    public void ResetStuck()
    {
        stuckAttempts = 0;
        lastProgressTime = DateTime.UtcNow;
        hasBaseline = false;
    }

    /// <summary>Mount when the destination is far, sprint when it is close.</summary>
    public void HandleMountAndSprint(float distance)
    {
        if (GameState.InCombat)
        {
            if (GameState.IsMounted) GameState.Dismount();
            return;
        }

        if (config.AutoMount && distance > config.MountDistance && !GameState.IsMounted)
        {
            GameState.Mount();
            return;
        }

        if (config.AutoSprint && distance > 8f && distance <= config.MountDistance && !GameState.IsMounted)
            GameState.Sprint();
    }
}
