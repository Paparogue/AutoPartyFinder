using System;
using System.Runtime.InteropServices;
using Dalamud;
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using AutoPartyFinder.Delegates;
using AutoPartyFinder.Constants;
using AutoPartyFinder.Structures;
using Dalamud.Game;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoPartyFinder.Services;

public unsafe class PartyFinderService
{
    private readonly IPluginLog _pluginLog;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly AutoPartyFinder _plugin;

    // Hook for StartRecruiting
    private Hook<StartRecruitingDelegate>? _startRecruitingHook;

    // Hook for PartyMemberChange
    private Hook<PartyMemberChangeDelegate>? _partyMemberChangeHook;

    // Store the detour delegate to call it manually
    private StartRecruitingDelegate? _startRecruitingDetourDelegate;

    // Flag to track if call is from plugin
    private bool _isPluginInitiatedCall = false;

    private OpenRecruitmentWindowDelegate? _openRecruitmentWindow;
    private LeaveDutyDelegate? _leaveDuty;
    private RefreshListingsDelegate? _refreshListings;
    private IsLocalPlayerPartyLeaderDelegate? _isLocalPlayerPartyLeader;
    private IsLocalPlayerInPartyDelegate? _isLocalPlayerInParty;
    private GetActiveRecruiterContentIdDelegate? _getActiveRecruiterContentId;
    private CrossRealmGetPartyMemberCountDelegate? _getCrossRealmPartyMemberCount;
    private CrossRealmFunc1Delegate? _crossRealmFunc1;
    private CrossRealmFunc2Delegate? _crossRealmFunc2;

    private IntPtr _openAddonPatchAddress = IntPtr.Zero;
    private byte[]? _originalOpenAddonBytes;
    private bool _isOpenAddonPatchApplied = false;

    // Backup data for slots and job masks
    public struct SlotBackupInfo
    {
        public int TotalSlots;
        public List<PartyFinderSlots.SlotInfo> SlotInfos;
        public DateTime BackupTime;
    }
    private SlotBackupInfo? _lastBackupData;

    public PartyFinderService(ISigScanner sigScanner, IPluginLog pluginLog, IGameInteropProvider gameInteropProvider, AutoPartyFinder plugin)
    {
        _pluginLog = pluginLog;
        _gameInteropProvider = gameInteropProvider;
        _plugin = plugin;
        InitializeSignatures(sigScanner);
    }

