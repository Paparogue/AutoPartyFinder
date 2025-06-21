using System;
using System.Numerics;
using ImGuiNET;

namespace AutoPartyFinder.UI;

public class ConfigWindow
{
    private readonly AutoPartyFinder _plugin;
    private readonly AutoPartyFinderConfig _config;

    // UI Components
    private readonly AutoRenewalUI _autoRenewalUI;
    private readonly JobOverrideUI _jobOverrideUI;
    private readonly StatusInfoUI _statusInfoUI;
    private readonly DebugUI _debugUI;

    public ConfigWindow(AutoPartyFinder plugin, AutoPartyFinderConfig config)
    {
        _plugin = plugin;
        _config = config;

        _autoRenewalUI = new AutoRenewalUI(plugin);
        _jobOverrideUI = new JobOverrideUI(plugin, config);
        _statusInfoUI = new StatusInfoUI(plugin);
        _debugUI = new DebugUI(plugin, config);
    }

    public bool Draw()
    {
        var drawConfig = true;
        var windowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;

        ImGui.SetNextWindowSize(new Vector2(550, _config.ShowDebugFunctions ? 800 : 600), ImGuiCond.FirstUseEver);
        ImGui.Begin($"{_plugin.Name} Configuration", ref drawConfig, windowFlags);

        if (ImGui.BeginTabBar("##MainTabBar"))
        {
            if (ImGui.BeginTabItem("Auto-Renewal"))
            {
                ImGui.Spacing();
                _autoRenewalUI.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Job Override"))
            {
                ImGui.Spacing();
                _jobOverrideUI.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Status"))
            {
                ImGui.Spacing();
                _statusInfoUI.Draw();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Debug button only in Status tab
                if (ImGui.Button("Show Debug Functions", new Vector2(200, 30)))
                {
                    _config.ShowDebugFunctions = !_config.ShowDebugFunctions;
                    _config.Save();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Toggle advanced debugging functions for testing individual Party Finder operations");
                }

                ImGui.EndTabItem();
            }

            if (_config.ShowDebugFunctions)
            {
                if (ImGui.BeginTabItem("Debug"))
                {
                    ImGui.Spacing();
                    _debugUI.Draw();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }

        ImGui.End();

        // Draw any popups (like job selection)
        _jobOverrideUI.DrawPopups();

        return drawConfig;
    }
}