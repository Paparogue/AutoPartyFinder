using System;
using System.Collections.Generic;
using Dalamud.Configuration;
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
    [NonSerialized] private ConfigWindow? _configWindow;

    public void Init(AutoPartyFinder plugin)
    {
        _plugin = plugin;
        if (_plugin != null)
        {
            _plugin.RecoveryStepDelayMs = RecoveryStepDelayMs;
            // Don't create ConfigWindow here - create it on demand in DrawConfigUI
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
        // Create ConfigWindow on demand if it doesn't exist
        if (_configWindow == null && _plugin != null)
        {
            _configWindow = new ConfigWindow(_plugin, this);
        }
        return _configWindow?.Draw() ?? false;
    }
}