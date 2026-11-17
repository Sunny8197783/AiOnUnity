// Strain (story/strain.js): ports of the body-strain tests of tests/story.test.mjs.
using AiOn.Story;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StrainTests
    {
        [Test]
        public void CarryingWaterTiresOnlyWhileWalking_RestingRecovers_F3()
        {
            Assert.That(Strain.CarryStep(1, 0, 20).Fatigue, Is.LessThan(0), "standing still rests");
            Assert.That(Strain.CarryStep(1, 3.2, 20).Fatigue, Is.GreaterThan(Strain.CarryStep(1, 1.8, 20).Fatigue), "running costs more");
            Assert.That(Strain.CarryStep(1, 1.8, 20).Fatigue, Is.GreaterThan(Strain.CarryStep(1, 1.8, 4).Fatigue), "20 L costs more than 4 L");
            // 165 m home at the heavy pace (0.62 x 3 m/s): about 11 fatigue, so only an already worn-out body gives out.
            double f = 0;
            for (double t = 0; t < 165 / 1.86; t += 0.1) f += Strain.CarryStep(0.1, 1.86, 20).Fatigue;
            Assert.That(f, Is.GreaterThan(8).And.LessThan(15), f.ToString());
            Assert.That(Strain.Carry.Warn, Is.LessThan(100));
        }

        [Test]
        public void CarryStep_EdgeValues()
        {
            var z = Strain.CarryStep(0, 2, 20);
            Assert.That(z.Fatigue, Is.EqualTo(0)); Assert.That(z.Thirst, Is.EqualTo(0));
            Assert.That(Strain.CarryStep(double.NaN, 2, 20).Fatigue, Is.EqualTo(0));
            Assert.That(Strain.CarryStep(1, 1, 4).Fatigue, Is.EqualTo(0.05)); Assert.That(Strain.CarryStep(1, 1, 4).Thirst, Is.EqualTo(0.05));
            Assert.That(Strain.CarryStep(1, 3, 13).Fatigue, Is.EqualTo(0.2));
            Assert.That(Strain.CarryStep(2, 0.1, 13).Fatigue, Is.EqualTo(-0.8));
        }

        static (double exposure, int hurt, double level) Run(double sec, double smoke, bool crouch = false, bool towel = false)
        {
            var s = new Strain.SmokeState();
            int hurt = 0; Strain.SmokeResult r = default;
            for (double t = 0; t < sec; t += 1 / 30.0) { r = Strain.SmokeStep(s, 1 / 30.0, smoke, crouch, towel); hurt += r.Hurt; }
            return (r.Exposure, hurt, r.Level);
        }

        [Test]
        public void Smoke_CrouchingWithAWetTowelStaysSafe_StandingBlursButIsSurvivableOnADirectPath_M7()
        {
            var low = Run(40, 0.7, crouch: true, towel: true);
            Assert.That(low.hurt, Is.LessThanOrEqualTo(12), $"crouch + towel: {low.hurt}");
            var direct = Run(8, 0.7);
            Assert.That(direct.exposure > Strain.Smoke.Safe && direct.level > 0.5, Is.True, "visibly blurred");
            Assert.That(direct.hurt, Is.LessThan(15), $"direct path: {direct.hurt}");
            Assert.That(Run(60, 0.7).hurt, Is.GreaterThan(100), "lingering in thick smoke is deadly");
            Assert.That(Run(10, 0).hurt, Is.EqualTo(0));
        }
    }
}
