using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // Markdown is the authored source; the editor packages the same text for every platform.
    public static class Narrative
    {
        static string story, factions;
        public static float3[] Costs { get; private set; }
        public static string[] Explanations { get; private set; }

        static IEnumerable<string[]> Rows(string text, int columns) => text.Split('\n').Where(line => line.TrimStart().StartsWith("|"))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray()).Where(cells => cells.Length == columns);

        public static void Load(string plot, string missions, string sides, string colonies)
        {
            var stages = Rows(missions, 8).Where(row => int.TryParse(row[0], out _)).ToArray();
            if (stages.Length != Campaign.Won) throw new FormatException($"MISSIONS.md must contain stages 0 to {Campaign.Citadel}.");
            var parsed = new Mission[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                var row = stages[i];
                if (int.Parse(row[0]) != i || !Enum.TryParse(row[3], out FoeKind guard) || !int.TryParse(row[4], out int count) || count < 0 || count > 64)
                    throw new FormatException($"Check the stage, guard and count in MISSIONS.md row {i}.");
                parsed[i] = new Mission { name = row[1], world = row[2], guards = guard, count = count,
                    wins = row[5].Split(',').Where(value => value.Trim().Length > 0).Select(value => (Gear)Enum.Parse(typeof(Gear), value.Trim())).ToArray(), task = row[6], brief = row[7] };
            }
            var costs = new float3[9];
            var explanations = new string[9];
            foreach (var row in Rows(colonies, 5))
            {
                if (!Enum.TryParse(row[0], out Building building)) continue;
                int index = (int)building;
                if (index < 0 || index >= costs.Length || explanations[index] != null) throw new FormatException("Check the building identifiers in COLONIES.md.");
                costs[index] = new float3(int.Parse(row[1]), int.Parse(row[2]), int.Parse(row[3]));
                if (math.any(costs[index] < 0)) throw new FormatException("Colony costs cannot be negative.");
                explanations[index] = row[4];
            }
            if (explanations.Skip(7).Any(line => string.IsNullOrWhiteSpace(line)) || !plot.Contains("## Public") || !sides.Contains("## Foundry"))
                throw new FormatException("Check the colony explanations and the Public section in STORY.md.");
            Campaign.Missions = parsed;
            Costs = costs;
            Explanations = explanations;
            story = plot;
            factions = sides.Substring(sides.IndexOf("## Foundry", StringComparison.Ordinal));
        }

        public static string Context(int stage, bool moonfall)
        {
            var known = new List<string>();
            bool include = false;
            foreach (string line in story.Split('\n'))
            {
                if (line.StartsWith("## "))
                {
                    include = line.Trim() == "## Public" || line.Trim() == "## After moonfall" && moonfall
                        || line.StartsWith("## Stage ") && int.TryParse(line.Substring(9).Trim(), out int reveal) && stage >= reveal;
                }
                else if (include && !string.IsNullOrWhiteSpace(line)) known.Add(line.Trim());
            }
            // Visual design notes are for the author, not the character's memory.
            return string.Join(" ", known) + " " + factions.Split(new[] { "## Silhouettes" }, StringSplitOptions.None)[0].Replace("## ", "");
        }
    }
}
