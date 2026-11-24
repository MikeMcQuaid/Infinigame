using UnityEngine;

namespace Infinigame
{
    public enum Surface { Sand, Grass, Rock, Snow, Bark, Dust, Water, Lava, Metal, Ceramic, Seal, Chitin }

    public static class Textures
    {
        public static Texture2D Albedo(Surface surface) => Resources.Load<Texture2D>("Surfaces/" + surface);
        public static Texture2D Normal(Surface surface) => Resources.Load<Texture2D>("Surfaces/" + surface + " relief");
    }
}
