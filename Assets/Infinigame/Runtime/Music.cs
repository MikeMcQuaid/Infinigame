using System;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Two decks per mood overlap complete songs. The mood mix and track mix both preserve power.
    public class Music : MonoBehaviour
    {
        public static bool Muted;
        [NonSerialized] public Game game;
        readonly AudioSource[,] decks = new AudioSource[2, 2];
        readonly Playlist[] lists = new Playlist[2];
        readonly int[] front = new int[2];
        AudioClip[][] tracks = new AudioClip[2][];
        uint? seed;
        float gain, fight;

        void Awake()
        {
            for (int mood = 0; mood < 2; mood++)
            for (int deck = 0; deck < 2; deck++)
            {
                decks[mood, deck] = gameObject.AddComponent<AudioSource>();
                decks[mood, deck].playOnAwake = decks[mood, deck].loop = false;
                decks[mood, deck].volume = 0;
            }
            var clips = Resources.LoadAll<AudioClip>("Music").OrderBy(clip => clip.name, StringComparer.Ordinal).ToArray();
            SetTracks(clips.Where(clip => !clip.name.StartsWith("battle", StringComparison.OrdinalIgnoreCase)).ToArray(),
                clips.Where(clip => clip.name.StartsWith("battle", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        // Also lets scene tests supply short generated clips without touching a player's music.
        public void SetTracks(AudioClip[] explore, AudioClip[] battle)
        {
            tracks = new[] { explore.Length > 0 ? explore : battle, battle.Length > 0 ? battle : explore };
            seed = null;
        }
        void Update()
        {
            if (game.Seed != seed)
            {
                seed = game.Seed;
                double start = AudioSettings.dspTime + 0.1;
                for (int mood = 0; mood < 2; mood++)
                {
                    decks[mood, 0].Stop();
                    decks[mood, 1].Stop();
                    lists[mood] = null;
                    if (tracks[mood].Length == 0) continue;
                    front[mood] = 0;
                    lists[mood] = new Playlist(tracks[mood].Select(clip => clip.length).ToArray(), seed.Value, start);
                    decks[mood, 0].clip = tracks[mood][lists[mood].index];
                    decks[mood, 0].PlayScheduled(start);
                    Schedule(mood);
                }
            }
            float dt = Time.unscaledDeltaTime;
            gain = Mathf.MoveTowards(gain, game.Paused || game.Speech.Speaking ? 0.35f : 1, dt);
            bool foes = game.Battle.Foes.Any(foe => !foe.friendly && !foe.scattered);
            fight = Mathf.MoveTowards(fight, foes ? 1 : 0, dt / (foes ? 1 : 5));
            for (int mood = 0; mood < 2; mood++)
            {
                if (lists[mood] == null) continue;
                if (lists[mood].Advance(AudioSettings.dspTime))
                {
                    decks[mood, front[mood]].Stop();
                    front[mood] = 1 - front[mood];
                    Schedule(mood);
                }
                float2 fade = lists[mood].Gains(AudioSettings.dspTime);
                float level = Muted || Application.isBatchMode ? 0 : Mix.Music * gain * (ReferenceEquals(tracks[0], tracks[1]) ? mood == 0 ? 1 : 0 : mood == 0 ? math.cos(fight * math.PI / 2) : math.sin(fight * math.PI / 2));
                decks[mood, front[mood]].volume = level * fade.x;
                decks[mood, 1 - front[mood]].volume = level * fade.y;
            }
        }
        void Schedule(int mood)
        {
            var next = decks[mood, 1 - front[mood]];
            next.clip = tracks[mood][lists[mood].Next];
            next.volume = 0;
            next.PlayScheduled(lists[mood].nextAt);
        }
    }
}
