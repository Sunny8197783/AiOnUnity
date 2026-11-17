// Port of story/strain.js: the body under load, as plain numbers (no scene) so chapters stay short and tests can pin it.
//
// CarryStep(dt, speed, carry) -> (fatigue, thirst) deltas for one frame of carrying water home.
//   Walking with a load tires; running tires more; standing still (the jug put down) lets you catch your breath.
// SmokeStep(s, dt, smoke, crouch, towel) -> (exposure, hurt, level)
//   s = { Exposure, HurtT } is updated in place. smoke 0..1 is how thick the air is where you are.
//   Crouching (C) and a wet towel over nose and mouth cut what you breathe in (fact fire_big).
//   Past Smoke.Safe, health goes 1 point at a time, faster the longer you stay. level 0..1 drives the blur.
using System;

namespace AiOn.Story
{
    public static class Strain
    {
        /// <summary>CARRY: heavy (litres above which the load is heavy), walk/run fatigue per second [light, heavy], rest, thirst, speed thresholds, warn.</summary>
        public static class Carry
        {
            public const double Heavy = 12;
            public static readonly double[] Walk = { 0.05, 0.12 };
            public static readonly double[] Run = { 0.1, 0.2 };
            public const double Rest = 0.4, Thirst = 0.05, Moving = 0.3, Running = 2.4, Warn = 85;
        }

        public readonly struct CarryDelta
        {
            public readonly double Fatigue, Thirst;
            public CarryDelta(double fatigue, double thirst) { Fatigue = fatigue; Thirst = thirst; }
        }

        public static CarryDelta CarryStep(double dt, double speed, double carry)
        {
            if (!(dt > 0)) return new CarryDelta(0, 0);
            int heavy = carry > Carry.Heavy ? 1 : 0;
            if (speed < Carry.Moving) return new CarryDelta(-Carry.Rest * dt, 0);
            double rate = (speed > Carry.Running ? Carry.Run : Carry.Walk)[heavy];
            return new CarryDelta(rate * dt, Carry.Thirst * dt);
        }

        /// <summary>SMOKE constants.</summary>
        public static class Smoke
        {
            public const double Base = 1, Thick = 1.5, Crouch = 0.3, Towel = 0.4, Safe = 6, Ramp = 12, Every = 2, Blur = 8;
        }

        /// <summary>Per-exposure state the caller keeps between frames (the original's s = {}).</summary>
        public sealed class SmokeState
        {
            public double Exposure, HurtT;
        }

        public readonly struct SmokeResult
        {
            public readonly double Exposure;
            public readonly int Hurt;
            public readonly double Level;
            public SmokeResult(double exposure, int hurt, double level) { Exposure = exposure; Hurt = hurt; Level = level; }
        }

        public static SmokeResult SmokeStep(SmokeState s, double dt, double smoke = 0, bool crouch = false, bool towel = false)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int hurt = 0;
            if (dt > 0 && smoke > 0)
            {
                s.Exposure += dt * smoke * (Smoke.Base + Smoke.Thick * smoke) * (crouch ? Smoke.Crouch : 1) * (towel ? Smoke.Towel : 1);
                if (s.Exposure > Smoke.Safe)
                {
                    s.HurtT += dt * (1 + (s.Exposure - Smoke.Safe) / Smoke.Ramp);
                    while (s.HurtT >= Smoke.Every) { s.HurtT -= Smoke.Every; hurt += 1; }
                }
            }
            return new SmokeResult(s.Exposure, hurt, Math.Min(1, s.Exposure / Smoke.Blur));
        }
    }
}
