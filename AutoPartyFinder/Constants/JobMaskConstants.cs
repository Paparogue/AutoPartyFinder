using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoPartyFinder.Constants
{
    public static class JobMaskConstants
    {
        // Individual job masks
        public const ulong Paladin = 0x100;
        public const ulong Warrior = 0x400;
        public const ulong DarkKnight = 0x200000;
        public const ulong Gunbreaker = 0x4000000;

        public const ulong WhiteMage = 0x2000;
        public const ulong Scholar = 0x20000;
        public const ulong Astrologian = 0x400000;
        public const ulong Sage = 0x20000000;

        public const ulong Monk = 0x200;
        public const ulong Dragoon = 0x800;
        public const ulong Ninja = 0x80000;
        public const ulong Samurai = 0x800000;
        public const ulong Reaper = 0x10000000;
        public const ulong Viper = 0x40000000;

        public const ulong Bard = 0x1000;
        public const ulong Machinist = 0x100000;
        public const ulong Dancer = 0x8000000;

        public const ulong BlackMage = 0x4000;
        public const ulong Summoner = 0x10000;
        public const ulong RedMage = 0x1000000;
        public const ulong Pictomancer = 0x80000000;

        public const ulong BlueMage = 0x2000000;

        // Role categories
        public const ulong AllTanks = 0x4200500;
        public const ulong AllHealers = 0x20422040;
        public const ulong PureHealers = 0x402040;      // WHM + AST
        public const ulong BarrierHealers = 0x20020000; // SCH + SGE
        public const ulong AllMeleeDPS = 0x508C0A14;
        public const ulong AllPhysicalRangedDPS = 0x8101020;
        public const ulong AllMagicalRangedDPS = 0x8301C080;
        public const ulong AllDPS = 0xDB9DDAB4;
        public const ulong AllJobs = 0xFFFFFFFE;
        public const ulong Anybody = 0xFFFFFFFE;
        public const ulong None = 0x0;

        // Job information structure
        public struct JobInfo
        {
            public string Name { get; set; }
            public byte JobId { get; set; }
            public ulong Mask { get; set; }
            public JobCategory Category { get; set; }
            public HealerSubcategory? HealerSubcategory { get; set; }
        }

        public enum JobCategory
        {
            Tank,
            Healer,
            MeleeDPS,
            PhysicalRangedDPS,
            MagicalRangedDPS
        }

        public enum HealerSubcategory
        {
            Pure,
            Barrier
        }

        // Job definitions with IDs
        public static readonly Dictionary<string, JobInfo> Jobs = new()
        {
            // Tanks
            ["Paladin"] = new JobInfo { Name = "Paladin", JobId = 19, Mask = Paladin, Category = JobCategory.Tank },
            ["Warrior"] = new JobInfo { Name = "Warrior", JobId = 21, Mask = Warrior, Category = JobCategory.Tank },
            ["DarkKnight"] = new JobInfo { Name = "Dark Knight", JobId = 32, Mask = DarkKnight, Category = JobCategory.Tank },
            ["Gunbreaker"] = new JobInfo { Name = "Gunbreaker", JobId = 37, Mask = Gunbreaker, Category = JobCategory.Tank },

            // Healers
            ["WhiteMage"] = new JobInfo { Name = "White Mage", JobId = 24, Mask = WhiteMage, Category = JobCategory.Healer, HealerSubcategory = HealerSubcategory.Pure },
            ["Scholar"] = new JobInfo { Name = "Scholar", JobId = 28, Mask = Scholar, Category = JobCategory.Healer, HealerSubcategory = HealerSubcategory.Barrier },
            ["Astrologian"] = new JobInfo { Name = "Astrologian", JobId = 33, Mask = Astrologian, Category = JobCategory.Healer, HealerSubcategory = HealerSubcategory.Pure },
            ["Sage"] = new JobInfo { Name = "Sage", JobId = 40, Mask = Sage, Category = JobCategory.Healer, HealerSubcategory = HealerSubcategory.Barrier },

            // Melee DPS
            ["Monk"] = new JobInfo { Name = "Monk", JobId = 20, Mask = Monk, Category = JobCategory.MeleeDPS },
            ["Dragoon"] = new JobInfo { Name = "Dragoon", JobId = 22, Mask = Dragoon, Category = JobCategory.MeleeDPS },
            ["Ninja"] = new JobInfo { Name = "Ninja", JobId = 30, Mask = Ninja, Category = JobCategory.MeleeDPS },
            ["Samurai"] = new JobInfo { Name = "Samurai", JobId = 34, Mask = Samurai, Category = JobCategory.MeleeDPS },
            ["Reaper"] = new JobInfo { Name = "Reaper", JobId = 39, Mask = Reaper, Category = JobCategory.MeleeDPS },
            ["Viper"] = new JobInfo { Name = "Viper", JobId = 41, Mask = Viper, Category = JobCategory.MeleeDPS },

            // Physical Ranged DPS
            ["Bard"] = new JobInfo { Name = "Bard", JobId = 23, Mask = Bard, Category = JobCategory.PhysicalRangedDPS },
            ["Machinist"] = new JobInfo { Name = "Machinist", JobId = 31, Mask = Machinist, Category = JobCategory.PhysicalRangedDPS },
            ["Dancer"] = new JobInfo { Name = "Dancer", JobId = 38, Mask = Dancer, Category = JobCategory.PhysicalRangedDPS },

            // Magical Ranged DPS
            ["BlackMage"] = new JobInfo { Name = "Black Mage", JobId = 25, Mask = BlackMage, Category = JobCategory.MagicalRangedDPS },
            ["Summoner"] = new JobInfo { Name = "Summoner", JobId = 27, Mask = Summoner, Category = JobCategory.MagicalRangedDPS },
            ["RedMage"] = new JobInfo { Name = "Red Mage", JobId = 35, Mask = RedMage, Category = JobCategory.MagicalRangedDPS },
            ["Pictomancer"] = new JobInfo { Name = "Pictomancer", JobId = 42, Mask = Pictomancer, Category = JobCategory.MagicalRangedDPS },

            // Limited Jobs
            ["BlueMage"] = new JobInfo { Name = "Blue Mage", JobId = 36, Mask = BlueMage, Category = JobCategory.MagicalRangedDPS }
        };

        // Create reverse lookup by job ID
        public static readonly Dictionary<byte, JobInfo> JobsByID = Jobs.Values.ToDictionary(j => j.JobId);

        // Get job info by ID
        public static JobInfo? GetJobByID(byte jobId)
        {
            return JobsByID.TryGetValue(jobId, out var job) ? job : null;
        }

        // Calculate specificity score for a mask
        public static int GetSpecificityScore(ulong mask)
        {
            // Check if it's a single job (highest specificity)
            if (IsSingleJob(mask))
                return 4;

            // Check for subcategories
            if (mask == PureHealers || mask == BarrierHealers ||
                mask == AllMeleeDPS || mask == AllPhysicalRangedDPS || mask == AllMagicalRangedDPS)
                return 3;

            // Check for main categories
            if (mask == AllTanks || mask == AllHealers || mask == AllDPS)
                return 2;

            // Any job or custom combinations
            return 1;
        }

        // Check if a mask represents a single job
        public static bool IsSingleJob(ulong mask)
        {
            // A single job mask will have exactly one job bit set
            return Jobs.Values.Any(j => j.Mask == mask);
        }

        // Check if a job satisfies a mask requirement
        public static bool JobSatisfiesMask(byte jobId, ulong mask)
        {
            var jobInfo = GetJobByID(jobId);
            if (jobInfo == null)
                return false;

            return (jobInfo.Value.Mask & mask) != 0;
        }

        // Helper method to get job names from a mask
        public static List<string> GetJobNamesFromMask(ulong mask)
        {
            var jobNames = new List<string>();

            foreach (var job in Jobs.Values)
            {
                if ((mask & job.Mask) != 0)
                {
                    jobNames.Add(job.Name);
                }
            }

            return jobNames;
        }

        // Helper method to get a display string for a mask
        public static string GetJobDisplayString(ulong mask)
        {
            // Special cases first
            if (mask == None) return "None";
            if (mask == Anybody) return "Anybody";
            if (mask == AllJobs) return "All Jobs";
            if (mask == AllTanks) return "All Tanks";
            if (mask == AllHealers) return "All Healers";
            if (mask == PureHealers) return "Pure Healers";
            if (mask == BarrierHealers) return "Barrier Healers";
            if (mask == AllDPS) return "All DPS";
            if (mask == AllMeleeDPS) return "All Melee DPS";
            if (mask == AllPhysicalRangedDPS) return "All Physical Ranged DPS";
            if (mask == AllMagicalRangedDPS) return "All Magical Ranged DPS";

            var jobNames = GetJobNamesFromMask(mask);
            if (jobNames.Count == 0) return $"Custom (0x{mask:X})";
            if (jobNames.Count <= 3) return string.Join(", ", jobNames);

            return $"{jobNames.Count} jobs selected";
        }

        // Get jobs by category
        public static List<JobInfo> GetJobsByCategory(JobCategory category)
        {
            return Jobs.Values.Where(j => j.Category == category).ToList();
        }

        // Get healers by subcategory
        public static List<JobInfo> GetHealersBySubcategory(HealerSubcategory subcategory)
        {
            return Jobs.Values.Where(j => j.Category == JobCategory.Healer && j.HealerSubcategory == subcategory).ToList();
        }
    }
}