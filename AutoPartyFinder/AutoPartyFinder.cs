using System;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoPartyFinder;

public class AutoPartyFinder : IDalamudPlugin
{
    public string Name => "AutoPartyFinder";
    public AutoPartyFinderConfig PluginConfig { get; private set; }
    private bool drawConfigWindow;

    private delegate long StartRecruitingDelegate(IntPtr agent);
    private StartRecruitingDelegate _startRecruiting;

    public IDalamudPluginInterface PluginInterface { get; init; }
    public ISigScanner SigScanner { get; init; }
    public ICommandManager CommandManager { get; init; }
    public IPluginLog PluginLog { get; init; }

    public unsafe AutoPartyFinder(
        IDalamudPluginInterface pluginInterface,
        ISigScanner sigScanner,
        ICommandManager commandManager,
        IPluginLog pluginLog
    )
    {
        PluginInterface = pluginInterface;
        SigScanner = sigScanner;
        CommandManager = commandManager;
        PluginLog = pluginLog;

        PluginConfig = PluginInterface.GetPluginConfig() as AutoPartyFinderConfig ?? new AutoPartyFinderConfig();
        PluginConfig.Init(this);

        SetupCommands();
        InitSigs();

        PluginInterface.UiBuilder.Draw += BuildUI;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= BuildUI;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        RemoveCommands();
    }

    private void InitSigs()
    {
        try
        {
            var startRecruitingPtr = SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B CB 44 89 A3");
            _startRecruiting = Marshal.GetDelegateForFunctionPointer<StartRecruitingDelegate>(startRecruitingPtr);
            PluginLog.Information($"StartRecruiting function found at 0x{startRecruitingPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to find StartRecruiting signature");
        }
    }

    public unsafe void StartRecruiting()
    {
        try
        {
            if (_startRecruiting == null)
            {
                PluginLog.Error("StartRecruiting function pointer is null");
                return;
            }

            var moduleInstance = AgentModule.Instance();
            if (moduleInstance == null)
            {
                PluginLog.Error("AgentModule instance is null");
                return;
            }

            IntPtr agent = (IntPtr)moduleInstance->GetAgentByInternalId(AgentId.LookingForGroup);
            if (agent == IntPtr.Zero)
            {
                PluginLog.Error("LookingForGroup agent is null");
                return;
            }

            PluginLog.Information($"Calling StartRecruiting with agent at 0x{agent.ToInt64():X}");
            var result = _startRecruiting(agent);
            PluginLog.Information($"StartRecruiting returned: {result}");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error in StartRecruiting");
        }
    }

    public void SetupCommands()
    {
        CommandManager.AddHandler("/apf", new CommandInfo(OnConfigCommandHandler)
        {
            HelpMessage = $"Opens the config window for {Name}.",
            ShowInHelp = true
        });
    }

    private void OpenConfigUi()
    {
        OnConfigCommandHandler(null, null);
    }

    public void OnConfigCommandHandler(string? command, string? args)
    {
        drawConfigWindow = true;
    }

    public void RemoveCommands()
    {
        CommandManager.RemoveHandler("/apf");
    }

    private void BuildUI()
    {
        drawConfigWindow = drawConfigWindow && PluginConfig.DrawConfigUI();
    }
}