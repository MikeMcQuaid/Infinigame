using System;

namespace Infinigame.Core
{
    public enum Upgrade { Damage, FireRate, Toughness, Energy, Recovery }

    [Serializable]
    public class Progression
    {
        public int experience, level;
        public int[] ranks = new int[5];
        public int Required => 45 + level * 20;
        public bool Pending => experience >= Required;
        public float Damage => 1 + ranks[0] * 0.15f;
        public float Rate => 1 + ranks[1] * 0.12f;
        public float Toughness => 1 + ranks[2] * 0.15f;
        public float Energy => 1 + ranks[3] * 0.18f;
        public float Recovery => 1 + ranks[4] * 0.18f;
        public string Preview(Upgrade upgrade, float armour, float hull, float damage)
        {
            return upgrade switch
            {
                Upgrade.Damage => $"HIT DAMAGE   {damage:0.#} → {damage / Damage * (Damage + 0.15f):0.#}",
                Upgrade.FireRate => $"FIRE / CHARGE RATE   {Rate:0.00}× → {Rate + 0.12f:0.00}×",
                Upgrade.Toughness => $"SUIT   {armour:0} → {armour / Toughness * (Toughness + 0.15f):0}     HULL   {hull:0} → {hull / Toughness * (Toughness + 0.15f):0}",
                Upgrade.Energy => $"ENERGY / SEC   {Suit.Recharge * Energy:0.0} → {Suit.Recharge * (Energy + 0.18f):0.0}",
                _ => $"REPAIR / RELOAD RATE   {Recovery:0.00}× → {Recovery + 0.18f:0.00}×",
            };
        }
        public bool Improve(Upgrade upgrade)
        {
            int index = (int)upgrade;
            if (index < 0 || index >= ranks.Length || !Pending) return false;
            experience -= Required; level++;
            ranks[index]++;
            return true;
        }
        public Progression Copy() => new Progression { experience = experience, level = level, ranks = (int[])ranks.Clone() };
    }
}
