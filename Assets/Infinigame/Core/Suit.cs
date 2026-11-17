using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public static class Suit
    {
        public const int Slots = 8, Budget = 24;
        public const float Capacity = 100f, Recharge = 18f, HeatLimit = 4200f, Ceiling = 2400f, SurfaceCeiling = 65f;
        public static readonly Gear[] Modules = { Gear.Thrusters, Gear.Beams, Gear.BackMissiles, Gear.Rail, Gear.Armour, Gear.Stealth, Gear.Capacitor, Gear.Nova };
        public static int Cost(Gear gear) => gear == Gear.Beams || gear == Gear.Rail ? 3 : gear == Gear.Capacitor ? 1 : 2;
        public static bool Fits(int owned, int fitted) => (fitted & ~owned) == 0 && (fitted & ~Modules.Aggregate(0, (bits, gear) => bits | 1 << (int)gear)) == 0
            && Modules.Count(gear => Campaign.Has(fitted, gear)) <= Slots && Modules.Where(gear => Campaign.Has(fitted, gear)).Sum(Cost) <= Budget;
        public static int Default(int owned)
        {
            int fitted = 0;
            foreach (var gear in Modules)
                if (Campaign.Has(owned, gear) && Fits(owned, fitted | 1 << (int)gear)) fitted |= 1 << (int)gear;
            return fitted;
        }
        public static bool Spend(ref float energy, float amount)
        {
            if (amount < 0f || energy < amount) return false;
            energy -= amount;
            return true;
        }
    }
}
