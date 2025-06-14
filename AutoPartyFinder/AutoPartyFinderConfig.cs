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

            ImGui.SetNextWindowSize(new Vector2(300, 200), ImGuiCond.FirstUseEver);

            ImGui.Begin($"{plugin.Name} Configuration", ref drawConfig, windowFlags);

            ImGui.Text("Party Finder Controls");
            ImGui.Separator();

            ImGui.Spacing();

            if (ImGui.Button("Start Recruiting", new Vector2(150, 30)))
            {
                plugin.StartRecruiting();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Click to start recruiting in Party Finder");
            }

            ImGui.Spacing();
            ImGui.Separator();

            ImGui.End();

            return drawConfig;
        }
    }
}