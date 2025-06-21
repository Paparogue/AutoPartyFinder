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

    private bool _drawConfigWindow;

    private bool _autoRenewalEnabled = false;
    private DateTime _lastRecruitmentTime = DateTime.MinValue;
    private DateTime _lastOnDrawCheck = DateTime.MinValue;
    private DateTime _lastPartySizeCheck = DateTime.MinValue;
    private bool _isRenewalInProgress = false;

    // Party size tracking variables
    private int _lastKnownPartySize = -1;
    private DateTime _partyDecreaseDetectedTime = DateTime.MinValue;
    private bool _pendingPartyRecovery = false;

    // Configurable renewal interval
    public const int RENEWAL_INTERVAL_MINUTES = 55;  // Renews party listing every X minutes
    private const int ONDRAW_CHECK_INTERVAL_MS = 500;
    private const int PARTY_SIZE_CHECK_INTERVAL_MS = 100;
    private const int RECRUITMENT_GRACE_PERIOD_SECONDS = 1; // Grace period after starting recruitment
    private const int PARTY_DECREASE_DEBOUNCE_SECONDS = 10; // Wait time after party size decrease

    public int RecoveryStepDelayMs { get; set; } = 100;
    public bool UseJobMaskOverride => PluginConfig?.UseJobMaskOverride ?? false;

    public IDalamudPluginInterface PluginInterface { get; init; }
    public ICommandManager CommandManager { get; init; }
    public IClientState ClientState { get; init; }
    public IPluginLog PluginLog { get; init; }
    public IPartyList PartyList { get; init; }

    public PartyFinderService GetPartyFinderService() => _partyFinderService;

    public bool IsAutoRenewalEnabled => _autoRenewalEnabled;
    public DateTime LastRecruitmentTime => _lastRecruitmentTime;
    public int LastKnownPartySize => _lastKnownPartySize;
    public bool PendingPartyRecovery => _pendingPartyRecovery;
    public DateTime PartyDecreaseDetectedTime => _partyDecreaseDetectedTime;

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

    // Reset the recruitment timer
    public void ResetRecruitmentTimer(string reason)
    {
        _lastRecruitmentTime = DateTime.MinValue;
        _lastKnownPartySize = -1;
        _partyDecreaseDetectedTime = DateTime.MinValue;
        _pendingPartyRecovery = false;
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

    // Execute restore party mask routine
    private void ExecutePartyDecreaseRecovery()
    {
        PluginLog.Information("[AutoRenewal] Executing party decrease recovery sequence");
        PluginLog.Information($"[AutoRenewal] Using {RecoveryStepDelayMs}ms delay between steps");
        _isRenewalInProgress = true;

        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[AutoRenewal] LookingForGroup agent is null during recovery");
                    _isRenewalInProgress = false;
                    return;
                }

                // Step 1: Disable OpenAddon
                try
                {
                    PluginLog.Information("[Recovery] Step 1: Disabling OpenAddon");
                    _partyFinderService.DisableOpenAddon();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[Recovery] Failed to disable OpenAddon, continuing anyway");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Unexpected error in recovery sequence step 1");
                _isRenewalInProgress = false;
            }
        });

        // Step 2: Open Recruitment Window
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Recovery] LookingForGroup agent is null during recovery");
                    _isRenewalInProgress = false;
                    return;
                }

                try
                {
                    PluginLog.Information("[Recovery] Step 2: Opening recruitment window");
                    _partyFinderService.OpenRecruitmentWindow(agent, 0);
                    Marshal.WriteInt32(agent + 0x30F0, 1);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[Recovery] Failed to open recruitment window, continuing anyway");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Unexpected error in recovery sequence step 2");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 1);

        // Step 3: Enable OpenAddon
        _queueService.QueueAction(() =>
        {
            try
            {
                try
                {
                    PluginLog.Information("[Recovery] Step 3: Re-enabling OpenAddon");
                    _partyFinderService.EnableOpenAddon();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[Recovery] Failed to enable OpenAddon, continuing anyway");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Unexpected error in recovery sequence step 3");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 2);

        // Step 4: Restore job masks for empty slots
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Recovery] LookingForGroup agent is null during recovery");
                    _isRenewalInProgress = false;
                    return;
                }

                try
                {
                    PluginLog.Information("[Recovery] Step 4: Restoring job masks for empty slots");
                    RestoreJobMasksForEmptySlots(agent);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[Recovery] Failed to restore job masks, continuing anyway");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Unexpected error in recovery sequence step 4");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 3);

        // Step 5: Leave duty
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Recovery] LookingForGroup agent is null during recovery");
                    _isRenewalInProgress = false;
                    return;
                }

                try
                {
                    PluginLog.Information("[Recovery] Step 5: Leaving duty");
                    _partyFinderService.LeaveDuty(agent);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[Recovery] Failed to leave duty, continuing anyway");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Unexpected error in recovery sequence step 5");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 10);

        // Step 6: Start recruiting
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Recovery] LookingForGroup agent is null when trying to start recruiting");
                    _isRenewalInProgress = false;
                    return;
                }

                PluginLog.Information("[Recovery] Step 6: Starting recruitment");
                var result = _partyFinderService.StartRecruiting(agent);
                PluginLog.Information($"[Recovery] StartRecruiting returned: {result}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Failed to start recruiting");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 20);

        // Step 7: Cleanup
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("[Recovery] LookingForGroup agent is null when trying to cleanup");
                    _isRenewalInProgress = false;
                    return;
                }

                // Clear fake addon handle
                Marshal.WriteInt32(agent + 0x30F0, 0);

                _isRenewalInProgress = false;
                PluginLog.Information("[Recovery] Party decrease recovery sequence completed");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[Recovery] Failed to cleanup");
                _isRenewalInProgress = false;
            }
        }, RecoveryStepDelayMs * 30);
    }

    public void ManualExecutePartyDecreaseRecovery()
    {
        if (_isRenewalInProgress)
        {
            PluginLog.Warning("[ManualRecovery] Recovery already in progress, skipping manual trigger");
            return;
        }

        PluginLog.Information("[ManualRecovery] Manual party decrease recovery triggered");
        ExecutePartyDecreaseRecovery();
    }

    // Restore job masks for empty slots only
    private void RestoreJobMasksForEmptySlots(IntPtr agent)
    {
        var slots = new PartyFinderSlots(agent, PluginLog);
        var currentSlots = slots.GetAllSlots();

        foreach (var currentSlot in currentSlots)
        {
            // Only restore if slot is currently empty
            if (!currentSlot.IsTaken)
            {
                ulong maskToSet = 0;

                // First check for override
                var overrideMask = GetJobMaskOverrideForSlot(currentSlot.Index);
                if (overrideMask.HasValue)
                {
                    maskToSet = overrideMask.Value;
                    PluginLog.Information($"[Recovery] Using override mask for slot {currentSlot.Index + 1}: 0x{maskToSet:X}");
                }
                else
                {
                    // Fall back to backup data
                    var backupData = _partyFinderService.GetLastBackupData();
                    if (backupData.HasValue)
                    {
                        // Find corresponding backup slot
                        var backupSlot = backupData.Value.SlotInfos.Find(s => s.Index == currentSlot.Index);
                        if (backupSlot.AllowedJobsMask != 0)
                        {
                            maskToSet = backupSlot.AllowedJobsMask;
                            PluginLog.Information($"[Recovery] Using backup mask for slot {currentSlot.Index + 1}: 0x{maskToSet:X}");
                        }
                    }
                }

                // Apply the mask if we have one
                if (maskToSet != 0)
                {
                    try
                    {
                        _partyFinderService.SetAllowedJobsMask(agent, currentSlot.Index, maskToSet);
                        PluginLog.Information($"[Recovery] Restored job mask for slot {currentSlot.Index + 1}: 0x{maskToSet:X}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, $"[Recovery] Failed to restore job mask for slot {currentSlot.Index + 1}");
                    }
                }
            }
            else
            {
                PluginLog.Debug($"[Recovery] Slot {currentSlot.Index + 1} is taken, skipping job mask restoration");
            }
        }
    }

    private void OnDraw()
    {
        var now = DateTime.UtcNow;

        // PART 1: Party size monitoring
        if ((now - _lastPartySizeCheck).TotalMilliseconds >= PARTY_SIZE_CHECK_INTERVAL_MS)
        {
            _lastPartySizeCheck = now;

            // Only check party size if we're tracking
            if (_lastKnownPartySize >= 0 && ClientState.IsLoggedIn && _autoRenewalEnabled && !_isRenewalInProgress)
            {
                int currentPartySize = _partyFinderService.GetCurrentPartySize();

                // Check for party size decrease
                if (currentPartySize < _lastKnownPartySize)
                {
                    // Party size decreased
                    _partyDecreaseDetectedTime = now;
                    _pendingPartyRecovery = true;
                    PluginLog.Information($"[AutoRenewal] Party size decreased from {_lastKnownPartySize} to {currentPartySize}");
                    PluginLog.Information($"[AutoRenewal] Starting {PARTY_DECREASE_DEBOUNCE_SECONDS} second debounce timer");
                }

                // Always update last known party size
                _lastKnownPartySize = currentPartySize;
            }
        }

        // PART 2: All other renewal logic
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

        // Check if we have a pending recovery and debounce time has passed
        if (_pendingPartyRecovery && _partyDecreaseDetectedTime != DateTime.MinValue)
        {
            var timeSinceDecrease = now - _partyDecreaseDetectedTime;
            if (timeSinceDecrease.TotalSeconds >= PARTY_DECREASE_DEBOUNCE_SECONDS)
            {
                PluginLog.Information($"[AutoRenewal] Debounce period passed, executing recovery");
                _pendingPartyRecovery = false;
                _partyDecreaseDetectedTime = DateTime.MinValue;
                ExecutePartyDecreaseRecovery();
                // Don't continue with normal renewal check
                return;
            }
            else
            {
                PluginLog.Debug($"[AutoRenewal] Waiting for debounce: {PARTY_DECREASE_DEBOUNCE_SECONDS - timeSinceDecrease.TotalSeconds:F1} seconds remaining");
            }
        }

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

    // OpenAddon patch tests
    public void TestDisableOpenAddon()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                PluginLog.Information("Testing DisableOpenAddon");
                _partyFinderService.DisableOpenAddon();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to disable OpenAddon");
            }
        });
    }

    public void TestEnableOpenAddon()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                PluginLog.Information("Testing EnableOpenAddon");
                _partyFinderService.EnableOpenAddon();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to enable OpenAddon");
            }
        });
    }

    public void TestOpenRecruitmentWindow()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                PluginLog.Information($"Calling OpenRecruitmentWindow with agent at 0x{agent.ToInt64():X}");
                _partyFinderService.OpenRecruitmentWindow(agent, 0);
                PluginLog.Information("OpenRecruitmentWindow called successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call OpenRecruitmentWindow");
            }
        });
    }

    public void TestStartRecruiting()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                PluginLog.Information($"Calling StartRecruiting with agent at 0x{agent.ToInt64():X}");
                var result = _partyFinderService.StartRecruiting(agent);
                PluginLog.Information($"StartRecruiting returned: {result}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call StartRecruiting");
            }
        });
    }

    public void TestLeaveDuty()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                PluginLog.Information($"Calling LeaveDuty with agent at 0x{agent.ToInt64():X}");
                _partyFinderService.LeaveDuty(agent);
                PluginLog.Information("LeaveDuty called successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call LeaveDuty");
            }
        });
    }

    public void TestRefreshListings()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                PluginLog.Information($"Calling RefreshListings with agent at 0x{agent.ToInt64():X} and command ID 0xE");
                var result = _partyFinderService.RefreshListings(agent, 0xE);
                PluginLog.Information($"RefreshListings returned: {result}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to call RefreshListings");
            }
        });
    }

    public void TestIsLocalPlayerPartyLeader()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                PluginLog.Information("Testing IsLocalPlayerPartyLeader");
                bool isLeader = _partyFinderService.IsLocalPlayerPartyLeader();
                PluginLog.Information($"IsLocalPlayerPartyLeader returned: {isLeader}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to test IsLocalPlayerPartyLeader");
            }
        });
    }

    public void TestIsLocalPlayerInParty()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                PluginLog.Information("Testing IsLocalPlayerInParty");
                bool inParty = _partyFinderService.IsLocalPlayerInParty();
                PluginLog.Information($"IsLocalPlayerInParty returned: {inParty}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to test IsLocalPlayerInParty");
            }
        });
    }

    public void TestGetActiveRecruiterContentId()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                PluginLog.Information("Testing GetActiveRecruiterContentId");
                ulong contentId = _partyFinderService.GetActiveRecruiterContentId();
                PluginLog.Information($"GetActiveRecruiterContentId returned: 0x{contentId:X} ({contentId})");

                if (contentId != 0)
                {
                    PluginLog.Information("Someone in the party has 'Looking for Party' status");
                }
                else
                {
                    PluginLog.Information("No one in the party has 'Looking for Party' status");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to test GetActiveRecruiterContentId");
            }
        });
    }

    public void TestPartyFinderSlots()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    PluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                PluginLog.Information($"Testing PartyFinderSlots with agent at 0x{agent.ToInt64():X}");
                var slots = new PartyFinderSlots(agent, PluginLog);

                slots.CheckAllSlots();

                int availableSlots = slots.GetAvailableSlotCount();
                PluginLog.Information($"Available slots: {availableSlots}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to test PartyFinderSlots");
            }
        });
    }
}