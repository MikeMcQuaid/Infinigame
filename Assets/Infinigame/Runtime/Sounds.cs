using System;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // All sound is synthesised at start-up: engine, wind, jetpack, footsteps, relic chimes and the warp.
    public class Sounds : MonoBehaviour
    {
        const int Rate = 44100;

        [NonSerialized] public Player player;
        AudioSource engine, wind, jetpack, effects, steps;
        AudioClip step, chime, fanfare, warp;
        float stride, lastFuel;

        void Awake()
        {
            // 55 Hz and its fifth with a slow throb; two seconds holds whole cycles of all three so it loops cleanly
            engine = Loop(Clip("Engine", 2f, t => 0.5f * Saw(55f * t) + 0.3f * Saw(82.5f * t) * (0.7f + 0.3f * math.sin(2 * math.PI * 3f * t))));
            wind = Loop(Noise("Wind", 3f, 0.04f));
            jetpack = Loop(Noise("Jetpack", 1f, 0.35f));
            effects = gameObject.AddComponent<AudioSource>();
            steps = gameObject.AddComponent<AudioSource>();
            step = Clip("Step", 0.09f, t => math.sin(2 * math.PI * 95f * t) * math.exp(-45f * t));
            chime = Notes("Chime", 880f, 1320f);
            fanfare = Notes("Fanfare", 660f, 880f, 1100f, 1320f, 1760f);
            warp = Clip("Warp", 1.6f, t => math.sin(2 * math.PI * (80f * t + 350f * t * t)) * math.sin(math.PI * t / 1.6f));
        }

        void Update()
        {
            float speed = math.length(player.Velocity), blend = 6f * Time.deltaTime;
            bool airborne = player.flying && !player.ship.landed;
            engine.volume = Mathf.Lerp(engine.volume, airborne ? 0.12f + 0.2f * math.saturate(speed / 150f) : 0f, blend);
            engine.pitch = 0.7f + speed / 250f;
            wind.volume = Mathf.Lerp(wind.volume, 0.5f * math.saturate(speed / 200f), blend);

            // falling fuel means the jetpack is firing
            jetpack.volume = Mathf.Lerp(jetpack.volume, !player.flying && player.walker.fuel < lastFuel ? 0.35f : 0f, 3f * blend);
            lastFuel = player.walker.fuel;

            if (player.flying || !player.walker.grounded) return;
            stride += speed * Time.deltaTime;
            if (stride < 2.2f) return;
            stride = 0f;
            steps.pitch = UnityEngine.Random.Range(0.85f, 1.15f);
            steps.PlayOneShot(step, 0.5f);
        }

        public void Chime(bool last) => effects.PlayOneShot(last ? fanfare : chime, 0.6f);

        public void Warp() => effects.PlayOneShot(warp, 0.7f);

        AudioSource Loop(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.Play();
            return source;
        }

        static float Saw(float cycles) => 2f * (cycles - math.floor(cycles)) - 1f;

        static AudioClip Clip(string name, float seconds, Func<float, float> wave)
        {
            var samples = new float[(int)(seconds * Rate)];
            for (int i = 0; i < samples.Length; i++) samples[i] = wave(i / (float)Rate);
            return Clip(name, samples);
        }

        static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Low-passed white noise; the tail is cross-faded into the head so the loop has no click.
        static AudioClip Noise(string name, float seconds, float brightness)
        {
            int length = (int)(seconds * Rate), fade = Rate / 10;
            var rnd = new Random(7);
            var raw = new float[length + fade];
            float value = 0f;
            for (int i = 0; i < raw.Length; i++) raw[i] = value += brightness * (rnd.NextFloat(-1f, 1f) - value);
            var samples = new float[length];
            for (int i = 0; i < length; i++)
                samples[i] = (i < fade ? math.lerp(raw[length + i], raw[i], i / (float)fade) : raw[i]) / math.sqrt(brightness);
            return Clip(name, samples);
        }

        // A run of bell-like notes, one every 0.12 s.
        static AudioClip Notes(string name, params float[] pitches)
        {
            var samples = new float[(int)((pitches.Length * 0.12f + 0.6f) * Rate)];
            for (int n = 0; n < pitches.Length; n++)
            for (int i = (int)(n * 0.12f * Rate); i < samples.Length; i++)
            {
                float t = i / (float)Rate - n * 0.12f;
                samples[i] += 0.4f * math.sin(2 * math.PI * pitches[n] * t) * math.exp(-6f * t) / math.sqrt(pitches.Length);
            }
            return Clip(name, samples);
        }
    }
}
