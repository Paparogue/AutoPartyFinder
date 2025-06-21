using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using ImGuiNET;
using AutoPartyFinder.Services;
using AutoPartyFinder.Constants;
using AutoPartyFinder.UI;

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
    [NonSerialized] private bool _allTanksSelected = false;
    [NonSerialized] private bool _allHealersSelected = false;
    [NonSerialized] private bool _allMeleeDPSSelected = false;
    [NonSerialized] private bool _allPhysicalRangedDPSSelected = false;
    [NonSerialized] private bool _allMagicalRangedDPSSelected = false;
    [NonSerialized] private bool _allDPSSelected = false;
    [NonSerialized] private bool _isAnyLocked = false;
    [NonSerialized] private bool _allJobsMode = false; // Track if "All Jobs" is selected

    // Status UI component
    [NonSerialized] private StatusInfoUI? _statusInfoUI;

    public void Init(AutoPartyFinder plugin)
    {
        _plugin = plugin;
        if (_plugin != null)
        {
            _plugin.RecoveryStepDelayMs = RecoveryStepDelayMs;
            _statusInfoUI = new StatusInfoUI(_plugin);
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

        ImGui.SetNextWindowSize(new Vector2(550, ShowDebugFunctions ? 800 : 600), ImGuiCond.FirstUseEver);
        ImGui.Begin($"{_plugin.Name} Configuration", ref drawConfig, windowFlags);

        if (ImGui.BeginTabBar("##MainTabBar"))
        {
            if (ImGui.BeginTabItem("Auto-Renewal"))
            {
                ImGui.Spacing();
                DrawAutoRenewalSection();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Job Override"))
            {
                ImGui.Spacing();
                DrawJobMaskOverrideSection();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Status"))
            {
                ImGui.Spacing();
                _statusInfoUI?.Draw();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Debug button only in Status tab
                if (ImGui.Button("Show Debug Functions", new Vector2(200, 30)))
                {
                    ShowDebugFunctions = !ShowDebugFunctions;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Toggle advanced debugging functions for testing individual Party Finder operations");
                }

                ImGui.EndTabItem();
            }

            if (ShowDebugFunctions)
            {
                if (ImGui.BeginTabItem("Debug"))
                {
                    ImGui.Spacing();
                    DrawDebugFunctions();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
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
                        _allTanksSelected = false;
                        _allHealersSelected = false;
                        _allMeleeDPSSelected = false;
                        _allPhysicalRangedDPSSelected = false;
                        _allMagicalRangedDPSSelected = false;
                        _allDPSSelected = false;
                        _isAnyLocked = false;
                        _allJobsMode = false;

                        // Load existing selection if any
                        if (SlotJobMaskOverrides.TryGetValue(i, out ulong existingMask))
                        {
                            // Check for category selections
                            if (existingMask == JobMaskConstants.AllJobs)
                            {
                                // All Jobs = All Tanks + All Healers + All DPS
                                _allTanksSelected = true;
                                _allHealersSelected = true;
                                _allDPSSelected = true;
                            }
                            else if (existingMask == JobMaskConstants.AllTanks)
                            {
                                _allTanksSelected = true;
                                _isAnyLocked = true;
                            }
                            else if (existingMask == JobMaskConstants.AllHealers)
                            {
                                _allHealersSelected = true;
                                _isAnyLocked = true;
                            }
                            else if (existingMask == JobMaskConstants.AllMeleeDPS)
                            {
                                _allMeleeDPSSelected = true;
                                _isAnyLocked = true;
                            }
                            else if (existingMask == JobMaskConstants.AllPhysicalRangedDPS)
                            {
                                _allPhysicalRangedDPSSelected = true;
                                _isAnyLocked = true;
                            }
                            else if (existingMask == JobMaskConstants.AllMagicalRangedDPS)
                            {
                                _allMagicalRangedDPSSelected = true;
                                _isAnyLocked = true;
                            }
                            else if (existingMask == JobMaskConstants.AllDPS)
                            {
                                _allDPSSelected = true;
                                _isAnyLocked = true;
                            }
                            else
                            {
                                // Load individual jobs
                                foreach (var job in JobMaskConstants.Jobs.Values)
                                {
                                    if ((existingMask & job.Mask) != 0)
                                    {
                                        _tempSelectedJobs.Add(job.Mask);
                                    }
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
                // Get the current mask from the first configured slot, or create a default
                ulong commonMask = 0;

                if (SlotJobMaskOverrides.Count > 0)
                {
                    commonMask = SlotJobMaskOverrides.Values.FirstOrDefault();
                }
                else
                {
                    // Default to All Jobs if nothing is configured
                    commonMask = JobMaskConstants.AllJobs;
                }

                if (commonMask != 0)
                {
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
        ImGui.SetNextWindowSize(new Vector2(600, 600), ImGuiCond.FirstUseEver);

        bool popupOpen = true;
        if (ImGui.Begin($"Configure Jobs for Slot {_currentConfigSlot + 1}###JobSelectionPopup",
            ref popupOpen, ImGuiWindowFlags.NoCollapse))
        {
            // Quick select buttons
            if (ImGui.Button("All Jobs", new Vector2(100, 30)))
            {
                ApplyAllJobs();
            }

            ImGui.SameLine();

            if (ImGui.Button("None", new Vector2(100, 30)))
            {
                ClearAllSelections();
                _allJobsMode = false; // Clear All Jobs mode
            }

            ImGui.Separator();
            ImGui.Spacing();

            // Category checkboxes with proper colors
            bool shouldLock = _isAnyLocked;

            // All Tanks
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.5f, 1f, 1)); // Blue
            if (shouldLock && !_allTanksSelected) ImGui.BeginDisabled();
            bool allTanksTemp = _allTanksSelected;
            if (ImGui.Checkbox("All Tanks", ref allTanksTemp))
            {
                if (allTanksTemp)
                {
                    ApplyCategorySelection(JobMaskConstants.JobCategory.Tank);
                }
                else
                {
                    // Unchecked - clear everything
                    ClearAllSelections();
                    _allJobsMode = false; // Clear All Jobs mode when unchecking
                }
            }
            if (shouldLock && !_allTanksSelected) ImGui.EndDisabled();
            ImGui.PopStyleColor();

            // All Healers
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.5f, 1)); // Green
            if (shouldLock && !_allHealersSelected) ImGui.BeginDisabled();
            bool allHealersTemp = _allHealersSelected;
            if (ImGui.Checkbox("All Healers", ref allHealersTemp))
            {
                if (allHealersTemp)
                {
                    ApplyCategorySelection(JobMaskConstants.JobCategory.Healer);
                }
                else
                {
                    // Unchecked - clear everything
                    ClearAllSelections();
                    _allJobsMode = false; // Clear All Jobs mode when unchecking
                }
            }
            if (shouldLock && !_allHealersSelected) ImGui.EndDisabled();
            ImGui.PopStyleColor();

            // All DPS
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1)); // Red
            if (shouldLock && !_allDPSSelected) ImGui.BeginDisabled();
            bool allDPSTemp = _allDPSSelected;
            if (ImGui.Checkbox("All DPS", ref allDPSTemp))
            {
                if (allDPSTemp)
                {
                    ApplyCategorySelection(JobMaskConstants.JobCategory.MeleeDPS, true); // All DPS flag
                }
                else
                {
                    // Unchecked - clear everything
                    ClearAllSelections();
                    _allJobsMode = false; // Clear All Jobs mode when unchecking
                }
            }
            if (shouldLock && !_allDPSSelected) ImGui.EndDisabled();
            ImGui.PopStyleColor();

            // Only show DPS subcategories if not in "All Jobs" mode
            if (!_allJobsMode)
            {
                ImGui.Separator();
                ImGui.Spacing();

                // Sub-category checkboxes
                // All Melee DPS
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1)); // Red
                if (shouldLock && !_allMeleeDPSSelected) ImGui.BeginDisabled();
                bool allMeleeTemp = _allMeleeDPSSelected;
                if (ImGui.Checkbox("All Melee DPS", ref allMeleeTemp))
                {
                    if (allMeleeTemp)
                    {
                        ApplyCategorySelection(JobMaskConstants.JobCategory.MeleeDPS);
                    }
                    else
                    {
                        // Unchecked - clear everything
                        ClearAllSelections();
                    }
                }
                if (shouldLock && !_allMeleeDPSSelected) ImGui.EndDisabled();
                ImGui.PopStyleColor();

                // All Physical Ranged DPS
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.2f, 1)); // Dark Yellow
                if (shouldLock && !_allPhysicalRangedDPSSelected) ImGui.BeginDisabled();
                bool allPhysicalTemp = _allPhysicalRangedDPSSelected;
                if (ImGui.Checkbox("All Physical Ranged DPS", ref allPhysicalTemp))
                {
                    if (allPhysicalTemp)
                    {
                        ApplyCategorySelection(JobMaskConstants.JobCategory.PhysicalRangedDPS);
                    }
                    else
                    {
                        // Unchecked - clear everything
                        ClearAllSelections();
                    }
                }
                if (shouldLock && !_allPhysicalRangedDPSSelected) ImGui.EndDisabled();
                ImGui.PopStyleColor();

                // All Magical Ranged DPS
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.3f, 1f, 1)); // Purple
                if (shouldLock && !_allMagicalRangedDPSSelected) ImGui.BeginDisabled();
                bool allMagicalTemp = _allMagicalRangedDPSSelected;
                if (ImGui.Checkbox("All Magical Ranged DPS", ref allMagicalTemp))
                {
                    if (allMagicalTemp)
                    {
                        ApplyCategorySelection(JobMaskConstants.JobCategory.MagicalRangedDPS);
                    }
                    else
                    {
                        // Unchecked - clear everything
                        ClearAllSelections();
                    }
                }
                if (shouldLock && !_allMagicalRangedDPSSelected) ImGui.EndDisabled();
                ImGui.PopStyleColor();
            }

            ImGui.Separator();
            ImGui.Spacing();

            // Individual job checkboxes by category
            ImGui.Text("Individual Jobs:");
            ImGui.Spacing();

            // Tanks
            ImGui.TextColored(new Vector4(0.3f, 0.5f, 1f, 1), "Tanks");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.Tank, shouldLock);

            ImGui.Spacing();

            // Healers
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.5f, 1), "Healers");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.Healer, shouldLock);

            ImGui.Spacing();

            // Melee DPS
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1), "Melee DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.MeleeDPS, shouldLock);

            ImGui.Spacing();

            // Physical Ranged DPS
            ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.2f, 1), "Physical Ranged DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.PhysicalRangedDPS, shouldLock);

            ImGui.Spacing();

            // Magical Ranged DPS
            ImGui.TextColored(new Vector4(0.8f, 0.3f, 1f, 1), "Magical Ranged DPS");
            DrawJobCheckboxes(JobMaskConstants.JobCategory.MagicalRangedDPS, shouldLock);

            ImGui.Separator();
            ImGui.Spacing();

            // Current selection display
            ulong currentMask = GetCurrentSelectionMask();

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

    private void ClearAllSelections()
    {
        _tempSelectedJobs.Clear();
        _allTanksSelected = false;
        _allHealersSelected = false;
        _allDPSSelected = false;
        _allMeleeDPSSelected = false;
        _allPhysicalRangedDPSSelected = false;
        _allMagicalRangedDPSSelected = false;
        _isAnyLocked = false;
        _allJobsMode = false;
    }

    private void ApplyAllJobs()
    {
        ClearAllSelections();
        // All Jobs = tick all tanks, all healers, and all dps
        _allTanksSelected = true;
        _allHealersSelected = true;
        _allDPSSelected = true;
        _allJobsMode = true; // Set All Jobs mode
        _isAnyLocked = true; // Lock everything

        // Tick all individual jobs for each category
        foreach (var job in JobMaskConstants.Jobs.Values)
        {
            _tempSelectedJobs.Add(job.Mask);
        }
    }

    private void ApplyCategorySelection(JobMaskConstants.JobCategory category, bool isAllDPS = false)
    {
        ClearAllSelections();
        _isAnyLocked = true;
        _allJobsMode = false; // Clear All Jobs mode when selecting any specific category

        if (isAllDPS)
        {
            // All DPS selected - tick all DPS subcategories
            _allDPSSelected = true;
            _allMeleeDPSSelected = true;
            _allPhysicalRangedDPSSelected = true;
            _allMagicalRangedDPSSelected = true;

            // Add all DPS jobs
            var allDPSJobs = JobMaskConstants.GetJobsByCategory(JobMaskConstants.JobCategory.MeleeDPS)
                .Concat(JobMaskConstants.GetJobsByCategory(JobMaskConstants.JobCategory.PhysicalRangedDPS))
                .Concat(JobMaskConstants.GetJobsByCategory(JobMaskConstants.JobCategory.MagicalRangedDPS));

            foreach (var job in allDPSJobs)
            {
                _tempSelectedJobs.Add(job.Mask);
            }
        }
        else
        {
            // Individual category selection
            switch (category)
            {
                case JobMaskConstants.JobCategory.Tank:
                    _allTanksSelected = true;
                    break;
                case JobMaskConstants.JobCategory.Healer:
                    _allHealersSelected = true;
                    break;
                case JobMaskConstants.JobCategory.MeleeDPS:
                    _allMeleeDPSSelected = true;
                    break;
                case JobMaskConstants.JobCategory.PhysicalRangedDPS:
                    _allPhysicalRangedDPSSelected = true;
                    break;
                case JobMaskConstants.JobCategory.MagicalRangedDPS:
                    _allMagicalRangedDPSSelected = true;
                    break;
            }

            // Add jobs for this category
            var jobs = JobMaskConstants.GetJobsByCategory(category);
            foreach (var job in jobs)
            {
                _tempSelectedJobs.Add(job.Mask);
            }
        }
    }

    private ulong GetCurrentSelectionMask()
    {
        // Check for special category selections first
        if (_allTanksSelected && _allHealersSelected && _allDPSSelected)
            return JobMaskConstants.AllJobs;
        if (_allTanksSelected)
            return JobMaskConstants.AllTanks;
        if (_allHealersSelected)
            return JobMaskConstants.AllHealers;
        if (_allDPSSelected)
            return JobMaskConstants.AllDPS;
        if (_allMeleeDPSSelected)
            return JobMaskConstants.AllMeleeDPS;
        if (_allPhysicalRangedDPSSelected)
            return JobMaskConstants.AllPhysicalRangedDPS;
        if (_allMagicalRangedDPSSelected)
            return JobMaskConstants.AllMagicalRangedDPS;

        // Otherwise, calculate from individual selections
        return _tempSelectedJobs.Count > 0 ? _tempSelectedJobs.Aggregate((a, b) => a | b) : 0;
    }

    private void DrawJobCheckboxes(JobMaskConstants.JobCategory category, bool disabled)
    {
        var jobs = JobMaskConstants.GetJobsByCategory(category);
        int columnsPerRow = 3;

        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.BeginTable($"JobTable_{category}", columnsPerRow, ImGuiTableFlags.SizingStretchSame))
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                ImGui.TableNextColumn();

                var job = jobs[i];
                bool isSelected = _tempSelectedJobs.Contains(job.Mask);

                // Apply category color
                switch (category)
                {
                    case JobMaskConstants.JobCategory.Tank:
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.3f, 0.5f, 1f, 1));
                        break;
                    case JobMaskConstants.JobCategory.Healer:
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.3f, 1f, 0.5f, 1));
                        break;
                    case JobMaskConstants.JobCategory.MeleeDPS:
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1f, 0.3f, 0.3f, 1));
                        break;
                    case JobMaskConstants.JobCategory.PhysicalRangedDPS:
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.8f, 0.6f, 0.2f, 1));
                        break;
                    case JobMaskConstants.JobCategory.MagicalRangedDPS:
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.8f, 0.3f, 1f, 1));
                        break;
                }

                if (ImGui.Checkbox($"{job.Name}##job{job.Name}", ref isSelected))
                {
                    if (isSelected)
                    {
                        if (_isAnyLocked)
                        {
                            // If a category is locked, clear all and add just this job
                            ClearAllSelections();
                            _tempSelectedJobs.Add(job.Mask);
                        }
                        else
                        {
                            // No category locked - just add this job
                            _tempSelectedJobs.Add(job.Mask);
                        }
                    }
                    else
                    {
                        _tempSelectedJobs.Remove(job.Mask);
                        // If we're deselecting a job, clear All Jobs mode
                        _allJobsMode = false;
                    }
                }

                ImGui.PopStyleColor();
            }

            ImGui.EndTable();
        }

        if (disabled)
        {
            ImGui.EndDisabled();
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