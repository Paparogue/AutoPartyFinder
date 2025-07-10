using System;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using AutoPartyFinder.Services;
using AutoPartyFinder.UI;
using Dalamud.Game;
using System.Runtime.InteropServices;

namespace AutoPartyFinder;

public unsafe class AutoPartyFinder : IDalamudPlugin
{
    public string Name => "AutoPartyFinder";
    public AutoPartyFinderConfig PluginConfig { get; private set; }

    private readonly PartyFinderService _partyFinderService;
    private readonly FrameworkQueueService _queueService;
    private readonly TestFunctions _testFunctions;

    private bool _drawConfigWindow;

    private bool _autoRenewalEnabled = false;
    private DateTime _lastRecruitmentTime = DateTime.MinValue;
    private DateTime _lastOnDrawCheck = DateTime.MinValue;
    private bool _isRenewalInProgress = false;

    // Party size tracking variables
    private int _lastKnownPartySize = -1;

    // Configurable renewal interval
    public const int RENEWAL_INTERVAL_MINUTES = 55;  // Renews party listing every X minutes
    private const int ONDRAW_CHECK_INTERVAL_MS = 500;
    private const int RECRUITMENT_GRACE_PERIOD_SECONDS = 1; // Grace period after starting recruitment

    public int RecoveryStepDelayMs { get; set; } = 100;
    public bool UseJobMaskOverride => PluginConfig?.UseJobMaskOverride ?? false;

    public IDalamudPluginInterface PluginInterface { get; init; }
    public ICommandManager CommandManager { get; init; }
    public IClientState ClientState { get; init; }
    public IPluginLog PluginLog { get; init; }
    public IPartyList PartyList { get; init; }

    public PartyFinderService GetPartyFinderService() => _partyFinderService;
    public TestFunctions GetTestFunctions() => _testFunctions;

    public bool IsAutoRenewalEnabled => _autoRenewalEnabled;
    public DateTime LastRecruitmentTime => _lastRecruitmentTime;
    public int LastKnownPartySize => _lastKnownPartySize;

    public AutoPartyFinder(
        IDalamudPluginInterface pluginInterface,
        ISigScanner sigScanner,
        ICommandManager commandManager,
        IPluginLog pluginLog,
        IFramework framework,
        IClientState clientState,
        IGameInteropProvider gameInteropProvider,
        IPartyList partyList)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        PluginLog = pluginLog;
        ClientState = clientState;
        PartyList = partyList;

        PluginConfig = PluginInterface.GetPluginConfig() as AutoPartyFinderConfig ?? new AutoPartyFinderConfig();
        PluginConfig.Init(this);

        _partyFinderService = new PartyFinderService(sigScanner, pluginLog, gameInteropProvider, this);
        _queueService = new FrameworkQueueService(framework, pluginLog);
        _testFunctions = new TestFunctions(this, _partyFinderService, _queueService, pluginLog);

        SetupCommands();

