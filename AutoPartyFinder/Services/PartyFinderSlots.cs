using System;
using System.Collections.Generic;
using AutoPartyFinder.Constants;
using Dalamud.Plugin.Services;

namespace AutoPartyFinder.Services;

public unsafe class PartyFinderSlots
{
    private readonly IntPtr _agentPtr;
    private readonly IPluginLog _pluginLog;

    public struct SlotInfo
    {
        public int Index { get; set; }
        public bool IsTaken { get; set; }
        public byte JobId { get; set; }
        public ulong ContentId { get; set; }
        public ulong AllowedJobsMask { get; set; }
    }

    public PartyFinderSlots(IntPtr agentPtr, IPluginLog pluginLog)
    {
        _agentPtr = agentPtr;
        _pluginLog = pluginLog;
    }

    public int GetTotalSlots()
    {
        try
        {
            // Get the maximum party size for current duty
            return *((byte*)(_agentPtr + AgentOffsets.MaxPartySize));
        }
        catch
        {
            return 0;
        }
    }

    public bool IsSlotTaken(int slotIndex)
    {
        // First check if this slot even exists for current duty
        if (slotIndex >= GetTotalSlots())
            return false; // Slot doesn't exist

        byte jobId = *((byte*)(_agentPtr + AgentOffsets.CurrentJobs + slotIndex));
        return jobId != 0;
    }



    public List<SlotInfo> GetAllSlots()
    {
        var slots = new List<SlotInfo>();
        int totalSlots = GetTotalSlots();

        // Only check slots that exist for this duty
        for (int slot = 0; slot < totalSlots; slot++)
        {
            byte jobId = *((byte*)(_agentPtr + AgentOffsets.CurrentJobs + slot));
            ulong contentId = 0;
            ulong allowedJobsMask = 0;

            if (jobId != 0)
            {
                contentId = *((ulong*)(_agentPtr + AgentOffsets.ContentIds + (slot * 8)));
            }

            // Get allowed jobs mask for this slot
            allowedJobsMask = *((ulong*)(_agentPtr + AgentOffsets.AllowedJobs + (slot * 8)));

            slots.Add(new SlotInfo
            {
                Index = slot,
                IsTaken = jobId != 0,
                JobId = jobId,
                ContentId = contentId,
                AllowedJobsMask = allowedJobsMask
            });
        }

        return slots;
    }

    public void CheckAllSlots()
    {
        int totalSlots = GetTotalSlots();
        _pluginLog.Information($"This duty has {totalSlots} slots total");

        // Only check slots that exist for this duty
        for (int slot = 0; slot < totalSlots; slot++)
        {
            byte jobId = *((byte*)(_agentPtr + AgentOffsets.CurrentJobs + slot));
            ulong allowedJobsMask = *((ulong*)(_agentPtr + AgentOffsets.AllowedJobs + (slot * 8)));

            if (jobId != 0)
            {
                ulong contentId = *((ulong*)(_agentPtr + AgentOffsets.ContentIds + (slot * 8)));
                _pluginLog.Information($"Slot {slot + 1} is TAKEN by Job ID {jobId} (Content ID: 0x{contentId:X})");
            }
            else
            {
                _pluginLog.Information($"Slot {slot + 1} is EMPTY");
            }

            _pluginLog.Information($"  Allowed Jobs Mask: 0x{allowedJobsMask:X}");
        }
    }

    public int GetAvailableSlotCount()
    {
        int totalSlots = GetTotalSlots();
        int takenSlots = 0;

        for (int i = 0; i < totalSlots; i++)
        {
            if (IsSlotTaken(i))
                takenSlots++;
        }

        return totalSlots - takenSlots;
    }
}