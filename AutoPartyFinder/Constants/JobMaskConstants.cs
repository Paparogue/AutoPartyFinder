using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoPartyFinder.Constants
{
    public static class JobMaskConstants
    {
        // Individual job masks
        public const ulong Gladiator = 0x2;
        public const ulong Paladin = 0x100;
        public const ulong Marauder = 0x8;
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

        // Pre-calculated combination masks
        public const ulong AllTanks = 0x420050A;
        public const ulong AllHealers = 0x20422040;
        public const ulong AllMeleeDPS = 0x508C0A14;
        public const ulong AllPhysicalRangedDPS = 0x8101020;
        public const ulong AllMagicalRangedDPS = 0x8301C080;
        public const ulong AllDPS = 0xDB9DDAB4;
        public const ulong AllJobs = AllTanks | AllHealers | AllDPS; // Calculated from categories
        public const ulong Anybody = 0xFFFFFFFE;
        public const ulong None = 0x0;

        // Job information structure
        public struct JobInfo
        {
            public string Name { get; set; }
            public ulong Mask { get; set; }
            public JobCategory Category { get; set; }
        }

        public enum JobCategory
        {
            Tank,
            Healer,
            MeleeDPS,
            PhysicalRangedDPS,
            MagicalRangedDPS
        }

        // Job definitions without IDs
        public static readonly Dictionary<string, JobInfo> Jobs = new()
        {
            // Tanks
            ["Gladiator"] = new JobInfo { Name = "Gladiator", Mask = Gladiator, Category = JobCategory.Tank },
            ["Paladin"] = new JobInfo { Name = "Paladin", Mask = Paladin, Category = JobCategory.Tank },
            ["Marauder"] = new JobInfo { Name = "Marauder", Mask = Marauder, Category = JobCategory.Tank },
            ["Warrior"] = new JobInfo { Name = "Warrior", Mask = Warrior, Category = JobCategory.Tank },
            ["DarkKnight"] = new JobInfo { Name = "Dark Knight", Mask = DarkKnight, Category = JobCategory.Tank },
            ["Gunbreaker"] = new JobInfo { Name = "Gunbreaker", Mask = Gunbreaker, Category = JobCategory.Tank },

            // Healers
            ["WhiteMage"] = new JobInfo { Name = "White Mage", Mask = WhiteMage, Category = JobCategory.Healer },
            ["Scholar"] = new JobInfo { Name = "Scholar", Mask = Scholar, Category = JobCategory.Healer },
            ["Astrologian"] = new JobInfo { Name = "Astrologian", Mask = Astrologian, Category = JobCategory.Healer },
            ["Sage"] = new JobInfo { Name = "Sage", Mask = Sage, Category = JobCategory.Healer },

            // Melee DPS
            ["Monk"] = new JobInfo { Name = "Monk", Mask = Monk, Category = JobCategory.MeleeDPS },
            ["Dragoon"] = new JobInfo { Name = "Dragoon", Mask = Dragoon, Category = JobCategory.MeleeDPS },
            ["Ninja"] = new JobInfo { Name = "Ninja", Mask = Ninja, Category = JobCategory.MeleeDPS },
            ["Samurai"] = new JobInfo { Name = "Samurai", Mask = Samurai, Category = JobCategory.MeleeDPS },
            ["Reaper"] = new JobInfo { Name = "Reaper", Mask = Reaper, Category = JobCategory.MeleeDPS },
            ["Viper"] = new JobInfo { Name = "Viper", Mask = Viper, Category = JobCategory.MeleeDPS },

            // Physical Ranged DPS
            ["Bard"] = new JobInfo { Name = "Bard", Mask = Bard, Category = JobCategory.PhysicalRangedDPS },
            ["Machinist"] = new JobInfo { Name = "Machinist", Mask = Machinist, Category = JobCategory.PhysicalRangedDPS },
            ["Dancer"] = new JobInfo { Name = "Dancer", Mask = Dancer, Category = JobCategory.PhysicalRangedDPS },

            // Magical Ranged DPS
            ["BlackMage"] = new JobInfo { Name = "Black Mage", Mask = BlackMage, Category = JobCategory.MagicalRangedDPS },
            ["Summoner"] = new JobInfo { Name = "Summoner", Mask = Summoner, Category = JobCategory.MagicalRangedDPS },
            ["RedMage"] = new JobInfo { Name = "Red Mage", Mask = RedMage, Category = JobCategory.MagicalRangedDPS },
            ["Pictomancer"] = new JobInfo { Name = "Pictomancer", Mask = Pictomancer, Category = JobCategory.MagicalRangedDPS },

            // Limited Jobs
            ["BlueMage"] = new JobInfo { Name = "Blue Mage", Mask = BlueMage, Category = JobCategory.MagicalRangedDPS }
        };

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
    }
}