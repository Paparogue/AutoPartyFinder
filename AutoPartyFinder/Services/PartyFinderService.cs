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

namespace AutoPartyFinder.Services;

public unsafe class PartyFinderService
{
    private readonly IPluginLog _pluginLog;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly AutoPartyFinder _plugin;

    // Hook for StartRecruiting
    private Hook<StartRecruitingDelegate>? _startRecruitingHook;

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
        // Disable and dispose of the hook
        if (_startRecruitingHook != null)
        {
            _pluginLog.Information("Disabling and disposing StartRecruiting hook");
            _startRecruitingHook.Disable();
            _startRecruitingHook.Dispose();
            _startRecruitingHook = null;
        }

        // Clear the detour delegate reference
        _startRecruitingDetourDelegate = null;

        // Restore patches if applied
        if (_isOpenAddonPatchApplied)
            EnableOpenAddon();
    }
}