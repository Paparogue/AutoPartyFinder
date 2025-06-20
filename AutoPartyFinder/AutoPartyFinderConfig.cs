using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using ImGuiNET;
using AutoPartyFinder.Services;
using AutoPartyFinder.Constants;

namespace AutoPartyFinder;

public class AutoPartyFinderConfig : IPluginConfiguration
{
    public int Version { get; set; }

    public bool ShowDebugFunctions { get; set; } = false;
    public int RecoveryStepDelayMs { get; set; } = 1000;

    // Job Mask Override Settings
    public bool UseJobMaskOverride { get; set; } = false;
    public Dictionary<int, ulong> SlotJobMaskOverrides { get; set; } = new();

    [NonSerialized] private AutoPartyFinder? _plugin;

    // UI State (not saved)
    [NonSerialized] private bool _showJobSelectionPopup = false;
    [NonSerialized] private int _currentConfigSlot = -1;
    [NonSerialized] private HashSet<ulong> _tempSelectedJobs = new();

    public void Init(AutoPartyFinder plugin)
    {
        _plugin = plugin;
        if (_plugin != null)
        {
            _plugin.RecoveryStepDelayMs = RecoveryStepDelayMs;
        }
    }

    public void Save()
    {
        if (_plugin != null)
        {
            RecoveryStepDelayMs = _plugin.RecoveryStepDelayMs;
        }
        _plugin?.PluginInterface.SavePluginConfig(this);
    }

    public bool DrawConfigUI()
    {
        if (_plugin == null)
            return false;

        var drawConfig = true;
        var windowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;

        ImGui.SetNextWindowSize(new Vector2(450, ShowDebugFunctions ? 1400 : 1000), ImGuiCond.FirstUseEver);
        ImGui.Begin($"{_plugin.Name} Configuration", ref drawConfig, windowFlags);

        // Auto-Renewal Feature Section
        DrawAutoRenewalSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Job Mask Override Section
        DrawJobMaskOverrideSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Party Size Tracking Section
        DrawPartySizeTracking();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Party Status Section
        DrawPartyStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Active Recruiter Status
        DrawActiveRecruiterStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Party Finder Slots Status - Always visible
        DrawPartyFinderSlotsStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Debug Mode Toggle
        bool debugMode = ShowDebugFunctions;
        if (ImGui.Checkbox("Show Debug Functions", ref debugMode))
        {
            ShowDebugFunctions = debugMode;
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show advanced debugging functions for testing individual Party Finder operations");
        }

        // Only show debug functions if enabled
        if (ShowDebugFunctions)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawDebugFunctions();
        }

        ImGui.End();

        // Draw job selection popup if needed
        if (_showJobSelectionPopup)
        {
            DrawJobSelectionPopup();
        }

