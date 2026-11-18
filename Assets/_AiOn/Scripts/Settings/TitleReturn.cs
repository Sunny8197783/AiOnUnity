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

        public static void ToTitle()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            InputGate.Clear();
            AiOn.UI.UiScreen.ResetModals(); // a card or time skip torn down mid-await never pops its count
            AiOn.UI.GameHud.Cinematic = false;
            if (EnvironmentController.Instance) EnvironmentController.Instance.SetSmoke(0); // its statics outlive the scene
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
