namespace AutoPartyFinder.Constants;

public static class AgentOffsets
{
    public const int LeaveDutyPointer = 0x1C;
    public const int CurrentJobs = 0x1478;    // 1 byte per slot
    public const int ContentIds = 0x2348;     // 8 bytes per slot
    public const int MaxPartySize = 0x233D;   // 1 byte - total slots for this duty
    public const int AllowedJobs = 0x24C8;    // 8 bytes per slot - bitmask of allowed jobs
}