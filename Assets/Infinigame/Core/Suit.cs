using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct StickNavigation
    {
        int direction;
        float repeat;
        public int Read(float left, float right, float now)
        {
            float axis = math.abs(left) > math.abs(right) ? left : right;
            int next = axis > 0.7f ? 1 : axis < -0.7f ? -1 : math.abs(axis) < 0.4f ? 0 : direction;
            bool step = next != 0 && (next != direction || now >= repeat);
            if (step) repeat = now + (next == direction ? 0.16f : 0.4f);
            direction = next;
            return step ? next : 0;
        }
    }

    public static class Suit
    {
        static readonly string[] PowerUp =
        {
            "POWER LOSS RECORDED", "RESERVE CELL CONNECTED", "RECOVERING LAST PRESSURE TRACE", "ISOLATING DAMAGED FEEDS",
            "BREATHING CIRCUIT OPEN", "SEAL PRESSURE HOLDING", "SPINAL BUS RESPONDING", "MOTOR INTERLOCK ENGAGED",
            "OPENING COMPANION CHANNEL", "READING GROUND ECHOES", "REBUILDING INERTIAL FRAME", "RECOVERY KERNEL ONLINE"
        };
        static readonly string[] Systems =
        {
            "WRIST TERMINAL", "HEEL TRANSDUCER", "SPINAL BUS", "NECK SEAL", "VISOR OPTICS", "RANGE ARRAY", "THRUST GOVERNOR",
            "PORT MANIFOLD", "HIP ACTUATOR", "AIR SCRUBBER", "CAPACITOR BANK", "PACK TELEMETRY", "JAW MICROPHONE", "STERNUM PLATE",
            "PALM SENSOR", "OXYGEN RESERVE", "STARBOARD MANIFOLD", "GROUND SCANNER", "FIELD COIL", "KNEE SERVO", "THERMAL LOOP",
            "COMPANION RADIO", "GAIT CONTROLLER", "PRESSURE BULKHEAD", "BALANCE GYRO", "TACTILE MESH", "RETINAL PROJECTOR"
        };
        static readonly string[] Checks =
        {
            "READING FAULT LATCH", "MAPPING RETURN PATH", "SAMPLING REFERENCE VOLTAGE", "VERIFYING PACKET ORDER",
            "MEASURING RESPONSE TIME", "LOADING TRIM VALUES", "CHECKING REDUNDANT FEED", "MATCHING CLOCK EDGES",
            "RESTORING CHANNEL MAP", "COMPARING CHECKSUM", "ACQUIRING BASELINE", "PROBING SIGNAL MARGIN",
            "CHECKING THERMAL DRIFT", "VERIFYING BUS ADDRESS", "RECOVERING LAST SAMPLE", "ACKNOWLEDGING HEARTBEAT",
            "TESTING WATCHDOG", "VALIDATING CONTROL LIMITS", "CORRECTING ZERO OFFSET", "TRACING FEEDBACK LOOP",
            "READING CALIBRATION BANK", "RECONCILING SENSOR PAIR", "MEASURING IDLE CURRENT"
        };
        public static string Diagnostic(int line)
        {
            if (line < PowerUp.Length) return PowerUp[line];
            int address = line - PowerUp.Length;
            int location = (address * 40503 + 0xAB43) & 0xffff;
            return $"{Systems[address % Systems.Length]} [{location:X4}] · {Checks[address % Checks.Length]}";
        }

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
