// A title return (메인 메뉴로, F10) clears the input holds first and then destroys the host and the overlay. The old run's
// chain can still go on for a step (a screen its teardown resolved, then a card): the holds it takes on the destroyed
// host and overlay must come to nothing, or the next run starts with the player frozen and Esc ignored, and the screens
// it opens must not be built while the scene unloads.
using System;
using System.Reflection;
using System.Threading.Tasks;
using AiOn.Player;
using AiOn.Story;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AiOn.Tests
{
    public class TitleReturnHoldTests
    {
        [SetUp, TearDown]
        public void ClearHolds() => InputGate.Clear();

        [Test]
        public void ADestroyedHost_TakesNoHold_ALiveOneStill()
        {
            var go = new GameObject("Host");
            var host = go.AddComponent<UnityStoryHost>();
            host.SetControls(false);
            Assert.IsTrue(InputGate.Blocked, "a live host holds the player");
            host.SetControls(true);
            Assert.IsFalse(InputGate.Blocked);

            Object.DestroyImmediate(go);
            host.SetControls(false); // the old run's card after the teardown
            Assert.IsFalse(InputGate.Blocked, "the next run would start frozen");
        }

        [Test]
        public void AHostFromBeforeATitleReturn_NeitherHoldsNorTakesTheNewTitlesHold()
        {
            var go = new GameObject("OldHost");
            try
            {
                var host = go.AddComponent<UnityStoryHost>();
                host.SetControls(false);
                AiOn.Settings.TitleReturn.Advance(); // ToTitle: the count goes up, then the holds are cleared
                InputGate.Clear();
                InputGate.Hold(); // the new title's own hold (its landing)
                host.SetControls(true); // the old run's modal closing while the host is still being unloaded
                Assert.IsTrue(InputGate.Blocked, "the old host let go of the new title's hold");
                InputGate.Clear();
                host.SetControls(false); // the old run's next card
                Assert.IsFalse(InputGate.Blocked, "the next run would start frozen");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AHostFromBeforeATitleReturn_OpensNoScreen_AndTheOldRunWaitsForever()
        {
            var go = new GameObject("OldHost");
            try
            {
                var host = go.AddComponent<UnityStoryHost>();
                int opened = 0;
                host.Screens["compose"] = o => { opened++; return Task.FromResult<JToken>(new JValue("sent")); };
                Assert.AreEqual("sent", (string)host.Open("compose", new JObject()).Result, "a live host opens its screens");
                AiOn.Settings.TitleReturn.Advance();
                var late = host.Open("compose", new JObject()); // the old run, after the screen its teardown resolved
                Assert.AreEqual(1, opened, "a screen built while the scene unloads is left over ('not cleaned up')");
                Assert.IsFalse(late.IsCompleted, "the old run's chain stops here");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AHostFromBeforeATitleReturn_LoadsNoLevel_AndTheOldRunWaitsForever()
        {
            var go = new GameObject("OldHost");
            try
            {
                var host = go.AddComponent<UnityStoryHost>();
                AiOn.Settings.TitleReturn.Advance();
                // the old run's ctx.Goto after a screen or cutscene its teardown resolved: LoadSceneAsync would run (and
                // throw in edit mode, which would complete the task as faulted)
                var late = host.Enter("apartment", "home");
                Assert.IsFalse(late.IsCompleted, "no level is loaded over the new title; the old chain stops here");
                Assert.IsFalse(host.Loading, "the frame governor is not told a load is under way");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ADestroyedOverlay_TakesNoHold_ALiveOneStill()
        {
            var scope = typeof(StoryOverlay).GetNestedType("ModalScope", BindingFlags.NonPublic);
            Assert.IsNotNull(scope);
            var go = new GameObject("Overlay");
            var overlay = go.AddComponent<StoryOverlay>();
            var live = (IDisposable)Activator.CreateInstance(scope, overlay, false, false);
            Assert.IsTrue(InputGate.Blocked, "a talk on a live overlay holds the player");
            live.Dispose();
            Assert.IsFalse(InputGate.Blocked);

            Object.DestroyImmediate(go);
            var late = (IDisposable)Activator.CreateInstance(scope, overlay, false, false);
            Assert.IsFalse(InputGate.Blocked, "a card on the destroyed overlay: nothing would ever let go of it");
            late.Dispose();
            Assert.IsFalse(InputGate.Blocked);
        }
    }
}
