using BozjaPilot.Core;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System.Numerics;

namespace BozjaPilot.UI;

public sealed class MainWindow : WindowBase
{
    private static readonly Vector4 Red = new(0.94f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.42f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.96f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("BozjaPilot###BozjaPilotMain")
    {
        this.plugin = plugin;
    }

    private AutomationEngine Engine => plugin.Engine;

    protected override void DrawContents()
    {
        DrawHeader();
        ImGui.Separator();
        DrawStats();
        ImGui.Separator();

        if (plugin.Config.ShowActivityList) DrawActivityTable();

        ImGui.Separator();
        DrawFooter();
    }

    private void DrawHeader()
    {
        var engine = Engine;

        ImGui.TextUnformatted("状态");
        ImGui.SameLine(70);
        ImGui.TextColored(StateColor(engine.State), $"{StateText(engine.State)}");

        ImGui.TextUnformatted("说明");
        ImGui.SameLine(70);
        ImGui.TextWrapped(engine.StatusMessage);

        var buttonLabel = engine.Enabled ? "停止" : "开始";
        if (ImGui.Button($"{buttonLabel}###toggle", new Vector2(90, 0))) engine.Toggle();
        ImGui.SameLine();
        if (ImGui.Button("设置###cfg", new Vector2(70, 0))) plugin.OpenConfig();
        ImGui.SameLine();
        if (ImGui.Button("扫描###scan", new Vector2(70, 0))) DumpScan();
    }

    private void DumpScan()
    {
        var activities = Engine.Scanner.Scan(DateTime.UtcNow);
        Services.Print($"扫描到 {activities.Count} 个活动");
        foreach (var activity in activities) Services.Print($"  {activity}");
    }

    private void DrawStats()
    {
        ImGui.TextColored(Grey, $"当前战果 {GameState.Mettle:N0}   本次 +{Engine.SessionMettleGain:N0}   约 {Engine.MettlePerHour:N0}/小时");
        ImGui.TextColored(Grey, $"参与 {Engine.ActivitiesJoined} 次（CE {Engine.CriticalEngagementsJoined} 次）   寻路 {(Engine.Movement.NavmeshAvailable ? "vnavmesh 已连接" : "未找到 vnavmesh")}");
    }

    private void DrawActivityTable()
    {
        var activities = Engine.Scanner.Current;
        ImGui.TextUnformatted($"活动列表（{activities.Count}）");

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                                      | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("activities", 6, flags, new Vector2(0, 220)))
            return;

        ImGui.TableSetupColumn("类型", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("距离", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("剩余", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableHeadersRow();

        foreach (var activity in activities
                     .OrderByDescending(a => a.IsCriticalEngagement)
                     .ThenBy(a => a.Distance))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(activity.IsCriticalEngagement ? Yellow : Grey, activity.IsCriticalEngagement ? "CE" : "SM");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(activity.Name);

            ImGui.TableNextColumn();
            ImGui.TextColored(activity.RegisteredByMe ? Green : Grey, ActivityStateText(activity));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{activity.Distance:F0}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(activity.IsRegistrationOpen ? "报名中" : $"{activity.SecondsLeft:F0}s");

            ImGui.TableNextColumn();
            ImGui.PushID(activity.Id);
            if (ImGui.SmallButton("前往")) Engine.ForceTarget(activity);
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawFooter()
    {
        ImGui.TextColored(Grey, "需要 vnavmesh；战斗循环请自行开启（RSR / WrathCombo / BossMod）。");
        if (!ImGui.CollapsingHeader("使用说明###help")) return;

        ImGui.BulletText("进入南方战线 / 天佑女王 后点击开始。");
        ImGui.BulletText("CE 报名阶段会自动赶往魔法阵并确认报名状态。");
        ImGui.BulletText("遭遇战与 CE 会按“剩余时间 - 路程”综合评分挑选，不会白跑。");
        ImGui.BulletText("卡住会自动跳跃 / 重新上坐骑 / 重新寻路，三次失败则拉黑该目标 90 秒。");
        ImGui.BulletText("阵亡会等待复活，超时后自动返回营地。");
    }

    private static string ActivityStateText(Activity activity)
    {
        if (activity.RegisteredByMe) return "已报名";
        return activity.State switch
        {
            DynamicEventState.Register => "报名",
            DynamicEventState.Warmup => "准备",
            DynamicEventState.Battle => "进行",
            _ => "未知"
        };
    }

    private static string StateText(AutomationState state) => state switch
    {
        AutomationState.Disabled => "已停止",
        AutomationState.OutOfZone => "不在区域内",
        AutomationState.Busy => "处理中",
        AutomationState.Dead => "已阵亡",
        AutomationState.Idle => "待机",
        AutomationState.Travelling => "赶路",
        AutomationState.Registering => "报名/等待",
        AutomationState.Fighting => "战斗中",
        AutomationState.Recovering => "恢复中",
        AutomationState.Stuck => "卡住了",
        _ => state.ToString()
    };

    private static Vector4 StateColor(AutomationState state) => state switch
    {
        AutomationState.Disabled => Grey,
        AutomationState.OutOfZone => Grey,
        AutomationState.Fighting => Green,
        AutomationState.Registering => Green,
        AutomationState.Travelling => Yellow,
        AutomationState.Dead => Red,
        AutomationState.Stuck => Red,
        _ => Grey
    };
}
