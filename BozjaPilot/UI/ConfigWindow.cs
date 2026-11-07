using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace BozjaPilot.UI;

public sealed class ConfigWindow : WindowBase
{
    private readonly Plugin plugin;

    // Deliberately a field, not a property: ImGui needs `ref` access to the members below.
    private Configuration config;

    public ConfigWindow(Plugin plugin) : base("BozjaPilot 设置###BozjaPilotConfig")
    {
        this.plugin = plugin;
        config = plugin.Config;
    }

    protected override void DrawContents()
    {
        if (ImGui.BeginChild("scroll", new Vector2(0, -30), true))
        {
            DrawAutomation();
            DrawMovement();
            DrawCombat();
            DrawRecovery();
            DrawUi();
        }

        ImGui.EndChild();

        if (ImGui.Button("保存并关闭", new Vector2(120, 0)))
        {
            config.Save();
            IsOpen = false;
        }

        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "改动会自动保存");
    }

    private void DrawAutomation()
    {
        if (!ImGui.CollapsingHeader("自动化###automation", ImGuiTreeNodeFlags.DefaultOpen)) return;

        ImGui.Checkbox("参与遭遇战 (Skirmish)", ref config.JoinSkirmishes);
        ImGui.Checkbox("参与紧急任务 (CE)", ref config.JoinCriticalEngagements);
        ImGui.Checkbox("CE 报名时放弃当前遭遇战", ref config.LeaveSkirmishForCe);

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("报名安全余量 (秒)", ref config.RegisterSafetyMargin, 0f, 60f, "%.0f")) config.Save();

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("到场后至少剩余 (秒)", ref config.MinTimeOnArrival, 0f, 120f, "%.0f")) config.Save();

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("最远参与距离 (米)", ref config.MaxTravelDistance, 100f, 1200f, "%.0f")) config.Save();
    }

    private void DrawMovement()
    {
        if (!ImGui.CollapsingHeader("移动###movement")) return;

        ImGui.Checkbox("自动上坐骑", ref config.AutoMount);
        ImGui.SameLine();
        ImGui.Checkbox("自动疾跑", ref config.AutoSprint);

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("上坐骑距离", ref config.MountDistance, 10f, 150f, "%.0f")) config.Save();

        ImGui.Checkbox("战斗中停止移动", ref config.StopMovingInCombat);

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("停手距离", ref config.CombatStopDistance, 3f, 30f, "%.1f")) config.Save();

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("卡住判定 (秒)", ref config.StuckTimeout, 1f, 10f, "%.1f")) config.Save();
    }

    private void DrawCombat()
    {
        if (!ImGui.CollapsingHeader("战斗###combat")) return;

        ImGui.Checkbox("自动选目标", ref config.AutoTarget);

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("目标搜索半径", ref config.TargetSearchRadius, 10f, 60f, "%.0f")) config.Save();

        ImGui.TextUnformatted("开战时执行命令（可留空）");
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputText("###startcmd", ref config.CombatStartCommand, 120)) config.Save();

        ImGui.TextUnformatted("非战斗时执行命令（可留空）");
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputText("###stopcmd", ref config.CombatStopCommand, 120)) config.Save();

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "例：/rotation auto、/wrath auto");
    }

    private void DrawRecovery()
    {
        if (!ImGui.CollapsingHeader("恢复###recovery")) return;

        ImGui.Checkbox("自动复活 / 返回营地", ref config.AutoRelease);

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("等待复活时间 (秒)", ref config.WaitForRaiseSeconds, 0f, 120f, "%.0f")) config.Save();

        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("空闲多久后返回营地 (秒，0=不返回)", ref config.ReturnWhenIdleSeconds, 0f, 300f, "%.0f")) config.Save();
    }

    private void DrawUi()
    {
        if (!ImGui.CollapsingHeader("界面###ui")) return;

        ImGui.Checkbox("显示活动列表", ref config.ShowActivityList);
        ImGui.SameLine();
        ImGui.Checkbox("显示悬浮窗", ref config.ShowOverlay);
        ImGui.Checkbox("调试日志", ref config.DebugLogging);

        ImGui.TextUnformatted($"已学习 CE 编号：{config.KnownCriticalEngagementIds.Count}");
        ImGui.TextUnformatted($"已学习博兹雅区域：{string.Join(", ", config.KnownBozjaTerritories)}");
    }
}
