using System;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using AutoPartyFinder.Services;

namespace AutoPartyFinder.UI;

public class TestFunctions
{
    private readonly AutoPartyFinder _plugin;
    private readonly PartyFinderService _partyFinderService;
    private readonly FrameworkQueueService _queueService;
    private readonly IPluginLog _pluginLog;

    public TestFunctions(AutoPartyFinder plugin, PartyFinderService partyFinderService, FrameworkQueueService queueService, IPluginLog pluginLog)
    {
        _plugin = plugin;
        _partyFinderService = partyFinderService;
        _queueService = queueService;
        _pluginLog = pluginLog;
    }

    public void TestDisableOpenAddon()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                _pluginLog.Information("Testing DisableOpenAddon");
                _partyFinderService.DisableOpenAddon();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to disable OpenAddon");
            }
        });
    }

    public void TestEnableOpenAddon()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                _pluginLog.Information("Testing EnableOpenAddon");
                _partyFinderService.EnableOpenAddon();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to enable OpenAddon");
            }
        });
    }

    public void TestOpenRecruitmentWindow()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Calling OpenRecruitmentWindow with agent at 0x{agent.ToInt64():X}");
                _partyFinderService.OpenRecruitmentWindow(agent, 0);
                _pluginLog.Information("OpenRecruitmentWindow called successfully");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to call OpenRecruitmentWindow");
            }
        });
    }

    public void TestStartRecruiting()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Calling StartRecruiting with agent at 0x{agent.ToInt64():X}");
                var result = _partyFinderService.StartRecruiting(agent);
                _pluginLog.Information($"StartRecruiting returned: {result}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to call StartRecruiting");
            }
        });
    }

    public void TestLeaveDuty()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Calling LeaveDuty with agent at 0x{agent.ToInt64():X}");
                _partyFinderService.LeaveDuty(agent);
                _pluginLog.Information("LeaveDuty called successfully");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to call LeaveDuty");
            }
        });
    }

    public void TestRefreshListings()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Calling RefreshListings with agent at 0x{agent.ToInt64():X} and command ID 0xE");
                var result = _partyFinderService.RefreshListings(agent, 0xE);
                _pluginLog.Information($"RefreshListings returned: {result}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to call RefreshListings");
            }
        });
    }

    public void TestIsLocalPlayerPartyLeader()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                _pluginLog.Information("Testing IsLocalPlayerPartyLeader");
                bool isLeader = _partyFinderService.IsLocalPlayerPartyLeader();
                _pluginLog.Information($"IsLocalPlayerPartyLeader returned: {isLeader}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to test IsLocalPlayerPartyLeader");
            }
        });
    }

    public void TestIsLocalPlayerInParty()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                _pluginLog.Information("Testing IsLocalPlayerInParty");
                bool inParty = _partyFinderService.IsLocalPlayerInParty();
                _pluginLog.Information($"IsLocalPlayerInParty returned: {inParty}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to test IsLocalPlayerInParty");
            }
        });
    }

    public void TestGetActiveRecruiterContentId()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                _pluginLog.Information("Testing GetActiveRecruiterContentId");
                ulong contentId = _partyFinderService.GetActiveRecruiterContentId();
                _pluginLog.Information($"GetActiveRecruiterContentId returned: 0x{contentId:X} ({contentId})");

                if (contentId != 0)
                {
                    _pluginLog.Information("Someone in the party has 'Looking for Party' status");
                }
                else
                {
                    _pluginLog.Information("No one in the party has 'Looking for Party' status");
                }
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to test GetActiveRecruiterContentId");
            }
        });
    }

    public void TestPartyFinderSlots()
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Testing PartyFinderSlots with agent at 0x{agent.ToInt64():X}");
                var slots = new PartyFinderSlots(agent, _pluginLog);

                slots.CheckAllSlots();

                int availableSlots = slots.GetAvailableSlotCount();
                _pluginLog.Information($"Available slots: {availableSlots}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to test PartyFinderSlots");
            }
        });
    }

    // New UpdatePartyFinderListings test function
    public void TestUpdatePartyFinderListings(byte preserveSelection)
    {
        _queueService.QueueAction(() =>
        {
            try
            {
                var agent = _partyFinderService.GetLookingForGroupAgent();
                if (agent == IntPtr.Zero)
                {
                    _pluginLog.Error("LookingForGroup agent is null");
                    return;
                }

                _pluginLog.Information($"Calling UpdatePartyFinderListings with agent at 0x{agent.ToInt64():X} and preserveSelection={preserveSelection}");
                _partyFinderService.UpdatePartyFinderListings(agent, preserveSelection);
                _pluginLog.Information("UpdatePartyFinderListings called successfully");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to call UpdatePartyFinderListings");
            }
        });
    }
}