        return drawConfig;
    }

    private void DrawJobMaskOverrideSection()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 0.5f, 1));
        ImGui.Text("Job Mask Override");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        // Main toggle
        bool useOverride = UseJobMaskOverride;
        if (ImGui.Checkbox("Enable Job Mask Override", ref useOverride))
        {
            UseJobMaskOverride = useOverride;
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, uses custom job masks instead of the ones saved when recruitment started");
        }

        if (UseJobMaskOverride)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "● Override Active");
            ImGui.TextWrapped("Custom job masks will be used when party decrease recovery runs");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "○ Override Inactive");
            ImGui.TextWrapped("Original job masks from recruitment start will be used");
        }

        ImGui.Spacing();

        // Get current duty slots
        var agent = _plugin!.GetPartyFinderService().GetLookingForGroupAgent();
        int totalSlots = 0;

        if (agent != IntPtr.Zero)
        {
            var slots = new PartyFinderSlots(agent, _plugin!.PluginLog);
            totalSlots = slots.GetTotalSlots();
        }

        if (totalSlots > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.5f, 1),
                $"Configuring {totalSlots} slots for current duty");

            ImGui.Spacing();

            // Slot configuration table
            if (ImGui.BeginTable("JobMaskOverrideTable", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Current Mask", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < totalSlots; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    // Slot number with indicator if override exists
                    if (SlotJobMaskOverrides.ContainsKey(i))
                    {
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "●");
                        ImGui.SameLine();
                    }
                    ImGui.Text($"{i + 1}");

                    ImGui.TableNextColumn();

                    // Show current mask value
                    if (SlotJobMaskOverrides.TryGetValue(i, out ulong mask))
                    {
                        string maskDisplay = JobMaskConstants.GetJobDisplayString(mask);
                        ImGui.Text($"0x{mask:X}");

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(maskDisplay);
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("Not configured");
                    }

                    ImGui.TableNextColumn();

                    // Configure button
                    if (ImGui.Button($"Configure##slot{i}", new Vector2(90, 0)))
                    {
                        _currentConfigSlot = i;
                        _tempSelectedJobs.Clear();

                        // Load existing selection if any
                        if (SlotJobMaskOverrides.TryGetValue(i, out ulong existingMask))
                        {
                            foreach (var job in JobMaskConstants.Jobs.Values)
                            {
                                if ((existingMask & job.Mask) != 0)
                                {
                                    _tempSelectedJobs.Add(job.Mask);
                                }
                            }
                        }

                        _showJobSelectionPopup = true;
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            // Apply buttons
            if (ImGui.Button("Apply to All Slots", new Vector2(150, 30)))
            {
                if (_tempSelectedJobs.Count > 0 || SlotJobMaskOverrides.Count > 0)
                {
                    ulong commonMask = _tempSelectedJobs.Count > 0
                        ? _tempSelectedJobs.Aggregate((a, b) => a | b)
                        : SlotJobMaskOverrides.Values.FirstOrDefault();

                    for (int i = 0; i < totalSlots; i++)
                    {
                        SlotJobMaskOverrides[i] = commonMask;
                    }
                    Save();
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Copy from Backup", new Vector2(150, 30)))
            {
                CopyMasksFromBackup();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Copy job masks from the last recruitment backup");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No duty selected or Party Finder not open");
        }
    }

    private void DrawJobSelectionPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);

        bool popupOpen = true;
        if (ImGui.Begin($"Configure Jobs for Slot {_currentConfigSlot + 1}###JobSelectionPopup",
            ref popupOpen, ImGuiWindowFlags.NoCollapse))
        {
            // Quick select buttons
            if (ImGui.Button("All Jobs", new Vector2(100, 30)))
            {
                _tempSelectedJobs.Clear();
                foreach (var job in JobMaskConstants.Jobs.Values)
                {
                    if (job.Name != "Blue Mage") // Exclude Blue Mage from "All"
                        _tempSelectedJobs.Add(job.Mask);
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("None", new Vector2(100, 30)))
            {
                _tempSelectedJobs.Clear();
            }

            ImGui.Separator();
            ImGui.Spacing();

            // Category checkboxes
            DrawCategoryCheckbox("All Tanks", JobMaskConstants.AllTanks,
                JobMaskConstants.JobCategory.Tank);

            DrawCategoryCheckbox("All Healers", JobMaskConstants.AllHealers,
                JobMaskConstants.JobCategory.Healer);

            DrawCategoryCheckbox("All DPS", JobMaskConstants.AllDPS,
                JobMaskConstants.JobCategory.MeleeDPS,
                JobMaskConstants.JobCategory.PhysicalRangedDPS,
                JobMaskConstants.JobCategory.MagicalRangedDPS);

            ImGui.Separator();
            ImGui.Spacing();

            // Sub-category checkboxes
            DrawCategoryCheckbox("All Melee DPS", JobMaskConstants.AllMeleeDPS,
                JobMaskConstants.JobCategory.MeleeDPS);

            DrawCategoryCheckbox("All Physical Ranged DPS", JobMaskConstants.AllPhysicalRangedDPS,
                JobMaskConstants.JobCategory.PhysicalRangedDPS);

            DrawCategoryCheckbox("All Magical Ranged DPS", JobMaskConstants.AllMagicalRangedDPS,
                JobMaskConstants.JobCategory.MagicalRangedDPS);

            ImGui.Separator();
            ImGui.Spacing();

            // Individual job checkboxes by category
            ImGui.Text("Individual Jobs:");
            ImGui.Spacing();

            // Tanks
            ImGui.TextColored(new Vector4(0.3f, 0.5f, 1f, 1), "Tanks");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.Tank);

            ImGui.Spacing();

            // Healers
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.5f, 1), "Healers");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.Healer);

            ImGui.Spacing();

            // Melee DPS
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1), "Melee DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.MeleeDPS);

            ImGui.Spacing();

            // Physical Ranged DPS
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1), "Physical Ranged DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.PhysicalRangedDPS);

            ImGui.Spacing();

            // Magical Ranged DPS
            ImGui.TextColored(new Vector4(0.8f, 0.3f, 1f, 1), "Magical Ranged DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.MagicalRangedDPS);

            ImGui.Separator();
            ImGui.Spacing();

            // Current selection display
            ulong currentMask = _tempSelectedJobs.Count > 0
                ? _tempSelectedJobs.Aggregate((a, b) => a | b)
                : 0;

            ImGui.Text($"Current Selection: 0x{currentMask:X}");
            ImGui.TextWrapped(JobMaskConstants.GetJobDisplayString(currentMask));

            ImGui.Spacing();

            // Save/Cancel buttons
            if (ImGui.Button("Save", new Vector2(100, 30)))
            {
                if (currentMask != 0)
                {
                    SlotJobMaskOverrides[_currentConfigSlot] = currentMask;
                }
                else
                {
                    SlotJobMaskOverrides.Remove(_currentConfigSlot);
                }
                Save();
                _showJobSelectionPopup = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 30)))
            {
                _showJobSelectionPopup = false;
            }

            ImGui.End();
        }

        if (!popupOpen)
        {
            _showJobSelectionPopup = false;
        }
    }

    private void DrawCategoryCheckbox(string label, ulong categoryMask,
        params JobMaskConstants.JobCategory[] categories)
    {
        var jobsInCategory = new List<JobMaskConstants.JobInfo>();
        foreach (var category in categories)
        {
            jobsInCategory.AddRange(JobMaskConstants.GetJobsByCategory(category));
        }

        bool allSelected = jobsInCategory.All(j => _tempSelectedJobs.Contains(j.Mask));
        bool someSelected = jobsInCategory.Any(j => _tempSelectedJobs.Contains(j.Mask));

        if (someSelected && !allSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        }

        bool isChecked = allSelected;
        if (ImGui.Checkbox(label, ref isChecked))
        {
            if (isChecked)
            {
                foreach (var job in jobsInCategory)
                {
                    _tempSelectedJobs.Add(job.Mask);
                }
            }
            else
            {
                foreach (var job in jobsInCategory)
                {
                    _tempSelectedJobs.Remove(job.Mask);
                }
            }
        }

        if (someSelected && !allSelected)
        {
            ImGui.PopStyleColor();
        }
    }

    private void DrawJobCheckboxes(JobMaskConstants.JobCategory category)
    {
        var jobs = JobMaskConstants.GetJobsByCategory(category);
        int columnsPerRow = 3;

        for (int i = 0; i < jobs.Count; i++)
        {
            if (i % columnsPerRow != 0)
                ImGui.SameLine();

            var job = jobs[i];
            bool isSelected = _tempSelectedJobs.Contains(job.Mask);

            if (ImGui.Checkbox($"{job.Name}##job{job.Id}", ref isSelected))
            {
                if (isSelected)
                    _tempSelectedJobs.Add(job.Mask);
                else
                    _tempSelectedJobs.Remove(job.Mask);
            }
        }
    }

    private void CopyMasksFromBackup()
    {
        var backupData = _plugin!.GetPartyFinderService().GetLastBackupData();
        if (!backupData.HasValue)
        {
            return;
        }

        SlotJobMaskOverrides.Clear();

        foreach (var slot in backupData.Value.SlotInfos)
        {
            if (slot.AllowedJobsMask != 0)
            {
                SlotJobMaskOverrides[slot.Index] = slot.AllowedJobsMask;
            }
        }

        Save();
    }

    private void DrawAutoRenewalSection()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.8f, 0, 1));
        ImGui.Text("Auto-Renewal Feature");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        bool isEnabled = _plugin!.IsAutoRenewalEnabled;

        // Show status with visual indication
        if (isEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
            ImGui.Text("● AUTO-RENEWAL ACTIVE");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1));
            ImGui.Text("○ Auto-Renewal Inactive");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();

        // Main toggle button
        if (isEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1));

            if (ImGui.Button("DISABLE AUTO-RENEWAL", new Vector2(400, 40)))
            {
                _plugin.ToggleAutoRenewal();
            }

            ImGui.PopStyleColor(3);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.5f, 0.1f, 1));

            if (ImGui.Button("ENABLE AUTO-RENEWAL", new Vector2(400, 40)))
            {
                _plugin.ToggleAutoRenewal();
            }

            ImGui.PopStyleColor(3);
        }

        ImGui.Spacing();

        // Show additional info when enabled
        if (isEnabled)
        {
            ImGui.Spacing();

            // Show last recruitment time
            var lastRecruitment = _plugin.LastRecruitmentTime;
            if (lastRecruitment != DateTime.MinValue)
            {
                var timeSince = DateTime.UtcNow - lastRecruitment;
                ImGui.Text($"Last recruitment: {(int)timeSince.TotalMinutes} minutes ago");

                // Progress bar showing time until next renewal
                float progress = (float)(timeSince.TotalMinutes / AutoPartyFinder.RENEWAL_INTERVAL_MINUTES);
                ImGui.ProgressBar(progress, new Vector2(400, 20),
                    $"{(int)timeSince.TotalMinutes}/{AutoPartyFinder.RENEWAL_INTERVAL_MINUTES} minutes");

                if (progress >= 1.0f)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠ Renewal pending...");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Waiting for any recruitment attempt...");
                ImGui.TextWrapped("Start recruiting manually.");
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.5f, 1), "Timer starts immediately when recruitment is attempted.");
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1),
                $"Auto-renewal will trigger every {AutoPartyFinder.RENEWAL_INTERVAL_MINUTES} minutes");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1),
                $"({60 - AutoPartyFinder.RENEWAL_INTERVAL_MINUTES} minutes before listing expiry)");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Click the button above to enable automatic Party Finder renewal.");
        }
    }

    private void DrawPartySizeTracking()
    {
        ImGui.TextColored(new Vector4(0.5f, 1f, 0.8f, 1), "Party Size Tracking:");
        ImGui.Separator();

        var lastKnownSize = _plugin!.LastKnownPartySize;
        var pendingRecovery = _plugin.PendingPartyRecovery;
        var decreaseDetectedTime = _plugin.PartyDecreaseDetectedTime;
        var currentSize = _plugin.GetPartyFinderService().GetCurrentPartySize();

        if (lastKnownSize == -1)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Not tracking (not recruiting)");
        }
        else
        {
            ImGui.Text($"Current party size: {currentSize}");
            ImGui.Text($"Last known size: {lastKnownSize}");

            if (pendingRecovery && decreaseDetectedTime != DateTime.MinValue)
            {
                var timeSinceDecrease = DateTime.UtcNow - decreaseDetectedTime;
                var timeRemaining = Math.Max(0, 10 - timeSinceDecrease.TotalSeconds);

                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Party size decreased - Recovery pending");
                ImGui.ProgressBar((float)(timeSinceDecrease.TotalSeconds / 10), new Vector2(400, 20),
                    $"Recovery in {timeRemaining:F1} seconds");
            }
            else if (currentSize < lastKnownSize)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Party size just decreased!");
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓ Party size stable");
            }
        }

        ImGui.Spacing();
    }

    private void DrawPartyStatus()
    {
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Party Status:");
        ImGui.Separator();

        try
        {
            bool inParty = _plugin!.GetPartyFinderService().IsLocalPlayerInParty();
            bool isLeader = _plugin.GetPartyFinderService().IsLocalPlayerPartyLeader();

            if (inParty)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "You are in a party");

                if (isLeader)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "You are the party leader");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "You are not the party leader");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "You are not in a party");
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error checking party status: {ex.Message}");
        }
    }

    private void DrawActiveRecruiterStatus()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Active Recruiter Status:");
        ImGui.Separator();

        try
        {
            ulong contentId = _plugin!.GetPartyFinderService().GetActiveRecruiterContentId();

            if (contentId != 0)
            {
                ImGui.TextColored(new Vector4(0, 1, 1, 1), "Active recruiter found!");
                ImGui.Text($"Content ID: 0x{contentId:X}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No active recruiter");
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error checking recruiter status: {ex.Message}");
        }
    }

    private void DrawPartyFinderSlotsStatus()
    {
        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Party Finder Slots Status:");
        ImGui.Separator();

        try
        {
            var agent = _plugin!.GetPartyFinderService().GetLookingForGroupAgent();
            if (agent == IntPtr.Zero)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Party Finder agent not available");
                return;
            }

            var slots = new PartyFinderSlots(agent, _plugin!.PluginLog);
            var slotInfos = slots.GetAllSlots();
            int totalSlots = slots.GetTotalSlots();
            int availableSlots = slots.GetAvailableSlotCount();

            if (totalSlots == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No duty selected or data not available");
                return;
            }

            ImGui.Text($"Total Slots: {totalSlots}");
            ImGui.Text($"Available Slots: {availableSlots}");
            ImGui.Spacing();

            // Create a table for better visualization
            if (ImGui.BeginTable("SlotTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 40);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Job ID", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Content ID", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Allowed Jobs Mask", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var slot in slotInfos)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{slot.Index + 1}");

                    ImGui.TableNextColumn();
                    if (slot.IsTaken)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "TAKEN");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "EMPTY");
                    }

                    ImGui.TableNextColumn();
                    if (slot.IsTaken)
                    {
                        ImGui.Text($"{slot.JobId}");
                    }
                    else
                    {
                        ImGui.TextDisabled("-");
                    }

                    ImGui.TableNextColumn();
                    if (slot.IsTaken)
                    {
                        ImGui.Text($"0x{slot.ContentId:X}");
                    }
                    else
                    {
                        ImGui.TextDisabled("-");
                    }

                    ImGui.TableNextColumn();
                    if (slot.AllowedJobsMask != 0)
                    {
                        ImGui.Text($"0x{slot.AllowedJobsMask:X}");
                    }
                    else
                    {
                        ImGui.TextDisabled("0x0");
                    }
                }

                ImGui.EndTable();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading slots: {ex.Message}");
        }
    }

    private void DrawDebugFunctions()
    {
        ImGui.Text("Party Finder Debug Controls");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), "Individual Function Tests:");
        ImGui.Spacing();

        ImGui.Text("OpenAddon Patch Controls:");
        if (ImGui.Button("Disable OpenAddon", new Vector2(140, 30)))
        {
            _plugin!.TestDisableOpenAddon();
        }
        ImGui.SameLine();
        if (ImGui.Button("Enable OpenAddon", new Vector2(140, 30)))
        {
            _plugin!.TestEnableOpenAddon();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Disable/Enable OpenAddon function");
        }

        ImGui.Spacing();

        ImGui.Text("Window Operations:");
        if (ImGui.Button("Open Recruitment Window", new Vector2(200, 30)))
        {
            _plugin!.TestOpenRecruitmentWindow();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Opens the Party Finder recruitment window");
        }

        ImGui.Spacing();

        ImGui.Text("Recruitment Actions:");
        if (ImGui.Button("Start Recruiting", new Vector2(200, 30)))
        {
            _plugin!.TestStartRecruiting();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Starts Party Finder recruitment");
        }

        ImGui.Spacing();

        if (ImGui.Button("Leave Duty", new Vector2(200, 30)))
        {
            _plugin!.TestLeaveDuty();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Leaves the current duty");
        }

        ImGui.Spacing();

        if (ImGui.Button("Refresh Listings", new Vector2(200, 30)))
        {
            _plugin!.TestRefreshListings();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Calls RefreshListings with command ID 0xE\nThis triggers recruitment-related actions");
        }

        ImGui.Spacing();

        ImGui.Text("Status Check Functions:");
        if (ImGui.Button("Test Party Leader Status", new Vector2(200, 30)))
        {
            _plugin!.TestIsLocalPlayerPartyLeader();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Check if you are the party leader");
        }

        if (ImGui.Button("Test In Party Status", new Vector2(200, 30)))
        {
            _plugin!.TestIsLocalPlayerInParty();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Check if you are in a party");
        }

        if (ImGui.Button("Test Active Recruiter", new Vector2(200, 30)))
        {
            _plugin!.TestGetActiveRecruiterContentId();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Get the content ID of the player with 'Looking for Party' status");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Recovery Controls
        ImGui.TextColored(new Vector4(1, 0.5f, 1, 1), "Recovery Controls:");
        ImGui.Separator();

        // Recovery Step Delay Configuration
        ImGui.Text("Recovery Step Delay (ms):");
        int delayMs = _plugin!.RecoveryStepDelayMs;
        if (ImGui.InputInt("##RecoveryStepDelay", ref delayMs, 100, 500))
        {
            // Clamp between 100ms and 10000ms
            delayMs = Math.Max(100, Math.Min(10000, delayMs));
            _plugin.RecoveryStepDelayMs = delayMs;
            Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Delay between each step in the recovery sequence (100-10000ms)");
        }

        ImGui.Text($"Current delay: {_plugin.RecoveryStepDelayMs}ms");

        ImGui.Spacing();

        // Manual Recovery Button
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.2f, 0.8f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.3f, 0.9f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.1f, 0.7f, 1));

        if (ImGui.Button("Execute Party Decrease Recovery", new Vector2(400, 35)))
        {
            _plugin.ManualExecutePartyDecreaseRecovery();
        }

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Manually trigger the party decrease recovery sequence\nThis will:\n" +
                            "1. Disable OpenAddon\n" +
                            "2. Open recruitment window\n" +
                            "3. Re-enable OpenAddon\n" +
                            "4. Restore job masks\n" +
                            "5. Leave duty\n" +
                            "6. Start recruiting");
        }
    }
}