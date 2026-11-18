using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public enum Upgrade { Damage, FireRate, Toughness, Energy, Recovery }

    [Serializable]
    public class Progression
    {
        public int experience, level, salvage;
        public int[] ranks = new int[5];
        public int Required => 45 + level * 20;
        public bool Pending => experience >= Required;
        public float Damage => 1 + ranks[0] * 0.15f;
        public float Rate => 1 + ranks[1] * 0.12f;
        public float Toughness => 1 + ranks[2] * 0.15f;
        public float Energy => 1 + ranks[3] * 0.18f;
        public float Recovery => 1 + ranks[4] * 0.18f;
        public Upgrade Choice(int index) => (Upgrade)((level * 3 + index) % ranks.Length);
        public string Preview(Upgrade upgrade, float armour, float damage, float period)
        {
            return upgrade switch
            {
                Upgrade.Damage => $"HIT DAMAGE   {damage:0.#} → {damage / Damage * (Damage + 0.15f):0.#}",
                Upgrade.FireRate => $"FISTS PER SECOND   {Rate / period:0.00} → {(Rate + 0.12f) / period:0.00}",
                Upgrade.Toughness => $"SUIT ARMOUR   {armour:0} → {armour / Toughness * (Toughness + 0.15f):0}",
                Upgrade.Energy => $"ENERGY / SEC   {Suit.Recharge * Energy:0.0} → {Suit.Recharge * (Energy + 0.18f):0.0}",
                _ => $"REPAIR HP / SEC   {4 * Recovery:0.0} → {4 * (Recovery + 0.18f):0.0}",
            };
        }
        public int Cost(Upgrade upgrade) => 15 + ranks[(int)upgrade] * 10;
        public bool Improve(Upgrade upgrade, bool purchase)
        {
            int index = (int)upgrade;
            if (index < 0 || index >= ranks.Length || (purchase ? salvage < Cost(upgrade) : !Pending)) return false;
            if (purchase) salvage -= Cost(upgrade);
            else { experience -= Required; level++; }
            ranks[index]++;
            return true;
        }
        public Progression Copy() => new Progression { experience = experience, level = level, salvage = salvage, ranks = (int[])ranks.Clone() };
        public static string Name(Upgrade upgrade) => upgrade switch
        {
            Upgrade.Damage => "WEAPON DAMAGE +15%", Upgrade.FireRate => "FIRE / CHARGE RATE +12%",
            Upgrade.Toughness => "ARMOUR / HULL +15%", Upgrade.Energy => "ENERGY RECHARGE +18%", _ => "REPAIR / RELOAD +18%",
        };
    }
}