    private void InitializeSignatures(ISigScanner sigScanner)
    {
        // Hook StartRecruiting
        try
        {
            var startRecruitingPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B CB 44 89 A3");
            _pluginLog.Information($"StartRecruiting function found at 0x{startRecruitingPtr.ToInt64():X}");

            _startRecruitingHook = _gameInteropProvider.HookFromAddress<StartRecruitingDelegate>(
                startRecruitingPtr,
                StartRecruitingDetour);

            _startRecruitingDetourDelegate = StartRecruitingDetour;

            _startRecruitingHook.Enable();
            _pluginLog.Information("StartRecruiting hook enabled successfully");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to hook StartRecruiting signature");
        }

        // Hook PartyMemberChange
        try
        {
            var partyMemberChangePtr = sigScanner.ScanText("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8B 07 48 8B CF FF 90 ?? ?? ?? ?? 45 8B C6");
            _pluginLog.Information($"PartyMemberChange function found at 0x{partyMemberChangePtr.ToInt64():X}");

            _partyMemberChangeHook = _gameInteropProvider.HookFromAddress<PartyMemberChangeDelegate>(
                partyMemberChangePtr,
                PartyMemberChangeDetour);

            _partyMemberChangeHook.Enable();
            _pluginLog.Information("PartyMemberChange hook enabled successfully");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to hook PartyMemberChange signature");
        }

        try
        {
            var leaveDutyPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 41 83 8F");
            _leaveDuty = Marshal.GetDelegateForFunctionPointer<LeaveDutyDelegate>(leaveDutyPtr);
            _pluginLog.Information($"LeaveDuty function found at 0x{leaveDutyPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find LeaveDuty signature");
        }

        try
        {
            var openRecruitmentWindowPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 4D 89 A7 ?? ?? ?? ?? 4D 89 A7");
            _openRecruitmentWindow = Marshal.GetDelegateForFunctionPointer<OpenRecruitmentWindowDelegate>(openRecruitmentWindowPtr);
            _pluginLog.Information($"OpenRecruitmentWindow function found at 0x{openRecruitmentWindowPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find OpenRecruitmentWindow signature");
        }

        try
        {
            _openAddonPatchAddress = sigScanner.ScanText("83 BE ?? ?? ?? ?? ?? 48 8B 01 75");
            if (_openAddonPatchAddress != IntPtr.Zero)
            {
                _pluginLog.Information($"Found OpenAddon patch signature at 0x{_openAddonPatchAddress.ToInt64():X}");
                _originalOpenAddonBytes = new byte[7];
                Marshal.Copy(_openAddonPatchAddress, _originalOpenAddonBytes, 0, 7);
                _pluginLog.Debug($"Original OpenAddon bytes: {BitConverter.ToString(_originalOpenAddonBytes)}");
            }
            else
            {
                _pluginLog.Error("Failed to find OpenAddon patch signature");
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error scanning for OpenAddon patch signature");
        }

        try
        {
            var refreshListingsPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CF E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2 48 8B D6 48 8B CF E8 ?? ?? ?? ?? 0F B6 D8 E9 ?? ?? ?? ?? 45 8B C2");
            _refreshListings = Marshal.GetDelegateForFunctionPointer<RefreshListingsDelegate>(refreshListingsPtr);
            _pluginLog.Information($"RefreshListings function found at 0x{refreshListingsPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find RefreshListings signature");
        }

        try
        {
            var isLocalPlayerPartyLeaderPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 ?? 33 D2 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C8");
            _isLocalPlayerPartyLeader = Marshal.GetDelegateForFunctionPointer<IsLocalPlayerPartyLeaderDelegate>(isLocalPlayerPartyLeaderPtr);
            _pluginLog.Information($"IsLocalPlayerPartyLeader function found at 0x{isLocalPlayerPartyLeaderPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find IsLocalPlayerPartyLeader signature");
        }

        try
        {
            var isLocalPlayerInPartyPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 74 ?? E8 ?? ?? ?? ?? 84 C0 75 ?? 33 D2 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C8");
            _isLocalPlayerInParty = Marshal.GetDelegateForFunctionPointer<IsLocalPlayerInPartyDelegate>(isLocalPlayerInPartyPtr);
            _pluginLog.Information($"IsLocalPlayerInParty function found at 0x{isLocalPlayerInPartyPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find IsLocalPlayerInParty signature");
        }

        try
        {
            var getActiveRecruiterContentIdPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 ?? 48 8B 4E ?? 48 8B 11 FF 92 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 48 3B D8");
            _getActiveRecruiterContentId = Marshal.GetDelegateForFunctionPointer<GetActiveRecruiterContentIdDelegate>(getActiveRecruiterContentIdPtr);
            _pluginLog.Information($"GetActiveRecruiterContentId function found at 0x{getActiveRecruiterContentIdPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find GetActiveRecruiterContentId signature");
        }

        try
        {
            var getCrossRealmPartyMemberCountPtr = sigScanner.ScanText("E8 ?? ?? ?? ?? 3C ?? 0F 97 C0 48 83 C4 ?? C3 E8");
            _getCrossRealmPartyMemberCount = Marshal.GetDelegateForFunctionPointer<CrossRealmGetPartyMemberCountDelegate>(getCrossRealmPartyMemberCountPtr);
            _pluginLog.Information($"InfoProxyCrossRealm_GetPartyMemberCount function found at 0x{getCrossRealmPartyMemberCountPtr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find InfoProxyCrossRealm_GetPartyMemberCount signature");
        }

        // New CrossRealm functions
        try
        {
            var crossRealmFunc1Ptr = sigScanner.ScanText("E8 ?? ?? ?? ?? 49 8B CE 48 8B 5C 24 ?? 48 8B 6C 24 ?? 48 8B 74 24 ?? 48 83 C4 ?? 41 5F 41 5E 41 5D 41 5C 5F E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC 48 89 5C 24");
            _crossRealmFunc1 = Marshal.GetDelegateForFunctionPointer<CrossRealmFunc1Delegate>(crossRealmFunc1Ptr);
            _pluginLog.Information($"CrossRealmFunc1 function found at 0x{crossRealmFunc1Ptr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find CrossRealmFunc1 signature");
        }

        try
        {
            var crossRealmFunc2Ptr = sigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B 6C 24 ?? 48 8B 74 24 ?? 48 83 C4 ?? 41 5F 41 5E 5F C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 40 57");
            _crossRealmFunc2 = Marshal.GetDelegateForFunctionPointer<CrossRealmFunc2Delegate>(crossRealmFunc2Ptr);
            _pluginLog.Information($"CrossRealmFunc2 function found at 0x{crossRealmFunc2Ptr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to find CrossRealmFunc2 signature");
        }
    }

    // Get CrossRealm proxy
    public IntPtr GetCrossRealmProxy()
    {
        var framework = Framework.Instance();
        if (framework == null) return IntPtr.Zero;

        var uiModule = framework->GetUIModule();
        if (uiModule == null) return IntPtr.Zero;

        var infoModule = uiModule->GetInfoModule();
        if (infoModule == null) return IntPtr.Zero;

        // InfoProxyId.CrossRealmParty is typically 17 (0x11)
        var crossRealmProxy = (IntPtr)infoModule->GetInfoProxyById(InfoProxyId.CrossRealmParty);

        return crossRealmProxy;
    }

    // Call CrossRealm function 1
    public void CallCrossRealmFunc1(IntPtr agentPtr)
    {
        if (_crossRealmFunc1 == null)
            throw new InvalidOperationException("CrossRealmFunc1 function pointer is null");

        var crossRealmProxy = GetCrossRealmProxy();
        if (crossRealmProxy == IntPtr.Zero)
            throw new InvalidOperationException("CrossRealmProxy is null");

        var dataPtr = agentPtr + 0x2318;
        _crossRealmFunc1(crossRealmProxy, dataPtr);
    }

    // Call CrossRealm function 2
    public void CallCrossRealmFunc2(IntPtr agentPtr)
    {
        if (_crossRealmFunc2 == null)
            throw new InvalidOperationException("CrossRealmFunc2 function pointer is null");

        var crossRealmProxy = GetCrossRealmProxy();
        if (crossRealmProxy == IntPtr.Zero)
            throw new InvalidOperationException("CrossRealmProxy is null");

        var dataPtr = agentPtr + 0x2710;
        _crossRealmFunc2(crossRealmProxy, dataPtr);
    }

    // Detour function for PartyMemberChange
    private void PartyMemberChangeDetour(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4)
    {
        try
        {
            // Get party size before the change
            int partySizeBefore = GetCurrentPartySize();
            _pluginLog.Debug($"[HOOK] PartyMemberChange - Size before: {partySizeBefore}");

            // Call the original function
            _partyMemberChangeHook!.Original(a1, a2, a3, a4);

            // Get party size after the change
            int partySizeAfter = GetCurrentPartySize();
            _pluginLog.Debug($"[HOOK] PartyMemberChange - Size after: {partySizeAfter}");

            // Notify plugin if there was a change
            if (partySizeBefore != partySizeAfter)
            {
                _plugin.OnPartyMemberChange(partySizeBefore, partySizeAfter);
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "[HOOK] Exception in PartyMemberChangeDetour");
            // If we can't handle it, at least call the original
            try
            {
                _partyMemberChangeHook!.Original(a1, a2, a3, a4);
            }
            catch
            {
                // Last resort - can't do anything
            }
        }
    }

    // Detour function for StartRecruiting
    private long StartRecruitingDetour(IntPtr agent)
    {
        try
        {
            bool isGameInitiated = !_isPluginInitiatedCall;
            _pluginLog.Information($"[HOOK] StartRecruiting called with agent: 0x{agent.ToInt64():X} (Game-initiated: {isGameInitiated})");

            _plugin.OnStartRecruitingCalled();

            // Call the original function
            var result = _startRecruitingHook!.Original(agent);

            _pluginLog.Information($"[HOOK] StartRecruiting returned: {result}");

            // Only backup data if this was initiated by the game (user clicking)
            if (result == 0 && isGameInitiated)
            {
                _pluginLog.Information("[HOOK] StartRecruiting succeeded from game interaction - backing up data");
                BackupSlotData(agent);
            }
            else if (result == 0 && !isGameInitiated)
            {
                _pluginLog.Information("[HOOK] StartRecruiting succeeded from plugin - skipping backup");
            }
            else
            {
                _pluginLog.Warning($"[HOOK] StartRecruiting may have failed (returned {result})");
            }

            return result;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "[HOOK] Exception in StartRecruitingDetour");
            // If we can't handle it, try to call the original
            try
            {
                return _startRecruitingHook!.Original(agent);
            }
            catch
            {
                // If even that fails, return error
                return -1;
            }
        }
        finally
        {
            // Always reset the flag after use
            _isPluginInitiatedCall = false;
        }
    }

