using System;
using System.Numerics;
using ImGuiNET;
using AutoPartyFinder.Services;
using AutoPartyFinder.Constants;

namespace AutoPartyFinder.UI;

public class StatusInfoUI
{
    private readonly AutoPartyFinder _plugin;

    public StatusInfoUI(AutoPartyFinder plugin)
    {
        _plugin = plugin;
    }

    public void Draw()
    {
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

        // Party Finder Slots Status
        DrawPartyFinderSlotsStatus();
    }

    private void DrawPartySizeTracking()
    {
        ImGui.TextColored(new Vector4(0.5f, 1f, 0.8f, 1), "Party Size Tracking:");
        ImGui.Separator();

        var lastKnownSize = _plugin.LastKnownPartySize;
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
            bool inParty = _plugin.GetPartyFinderService().IsLocalPlayerInParty();
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
            ulong contentId = _plugin.GetPartyFinderService().GetActiveRecruiterContentId();

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
            var agent = _plugin.GetPartyFinderService().GetLookingForGroupAgent();
            if (agent == IntPtr.Zero)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Party Finder agent not available");
                return;
            }

            var slots = new PartyFinderSlots(agent, _plugin.PluginLog);
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

                        // Add tooltip showing which jobs are allowed
                        if (ImGui.IsItemHovered())
                        {
                            string jobDisplay = JobMaskConstants.GetJobDisplayString(slot.AllowedJobsMask);
                            ImGui.SetTooltip(jobDisplay);
                        }
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
}