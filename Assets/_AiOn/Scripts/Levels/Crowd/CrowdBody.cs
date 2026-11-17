using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AiOn.Levels.Crowd
{
    [Flags]
    public enum PropMask
    {
        None = 0, Backpack = 1, WaterJug = 2, Walker = 4, Flashlight = 8, Phone = 16, Baton = 32, Basket = 64, Cart = 128,
    }

    public static class Props
    {
        public static readonly (PropMask mask, string name)[] Names =
        {
            (PropMask.Backpack, "backpack"), (PropMask.WaterJug, "waterJug"), (PropMask.Walker, "walker"), (PropMask.Flashlight, "flashlight"),
            (PropMask.Phone, "phone"), (PropMask.Baton, "baton"), (PropMask.Basket, "basket"), (PropMask.Cart, "cart"),
        };

        public static PropMask Of(string name)
        {
            foreach (var (m, n) in Names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return m;
            return PropMask.None;
        }
    }

    /// <summary>
    /// One drawn person of a crowd, as the logic leaves it each frame (original frame). The view (CrowdView) turns it
    /// into a pooled person: position, yaw, animation, props.
    /// </summary>
    public sealed class CrowdBody
    {
        public int Seed;
        /// <summary>Person preset: "crowd" picks crowd1..6 by the seed.</summary>
        public string Preset = "crowd";
        public bool Visible;
        public double X, Y, Z, Yaw;
        public string Anim = "idle";
        /// <summary>Gait speed in m/s for walk / brisk / run (0 = the gait's own speed).</summary>
        public double Speed;
        public PropMask Props;
        /// <summary>The next sync switches the animation instantly (placement snaps).</summary>
        public bool Snap;

        public CrowdBody(int seed, string preset = "crowd") { Seed = seed; Preset = preset; }
    }

    /// <summary>What a crowd needs to know about the frame (original frame).</summary>
    public readonly struct CrowdFrame
    {
        public readonly double T;
        public readonly bool HasPlayer, HasCamera, Power;
        public readonly double Px, Pz, Cx, Cz, Fx, Fz;
        public readonly string Env;

        public CrowdFrame(double t, bool hasPlayer, double px, double pz, bool hasCamera, double cx, double cz, double fx, double fz, bool power, string env)
        {
            T = t; HasPlayer = hasPlayer; Px = px; Pz = pz; HasCamera = hasCamera; Cx = cx; Cz = cz; Fx = fx; Fz = fz; Power = power; Env = env;
        }

        public static CrowdFrame Empty(double t = 0) => new(t, false, 0, 0, false, 0, 0, 0, 0, true, "day");
    }

    /// <summary>A level's crowd: pure logic over CrowdBody records.</summary>
    public interface ICrowdLogic
    {
        IReadOnlyList<CrowdBody> Bodies { get; }
        string Mode { get; }
        /// <summary>Takes a level state the crowd listens to; false when the key is not the crowd's.</summary>
        bool Apply(string key, JToken value);
        void Update(double dt, in CrowdFrame f);
    }
}
