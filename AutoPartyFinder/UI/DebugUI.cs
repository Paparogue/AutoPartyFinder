﻿using System;
using System.Numerics;
using ImGuiNET;

namespace AutoPartyFinder.UI;

public class DebugUI
{
    private readonly AutoPartyFinder _plugin;
    private readonly AutoPartyFinderConfig _config;

    public DebugUI(AutoPartyFinder plugin, AutoPartyFinderConfig config)
    {
        _plugin = plugin;
        _config = config;
    }

    public void Draw()
    {
        ImGui.Text("Party Finder Debug Controls");
        ImGui.Separator();
        ImGui.Spacing();

        DrawIndividualFunctionTests();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawRecoveryControls();
    }

    private void DrawIndividualFunctionTests()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), "Individual Function Tests:");
        ImGui.Spacing();

        ImGui.Text("OpenAddon Patch Controls:");
        if (ImGui.Button("Disable OpenAddon", new Vector2(140, 30)))
        {
            _plugin.TestDisableOpenAddon();
        }
        ImGui.SameLine();
        if (ImGui.Button("Enable OpenAddon", new Vector2(140, 30)))
        {
            _plugin.TestEnableOpenAddon();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Disable/Enable OpenAddon function");
        }

        ImGui.Spacing();

        ImGui.Text("Window Operations:");
        if (ImGui.Button("Open Recruitment Window", new Vector2(200, 30)))
        {
            _plugin.TestOpenRecruitmentWindow();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Opens the Party Finder recruitment window");
        }

        ImGui.Spacing();

        ImGui.Text("Recruitment Actions:");
        if (ImGui.Button("Start Recruiting", new Vector2(200, 30)))
        {
            _plugin.TestStartRecruiting();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Starts Party Finder recruitment");
        }

        ImGui.Spacing();

        if (ImGui.Button("Leave Duty", new Vector2(200, 30)))
        {
            _plugin.TestLeaveDuty();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Leaves the current duty");
        }

        ImGui.Spacing();

        if (ImGui.Button("Refresh Listings", new Vector2(200, 30)))
        {
            _plugin.TestRefreshListings();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Calls RefreshListings with command ID 0xE\nThis triggers recruitment-related actions");
        }

        ImGui.Spacing();

        ImGui.Text("Status Check Functions:");
        if (ImGui.Button("Test Party Leader Status", new Vector2(200, 30)))
        {
            _plugin.TestIsLocalPlayerPartyLeader();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Check if you are the party leader");
        }

        if (ImGui.Button("Test In Party Status", new Vector2(200, 30)))
        {
            _plugin.TestIsLocalPlayerInParty();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Check if you are in a party");
        }

        if (ImGui.Button("Test Active Recruiter", new Vector2(200, 30)))
        {
            _plugin.TestGetActiveRecruiterContentId();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Get the content ID of the player with 'Looking for Party' status");
        }
    }

    private void DrawRecoveryControls()
    {
        ImGui.TextColored(new Vector4(1, 0.5f, 1, 1), "Recovery Controls:");
        ImGui.Separator();

        // Recovery Step Delay Configuration
        ImGui.Text("Recovery Step Delay (ms):");
        int delayMs = _plugin.RecoveryStepDelayMs;
        if (ImGui.InputInt("##RecoveryStepDelay", ref delayMs, 100, 500))
        {
            // Clamp between 100ms and 10000ms
            delayMs = Math.Max(100, Math.Min(10000, delayMs));
            _plugin.RecoveryStepDelayMs = delayMs;
            _config.Save();
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