    // Get current party size using CrossRealm first, then fallback to PartyList
    public int GetCurrentPartySize()
    {
        try
        {
            // First try CrossRealm party count
            if (_getCrossRealmPartyMemberCount != null)
            {
                byte crossRealmCount = _getCrossRealmPartyMemberCount();
                if (crossRealmCount > 0)
                {
                    return crossRealmCount;
                }
            }

            // Fallback to PartyList.Length
            int partyListCount = _plugin.PartyList.Length;
            if (partyListCount > 0)
            {
                return partyListCount;
            }

            // If both are 0, we're solo
            return 1;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "[PartySize] Error getting party size, defaulting to 1");
            return 1;
        }
    }

    // Backup slot data when recruitment succeeds
    private void BackupSlotData(IntPtr agent)
    {
        try
        {
            var slots = new PartyFinderSlots(agent, _pluginLog);
            var backupData = new SlotBackupInfo
            {
                TotalSlots = slots.GetTotalSlots(),
                SlotInfos = slots.GetAllSlots(),
                BackupTime = DateTime.UtcNow
            };

            _lastBackupData = backupData;

            _pluginLog.Information($"[BACKUP] Backed up slot data from game interaction at {backupData.BackupTime}");
            _pluginLog.Information($"[BACKUP] Total slots: {backupData.TotalSlots}");

            foreach (var slot in backupData.SlotInfos)
            {
                _pluginLog.Information($"[BACKUP] Slot {slot.Index + 1}:");
                _pluginLog.Information($"  - Taken: {slot.IsTaken}");
                _pluginLog.Information($"  - Job ID: {slot.JobId}");
                _pluginLog.Information($"  - Content ID: 0x{slot.ContentId:X}");
                _pluginLog.Information($"  - Allowed Jobs Mask: 0x{slot.AllowedJobsMask:X}");
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "[BACKUP] Failed to backup slot data");
        }
    }

    // Get the last backup data
    public SlotBackupInfo? GetLastBackupData()
    {
        return _lastBackupData;
    }

    // Set allowed jobs mask for a specific slot
    public void SetAllowedJobsMask(IntPtr agent, int slotIndex, ulong mask)
    {
        if (agent == IntPtr.Zero)
            throw new ArgumentException("Agent pointer is null");

        if (slotIndex < 0 || slotIndex >= 8) // Max 8 slots in party
            throw new ArgumentOutOfRangeException(nameof(slotIndex), "Slot index must be between 0 and 7");

        try
        {
            // Calculate the address for this slot's allowed jobs mask
            IntPtr allowedJobsAddress = agent + AgentOffsets.AllowedJobs + (slotIndex * 8);

            // Write the mask to memory
            *((ulong*)allowedJobsAddress) = mask;

            _pluginLog.Debug($"[SetAllowedJobsMask] Set slot {slotIndex + 1} allowed jobs mask to 0x{mask:X} at address 0x{allowedJobsAddress.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, $"[SetAllowedJobsMask] Failed to set allowed jobs mask for slot {slotIndex + 1}");
            throw;
        }
    }

    public IntPtr GetLookingForGroupAgent()
    {
        var moduleInstance = AgentModule.Instance();
        if (moduleInstance == null)
        {
            _pluginLog.Error("AgentModule instance is null");
            return IntPtr.Zero;
        }

        return (IntPtr)moduleInstance->GetAgentByInternalId(AgentId.LookingForGroup);
    }

    public void OpenRecruitmentWindow(IntPtr agent, byte update)
    {
        if (_openRecruitmentWindow == null)
            throw new InvalidOperationException("OpenRecruitmentWindow function pointer is null");

        _openRecruitmentWindow(agent, update, 0);
    }

    public long StartRecruiting(IntPtr agent)
    {
        if (_startRecruitingHook == null || !_startRecruitingHook.IsEnabled)
            throw new InvalidOperationException("StartRecruiting hook is not available or not enabled");

        _isPluginInitiatedCall = true;

        // Call our detour function directly, which will handle the timer and then call the original
        return _startRecruitingDetourDelegate != null
            ? _startRecruitingDetourDelegate(agent)
            : _startRecruitingHook.Original(agent);
    }

    public void LeaveDuty(IntPtr agent)
    {
        if (_leaveDuty == null)
            throw new InvalidOperationException("LeaveDuty function pointer is null");

        IntPtr pointerAt1C = Marshal.ReadIntPtr(agent + AgentOffsets.LeaveDutyPointer);
        _leaveDuty(pointerAt1C, 0);
    }

    public long RefreshListings(IntPtr agent, int commandId)
    {
        if (_refreshListings == null)
            throw new InvalidOperationException("RefreshListings function pointer is null");

        // Create AtkValue structure with command ID
        var atkValue = AtkValue.CreateLong(commandId);

        // Allocate memory for the AtkValue
        IntPtr atkValuePtr = Marshal.AllocHGlobal(Marshal.SizeOf<AtkValue>());
        try
        {
            Marshal.StructureToPtr(atkValue, atkValuePtr, false);
            return _refreshListings(agent, atkValuePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(atkValuePtr);
        }
    }

    public bool IsLocalPlayerPartyLeader()
    {
        if (_isLocalPlayerPartyLeader == null)
        {
            _pluginLog.Warning("IsLocalPlayerPartyLeader function pointer is null");
            return false;
        }

        try
        {
            return _isLocalPlayerPartyLeader() != 0;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error calling IsLocalPlayerPartyLeader");
            return false;
        }
    }

    public bool IsLocalPlayerInParty()
    {
        if (_isLocalPlayerInParty == null)
        {
            _pluginLog.Warning("IsLocalPlayerInParty function pointer is null");
            return false;
        }

        try
        {
            return _isLocalPlayerInParty() != 0;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error calling IsLocalPlayerInParty");
            return false;
        }
    }

    public ulong GetActiveRecruiterContentId()
    {
        if (_getActiveRecruiterContentId == null)
        {
            _pluginLog.Warning("GetActiveRecruiterContentId function pointer is null");
            return 0;
        }

        try
        {
            var agent = GetLookingForGroupAgent();
            if (agent == IntPtr.Zero)
            {
                _pluginLog.Warning("LookingForGroup agent is null for GetActiveRecruiterContentId");
                return 0;
            }

            return _getActiveRecruiterContentId(agent);
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error calling GetActiveRecruiterContentId");
            return 0;
        }
    }

    public void SmartRestoreJobMasks(IntPtr agent)
    {
        try
        {
            var slots = new PartyFinderSlots(agent, _pluginLog);
            var currentSlots = slots.GetAllSlots();

            // Determine whether to use overrides or backup
            bool useOverrides = _plugin.UseJobMaskOverride && _plugin.PluginConfig.SlotJobMaskOverrides.Count > 0;

            _pluginLog.Information($"[SmartRestore] Using {(useOverrides ? "OVERRIDE" : "BACKUP")} masks for ALL slots");

            var originalMasks = new List<(int Index, ulong Mask)>();

            if (useOverrides)
            {
                // Use ONLY override masks
                _pluginLog.Information("[SmartRestore] Override mode - will only use configured override masks");

                foreach (var kvp in _plugin.PluginConfig.SlotJobMaskOverrides)
                {
                    if (kvp.Key < currentSlots.Count && kvp.Value != 0)
                    {
                        originalMasks.Add((kvp.Key, kvp.Value));
                        _pluginLog.Information($"[SmartRestore] Using override mask for slot {kvp.Key + 1}: 0x{kvp.Value:X}");
                    }
                }

                if (originalMasks.Count == 0)
                {
                    _pluginLog.Warning("[SmartRestore] No valid override masks found despite override being enabled");
                    return;
                }
            }
            else
            {
                // Use ONLY backup masks
                var backupData = GetLastBackupData();

                if (!backupData.HasValue)
                {
                    _pluginLog.Warning("[SmartRestore] No backup data available");
                    return;
                }

                _pluginLog.Information("[SmartRestore] Backup mode - will only use masks from last recruitment");

                foreach (var backupSlot in backupData.Value.SlotInfos)
                {
                    if (backupSlot.Index < currentSlots.Count && backupSlot.AllowedJobsMask != 0)
                    {
                        originalMasks.Add((backupSlot.Index, backupSlot.AllowedJobsMask));
                        _pluginLog.Information($"[SmartRestore] Using backup mask for slot {backupSlot.Index + 1}: 0x{backupSlot.AllowedJobsMask:X}");
                    }
                }

                if (originalMasks.Count == 0)
                {
                    _pluginLog.Warning("[SmartRestore] No valid backup masks found");
                    return;
                }
            }

            // Now perform smart restoration with the chosen mask set
            var restorationMap = GetSmartRestorationMapping(currentSlots, originalMasks);

            // Apply the restoration
            foreach (var (slotIndex, mask) in restorationMap)
            {
                try
                {
                    SetAllowedJobsMask(agent, slotIndex, mask);
                    _pluginLog.Information($"[SmartRestore] Restored slot {slotIndex + 1} with mask 0x{mask:X}");
                }
                catch (Exception ex)
                {
                    _pluginLog.Error(ex, $"[SmartRestore] Failed to restore mask for slot {slotIndex + 1}");
                }
            }

            _pluginLog.Information($"[SmartRestore] Restoration complete - applied {restorationMap.Count} masks in {(useOverrides ? "OVERRIDE" : "BACKUP")} mode");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "[SmartRestore] Failed to perform smart restore");
        }
    }

    // Get smart restoration mapping
    private Dictionary<int, ulong> GetSmartRestorationMapping(List<PartyFinderSlots.SlotInfo> currentSlots, List<(int Index, ulong Mask)> originalMasks)
    {
        var restorationMap = new Dictionary<int, ulong>();
        var usedOriginalIndices = new HashSet<int>();

        _pluginLog.Information("[SmartRestore] Starting smart restoration mapping");
        _pluginLog.Information($"[SmartRestore] Current slots: {currentSlots.Count}, Original masks: {originalMasks.Count}");

        // First pass: identify which original masks are satisfied by current occupied slots
        foreach (var occupiedSlot in currentSlots.Where(s => s.IsTaken))
        {
            _pluginLog.Information($"[SmartRestore] Processing occupied slot {occupiedSlot.Index + 1} with job ID {occupiedSlot.JobId}");

            // Find the best matching original mask for this occupied slot
            var bestMatch = FindBestMatchingOriginalMask(occupiedSlot.JobId, originalMasks, usedOriginalIndices);
            if (bestMatch != null)
            {
                usedOriginalIndices.Add(bestMatch.Value.Index);
                _pluginLog.Information($"[SmartRestore] Occupied slot {occupiedSlot.Index + 1} matches original slot {bestMatch.Value.Index + 1}");
            }
            else
            {
                _pluginLog.Warning($"[SmartRestore] No matching original mask found for occupied slot {occupiedSlot.Index + 1}");
            }
        }

        // Second pass: restore empty slots with unused original masks
        var emptySlots = currentSlots.Where(s => !s.IsTaken).OrderBy(s => s.Index).ToList();
        var unusedOriginalMasks = originalMasks.Where(m => !usedOriginalIndices.Contains(m.Index)).OrderBy(m => m.Index).ToList();

        _pluginLog.Information($"[SmartRestore] Empty slots: {emptySlots.Count}, Unused masks: {unusedOriginalMasks.Count}");

        // Simply assign unused masks to empty slots in order
        for (int i = 0; i < Math.Min(emptySlots.Count, unusedOriginalMasks.Count); i++)
        {
            var emptySlot = emptySlots[i];
            var unusedMask = unusedOriginalMasks[i];

            restorationMap[emptySlot.Index] = unusedMask.Mask;
            _pluginLog.Information($"[SmartRestore] Mapping empty slot {emptySlot.Index + 1} to original mask from slot {unusedMask.Index + 1} (0x{unusedMask.Mask:X})");
        }

        return restorationMap;
    }

    // Find the best matching original mask for a given job
    private (int Index, ulong Mask)? FindBestMatchingOriginalMask(byte jobId, List<(int Index, ulong Mask)> originalMasks, HashSet<int> usedIndices)
    {
        var jobInfo = JobMaskConstants.GetJobByID(jobId);
        if (jobInfo == null)
        {
            _pluginLog.Warning($"[SmartRestore] Unknown job ID: {jobId}");
            return null;
        }

        // Find all original masks that this job satisfies
        var satisfiedMasks = originalMasks
            .Where(m => !usedIndices.Contains(m.Index) && JobMaskConstants.JobSatisfiesMask(jobId, m.Mask))
            .Select(m => (m.Index, m.Mask, Specificity: JobMaskConstants.GetSpecificityScore(m.Mask)))
            .OrderByDescending(m => m.Specificity) // Most specific first
            .ThenBy(m => m.Index) // Then by slot order
            .ToList();

        if (satisfiedMasks.Any())
        {
            var best = satisfiedMasks.First();
            _pluginLog.Information($"[SmartRestore] Job {jobInfo.Value.Name} best matches mask 0x{best.Mask:X} with specificity {best.Specificity}");
            return (best.Index, best.Mask);
        }

        return null;
    }

    // OpenAddon patching methods
    public void DisableOpenAddon()
    {
        if (_openAddonPatchAddress == IntPtr.Zero || _originalOpenAddonBytes == null || _isOpenAddonPatchApplied)
        {
            _pluginLog.Warning("Cannot apply OpenAddon patch: address not found, original bytes not saved, or patch already applied");
            return;
        }

        try
        {
            byte[] jumpPatch = new byte[] { 0xE9, 0x8D, 0x00, 0x00, 0x00, 0x90, 0x90 };
            SafeMemory.WriteBytes(_openAddonPatchAddress, jumpPatch);
            _pluginLog.Information($"Applied OpenAddon jump patch at 0x{_openAddonPatchAddress.ToInt64():X}");
            _isOpenAddonPatchApplied = true;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error applying OpenAddon patch");
            throw;
        }
    }

    public void EnableOpenAddon()
    {
        if (_openAddonPatchAddress == IntPtr.Zero || _originalOpenAddonBytes == null || !_isOpenAddonPatchApplied)
        {
            _pluginLog.Warning("Cannot restore OpenAddon patch: address not found, original bytes not saved, or patch not applied");
            return;
        }

        try
        {
            SafeMemory.WriteBytes(_openAddonPatchAddress, _originalOpenAddonBytes);
            _pluginLog.Information($"Restored original OpenAddon bytes at 0x{_openAddonPatchAddress.ToInt64():X}");
            _isOpenAddonPatchApplied = false;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Error restoring OpenAddon original bytes");
            throw;
        }
    }

    public void Dispose()
    {
        // Disable and dispose of the hooks
        if (_startRecruitingHook != null)
        {
            _pluginLog.Information("Disabling and disposing StartRecruiting hook");
            _startRecruitingHook.Disable();
            _startRecruitingHook.Dispose();
            _startRecruitingHook = null;
        }

        if (_partyMemberChangeHook != null)
        {
            _pluginLog.Information("Disabling and disposing PartyMemberChange hook");
            _partyMemberChangeHook.Disable();
            _partyMemberChangeHook.Dispose();
            _partyMemberChangeHook = null;
        }

        // Clear the detour delegate reference
        _startRecruitingDetourDelegate = null;

        // Restore patches if applied
        if (_isOpenAddonPatchApplied)
            EnableOpenAddon();
    }
}