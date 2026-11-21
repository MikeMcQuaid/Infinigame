using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Infinigame.Core;
using UnityEngine;

namespace Infinigame
{
    // Each save keeps the wording it heard. Only the current expedition can accept a pending reply.
    public class ExpeditionText
    {
        [Serializable] class Batch { public string[] lines; }
        readonly Dictionary<string, string> originals = new Dictionary<string, string>();
        readonly Dictionary<string, int> unlock = new Dictionary<string, int>();
        readonly Dictionary<string, string> variants = new Dictionary<string, string>();
        List<Entry> cache;
        int stage = -1, voice = -1, revision;
        uint seed;
        public bool Prepared(string key) => Oracle.Enabled && variants.ContainsKey(key);
        public bool WaitingForOpening { get; private set; }
        public bool Busy { get; private set; }
        string Authored(string key) => originals.TryGetValue(key, out var original) ? original : key;
        public string Read(string key) => Oracle.Enabled && variants.TryGetValue(key, out var written) ? written
            : Authored(key);

        static IEnumerable<string> Tokens(string text) => Regex.Matches(text, @"\{[^}]+\}|\d+(?:\.\d+)?").Cast<Match>().Select(m => m.Value).OrderBy(value => value, StringComparer.Ordinal);

        public async void Prepare(Game game, List<Entry> saved)
        {
            if (originals.Count == 0)
                foreach (var row in Resources.Load<TextAsset>("Radio").text.Split('\n'))
                {
                    var cells = row.TrimEnd('\r').Split('\t');
                    if (cells.Length != 3) continue;
                    originals.Add(cells[1], cells[2]);
                    unlock.Add(cells[1], int.Parse(cells[0]));
                }
            int selected = Oracle.Enabled && !game.Title ? Oracle.Voice : -1;
            if (cache == saved && seed == game.Seed && stage == game.Stage && voice == selected) return;
            cache = saved; seed = game.Seed; stage = game.Stage; voice = selected;
            int mine = ++revision;
            variants.Clear();
            // Older saves did not record their source. Refresh only the lines whose instructions changed.
            saved.RemoveAll(entry => entry.key == null || entry.text == null || (string.IsNullOrEmpty(entry.source)
                ? entry.key is "arm" or "foundry" or "frogs" or "optics" or "rescue" or "escortCall" or "shipyard" : entry.source != Authored(entry.key)));
            foreach (var entry in saved) { entry.source = Authored(entry.key); variants[entry.key] = entry.text; }
            WaitingForOpening = Busy = false;
            if (selected < 0) return;
            var rows = new List<string>();
            if (game.Buried || stage == Campaign.Stranger)
                rows.AddRange(new[] { "wake", "rescue", "optics", "legs", "fighter" });
            if (game.Depth > 1) rows.Insert(0, "newSystem");
            rows.AddRange(unlock.Where(item => item.Value == stage || item.Value == stage + 1).Select(item => item.Key));
            foreach (var mission in Campaign.Missions.Skip(Math.Min(stage, Campaign.Citadel)).Take(2))
            { rows.Add(mission.brief); rows.Add(mission.task); }
            rows.AddRange(unlock.Where(item => item.Value < stage).Select(item => item.Key));
            foreach (var world in game.System.Descriptor.bodies) rows.Add(Scan.Describe(world));
            rows = rows.Distinct().ToList();
            rows.RemoveAll(variants.ContainsKey);
            string context = "You write for this expedition in UK English. Write a fresh, brief version of every supplied line in the same voice. Vary phrasing even in the opening dialogue. Preserve every fact, instruction, number and {placeholder}. Keep objectives short. Return only a JSON object with a lines array of strings in order. Use this lore without revealing anything beyond each source line. Expedition " + seed + ". " + Narrative.Context(stage, game.Fall.destroyed);
            Busy = rows.Count > 0;
            WaitingForOpening = Busy && ((game.Buried || stage == Campaign.Stranger) && rows.Contains("rescue") || game.Buried && rows.Contains("wake") || game.Depth > 1 && rows.Contains("newSystem"));
            try
            {
                for (int i = 0; i < rows.Count; i += 8)
                {
                    var keys = rows.GetRange(i, Math.Min(8, rows.Count - i));
                    var batch = new Batch { lines = keys.Select(Read).ToArray() };
                    string reply = await Oracle.Ask(context, JsonUtility.ToJson(batch)) ?? "";
                    if (game == null || mine != revision || game.Title || game.Seed != seed || !Oracle.Enabled || Oracle.Voice != selected) return;
                    int first = reply.IndexOf('{'), last = reply.LastIndexOf('}');
                    var lines = first >= 0 && last > first ? JsonUtility.FromJson<Batch>(reply.Substring(first, last - first + 1))?.lines : null;
                    if (lines == null || lines.Length != keys.Count) { WaitingForOpening = false; continue; }
                    for (int j = 0; j < lines.Length; j++)
                    {
                        string line = lines[j]?.Trim(), source = batch.lines[j];
                        if (string.IsNullOrEmpty(line) || line.Length > Math.Max(80, source.Length * 2) || line.IndexOfAny(new[] { '<', '>', '\n' }) >= 0) continue;
                        if (!Tokens(source).SequenceEqual(Tokens(line))) continue;
                        variants[keys[j]] = line;
                        saved.Add(new Entry { key = keys[j], text = line, source = source });
                    }
                    game.Write();
                    WaitingForOpening = false;
                }
            }
            catch (Exception)
            {
                if (game != null && mine == revision && !game.Title)
                    game.Notify("COMPANION LINK UNAVAILABLE · USING RECORDED GUIDANCE");
            }
            finally { if (mine == revision) WaitingForOpening = Busy = false; }
        }
    }
}
