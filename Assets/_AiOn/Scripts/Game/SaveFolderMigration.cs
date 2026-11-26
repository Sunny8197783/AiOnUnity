// The game's own folder moved (competition build): Player Settings now name the company 'HWH' and the product '경보 이전'
// (the window title), so Application.persistentDataPath went from LocalLow/DefaultCompany/AiOnUnityProject to
// LocalLow/HWH/경보 이전. Once, at startup, what the player had in the old folder is copied (never moved: the old folder
// stays as it was, a fallback if anything goes wrong) into the new one: the store (이어하기, the ending gallery, records,
// settings), the old checkpoint file, the exported playtest records, the saved family cards and the survival record.
// Pure logic plus System.IO; the Unity entry point only passes the paths.
//
// PlayerPrefs are not carried over: they live under the company and product names too (the registry on Windows), and
// the game keeps only one thing there, the lounge name fallback (LoungeScreen), which the story never needs because
// StoryDirector.LoungeName keeps the name in the store. Unity's own window size keys reset to their defaults.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AiOn.Game
{
    public static class SaveFolderMigration
    {
        /// <summary>The Player Settings names before the competition build (the folder under LocalLow).</summary>
        public const string OldCompany = "DefaultCompany", OldProduct = "AiOnUnityProject";

        /// <summary>The store's file name (FileStoryStore): the one file whose presence means 'this folder is in use'.</summary>
        public const string StoreFile = "aion-store.json";
        /// <summary>The checkpoint file UnityStoryHost writes.</summary>
        public const string CheckpointFile = "aion-save.json";

        /// <summary>The files and folders copied, the store last (a copy cut short or failed is finished on the next start: DoneMarker).</summary>
        public static readonly string[] Files = { CheckpointFile, "survival-record.txt", StoreFile };
        public static readonly string[] Folders = { PlaytestExport.Folder, "cards" };

        const string Partial = ".migrating";
        /// <summary>
        /// Written in the new folder once everything there was to bring has arrived: the done flag. A store alone is not
        /// one, since a copy that failed (a file locked by the old build still open, say) leaves the game to write a fresh
        /// store in the same session.
        /// </summary>
        public const string DoneMarker = ".migrated";
        /// <summary>Written when a copy failed (what failed, one per line): the next start tries again.</summary>
        public const string FailedMarker = ".migrate-failed";
        /// <summary>On that retry, a store the game wrote after the failed copy is kept under this suffix, not overwritten blind.</summary>
        public const string SetAside = ".after-failed-copy";

        /// <summary>
        /// The old folder beside the new one: persistentDataPath is &lt;root&gt;/&lt;company&gt;/&lt;product&gt; (Windows
        /// LocalLow, also macOS and Linux), so the old one is &lt;root&gt;/DefaultCompany/AiOnUnityProject. Null when the
        /// path is not laid out that way (its parent is not the company folder) or when it already is the old folder.
        /// </summary>
        public static string OldFolder(string dataPath, string company)
        {
            if (string.IsNullOrEmpty(dataPath) || string.IsNullOrEmpty(company)) return null;
            var full = Path.GetFullPath(dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var companyDir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(companyDir) || !string.Equals(Path.GetFileName(companyDir), company, StringComparison.OrdinalIgnoreCase)) return null;
            var root = Path.GetDirectoryName(companyDir);
            if (string.IsNullOrEmpty(root)) return null;
            var old = Path.Combine(root, OldCompany, OldProduct);
            return string.Equals(Path.GetFullPath(old), full, StringComparison.OrdinalIgnoreCase) ? null : old;
        }

        /// <summary>
        /// Copies the old folder's files and folders into the new one when the new one is not marked done and the old one
        /// has something to bring. Nothing already in the new folder is overwritten (a folder is copied whole or not at all:
        /// it is written under a temporary name first), with one exception: after a failed copy, the store the game wrote
        /// meanwhile makes way for the player's old one and is kept beside it (SetAside). Each item is tried on its own;
        /// when one fails, FailedMarker says so and an IOException follows (AtStartup logs it), and the next start tries
        /// again. Returns the names copied (empty when nothing was to do).
        /// </summary>
        public static List<string> Migrate(string oldDir, string newDir)
        {
            var copied = new List<string>();
            if (string.IsNullOrEmpty(oldDir) || string.IsNullOrEmpty(newDir) || !Directory.Exists(oldDir)) return copied;
            if (File.Exists(Path.Combine(newDir, DoneMarker))) return copied;
            bool retry = File.Exists(Path.Combine(newDir, FailedMarker));
            // in use with no failed copy behind it: copied before the marker existed (11-25), or a fresh start
            if (!retry && File.Exists(Path.Combine(newDir, StoreFile))) { Mark(newDir); return copied; }
            if (!HasAny(oldDir)) return copied;
            Directory.CreateDirectory(newDir);
            var failed = new List<string>();

            foreach (var name in Folders)
            {
                string from = Path.Combine(oldDir, name), to = Path.Combine(newDir, name);
                if (!Directory.Exists(from) || Directory.Exists(to) || File.Exists(to)) continue;
                try
                {
                    var tmp = to + Partial;
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true); // left by a copy cut short
                    CopyTree(from, tmp);
                    Directory.Move(tmp, to);
                    copied.Add(name);
                }
                catch (Exception e) { failed.Add(name + ": " + e.Message); }
            }
            foreach (var name in Files)
            {
                string from = Path.Combine(oldDir, name), to = Path.Combine(newDir, name);
                if (!File.Exists(from) || Directory.Exists(to)) continue;
                bool replace = retry && name == StoreFile && File.Exists(to); // written after the failed copy
                if (File.Exists(to) && !replace) continue;
                try
                {
                    var tmp = to + Partial;
                    File.Copy(from, tmp, true); // write then swap: a crash never leaves half a file
                    if (replace)
                    {
                        var aside = to + SetAside;
                        if (File.Exists(aside)) File.Delete(aside);
                        File.Move(to, aside);
                    }
                    File.Move(tmp, to);
                    copied.Add(name);
                }
                catch (Exception e) { failed.Add(name + ": " + e.Message); }
            }
            if (failed.Count > 0)
            {
                File.WriteAllText(Path.Combine(newDir, FailedMarker), string.Join("\n", failed));
                throw new IOException("not copied: " + string.Join("; ", failed));
            }
            Mark(newDir);
            return copied;
        }

        static void Mark(string newDir)
        {
            File.WriteAllText(Path.Combine(newDir, DoneMarker), DateTime.UtcNow.ToString("o"));
            var f = Path.Combine(newDir, FailedMarker);
            if (File.Exists(f)) File.Delete(f);
        }

        static bool HasAny(string dir)
        {
            foreach (var f in Files) if (File.Exists(Path.Combine(dir, f))) return true;
            foreach (var d in Folders) if (Directory.Exists(Path.Combine(dir, d))) return true;
            return false;
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), false);
            foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        /// <summary>Before the first scene loads (so before the store is read): bring the old folder over once.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AtStartup()
        {
            try
            {
                var dataPath = Application.persistentDataPath;
                var copied = Migrate(OldFolder(dataPath, Application.companyName), dataPath);
                if (copied.Count > 0) Debug.Log($"[Save] copied from the old {OldCompany}/{OldProduct} folder: {string.Join(", ", copied)}");
            }
            catch (Exception e) { Debug.LogWarning($"[Save] could not copy the old save folder: {e.Message}"); } // the game goes on; the next start tries again (FailedMarker)
        }
    }
}
