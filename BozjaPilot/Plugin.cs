using BozjaPilot.Core;
using BozjaPilot.UI;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BozjaPilot;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/bzp";

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly OverlayWindow overlayWindow;
    private DateTime lastTick = DateTime.MinValue;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            Stage("注入 Dalamud 服务", () => pluginInterface.Create<Services>());
            Stage("初始化服务定位器", () => Services.Init(pluginInterface));

            Configuration? loadedConfig = null;
            Stage("读取配置", () => loadedConfig = pluginInterface.GetPluginConfig() as Configuration);
            Config = loadedConfig ?? new Configuration();

            AutomationEngine? engine = null;
            Stage("构建自动化引擎", () => engine = new AutomationEngine(Config));
            Engine = engine!;

            mainWindow = new MainWindow(this);
            configWindow = new ConfigWindow(this);
            overlayWindow = new OverlayWindow(this);

            Stage("注册命令", () =>
            {
                Services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "打开 BozjaPilot。参数：on / off / toggle / config / scan"
                });
                Services.Commands.AddHandler("/bozjapilot", new CommandInfo(OnCommand)
                {
                    HelpMessage = "打开 BozjaPilot（/bzp 的别名）",
                    ShowInHelp = false
                });
            });

            Stage("挂接界面与主循环", () =>
            {
                pluginInterface.UiBuilder.Draw += Draw;
                pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
                pluginInterface.UiBuilder.OpenMainUi += OpenMain;
                Services.Framework.Update += OnUpdate;
            });

            Services.Log.Info("[BozjaPilot] loaded");
        }
        catch (Exception ex)
        {
            // Dalamud 的安装/加载错误框只会说"安装失败"，不带原因。把真实异常落到插件目录，
            // 方便用户直接把文件发回来定位。
            WriteStartupFailure(pluginInterface, ex);
            throw;
        }
    }

    private static void Stage(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[BozjaPilot] 启动阶段『{name}』失败：{ex.Message}", ex);
        }
    }

    private static void WriteStartupFailure(IDalamudPluginInterface pluginInterface, Exception ex)
    {
        try
        {
            var dir = pluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-error.log");
            var text = $"""
                        === {DateTime.Now:O} ===
                        {ex}

                        """;
            File.AppendAllText(path, text);
        }
        catch
        {
            // 连写文件都失败就没办法了，至少异常仍会抛给 Dalamud。
        }
    }

    public Configuration Config { get; }

    public AutomationEngine Engine { get; }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.Commands.RemoveHandler(CommandName);
        Services.Commands.RemoveHandler("/bozjapilot");

        if (Services.PluginInterface != null)
        {
            Services.PluginInterface.UiBuilder.Draw -= Draw;
            Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
            Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        }

        Engine.Movement.Stop();
        Engine.Combat.Disengage();
        Config.Save();
    }

    public void OpenMain() => mainWindow.IsOpen = true;

    public void OpenConfig() => configWindow.IsOpen = true;

    private void OnCommand(string command, string arguments) =>
        HandleCommand(arguments.Trim().ToLowerInvariant());

    public void HandleCommand(string argument)
    {
        switch (argument)
        {
            case "":
            case "ui":
                OpenMain();
                break;
            case "on":
                Engine.Enable();
                break;
            case "off":
                Engine.Disable();
                break;
            case "toggle":
                Engine.Toggle();
                break;
            case "config":
            case "cfg":
                OpenConfig();
                break;
            case "scan":
                DumpScan();
                break;
            default:
                Services.Print("用法：/bzp [on|off|toggle|config|scan]");
                break;
        }
    }

    private void DumpScan()
    {
        var activities = Engine.Scanner.Scan(DateTime.UtcNow);
        Services.Print($"当前扫描到 {activities.Count} 个活动：");
        foreach (var activity in activities)
            Services.Print($"  {activity}");
    }

    private void Draw()
    {
        mainWindow.Draw();
        configWindow.Draw();
        overlayWindow.Draw();
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if ((now - lastTick).TotalMilliseconds < 50) return;
        lastTick = now;

        try
        {
            Engine.Tick(now);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "[BozjaPilot] tick failed");
            Engine.Disable();
        }
    }
}
