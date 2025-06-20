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

        public const ulong WhiteMage = 0x8000;      // DUMMY - Replace with actual value
        public const ulong Scholar = 0x20000;
        public const ulong Astrologian = 0x400000;
        public const ulong Sage = 0x20000000;

        public const ulong Monk = 0x200;
        public const ulong Dragoon = 0x800;
        public const ulong Ninja = 0x80000;
        public const ulong Samurai = 0x800000;
        public const ulong Reaper = 0x10000000;
        public const ulong Viper = 0x40000000;

        public const ulong Bard = 0x1000;          // DUMMY - Replace with actual value
        public const ulong Machinist = 0x100000;
        public const ulong Dancer = 0x8000000;

        public const ulong BlackMage = 0x4000;
        public const ulong Summoner = 0x10000;
        public const ulong RedMage = 0x2000000;     // DUMMY - Replace with actual value
        public const ulong Pictomancer = 0x80000000;

        public const ulong BlueMage = 0x100000000;  // DUMMY - Replace with actual value

        // Pre-calculated combination masks
        public const ulong AllTanks = Gladiator | Paladin | Marauder | Warrior | DarkKnight | Gunbreaker; // 0x420050A
        public const ulong AllHealers = WhiteMage | Scholar | Astrologian | Sage; // 0x20608000
        public const ulong AllMeleeDPS = Monk | Dragoon | Ninja | Samurai | Reaper | Viper; // 0x50880A00
        public const ulong AllPhysicalRangedDPS = Bard | Machinist | Dancer; // 0x8101000
        public const ulong AllMagicalRangedDPS = BlackMage | Summoner | RedMage | Pictomancer; // 0x82014000
        public const ulong AllDPS = AllMeleeDPS | AllPhysicalRangedDPS | AllMagicalRangedDPS; // 0xDA995A00
        public const ulong AllJobs = AllTanks | AllHealers | AllDPS; // 0xFAE9FF0A (excluding Blue Mage)
        public const ulong None = 0x0;

        // Job information structure
        public struct JobInfo
        {
            public string Name { get; set; }
            public byte Id { get; set; }
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

        // Job definitions with all information
        public static readonly Dictionary<string, JobInfo> Jobs = new()
        {
            // Tanks
            ["Gladiator"] = new JobInfo { Name = "Gladiator", Id = 1, Mask = Gladiator, Category = JobCategory.Tank },
            ["Paladin"] = new JobInfo { Name = "Paladin", Id = 19, Mask = Paladin, Category = JobCategory.Tank },
            ["Marauder"] = new JobInfo { Name = "Marauder", Id = 3, Mask = Marauder, Category = JobCategory.Tank },
            ["Warrior"] = new JobInfo { Name = "Warrior", Id = 21, Mask = Warrior, Category = JobCategory.Tank },
            ["DarkKnight"] = new JobInfo { Name = "Dark Knight", Id = 32, Mask = DarkKnight, Category = JobCategory.Tank },
            ["Gunbreaker"] = new JobInfo { Name = "Gunbreaker", Id = 37, Mask = Gunbreaker, Category = JobCategory.Tank },

            // Healers
            ["WhiteMage"] = new JobInfo { Name = "White Mage", Id = 24, Mask = WhiteMage, Category = JobCategory.Healer }, // DUMMY ID
            ["Scholar"] = new JobInfo { Name = "Scholar", Id = 28, Mask = Scholar, Category = JobCategory.Healer },
            ["Astrologian"] = new JobInfo { Name = "Astrologian", Id = 33, Mask = Astrologian, Category = JobCategory.Healer },
            ["Sage"] = new JobInfo { Name = "Sage", Id = 40, Mask = Sage, Category = JobCategory.Healer },

            // Melee DPS
            ["Monk"] = new JobInfo { Name = "Monk", Id = 20, Mask = Monk, Category = JobCategory.MeleeDPS },
            ["Dragoon"] = new JobInfo { Name = "Dragoon", Id = 22, Mask = Dragoon, Category = JobCategory.MeleeDPS },
            ["Ninja"] = new JobInfo { Name = "Ninja", Id = 30, Mask = Ninja, Category = JobCategory.MeleeDPS },
            ["Samurai"] = new JobInfo { Name = "Samurai", Id = 34, Mask = Samurai, Category = JobCategory.MeleeDPS },
            ["Reaper"] = new JobInfo { Name = "Reaper", Id = 39, Mask = Reaper, Category = JobCategory.MeleeDPS },
            ["Viper"] = new JobInfo { Name = "Viper", Id = 41, Mask = Viper, Category = JobCategory.MeleeDPS },

            // Physical Ranged DPS
            ["Bard"] = new JobInfo { Name = "Bard", Id = 23, Mask = Bard, Category = JobCategory.PhysicalRangedDPS }, // DUMMY ID
            ["Machinist"] = new JobInfo { Name = "Machinist", Id = 31, Mask = Machinist, Category = JobCategory.PhysicalRangedDPS },
            ["Dancer"] = new JobInfo { Name = "Dancer", Id = 38, Mask = Dancer, Category = JobCategory.PhysicalRangedDPS },

            // Magical Ranged DPS
            ["BlackMage"] = new JobInfo { Name = "Black Mage", Id = 25, Mask = BlackMage, Category = JobCategory.MagicalRangedDPS },
            ["Summoner"] = new JobInfo { Name = "Summoner", Id = 27, Mask = Summoner, Category = JobCategory.MagicalRangedDPS },
            ["RedMage"] = new JobInfo { Name = "Red Mage", Id = 35, Mask = RedMage, Category = JobCategory.MagicalRangedDPS }, // DUMMY ID
            ["Pictomancer"] = new JobInfo { Name = "Pictomancer", Id = 42, Mask = Pictomancer, Category = JobCategory.MagicalRangedDPS },

            // Limited Jobs
            ["BlueMage"] = new JobInfo { Name = "Blue Mage", Id = 36, Mask = BlueMage, Category = JobCategory.MagicalRangedDPS } // DUMMY ID
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
            if (mask == 0) return "None";
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