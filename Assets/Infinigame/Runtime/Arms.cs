using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // What the player carries, held up in view while they are on foot: a model for each of the
    // firearms in Combat.Arms, built from code like every other. The one in hand kicks when it
    // fires and sways with the walk, and is lowered out of sight before another is raised. On
    // the bike its handlebars are in view too.
    public class Arms : MonoBehaviour
    {
        [NonSerialized] public Game game;

        Transform hold;
        GameObject[] carried;
        GameObject bars;
        // which is in view, and how far up it has been brought: 0 out of sight, 1 ready
        int shown;
        float raised;

        static Material Paint(Color colour, float metallic, float smoothness)
        {
            var material = Shapes.Material("Lit", colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        // Each is built with its grip at the origin and its barrel along z, in metres, from three
        // kits: the metal of it, what the hands hold and whatever glows.
        void Start()
        {
            hold = new GameObject("Arms").transform;
            hold.SetParent(Camera.main.transform, false);
            Material steel = Paint(new Color(0.17f, 0.18f, 0.2f), 0.85f, 0.55f), stock = Paint(new Color(0.1f, 0.085f, 0.075f), 0f, 0.3f);

            GameObject Weapon(int which, Color glow, Action<MeshKit, MeshKit, MeshKit> build)
            {
                MeshKit metal = new MeshKit(), held = new MeshKit(), lit = new MeshKit();
                build(metal, held, lit);
                var weapon = new GameObject(Combat.Arms[which].name);
                weapon.transform.SetParent(hold, false);
                // held this close they would throw a shadow across the whole view
                Shapes.Part(weapon.transform, "Metal", metal.Mesh("Metal"), steel, Vector3.zero, Vector3.one, false);
                Shapes.Part(weapon.transform, "Stock", held.Mesh("Stock"), stock, Vector3.zero, Vector3.one, false);
                Shapes.Part(weapon.transform, "Glow", lit.Mesh("Glow"), Shapes.Material("Glow", glow), Vector3.zero, Vector3.one, false);
                weapon.SetActive(false);
                return weapon;
            }

            // a grip raked back from the body above it
            void Grip(MeshKit held, float z, float length = 0.11f) =>
                held.Place(MeshKit.Move(0f, -0.055f, z) * MeshKit.Turn(18f, 0f, 0f), () => held.Box(Vector3.zero, new Vector3(0.04f, length, 0.052f), 0.012f));

            carried = new[]
            {
                // the pistol: short and square, with a slide over its barrel
                Weapon(0, new Color(2.2f, 2.6f, 1.2f), (metal, held, lit) =>
                {
                    metal.Box(new Vector3(0f, 0.035f, 0.06f), new Vector3(0.036f, 0.05f, 0.2f), 0.008f);
                    metal.Box(new Vector3(0f, 0.062f, 0.065f), new Vector3(0.04f, 0.03f, 0.21f), 0.008f);
                    metal.Rod(new Vector3(0f, 0.058f, 0.16f), new Vector3(0f, 0.058f, 0.182f), 0.011f, 12);
                    metal.Box(new Vector3(0f, 0.004f, 0.12f), new Vector3(0.026f, 0.016f, 0.09f), 0.004f);
                    // the trigger guard
                    metal.Tube(new[] { new Vector3(0f, 0.008f, 0.07f), new Vector3(0f, -0.03f, 0.065f), new Vector3(0f, -0.036f, 0.03f), new Vector3(0f, -0.012f, 0.014f) }, 0.004f, 6);
                    Grip(held, -0.012f);
                    // sights that show in the dark
                    lit.Mirror(() => lit.Box(new Vector3(0.012f, 0.081f, -0.03f), new Vector3(0.005f, 0.006f, 0.006f)));
                    lit.Box(new Vector3(0f, 0.081f, 0.16f), new Vector3(0.005f, 0.006f, 0.006f));
                }),
                // the shotgun: one fat barrel over its magazine tube and a pump, and a stock to take the kick
                Weapon(1, new Color(4.5f, 1.6f, 0.3f), (metal, held, lit) =>
                {
                    metal.Rod(new Vector3(0f, 0.058f, 0.05f), new Vector3(0f, 0.058f, 0.66f), 0.019f, 14);
                    metal.Rod(new Vector3(0f, 0.022f, 0.05f), new Vector3(0f, 0.022f, 0.56f), 0.016f, 12);
                    metal.Box(new Vector3(0f, 0.04f, -0.02f), new Vector3(0.05f, 0.09f, 0.24f), 0.012f);
                    metal.Box(new Vector3(0f, 0.04f, 0.55f), new Vector3(0.05f, 0.075f, 0.022f), 0.006f);
                    metal.Tube(new[] { new Vector3(0f, -0.006f, 0.05f), new Vector3(0f, -0.042f, 0.045f), new Vector3(0f, -0.046f, 0.008f), new Vector3(0f, -0.02f, -0.004f) }, 0.004f, 6);
                    held.Rod(new Vector3(0f, 0.022f, 0.22f), new Vector3(0f, 0.022f, 0.42f), 0.028f, 12);
                    held.Place(MeshKit.Move(0f, 0.012f, -0.28f) * MeshKit.Turn(-9f, 0f, 0f), () => held.Box(Vector3.zero, new Vector3(0.046f, 0.11f, 0.32f), 0.02f));
                    Grip(held, -0.07f);
                    lit.Ball(new Vector3(0f, 0.08f, 0.645f), 0.004f, 8);
                }),
                // the rifle: a straight stock, a curved magazine, a handguard and a carrying handle
                Weapon(2, new Color(2.2f, 2.6f, 1.2f), (metal, held, lit) =>
                {
                    metal.Box(new Vector3(0f, 0.04f, 0.02f), new Vector3(0.044f, 0.075f, 0.34f), 0.01f);
                    metal.Rod(new Vector3(0f, 0.045f, 0.18f), new Vector3(0f, 0.045f, 0.62f), 0.011f, 12);
                    metal.Rod(new Vector3(0f, 0.045f, 0.58f), new Vector3(0f, 0.045f, 0.66f), 0.015f, 10);
                    // the gas tube, the front sight post and the carrying handle with its rear sight
                    metal.Rod(new Vector3(0f, 0.068f, 0.2f), new Vector3(0f, 0.068f, 0.46f), 0.006f, 8);
                    metal.Box(new Vector3(0f, 0.085f, 0.47f), new Vector3(0.01f, 0.05f, 0.016f), 0.002f);
                    metal.Tube(new[] { new Vector3(0f, 0.078f, -0.1f), new Vector3(0f, 0.115f, -0.08f), new Vector3(0f, 0.115f, 0.1f), new Vector3(0f, 0.078f, 0.13f) }, 0.007f, 6);
                    // the magazine curves forward as it goes down
                    metal.Sweep(MeshKit.Path(8, new Vector3(0f, 0.005f, 0.07f), new Vector3(0f, -0.07f, 0.085f), new Vector3(0f, -0.14f, 0.125f)), _ => MeshKit.Oval(8, 0.016f, 0.03f, 4f), Vector3.forward);
                    metal.Tube(new[] { new Vector3(0f, 0.002f, 0.02f), new Vector3(0f, -0.036f, 0.016f), new Vector3(0f, -0.04f, -0.02f), new Vector3(0f, -0.014f, -0.036f) }, 0.004f, 6);
                    held.Rod(new Vector3(0f, 0.042f, 0.2f), new Vector3(0f, 0.042f, 0.44f), 0.027f, 12);
                    held.Place(MeshKit.Move(0f, 0.03f, -0.3f), () => held.Box(Vector3.zero, new Vector3(0.04f, 0.085f, 0.3f), 0.018f));
                    Grip(held, -0.1f);
                    lit.Box(new Vector3(0f, 0.112f, 0.47f), new Vector3(0.004f, 0.006f, 0.004f));
                }),
                // the sniper rifle: a long barrel under a scope, slow to load and hard in the hitting
                Weapon(3, new Color(0.9f, 1.4f, 4.5f), (metal, held, lit) =>
                {
                    metal.Rod(new Vector3(0f, 0.045f, 0.1f), new Vector3(0f, 0.045f, 0.9f), 0.013f, 12);
                    metal.Rod(new Vector3(0f, 0.045f, 0.86f), new Vector3(0f, 0.045f, 0.96f), 0.023f, 10);
                    metal.Box(new Vector3(0f, 0.04f, 0.01f), new Vector3(0.05f, 0.08f, 0.32f), 0.012f);
                    metal.Rod(new Vector3(0f, 0.108f, -0.1f), new Vector3(0f, 0.108f, 0.2f), 0.021f, 16);
                    metal.Rod(new Vector3(0f, 0.108f, 0.2f), new Vector3(0f, 0.108f, 0.27f), 0.021f, 16, 0.031f);
                    metal.Rod(new Vector3(0f, 0.108f, -0.1f), new Vector3(0f, 0.108f, -0.14f), 0.021f, 16, 0.027f);
                    foreach (float z in new[] { -0.03f, 0.13f }) metal.Box(new Vector3(0f, 0.084f, z), new Vector3(0.02f, 0.03f, 0.024f), 0.004f);
                    metal.Box(new Vector3(0f, -0.034f, 0.05f), new Vector3(0.034f, 0.08f, 0.07f), 0.008f);
                    // the bolt handle, on the side nearest the hand
                    metal.Rod(new Vector3(0.024f, 0.05f, -0.09f), new Vector3(0.06f, 0.028f, -0.09f), 0.006f, 8);
                    metal.Ball(new Vector3(0.064f, 0.026f, -0.09f), 0.011f, 10);
                    metal.Tube(new[] { new Vector3(0f, -0.006f, 0.0f), new Vector3(0f, -0.042f, -0.005f), new Vector3(0f, -0.046f, -0.042f), new Vector3(0f, -0.02f, -0.054f) }, 0.004f, 6);
                    held.Box(new Vector3(0f, 0.01f, 0.32f), new Vector3(0.05f, 0.046f, 0.34f), 0.015f);
                    held.Place(MeshKit.Move(0f, 0.008f, -0.36f) * MeshKit.Turn(-6f, 0f, 0f), () => held.Box(Vector3.zero, new Vector3(0.046f, 0.11f, 0.4f), 0.02f));
                    Grip(held, -0.12f);
                    lit.Rod(new Vector3(0f, 0.108f, 0.266f), new Vector3(0f, 0.108f, 0.268f), 0.027f, 16);
                }),
                // the launcher: a fat tube flared at both ends, a sight box on its side and a grip under each hand
                Weapon(4, new Color(4.5f, 1.1f, 0.25f), (metal, held, lit) =>
                {
                    metal.Rod(new Vector3(0f, 0.075f, -0.42f), new Vector3(0f, 0.075f, 0.62f), 0.055f, 16);
                    metal.Rod(new Vector3(0f, 0.075f, 0.62f), new Vector3(0f, 0.075f, 0.7f), 0.055f, 16, 0.072f);
                    metal.Rod(new Vector3(0f, 0.075f, -0.42f), new Vector3(0f, 0.075f, -0.54f), 0.055f, 16, 0.08f);
                    foreach (float z in new[] { -0.2f, 0.3f }) metal.Rod(new Vector3(0f, 0.075f, z - 0.015f), new Vector3(0f, 0.075f, z + 0.015f), 0.061f, 16);
                    metal.Box(new Vector3(-0.075f, 0.12f, 0.12f), new Vector3(0.05f, 0.07f, 0.13f), 0.01f);
                    metal.Tube(new[] { new Vector3(0f, 0.022f, 0.06f), new Vector3(0f, -0.03f, 0.055f), new Vector3(0f, -0.036f, 0.02f), new Vector3(0f, 0.02f, 0.004f) }, 0.004f, 6);
                    held.Rod(new Vector3(0f, 0.02f, 0.3f), new Vector3(0f, -0.09f, 0.31f), 0.02f, 10);
                    Grip(held, -0.02f);
                    // the eye of the missile waiting in the tube, and the glass of the sight
                    lit.Rod(new Vector3(0f, 0.075f, 0.69f), new Vector3(0f, 0.075f, 0.692f), 0.04f, 16);
                    lit.Box(new Vector3(-0.075f, 0.12f, 0.186f), new Vector3(0.034f, 0.05f, 0.004f));
                }),
            };

            // the bike's handlebars and the top of its fairing, seen from the saddle
            MeshKit frame = new MeshKit(), grips = new MeshKit(), lamp = new MeshKit();
            frame.Rod(new Vector3(-0.3f, 0f, 0f), new Vector3(0.3f, 0f, 0f), 0.014f, 10);
            frame.Rod(new Vector3(0f, 0f, 0f), new Vector3(0f, -0.22f, 0.12f), 0.022f, 10);
            frame.Box(new Vector3(0f, -0.07f, 0.2f), new Vector3(0.34f, 0.07f, 0.26f), 0.03f);
            frame.Mirror(() => frame.Rod(new Vector3(0.3f, 0.01f, 0.03f), new Vector3(0.36f, 0.06f, 0.03f), 0.006f, 6));
            grips.Mirror(() => grips.Rod(new Vector3(0.3f, 0f, 0f), new Vector3(0.42f, 0f, 0f), 0.019f, 10));
            lamp.Box(new Vector3(0f, -0.032f, 0.2f), new Vector3(0.12f, 0.004f, 0.06f));
            bars = new GameObject("Bike");
            bars.transform.SetParent(Camera.main.transform, false);
            bars.transform.localPosition = new Vector3(0f, -0.3f, 0.55f);
            Shapes.Part(bars.transform, "Metal", frame.Mesh("Metal"), steel, Vector3.zero, Vector3.one, false);
            Shapes.Part(bars.transform, "Grips", grips.Mesh("Grips"), stock, Vector3.zero, Vector3.one, false);
            Shapes.Part(bars.transform, "Dial", lamp.Mesh("Dial"), Shapes.Material("Glow", new Color(0.5f, 2.6f, 3.4f)), Vector3.zero, Vector3.one, false);
            bars.SetActive(false);
        }

        void LateUpdate()
        {
            var player = game.Player;
            // on foot one of them is in hand; in the ship, at the map or in a menu that hides the world, none
            int arm = -1;
            for (int i = 0; i < Combat.Arms.Length && !player.flying; i++)
                if (Combat.Arms[i].name == player.Weapon.name)
                    arm = i;
            bool ready = arm >= 0 && player.Armed && !player.SystemView && !game.Dead && !game.Title;
            bars.SetActive(player.riding && !player.SystemView);
            // one is lowered out of sight before the next comes up
            raised = Mathf.MoveTowards(raised, ready && arm == shown ? 1f : 0f, 5f * Time.unscaledDeltaTime);
            if (raised == 0f && ready) shown = arm;
            for (int i = 0; i < carried.Length; i++) carried[i].SetActive(i == shown && raised > 0f);

            // a shot kicks it back and up, and a walk sways it
            float kick = math.saturate(1f - (Time.time - game.Battle.Fired) / 0.15f), lowered = 1f - raised;
            float sway = Mathf.Sin(Time.time * 9f) * 0.004f * (player.walker.grounded ? math.min(math.length(player.Velocity), 8f) / 5f : 0f);
            // where the shots leave from in Battle, so that they are seen to come out of the barrel
            hold.localPosition = new Vector3(0.25f + sway, -0.265f - 0.3f * lowered + 0.6f * Mathf.Abs(sway), 0.4f - 0.05f * kick);
            hold.localRotation = Quaternion.Euler(-7f * kick + 35f * lowered, -2.5f, 0f);
        }
    }
}
