using System.Numerics;
using Dalamud.Configuration;
using ImGuiNET;

namespace AutoPartyFinder
{
    public class AutoPartyFinderConfig : IPluginConfiguration
    {
        public int Version { get; set; }

        [NonSerialized] private AutoPartyFinder plugin;

        public void Init(AutoPartyFinder plugin)
        {
            this.plugin = plugin;
        }

        public void Save()
        {
            plugin.PluginInterface.SavePluginConfig(this);
        }

        public bool DrawConfigUI()
        {
            var drawConfig = true;

            var windowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;

            ImGui.SetNextWindowSize(new Vector2(350, 220), ImGuiCond.FirstUseEver);

            ImGui.Begin($"{plugin.Name} Configuration", ref drawConfig, windowFlags);

            ImGui.Text("Party Finder Controls");
            ImGui.Separator();

            ImGui.Spacing();

            bool isProcessing = plugin._isProcessingAutoRecruit;

            if (isProcessing)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Auto Recruit", new Vector2(200, 40)))
            {
                plugin.AutoRecruit();
            }

            if (isProcessing)
            {
                ImGui.EndDisabled();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Processing...");
            }

            if (ImGui.IsItemHovered() && !isProcessing)
            {
                ImGui.SetTooltip("Opens recruitment window and starts recruiting");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextWrapped("This plugin automates Party Finder recruitment.");
            ImGui.TextWrapped("Clicking the button will:");
            ImGui.BulletText("Open the recruitment window");
            ImGui.BulletText("Start recruiting automatically");
            ImGui.Spacing();
            ImGui.TextDisabled("Each step has a 2 second delay");

            ImGui.End();

            return drawConfig;
        }
    }
}