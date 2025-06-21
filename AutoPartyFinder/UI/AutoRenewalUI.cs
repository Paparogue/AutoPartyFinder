using System;
using System.Numerics;
using ImGuiNET;

namespace AutoPartyFinder.UI;

public class AutoRenewalUI
{
    private readonly AutoPartyFinder _plugin;

    public AutoRenewalUI(AutoPartyFinder plugin)
    {
        _plugin = plugin;
    }

    public void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.8f, 0, 1));
        ImGui.Text("Auto-Renewal Feature");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        bool isEnabled = _plugin.IsAutoRenewalEnabled;

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
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Renewal pending...");
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
}