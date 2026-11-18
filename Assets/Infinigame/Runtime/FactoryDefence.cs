using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class FactoryDefence : MonoBehaviour
    {
        public Game game;
        public int world;
        Transform mount;
        LineRenderer beam;
        float cooldown, glow;
        int weapon;
        void Start()
        {
            mount = Models.Building(Building.Turret); mount.name = "Rotating weapon mount";
            mount.SetParent(transform, false); mount.localPosition = Vector3.up * 3.3f; mount.localScale = Vector3.one * 0.6f;
            beam = new GameObject("Factory beam").AddComponent<LineRenderer>(); beam.transform.SetParent(mount, false);
            beam.positionCount = 2; beam.startWidth = beam.endWidth = 0.07f;
            beam.sharedMaterial = Shapes.Beam(new Color(0.2f, 1.2f, 2)); beam.enabled = false;
        }
        void Update()
        {
            if (mount == null || !game.Ready || world != game.System.Reference) return;
            beam.enabled = Time.time < glow;
            if (Time.deltaTime <= 0) return;
            float3 from = (float3)mount.TransformPoint(new Vector3(0, 2.6f, 2.8f)) + game.System.Anchor;
            var target = game.Battle.Foes.Where(foe => !foe.friendly && foe.health > 0 && math.distance(foe.position, from) < 400).OrderBy(foe => math.distancesq(foe.position, from)).FirstOrDefault();
            if (target.health <= 0) return;
            Vector3 direction = transform.InverseTransformDirection(target.position - from);
            mount.localRotation = Quaternion.RotateTowards(mount.localRotation, Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z)), 110 * Time.deltaTime);
            if ((cooldown -= Time.deltaTime) > 0 || game.Outpost.Shield.Blocks(from, target.position - from, out _)) return;
            var gun = Combat.Arms[weapon++ % Combat.Arms.Length];
            gun.damage *= game.Frontier.State(world, game.Frontier.Region(world, transform.localPosition)).mindDead ? 1 : 0.15f;
            gun.airOnly = false;
            if (gun.melee && math.distance(from, target.position) > 20) { cooldown = 0.2f; return; }
            float3 end = game.Battle.FriendlyFire(from, target.position, gun);
            beam.SetPosition(0, from - game.System.Anchor); beam.SetPosition(1, end - game.System.Anchor);
            glow = Time.time + (gun.beam || gun.sweep ? 0.35f : 0.12f);
            cooldown = math.max(0.6f, gun.period);
            if (gun.melee) game.Effects.Hit(target.position, math.normalizesafe(target.position), 3);
        }
    }
}
