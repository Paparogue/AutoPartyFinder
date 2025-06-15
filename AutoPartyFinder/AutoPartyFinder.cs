using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud;

namespace AutoPartyFinder;

public class AutoPartyFinder : IDalamudPlugin
{
    public string Name => "AutoPartyFinder";
    public AutoPartyFinderConfig PluginConfig { get; private set; }
    private bool drawConfigWindow;

    private delegate long StartRecruitingDelegate(IntPtr agent);
    private delegate void OpenRecruitmentWindowDelegate(IntPtr agent, byte isUpdate, byte skipRestoreState);
    private delegate void HandleListingActionDelegate(IntPtr agent, IntPtr actionType);
    private delegate void LeaveDutyDelegate(IntPtr agent, byte skipCrossRealmCheck);

    private StartRecruitingDelegate _startRecruiting;
    private OpenRecruitmentWindowDelegate _openRecruitmentWindow;
    private HandleListingActionDelegate _handleListingAction;
    private LeaveDutyDelegate _leaveDuty;

    private IntPtr _patchAddress = IntPtr.Zero;
    private byte[] _originalPatchBytes = null!;
    private bool _isPatchApplied = false;

    // Queue system for framework thread operations
    private readonly Queue<(Action action, DateTime executeTime)> _frameworkQueue = new();
    private readonly object _queueLock = new();
    public bool _isProcessingAutoRecruit = false;

    public IDalamudPluginInterface PluginInterface { get; init; }
    public ISigScanner SigScanner { get; init; }
    public ICommandManager CommandManager { get; init; }
    public IPluginLog PluginLog { get; init; }
    public IFramework Framework { get; init; }

