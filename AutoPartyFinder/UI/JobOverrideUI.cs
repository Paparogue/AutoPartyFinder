using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using AutoPartyFinder.Services;
using AutoPartyFinder.Constants;

namespace AutoPartyFinder.UI;

public class JobOverrideUI
{
    private readonly AutoPartyFinder _plugin;
    private readonly AutoPartyFinderConfig _config;

    // UI State
    private bool _showJobSelectionPopup = false;
    private int _currentConfigSlot = -1;
    private HashSet<ulong> _tempSelectedJobs = new();
    private bool _allTanksSelected = false;
    private bool _allHealersSelected = false;
    private bool _allMeleeDPSSelected = false;
    private bool _allPhysicalRangedDPSSelected = false;
    private bool _allMagicalRangedDPSSelected = false;
    private bool _allDPSSelected = false;
    private bool _isAnyLocked = false;
    private bool _allJobsMode = false;

    public JobOverrideUI(AutoPartyFinder plugin, AutoPartyFinderConfig config)
    {
        _plugin = plugin;
        _config = config;
    }

    public void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 0.5f, 1));
        ImGui.Text("Job Mask Override");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        // Main toggle
        bool useOverride = _config.UseJobMaskOverride;
        if (ImGui.Checkbox("Enable Job Mask Override", ref useOverride))
        {
            _config.UseJobMaskOverride = useOverride;
            _config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, uses custom job masks instead of the ones saved when recruitment started");
        }

        if (_config.UseJobMaskOverride)
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
        var agent = _plugin.GetPartyFinderService().GetLookingForGroupAgent();
        int totalSlots = 0;

        if (agent != IntPtr.Zero)
        {
            var slots = new PartyFinderSlots(agent, _plugin.PluginLog);
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
                    if (_config.SlotJobMaskOverrides.ContainsKey(i))
                    {
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "●");
                        ImGui.SameLine();
                    }
                    ImGui.Text($"{i + 1}");

                    ImGui.TableNextColumn();

                    // Show current mask value
                    if (_config.SlotJobMaskOverrides.TryGetValue(i, out ulong mask))
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
                        OpenJobSelectionPopup(i);
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            // Apply buttons
            if (ImGui.Button("Apply to All Slots", new Vector2(150, 30)))
            {
                ApplyToAllSlots(totalSlots);
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

    public void DrawPopups()
    {
        if (_showJobSelectionPopup)
        {
            DrawJobSelectionPopup();
        }
    }

    private void OpenJobSelectionPopup(int slotIndex)
    {
        _currentConfigSlot = slotIndex;
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
        if (_config.SlotJobMaskOverrides.TryGetValue(slotIndex, out ulong existingMask))
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

            DrawCategoryCheckboxes();

            ImGui.Separator();
            ImGui.Spacing();

            DrawIndividualJobsSection();

            ImGui.Separator();
            ImGui.Spacing();

            DrawCurrentSelection();

            ImGui.Spacing();

            // Save/Cancel buttons
            if (ImGui.Button("Save", new Vector2(100, 30)))
            {
                SaveJobSelection();
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

    private void DrawCategoryCheckboxes()
    {
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
                ClearAllSelections();
                _allJobsMode = false;
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
                ClearAllSelections();
                _allJobsMode = false;
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
                ClearAllSelections();
                _allJobsMode = false;
            }
        }
        if (shouldLock && !_allDPSSelected) ImGui.EndDisabled();
        ImGui.PopStyleColor();

        // Only show DPS subcategories if not in "All Jobs" mode and "All DPS" is not selected
        if (!_allJobsMode && !_allDPSSelected)
        {
            ImGui.Separator();
            ImGui.Spacing();

            DrawDPSSubcategories(shouldLock);
        }
    }

    private void DrawDPSSubcategories(bool shouldLock)
    {
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
                ClearAllSelections();
            }
        }
        if (shouldLock && !_allMagicalRangedDPSSelected) ImGui.EndDisabled();
        ImGui.PopStyleColor();
    }

    private void DrawIndividualJobsSection()
    {
        ImGui.Text("Individual Jobs:");
        ImGui.Spacing();

        bool shouldLock = _isAnyLocked;

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
    }

    private void DrawCurrentSelection()
    {
        ulong currentMask = GetCurrentSelectionMask();

        ImGui.Text($"Current Selection: 0x{currentMask:X}");
        ImGui.TextWrapped(JobMaskConstants.GetJobDisplayString(currentMask));
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

    private void SaveJobSelection()
    {
        ulong currentMask = GetCurrentSelectionMask();

        if (currentMask != 0)
        {
            _config.SlotJobMaskOverrides[_currentConfigSlot] = currentMask;
        }
        else
        {
            _config.SlotJobMaskOverrides.Remove(_currentConfigSlot);
        }

        _config.Save();
        _showJobSelectionPopup = false;
    }

    private void ApplyToAllSlots(int totalSlots)
    {
        // Get the current mask from the first configured slot, or create a default
        ulong commonMask = 0;

        if (_config.SlotJobMaskOverrides.Count > 0)
        {
            commonMask = _config.SlotJobMaskOverrides.Values.FirstOrDefault();
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
                _config.SlotJobMaskOverrides[i] = commonMask;
            }
            _config.Save();
        }
    }

    private void CopyMasksFromBackup()
    {
        var backupData = _plugin.GetPartyFinderService().GetLastBackupData();
        if (!backupData.HasValue)
        {
            return;
        }

        _config.SlotJobMaskOverrides.Clear();

        foreach (var slot in backupData.Value.SlotInfos)
        {
            if (slot.AllowedJobsMask != 0)
            {
                _config.SlotJobMaskOverrides[slot.Index] = slot.AllowedJobsMask;
            }
        }

        _config.Save();
    }
}