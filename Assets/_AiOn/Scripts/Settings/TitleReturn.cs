using System.Collections.Generic;
using AiOn.Levels;
using AiOn.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiOn.Settings
{
    /// <summary>
    /// The pause menu's '메인 메뉴로' (the original reloads the page): everything the game keeps across level loads is
    /// let go and the Boot scene starts over at the title screen. Progress since the last checkpoint is not saved, as in
    /// the original (the menu asks first).
    /// </summary>
    public static class TitleReturn
    {
        public const int BootScene = 0;

        /// <summary>
        /// Counts the returns to the title. A director remembers the count it was made under (StoryDirector.Stale): the
        /// awaits of the run left behind (a landing or card resolved by its teardown, a talk still waiting for a key) see
        /// it change and stop there, instead of driving the destroyed overlay. Never reset: an old director stays old.
        /// </summary>
        public static int Generation { get; private set; }

        /// <summary>The count goes up (ToTitle, before anything is torn down; tests call it alone).</summary>
        public static int Advance() => ++Generation;

        public static void ToTitle()
        {
            Advance(); // first: whatever the teardown below resumes already belongs to the old run
            Time.timeScale = 1f;
            AudioListener.pause = false;
            InputGate.Clear();
            AiOn.Game.Companion.Reset(); // 도우미 모드: the last run's goal and screens are not the title's
            AiOn.UI.UiScreen.ResetModals(); // a card or time skip torn down mid-await never pops its count
            AiOn.UI.GameHud.Cinematic = false;
            AiOn.UI.DemoKeyHint.On = false; // F10 in the key strip is a prepared run's only
            if (EnvironmentController.Instance) EnvironmentController.Instance.SetSmoke(0); // its statics outlive the scene
            AiOn.UI.Studio.StudioShared.Release(); // the minigame pictures' code-made maps
            // The persistent scene's roots (story host, player, camera, overlay, systems, cutscene canvases): found
            // through a probe moved there, then destroyed (deferred to the end of the frame, before the load runs).
            var probe = new GameObject("TitleReturnProbe");
            Object.DontDestroyOnLoad(probe);
            var roots = new List<GameObject>();
            probe.scene.GetRootGameObjects(roots);
            foreach (var go in roots)
                if (go && !go.name.StartsWith("[")) Object.Destroy(go); // not the engine's own helpers ('[Debug Updater]')
            SceneManager.LoadScene(BootScene, LoadSceneMode.Single);
        }
    }
}
