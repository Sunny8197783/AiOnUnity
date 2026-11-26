using System;
using System.IO;
using AiOn.Game;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>
    /// The competition build renamed the product ('경보 이전', company 'HWH'), which moved the save folder; the old folder's
    /// files are copied over once at startup (SaveFolderMigration). Runs in a temporary folder, never the real LocalLow.
    /// </summary>
    public class SaveFolderMigrationTests
    {
        string _root, _old, _new;

        [SetUp]
        public void Make()
        {
            _root = Path.Combine(Path.GetTempPath(), "aion-migrate-test-" + Guid.NewGuid().ToString("N"));
            _old = Path.Combine(_root, SaveFolderMigration.OldCompany, SaveFolderMigration.OldProduct);
            _new = Path.Combine(_root, "HWH", "경보 이전");
            Directory.CreateDirectory(_old);
        }

        [TearDown]
        public void Clean() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        void Old(string rel, string text)
        {
            var p = Path.Combine(_old, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, text);
        }

        string New(string rel) => Path.Combine(_new, rel);

        [Test]
        public void The_old_folder_is_beside_the_new_one_under_the_same_root()
        {
            Assert.AreEqual(Path.GetFullPath(_old), Path.GetFullPath(SaveFolderMigration.OldFolder(_new, "HWH")));
            Assert.AreEqual(Path.GetFullPath(_old), Path.GetFullPath(SaveFolderMigration.OldFolder(_new.Replace('\\', '/') + "/", "HWH")), "Unity's '/' path");
            Assert.IsNull(SaveFolderMigration.OldFolder(_new, "SomeoneElse"), "not laid out <company>/<product>: leave it alone");
            Assert.IsNull(SaveFolderMigration.OldFolder(_old, SaveFolderMigration.OldCompany), "the old names again: nothing to copy from");
            Assert.IsNull(SaveFolderMigration.OldFolder(null, "HWH"));
            Assert.IsNull(SaveFolderMigration.OldFolder(_new, ""));
        }

        [Test]
        public void A_first_start_copies_the_store_the_checkpoint_and_the_exports_and_leaves_the_old_folder_as_it_was()
        {
            Old(SaveFolderMigration.StoreFile, "{\"gyeongbo.records.v1\":\"x\"}");
            Old(SaveFolderMigration.CheckpointFile, "{}");
            Old("survival-record.txt", "기록");
            Old(Path.Combine(PlaytestExport.Folder, "playtest-run-1.json"), "{}");
            Old(Path.Combine(PlaytestExport.Folder, "playtest-run-1.csv"), "a,b");
            Old(Path.Combine("cards", "card.png"), "png");
            Old("Player.log", "unity's own log");

            var copied = SaveFolderMigration.Migrate(SaveFolderMigration.OldFolder(_new, "HWH"), _new);

            CollectionAssert.AreEquivalent(new[] { SaveFolderMigration.StoreFile, SaveFolderMigration.CheckpointFile, "survival-record.txt", PlaytestExport.Folder, "cards" }, copied);
            Assert.AreEqual(SaveFolderMigration.StoreFile, copied[copied.Count - 1], "the store last: a copy cut short is finished next time");
            Assert.AreEqual("{\"gyeongbo.records.v1\":\"x\"}", File.ReadAllText(New(SaveFolderMigration.StoreFile)));
            Assert.AreEqual("기록", File.ReadAllText(New("survival-record.txt")));
            Assert.AreEqual("a,b", File.ReadAllText(New(Path.Combine(PlaytestExport.Folder, "playtest-run-1.csv"))));
            Assert.IsTrue(File.Exists(New(Path.Combine("cards", "card.png"))));
            Assert.IsFalse(File.Exists(New("Player.log")), "only the game's own files");
            Assert.IsTrue(File.Exists(Path.Combine(_old, SaveFolderMigration.StoreFile)), "copied, not moved");
            Assert.IsTrue(Directory.Exists(Path.Combine(_old, PlaytestExport.Folder)));
            Assert.IsEmpty(Directory.GetFileSystemEntries(_new, "*.migrating", SearchOption.AllDirectories), "no temporary names left");
        }

        [Test]
        public void It_happens_once_a_new_folder_in_use_is_never_overwritten()
        {
            Old(SaveFolderMigration.StoreFile, "old");
            Old(Path.Combine(PlaytestExport.Folder, "a.json"), "old");
            Assert.IsNotEmpty(SaveFolderMigration.Migrate(_old, _new));
            File.WriteAllText(New(SaveFolderMigration.StoreFile), "new");
            Old(Path.Combine(PlaytestExport.Folder, "b.json"), "later");
            Assert.IsEmpty(SaveFolderMigration.Migrate(_old, _new), "the new folder has a store: nothing more");
            Assert.AreEqual("new", File.ReadAllText(New(SaveFolderMigration.StoreFile)));
            Assert.IsFalse(File.Exists(New(Path.Combine(PlaytestExport.Folder, "b.json"))));
        }

        [Test]
        public void A_copy_cut_short_is_finished_without_touching_what_already_arrived()
        {
            Old(SaveFolderMigration.StoreFile, "old store");
            Old(Path.Combine(PlaytestExport.Folder, "a.json"), "old");
            Directory.CreateDirectory(New(PlaytestExport.Folder));
            File.WriteAllText(New(Path.Combine(PlaytestExport.Folder, "mine.json")), "already here");
            Directory.CreateDirectory(New("cards.migrating")); // a folder copy that was cut short
            Old(Path.Combine("cards", "card.png"), "png");

            var copied = SaveFolderMigration.Migrate(_old, _new);

            CollectionAssert.AreEquivalent(new[] { "cards", SaveFolderMigration.StoreFile }, copied);
            Assert.IsFalse(File.Exists(New(Path.Combine(PlaytestExport.Folder, "a.json"))), "a folder already there is not merged into");
            Assert.AreEqual("already here", File.ReadAllText(New(Path.Combine(PlaytestExport.Folder, "mine.json"))));
            Assert.IsTrue(File.Exists(New(Path.Combine("cards", "card.png"))));
            Assert.IsFalse(Directory.Exists(New("cards.migrating")));
        }

        [Test]
        public void A_failed_copy_is_tried_again_and_the_old_store_wins_over_the_one_written_meanwhile()
        {
            Old(SaveFolderMigration.StoreFile, "old store");
            Old(SaveFolderMigration.CheckpointFile, "old checkpoint");
            using (new FileStream(Path.Combine(_old, SaveFolderMigration.StoreFile), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => SaveFolderMigration.Migrate(_old, _new), "the old build still has the store open");
            Assert.AreEqual("old checkpoint", File.ReadAllText(New(SaveFolderMigration.CheckpointFile)), "what could be copied was");
            Assert.IsFalse(File.Exists(New(SaveFolderMigration.StoreFile)));
            Assert.IsTrue(File.Exists(New(SaveFolderMigration.FailedMarker)));
            Assert.IsFalse(File.Exists(New(SaveFolderMigration.DoneMarker)));

            File.WriteAllText(New(SaveFolderMigration.StoreFile), "written by the session after"); // the game goes on
            var copied = SaveFolderMigration.Migrate(_old, _new);
            CollectionAssert.AreEqual(new[] { SaveFolderMigration.StoreFile }, copied, "the next start tries again");
            Assert.AreEqual("old store", File.ReadAllText(New(SaveFolderMigration.StoreFile)), "the player's old save is brought over");
            Assert.AreEqual("written by the session after", File.ReadAllText(New(SaveFolderMigration.StoreFile + SaveFolderMigration.SetAside)), "kept, not lost");
            Assert.AreEqual("old checkpoint", File.ReadAllText(New(SaveFolderMigration.CheckpointFile)));
            Assert.IsTrue(File.Exists(New(SaveFolderMigration.DoneMarker)));
            Assert.IsFalse(File.Exists(New(SaveFolderMigration.FailedMarker)));

            File.WriteAllText(New(SaveFolderMigration.StoreFile), "played on");
            Assert.IsEmpty(SaveFolderMigration.Migrate(_old, _new), "done: never again");
            Assert.AreEqual("played on", File.ReadAllText(New(SaveFolderMigration.StoreFile)));
        }

        [Test]
        public void A_folder_copied_before_the_marker_existed_is_marked_done_and_left_alone()
        {
            Old(SaveFolderMigration.StoreFile, "old");
            Directory.CreateDirectory(_new);
            File.WriteAllText(New(SaveFolderMigration.StoreFile), "copied on 11-25");
            Assert.IsEmpty(SaveFolderMigration.Migrate(_old, _new));
            Assert.AreEqual("copied on 11-25", File.ReadAllText(New(SaveFolderMigration.StoreFile)));
            Assert.IsTrue(File.Exists(New(SaveFolderMigration.DoneMarker)));
        }

        [Test]
        public void Nothing_to_bring_leaves_the_new_folder_alone()
        {
            Assert.IsEmpty(SaveFolderMigration.Migrate(_old, _new), "an empty old folder");
            Assert.IsFalse(Directory.Exists(_new), "not even created");
            Assert.IsEmpty(SaveFolderMigration.Migrate(Path.Combine(_root, "missing"), _new));
            Assert.IsEmpty(SaveFolderMigration.Migrate(null, _new));
        }

        [Test]
        public void The_names_copied_are_the_ones_the_game_writes()
        {
            CollectionAssert.Contains(SaveFolderMigration.Folders, PlaytestExport.Folder);
            var (cards, pictures, _) = FamilyCard.SaveFolder(null, _new);
            Assert.IsFalse(pictures);
            CollectionAssert.Contains(SaveFolderMigration.Folders, Path.GetFileName(cards), "the family card's fallback folder");
            CollectionAssert.Contains(SaveFolderMigration.Files, SaveFolderMigration.StoreFile);
            CollectionAssert.Contains(SaveFolderMigration.Files, SaveFolderMigration.CheckpointFile);
        }
    }
}