        PluginInterface.UiBuilder.Draw += BuildUI;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
    }

    public void Dispose()
    {
        _isRenewalInProgress = false;
        _queueService.Dispose();
        _partyFinderService.Dispose();

        PluginInterface.UiBuilder.Draw -= BuildUI;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

        RemoveCommands();
    }

    private void SetupCommands()
    {
        CommandManager.AddHandler("/apf", new CommandInfo(OnConfigCommandHandler)
        {
            HelpMessage = $"Opens the config window for {Name}.",
            ShowInHelp = true
        });
    }

    private void RemoveCommands()
    {
        CommandManager.RemoveHandler("/apf");
    }

    private void OnConfigCommandHandler(string? command, string? args)
    {
        _drawConfigWindow = true;
    }

    private void OpenConfigUi()
    {
        _drawConfigWindow = true;
    }

    private void BuildUI()
    {
        _drawConfigWindow = _drawConfigWindow && PluginConfig.DrawConfigUI();
    }

    // Called by PartyFinderService when StartRecruiting detour is triggered
    public void OnStartRecruitingCalled()
    {
        _lastRecruitmentTime = DateTime.UtcNow;
        PluginLog.Information($"[AutoRenewal] 60-minute timer started at {_lastRecruitmentTime}");
        // Initialize party size tracking when recruitment starts
        if (_lastKnownPartySize == -1)
        {
            _lastKnownPartySize = _partyFinderService.GetCurrentPartySize();
            PluginLog.Information($"[AutoRenewal] Initial party size: {_lastKnownPartySize}");
        }
    }

    // Called by PartyFinderService when party member change is detected
    public void OnPartyMemberChange(int previousSize, int newSize)
    {
        // Only process if we're actively tracking
        if (_lastKnownPartySize < 0 || !ClientState.IsLoggedIn || !_autoRenewalEnabled || _isRenewalInProgress)
            return;

        PluginLog.Information($"[AutoRenewal] Party member change detected: {previousSize} -> {newSize}");

        // Update last known party size
        _lastKnownPartySize = newSize;

        // Check if someone joined
        if (newSize > previousSize)
        {
            PluginLog.Information($"[AutoRenewal] Party member joined - updating PF listings");

            _queueService.QueueAction(() =>
            {
                try
                {
                    var agent = _partyFinderService.GetLookingForGroupAgent();
                    if (agent == IntPtr.Zero)
                    {
                        PluginLog.Error("[AutoRenewal] LookingForGroup agent is null during member join");
                        return;
                    }

                    // Update party finder listings to get accurate slot information
                    _partyFinderService.UpdatePartyFinderListings(agent, 1);
                    PluginLog.Information("[AutoRenewal] Updated PF listings after member join");
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[AutoRenewal] Failed to update PF listings after member join");
                }
            });
        }
        // Check if someone left
        else if (newSize < previousSize)
        {
            PluginLog.Information($"[AutoRenewal] Party member left - executing restoration sequence");
            ExecutePartyMemberLeftSequence();
        }
    }

    // Reset the recruitment timer
    public void ResetRecruitmentTimer(string reason)
    {
        _lastRecruitmentTime = DateTime.MinValue;
        _lastKnownPartySize = -1;
        PluginLog.Information($"[AutoRenewal] Recruitment timer reset due to {reason}");
    }

    // Toggle auto-renewal
    public void ToggleAutoRenewal()
    {
        _autoRenewalEnabled = !_autoRenewalEnabled;
        if (!_autoRenewalEnabled)
        {
            ResetRecruitmentTimer("toggle Auto-renewal pressed.");
            _isRenewalInProgress = false; // Reset the flag when disabling
        }
        PluginLog.Information($"[AutoRenewal] Auto-renewal {(_autoRenewalEnabled ? "enabled" : "disabled")}");
    }

    // Check if we're within the grace period after starting recruitment
    private bool IsWithinGracePeriod()
    {
        if (_lastRecruitmentTime == DateTime.MinValue)
            return false;

        var timeSinceRecruitment = DateTime.UtcNow - _lastRecruitmentTime;
        return timeSinceRecruitment.TotalSeconds <= RECRUITMENT_GRACE_PERIOD_SECONDS;
    }

    // Get job mask override for a specific slot
    public ulong? GetJobMaskOverrideForSlot(int slotIndex)
    {
        if (!UseJobMaskOverride)
            return null;

        if (PluginConfig.SlotJobMaskOverrides.TryGetValue(slotIndex, out ulong mask))
        {
            return mask;
        }

        return null;
    }

    // Execute restoration sequence when party member leaves
    private void ExecutePartyMemberLeftSequence()
    {
        PluginLog.Information("[AutoRenewal] Executing party member left restoration sequence");
        _isRenewalInProgress = true;

        // Step 1: Update Party Finder Listings
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Restoration] LookingForGroup agent is null");
                    _isRenewalInProgress = false;
                    return;
                }

                PluginLog.Information("[Restoration] Step 1: Updating PF listings");
                _partyFinderService.UpdatePartyFinderListings(agent, 1);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Restoration] Failed to update PF listings");
                _isRenewalInProgress = false;
            }
        });

        // Step 2: Smart restore job masks
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Restoration] LookingForGroup agent is null");
                    _isRenewalInProgress = false;
                    return;
                }

                PluginLog.Information("[Restoration] Step 2: Smart restoring job masks");
                _partyFinderService.SmartRestoreJobMasks(agent);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Restoration] Failed to restore job masks");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 2);

        // Step 3: Leave duty
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Restoration] LookingForGroup agent is null");
                    _isRenewalInProgress = false;
                    return;
                }

                PluginLog.Information("[Restoration] Step 3: Leaving duty");
                _partyFinderService.LeaveDuty(agent);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Restoration] Failed to leave duty");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 5);

        // Step 4: Start recruiting
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Restoration] LookingForGroup agent is null");
                    _isRenewalInProgress = false;
                    return;
                }

                PluginLog.Information("[Restoration] Step 4: Starting recruitment");
                var result = _partyFinderService.StartRecruiting(agent);
                PluginLog.Information($"[Restoration] StartRecruiting returned: {result}");

                _isRenewalInProgress = false;
                PluginLog.Information("[Restoration] Party member left restoration sequence completed");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Restoration] Failed to start recruiting");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 10);
    }

    // Manual recovery function for debug purposes
    public void ManualExecutePartyDecreaseRecovery()
    {
        if (_isRenewalInProgress)
        {
            PluginLog.Warning("[ManualRecovery] Recovery already in progress, skipping manual trigger");
            return;
        }

        PluginLog.Information("[ManualRecovery] Manual party decrease recovery triggered");
        ExecutePartyMemberLeftSequence();
    }

    private void OnDraw()
    {
        var now = DateTime.UtcNow;

        // Check renewal timer interval
        if ((now - _lastOnDrawCheck).TotalMilliseconds < ONDRAW_CHECK_INTERVAL_MS)
            return;

        _lastOnDrawCheck = now;

        // Skip if auto renewal is not enabled
        if (!_autoRenewalEnabled)
            return;

        // Skip if renewal is already in progress
        if (_isRenewalInProgress)
        {
            PluginLog.Debug("[AutoRenewal] Renewal already in progress, skipping check");
            return;
        }

        // Check if logged in
        if (!ClientState.IsLoggedIn)
        {
            ResetRecruitmentTimer("not logged in.");
            return;
        }

        // Check if in party and is party leader
        bool inParty = _partyFinderService.IsLocalPlayerInParty();
        bool isLeader = _partyFinderService.IsLocalPlayerPartyLeader();

        if (inParty && !isLeader)
        {
            ResetRecruitmentTimer("in group but not leader.");
            return;
        }

        // Check if there's an active recruiter
        ulong activeRecruiter = _partyFinderService.GetActiveRecruiterContentId();
        if (activeRecruiter == 0)
        {
            // Only reset if we're not within the grace period
            if (!IsWithinGracePeriod())
            {
                ResetRecruitmentTimer("no active recruiting.");
                return;
            }
            else
            {
                // Within grace period, log but don't reset
                PluginLog.Debug("[AutoRenewal] No active recruiter detected, but within grace period");
            }
        }

        // Check if we have a valid recruitment time
        if (_lastRecruitmentTime == DateTime.MinValue)
            return;

        // Check if it's time to renew
        var timeSinceRecruitment = now - _lastRecruitmentTime;
        if (timeSinceRecruitment.TotalMinutes >= RENEWAL_INTERVAL_MINUTES)
        {
            PluginLog.Information($"[AutoRenewal] {RENEWAL_INTERVAL_MINUTES} minutes have passed, renewing recruitment");

            _isRenewalInProgress = true;

            //Queue leave duty
            _queueService.QueueAction(() =>
            {
                try
                {
                    var agent = _partyFinderService.GetLookingForGroupAgent();
                    if (agent == IntPtr.Zero)
                    {
                        PluginLog.Error("[AutoRenewal] LookingForGroup agent is null during renewal");
                        _isRenewalInProgress = false;
                        return;
                    }

                    // Leave duty first
                    PluginLog.Information("[AutoRenewal] Leaving duty...");
                    _partyFinderService.LeaveDuty(agent);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[AutoRenewal] Failed to leave duty during renewal");
                    _isRenewalInProgress = false;
                }
            });

            // Queue start recruiting
            _queueService.QueueAction(() =>
            {
                try
                {
                    var agent = _partyFinderService.GetLookingForGroupAgent();
                    if (agent == IntPtr.Zero)
                    {
                        PluginLog.Error("[AutoRenewal] LookingForGroup agent is null during renewal");
                        _isRenewalInProgress = false;
                        return;
                    }

                    // Start recruiting again
                    PluginLog.Information("[AutoRenewal] Starting recruitment...");
                    var result = _partyFinderService.StartRecruiting(agent);
                    PluginLog.Information($"[AutoRenewal] StartRecruiting returned: {result}");

                    // Mark renewal as complete
                    _isRenewalInProgress = false;
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[AutoRenewal] Failed to start recruiting during renewal");
                    _isRenewalInProgress = false;
                }
            }, RecoveryStepDelayMs * 10);
        }
    }
}