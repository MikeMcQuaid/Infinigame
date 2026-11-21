using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Finished takes share a live spatial mix with engine speed, tunnel echoes and speech.
    public class Sounds : MonoBehaviour
    {
        [NonSerialized] public Game game;
        readonly AudioClip[][] shots = new AudioClip[Mix.Shots.Length][];
        readonly AudioSource[] voices = new AudioSource[8];
        readonly AudioClip[][] encounters = new AudioClip[Enum.GetValues(typeof(EncounterCue)).Length][];
        readonly (AudioSource source, AudioLowPassFilter filter, AudioEchoFilter echo)[] nearby = new (AudioSource, AudioLowPassFilter, AudioEchoFilter)[4];
        readonly float3[] origins = new float3[4];
        readonly float[] gains = new float[4];
        int soundWorld = -1;
        float duck = 1;
        readonly AudioSource[] geiger = new AudioSource[3];
        AudioSource engine, bike, whine, wind, rain, jetpack, beam, effects, charge, mind;
        AudioClip[] booms, bigBooms, thuds, steps, digs;
        AudioClip chime, warp, alarm, engage, hurt, reload, dryFire, nuke, last;
        float stride, riding, dose, lastFuel, lastHurt = float.MinValue;
        int turn, step;

        void Awake()
        {
            // runs nobody is watching, such as automated tests, stay silent
            AudioListener.volume = Application.isBatchMode ? 0f : 1f;
            effects = gameObject.AddComponent<AudioSource>();
            for (int i = 0; i < voices.Length; i++) voices[i] = gameObject.AddComponent<AudioSource>();
            for (int i = 0; i < nearby.Length; i++)
            {
                var emitter = new GameObject("Encounter sound");
                emitter.transform.SetParent(transform, false);
                var source = emitter.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1;
                source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 6;
                source.maxDistance = 100;
                var filter = emitter.AddComponent<AudioLowPassFilter>();
                var echo = emitter.AddComponent<AudioEchoFilter>();
                echo.delay = 68; echo.decayRatio = 0.23f; echo.wetMix = 0.2f;
                filter.enabled = echo.enabled = false;
                nearby[i] = (source, filter, echo);
            }
            for (int i = 0; i < encounters.Length; i++)
            {
                var cue = (EncounterCue)i;
                encounters[i] = Takes("Encounter " + cue, 3);
            }
            engine = Loop("Engine"); whine = Loop("Whine"); wind = Loop("Wind"); rain = Loop("Rain");
            jetpack = Loop("Jetpack"); beam = Loop("Beam"); charge = Loop("Charge"); mind = Loop("Mind", true); bike = Loop("Bike");
            for (int i = 0; i < geiger.Length; i++) geiger[i] = Loop("Geiger " + i);
            chime = Clip("Chime"); warp = Clip("Warp"); alarm = Clip("Alarm"); engage = Clip("Engage");
            hurt = Clip("Hurt"); reload = Clip("Reload"); dryFire = Clip("Dry fire"); nuke = Clip("Nuke");
            for (int i = 0; i < shots.Length; i++) shots[i] = Takes("Shot " + i, Mix.Takes[i]);
            steps = Takes("Step", Mix.Steps); thuds = Takes("Thud", Mix.Thuds); digs = Takes("Dig", Mix.Digs);
            booms = Takes("Boom", Mix.Booms); bigBooms = Takes("Heavy boom", Mix.Booms);
        }

        void Update()
        {
            duck = Mathf.MoveTowards(duck, game.Speech.Speaking ? 0.42f : 1f, Time.unscaledDeltaTime * 4);
            effects.volume = duck;
            foreach (var voice in voices) voice.volume = duck;
            for (int i = 0; i < nearby.Length; i++)
            {
                var source = nearby[i].source;
                if (!source.isPlaying) continue;
                if (soundWorld != game.System.Reference || game.Paused) { source.Stop(); continue; }
                source.transform.position = origins[i] - game.System.Anchor;
                source.volume = gains[i] * duck;
            }
            soundWorld = game.System.Reference;
            var player = game.Player;
            // a menu or the end of a run hushes the world, whose clock has stopped
            bool live = !game.Paused && !game.Dead && !game.Hermit.Talking && !game.Player.SystemView && !game.ShowControls, airborne = live && player.flying && !player.ship.landed;
            float speed = math.length(player.Velocity), cruise = math.saturate(speed / Tuning.ShipCruiseSpeed), space = math.saturate(speed / Tuning.ShipSpaceSpeed);
            float blend = 6f * Time.unscaledDeltaTime;
            // the turbine idles once the ship is off the ground and climbs in pitch and weight with speed
            engine.volume = Mathf.Lerp(engine.volume, airborne ? Mix.Engine * (0.45f + 0.55f * cruise) : 0f, blend);
            engine.pitch = 0.8f + 0.35f * cruise + 0.35f * space;
            whine.volume = Mathf.Lerp(whine.volume, airborne && player.Autopilot ? Mix.Whine : 0f, blend);
            whine.pitch = 0.85f + 0.4f * space;
            wind.volume = Mathf.Lerp(wind.volume, live ? Mix.Wind * math.saturate(speed / 200f) : 0f, blend);
            wind.pitch = 0.85f + 0.3f * cruise;
            rain.volume = Mathf.Lerp(rain.volume, live ? Mix.Rain * game.System.Rain : 0f, blend);
            beam.volume = Mathf.Lerp(beam.volume, live && game.Battle.Beaming ? Mix.Beam : 0f, 2 * blend);
            charge.volume = live && game.Battle.Charging ? 0.22f + game.Battle.RailCharge * 0.16f : 0;
            charge.pitch = 0.6f + game.Battle.RailCharge * 1.4f;
            float nearestMind = 90; float3 mindAt = player.Position;
            foreach (var foe in game.Battle.Foes)
                if (foe.kind == FoeKind.Mind && foe.health > 0 && math.distance(foe.position, player.Position) < nearestMind)
                { nearestMind = math.distance(foe.position, player.Position); mindAt = foe.position; }
            mind.transform.position = mindAt - game.System.Anchor;
            mind.volume = live && nearestMind < 90 ? game.Outpost.Visible(player.Position, mindAt) ? 0.5f : 0.2f : 0;
            // falling fuel means the jetpack is firing
            jetpack.volume = Mathf.Lerp(jetpack.volume, live && !player.flying && player.walker.fuel < lastFuel ? Mix.Jetpack : 0f, 3f * blend);
            lastFuel = player.walker.fuel;
            // the bike idles whenever it is ridden and climbs in pitch and weight to its top speed
            float revs = math.saturate(riding / 30f);
            bike.volume = Mathf.Lerp(bike.volume, live && riding > 0f ? Mix.Bike * (0.5f + 0.5f * revs) : 0f, blend);
            bike.pitch = 0.45f + 1.05f * revs;
            // the counter's three loops come in one over another: a few clicks a second, then a rattle, then a crackle
            for (int i = 0; i < geiger.Length; i++) geiger[i].volume = live ? Mix.Geiger * math.sqrt(math.saturate(3f * dose - i)) : 0f;
            // every knock is heard
            if (player.HurtAt != lastHurt) Play(hurt, Mix.Hurt, 0.05f);
            lastHurt = player.HurtAt;

            if (!live || player.flying || !player.walker.grounded || player.walker.swimming) return;
            stride += speed * Time.deltaTime;
            if (stride < 2.2f) return;
            stride = 0f;
            // never the same footfall twice running
            step = (step + UnityEngine.Random.Range(1, steps.Length)) % steps.Length;
            Play(steps[step], Mix.Step * UnityEngine.Random.Range(0.67f, 1f), 0.06f);
        }

        public void Chime() => Play(chime, Mix.Chime);

        public void Warp() => Play(warp, Mix.Warp);

        public void Alarm() => Play(alarm, Mix.Alarm);

        public void Engage() => Play(engage, Mix.Engage);

        // 0 ship pulse cannon, 1 ship missile launch, 2 ship lance, 3 pistol, 4 powered impact, 5 rail gauntlet,
        // 6 raider guns, 7 drone fireball, 8 station turret, 9 assault rifle, 10 ship torpedo launch. The
        // missile, the lance, the powered impact, the rail gauntlet and the torpedo are too long to share a voice.
        public void Shot(int voice, float volume = 1f) => Play(Take(shots[voice]), Mix.Shots[voice] * volume, voice is 1 or 2 or 4 or 5 or 10 ? 0f : 0.04f);

        // A magazine changed, start to finish: about a second and a half.
        public void Encounter(EncounterCue cue, float3 at)
        {
            float3 ear = game.Player.Position;
            float distance = math.distancesq(at, ear);
            if (distance > 100 * 100 || soundWorld != game.System.Reference) return;
            var clip = Take(encounters[(int)cue]);
            if (clip == null) return;
            float gain = cue == EncounterCue.Footfall ? 0.16f : cue == EncounterCue.Emitter ? 0.28f : cue == EncounterCue.ShellHit ? 0.43f : 0.76f;
            float priority = gain / math.max(36, distance);
            int best = -1;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (!nearby[i].source.isPlaying) { best = i; break; }
                float playing = gains[i] / math.max(36, math.distancesq(origins[i], ear));
                if (playing < priority) { best = i; priority = playing; }
            }
            if (best < 0) return;
            var (source, filter, echo) = nearby[best];
            source.Stop();
            bool obstructed = !game.Outpost.Visible(ear, at);
            filter.enabled = obstructed || distance > 24 * 24;
            if (filter.enabled) filter.cutoffFrequency = obstructed ? 1250 : Mathf.Lerp(8000, 2400, Mathf.Sqrt(distance) / 100);
            echo.enabled = game.Outpost.World == game.System.Reference && game.Outpost.Rooms != null && !game.Outpost.Entering;
            origins[best] = at;
            gains[best] = gain * (obstructed ? 0.55f : 1f);
            source.transform.position = at - game.System.Anchor;
            source.volume = gains[best] * duck;
            source.clip = clip;
            source.Play();
        }

        public void Reload() => Play(reload, Mix.Reload);

        public void DryFire() => Play(dryFire, Mix.DryFire, 0.03f);

        public void Dig() => Play(Take(digs), Mix.Dig, 0.06f);

        public void Nuke() => Play(nuke, Mix.Nuke);

        // For every frame: how fast the bike is going in metres a second, or 0 when nobody is riding it.
        public void Bike(float speed) => riding = speed;

        // For every frame: the dose, from 0, which is silence, to 1, where the counter crackles without a break.
        public void Geiger(float level) => dose = level;

        public void Boom(float volume, bool big = false) => Play(Take(big ? bigBooms : booms), Mix.Boom * volume);

        public void Thud(float volume) => Play(Take(thuds), Mix.Thud * volume, 0.08f);

        // Any take of a sound but the one heard last, so that nothing kept in several takes is played twice running.
        AudioClip Take(AudioClip[] takes)
        {
            int pick = UnityEngine.Random.Range(0, takes.Length);
            return last = takes[takes[pick] == last ? (pick + 1) % takes.Length : pick];
        }

        // A sound given some wander goes out on the next of a few voices in turn, its pitch a little
        // off true so that repeats are never identical. A voice's pitch bends whatever it is still
        // playing, so that is for short sounds; long or tuneful ones go out untouched.
        void Play(AudioClip clip, float volume, float wander = 0f)
        {
            if (clip == null) return;
            var source = wander > 0f ? voices[turn++ % voices.Length] : effects;
            source.pitch = 1f + UnityEngine.Random.Range(-wander, wander);
            source.PlayOneShot(clip, volume);
        }

        static AudioClip Clip(string name) => Resources.Load<AudioClip>("Effects/" + name);

        static AudioClip[] Takes(string name, int count)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Clip(name + " " + i);
            return clips;
        }

        AudioSource Loop(string name, bool spatial = false)
        {
            var source = (spatial ? new GameObject(name) : gameObject).AddComponent<AudioSource>();
            if (spatial)
            {
                source.transform.SetParent(transform, false);
                source.spatialBlend = 1; source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 3; source.maxDistance = 90;
            }
            source.loop = true;
            source.volume = 0;
            source.clip = Clip(name);
            source.Play();
            return source;
        }
    }
}