    public unsafe AutoPartyFinder(
        IDalamudPluginInterface pluginInterface,
        ISigScanner sigScanner,
        ICommandManager commandManager,
        IPluginLog pluginLog,
        IFramework framework
    )
    {
        PluginInterface = pluginInterface;
        SigScanner = sigScanner;
        CommandManager = commandManager;
        PluginLog = pluginLog;
        Framework = framework;

        PluginConfig = PluginInterface.GetPluginConfig() as AutoPartyFinderConfig ?? new AutoPartyFinderConfig();
        PluginConfig.Init(this);

        SetupCommands();
        InitSigs();

        PluginInterface.UiBuilder.Draw += BuildUI;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        if (_isPatchApplied)
            RestorePatch();

        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= BuildUI;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        RemoveCommands();
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        var now = DateTime.UtcNow;
        List<(Action action, DateTime executeTime)> actionsToExecute = new();

        // Check for actions that are ready to execute
        lock (_queueLock)
        {
            while (_frameworkQueue.Count > 0)
            {
                var (action, executeTime) = _frameworkQueue.Peek();
                if (now >= executeTime)
                {
                    actionsToExecute.Add(_frameworkQueue.Dequeue());
                }
                else
                {
                    break; // Queue is ordered by time, so we can stop checking
                }
            }
        }

        // Execute ready actions
        foreach (var (action, _) in actionsToExecute)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error executing queued framework action");
            }
        }
    }

    private void QueueFrameworkAction(Action action, int delayMs = 0)
    {
        lock (_queueLock)
        {
            var executeTime = DateTime.UtcNow.AddMilliseconds(delayMs);
            _frameworkQueue.Enqueue((action, executeTime));
        }
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

        try
        {
            var leaveDutyPtr = SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 41 83 8F");
            _leaveDuty = Marshal.GetDelegateForFunctionPointer<LeaveDutyDelegate>(leaveDutyPtr);
            PluginLog.Information($"HandleListingAction function found at 0x{leaveDutyPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to find HandleListingAction signature");
        }

        try
        {
            var handleListingActionPtr = SigScanner.ScanText("E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CF E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CF E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CF E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 48 8B CE");
            _handleListingAction = Marshal.GetDelegateForFunctionPointer<HandleListingActionDelegate>(handleListingActionPtr);
            PluginLog.Information($"HandleListingAction function found at 0x{handleListingActionPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to find HandleListingAction signature");
        }

        try
        {
            var openRecruitmentWindowPtr = SigScanner.ScanText("E8 ?? ?? ?? ?? 4D 89 A7 ?? ?? ?? ?? 4D 89 A7");
            _openRecruitmentWindow = Marshal.GetDelegateForFunctionPointer<OpenRecruitmentWindowDelegate>(openRecruitmentWindowPtr);
            PluginLog.Information($"OpenRecruitmentWindow function found at 0x{openRecruitmentWindowPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to find OpenRecruitmentWindow signature");
        }

        try
        {
            _patchAddress = SigScanner.ScanText("E8 ?? ?? ?? ?? 0F B7 C0 89 86 ?? ?? ?? ?? EB ?? 80 BE");
            if (_patchAddress != IntPtr.Zero)
            {
                PluginLog.Information($"Found patch signature at 0x{_patchAddress.ToInt64():X}");
                _originalPatchBytes = new byte[5];
                Marshal.Copy(_patchAddress, _originalPatchBytes, 0, 5);
                PluginLog.Debug($"Original bytes: {BitConverter.ToString(_originalPatchBytes)}");
            }
            else
            {
                PluginLog.Error("Failed to find patch signature");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error scanning for patch signature");
        }
    }

    public void LeaveDuty(IntPtr agentPtr)
    {
        IntPtr pointerAt1C = Marshal.ReadIntPtr(agentPtr + 0x1C);
         _leaveDuty(pointerAt1C, 0);
    }

    private void CallHandleListingAction(IntPtr agent, int actionType)
    {
        var bytes = new byte[16];
        bytes[0] = 2;  // Type = Int
        BitConverter.GetBytes(actionType).CopyTo(bytes, 8);

        var ptr = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(bytes, 0, ptr, 16);
            _handleListingAction(agent, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public unsafe void AutoRecruit()
    {
        if (_isProcessingAutoRecruit)
        {
            PluginLog.Warning("AutoRecruit is already in progress");
            return;
        }

        if (_openRecruitmentWindow == null)
        {
            PluginLog.Error("OpenRecruitmentWindow function pointer is null");
            return;
        }

        if (_startRecruiting == null)
        {
            PluginLog.Error("StartRecruiting function pointer is null");
            return;
        }

        _isProcessingAutoRecruit = true;

        // Queue the sequence with delays
        int currentDelay = 0;
        /*
        // Step 1: Apply NOP patch immediately
        QueueFrameworkAction(() =>
        {
            try
            {
                PluginLog.Information("Step 1: Applying NOP patch");
                //ApplyPatch();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to apply patch");
                _isProcessingAutoRecruit = false;
            }
        }, currentDelay);

        currentDelay += 2000; // Wait 2 seconds

        // Step 2: Call OpenRecruitmentWindow
        QueueFrameworkAction(() =>
        {
            try
            {
                var moduleInstance = AgentModule.Instance();
                if (moduleInstance == null)
                {
                    PluginLog.Error("AgentModule instance is null");
                    //RestorePatch();
                    _isProcessingAutoRecruit = false;
                    return;
                }

                IntPtr agent = (IntPtr)moduleInstance->GetAgentByInternalId(AgentId.LookingForGroup);
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    //RestorePatch();
                    _isProcessingAutoRecruit = false;
                    return;
                }

                PluginLog.Information($"Step 2: Calling OpenRecruitmentWindow with agent at 0x{agent.ToInt64():X}");
                //_openRecruitmentWindow(agent, 1, 0);
                PluginLog.Information("OpenRecruitmentWindow called successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call OpenRecruitmentWindow");
                //RestorePatch();
                _isProcessingAutoRecruit = false;
            }
        }, currentDelay);

        currentDelay += 2000; // Wait 2 seconds

        // Step 3: Restore original code
        QueueFrameworkAction(() =>
        {
            try
            {
                PluginLog.Information("Step 3: Restoring original code");
                //RestorePatch();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to restore patch");
                _isProcessingAutoRecruit = false;
            }
        }, currentDelay);

        currentDelay += 2000; // Wait 2 seconds
        */
        // Step 4: Call StartRecruiting
        QueueFrameworkAction(() =>
        {
            try
            {
                var moduleInstance = AgentModule.Instance();
                if (moduleInstance == null)
                {
                    PluginLog.Error("AgentModule instance is null");
                    _isProcessingAutoRecruit = false;
                    return;
                }

                IntPtr agent = (IntPtr)moduleInstance->GetAgentByInternalId(AgentId.LookingForGroup);
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    _isProcessingAutoRecruit = false;
                    return;
                }

                PluginLog.Information($"Step 4: Calling StartRecruiting with agent at 0x{agent.ToInt64():X}");
                //var result = _startRecruiting(agent);
                var result = 0;
                LeaveDuty(agent);
                PluginLog.Information($"StartRecruiting returned: {result}");
                PluginLog.Information("AutoRecruit sequence completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call StartRecruiting");
            }
            finally
            {
                _isProcessingAutoRecruit = false;
            }
        }, currentDelay);
    }

    private void ApplyPatch()
    {
        if (_patchAddress == IntPtr.Zero || _originalPatchBytes == null || _isPatchApplied)
        {
            PluginLog.Warning("Cannot apply patch: address not found, original bytes not saved, or patch already applied");
            return;
        }

        try
        {
            byte[] nopPatch = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };
            SafeMemory.WriteBytes(_patchAddress, nopPatch);
            PluginLog.Information($"Applied NOP patch at 0x{_patchAddress.ToInt64():X}");
            _isPatchApplied = true;
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error applying patch");
        }
    }

    private void RestorePatch()
    {
        if (_patchAddress == IntPtr.Zero || _originalPatchBytes == null || !_isPatchApplied)
        {
            PluginLog.Warning("Cannot restore patch: address not found, original bytes not saved, or patch not applied");
            return;
        }

        try
        {
            SafeMemory.WriteBytes(_patchAddress, _originalPatchBytes);
            PluginLog.Information($"Restored original bytes at 0x{_patchAddress.ToInt64():X}");
            _isPatchApplied = false;
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error restoring original bytes");
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