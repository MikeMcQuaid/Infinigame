using System;
using System.Threading.Tasks;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // The band. Every system has a calm track for exploring and a heavy one for when foes are
    // about; both play at once while this fades between them. The tracks are whatever audio files
    // sit in Resources/Music, the calm ones named explore... and the heavy ones battle..., one of
    // each chosen by the system's seed. Where there are none, the system's own song stands in:
    // written from its seed and rendered on a background thread in two arrangements that keep the
    // same time.
    public class Music : MonoBehaviour
    {
        public static bool Muted;

        [NonSerialized] public Game game;
        AudioSource calm, heavy;
        AudioClip[] explore, battle;
        Task<float[][]> song;
        uint seed;
        int loaded;
        float gain, fight;

        void Awake()
        {
            calm = gameObject.AddComponent<AudioSource>();
            heavy = gameObject.AddComponent<AudioSource>();
            calm.loop = heavy.loop = true;
            var tracks = Resources.LoadAll<AudioClip>("Music");
            // in name order, so that a seed picks the same track on every run
            Array.Sort(tracks, (a, b) => string.CompareOrdinal(a.name, b.name));
            explore = Array.FindAll(tracks, track => track.name.StartsWith("explore", StringComparison.OrdinalIgnoreCase));
            battle = Array.FindAll(tracks, track => track.name.StartsWith("battle", StringComparison.OrdinalIgnoreCase));
        }

        void Update()
        {
            if (game.Seed != seed)
            {
                // a new system: whichever half has no track to play is written and played on another thread while the old music fades out to make way
                uint wanted = seed = game.Seed;
                bool calmTrack = explore.Length > 0, heavyTrack = battle.Length > 0;
                song = Task.Run(() =>
                {
                    var written = Riffs.Compose(wanted);
                    return new[] { calmTrack ? null : Riffs.Render(written, false), heavyTrack ? null : Riffs.Render(written, true) };
                });
                loaded = 0;
            }
            if (song != null && song.IsCompletedSuccessfully && gain <= 0f)
            {
                // one arrangement a frame, as each is a lot to copy
                var source = loaded == 0 ? calm : heavy;
                var tracks = loaded == 0 ? explore : battle;
                source.Stop();
                // a track belongs to the project; only what was rendered here is thrown away
                if (tracks.Length == 0) Destroy(source.clip);
                source.clip = tracks.Length > 0 ? tracks[seed % tracks.Length] : Sounds.Clip(song.Result[loaded]);
                if (++loaded == 2)
                {
                    // both start on the same tick of the audio clock; two rendered arrangements are the same length, so they never drift apart
                    double start = AudioSettings.dspTime + 0.1;
                    calm.PlayScheduled(start);
                    heavy.PlayScheduled(start);
                    song = null;
                }
            }

            // the clock that menus stop is no use here: the music plays through them, only quieter,
            // and a run that has ended keeps its song until the next begins
            float dt = Time.unscaledDeltaTime;
            gain = Mathf.MoveTowards(gain, song != null && !game.Dead ? 0f : game.Paused ? 0.35f : 1f, dt);
            // quick to anger, slow to calm down
            bool foes = game.Battle.Foes.Count > 0;
            fight = Mathf.MoveTowards(fight, foes ? 1f : 0f, dt / (foes ? 1f : 5f));
            float level = Muted ? 0f : Mix.Music * gain;
            // the two are different performances, so power is what must stay level as one gives way to the other
            calm.volume = level * math.cos(fight * math.PI / 2f);
            heavy.volume = level * math.sin(fight * math.PI / 2f);
        }
    }
}
