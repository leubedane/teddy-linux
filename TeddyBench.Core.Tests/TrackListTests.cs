using System.Collections.Generic;
using System.IO;
using System.Linq;
using TeddyBench.Core.Tracks;
using Xunit;

namespace TeddyBench.Core.Tests
{
    public class NaturalStringComparerTests
    {
        [Fact]
        public void NumbersCompareByValue()
        {
            var names = new[] { "Folge 10", "Folge 2", "Folge 1", "Folge 11", "folge 3" };
            var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
            Assert.Equal(new[] { "Folge 1", "Folge 2", "folge 3", "Folge 10", "Folge 11" }, sorted);
        }

        [Fact]
        public void LeadingZerosAndMixedText()
        {
            var c = NaturalStringComparer.Instance;
            Assert.True(c.Compare("CD 2", "CD 10") < 0);
            Assert.True(c.Compare("02 Intro", "10 Outro") < 0);
            Assert.True(c.Compare("Track 2b", "Track 2a") > 0);
            Assert.True(c.Compare("a", "a1") < 0);
            Assert.True(c.Compare("Folge 01", "Folge 1") != 0);
            Assert.Equal(0, c.Compare("x", "x"));
            Assert.True(c.Compare(null, "a") < 0);
            /* longer than long.MaxValue must not overflow */
            Assert.True(c.Compare("99999999999999999999999", "100000000000000000000000") < 0);
        }
    }

    public class TrackListTests
    {
        private static readonly Dictionary<string, int?> NoTags = new Dictionary<string, int?>();

        private static TrackList NewList(Dictionary<string, int?> trackNumbers = null)
        {
            trackNumbers = trackNumbers ?? NoTags;
            return new TrackList(f => new TrackTags { TrackNumber = trackNumbers.TryGetValue(Path.GetFileName(f), out int? n) ? n : null });
        }

        private static string Touch(TempDir dir, params string[] parts)
        {
            string path = dir.Combine(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[0]);
            return path;
        }

        private static string[] Relative(TrackList list) => list.Tracks.Select(list.RelativePath).ToArray();

        [Fact]
        public void AddFolder_RecursesTakesAudioOnly_DefaultsToFolderThenName()
        {
            using (var dir = new TempDir())
            {
                Touch(dir, "Hörspiel", "CD 10", "Folge 1.mp3");
                Touch(dir, "Hörspiel", "CD 2", "Folge 10.mp3");
                Touch(dir, "Hörspiel", "CD 2", "Folge 2.MP3");
                Touch(dir, "Hörspiel", "CD 2", "cover.jpg");
                Touch(dir, "Hörspiel", "Intro.ogg");
                Touch(dir, "Hörspiel", "info.nfo");

                var list = NewList();
                var result = list.Add(new[] { dir.Combine("Hörspiel") });

                Assert.Equal(TrackSortCriterion.FolderThenName, list.Criterion);
                Assert.Equal(4, result.Added);
                Assert.Equal(0, result.Skipped);
                Assert.Equal(new[] { "Intro.ogg", "CD 2/Folge 2.MP3", "CD 2/Folge 10.mp3", "CD 10/Folge 1.mp3" }, Relative(list));
            }
        }

        [Fact]
        public void Add_SkipsNonAudioAndDuplicates_ReportsEmptyFolders()
        {
            using (var dir = new TempDir())
            {
                string a = Touch(dir, "A", "1.mp3");
                string notes = Touch(dir, "notes.txt");
                Directory.CreateDirectory(dir.Combine("Empty"));
                Touch(dir, "Empty", "cover.jpg");

                var list = NewList();
                Assert.Equal(1, list.Add(new[] { a }).Added);
                var result = list.Add(new[] { a, dir.Combine("A"), notes, dir.Combine("Empty") });

                Assert.Equal(0, result.Added);
                Assert.Equal(3, result.Skipped); /* a twice (direct + via folder) and notes.txt; cover.jpg is ignored silently */
                Assert.Equal(new[] { dir.Combine("Empty") }, result.EmptyFolders);
                Assert.Single(list.Tracks);
            }
        }

        [Fact]
        public void SortCriteria()
        {
            using (var dir = new TempDir())
            {
                string b10 = Touch(dir, "B", "Folge 10.mp3");
                string a2 = Touch(dir, "A", "Folge 2.mp3");
                string b1 = Touch(dir, "B", "Folge 1.mp3");
                string a3 = Touch(dir, "A", "Folge 3.mp3");

                var list = NewList(new Dictionary<string, int?> { ["Folge 10.mp3"] = 1, ["Folge 3.mp3"] = 2 });
                list.Add(new[] { b10 });
                list.Add(new[] { a2, b1, a3 });

                Assert.Equal(new[] { "A/Folge 2.mp3", "A/Folge 3.mp3", "B/Folge 1.mp3", "B/Folge 10.mp3" }, Relative(list));

                list.Sort(TrackSortCriterion.Name);
                Assert.Equal(new[] { "B/Folge 1.mp3", "A/Folge 2.mp3", "A/Folge 3.mp3", "B/Folge 10.mp3" }, Relative(list));

                list.Sort(TrackSortCriterion.Id3TrackNumber);
                Assert.Equal(new[] { "B/Folge 10.mp3", "A/Folge 3.mp3", "B/Folge 1.mp3", "A/Folge 2.mp3" }, Relative(list));

                list.Sort(TrackSortCriterion.OrderAdded);
                Assert.Equal(new[] { "B/Folge 10.mp3", "A/Folge 2.mp3", "B/Folge 1.mp3", "A/Folge 3.mp3" }, Relative(list));
            }
        }

        [Fact]
        public void OrderAdded_FolderContentsInFolderThenNameOrder()
        {
            using (var dir = new TempDir())
            {
                string single = Touch(dir, "z.mp3");
                Touch(dir, "F", "CD 2", "1.mp3");
                Touch(dir, "F", "CD 1", "10.mp3");
                Touch(dir, "F", "CD 1", "9.mp3");

                var list = NewList();
                list.Sort(TrackSortCriterion.OrderAdded);
                list.Add(new[] { single, dir.Combine("F") });

                Assert.Equal(new[] { "z.mp3", "F/CD 1/9.mp3", "F/CD 1/10.mp3", "F/CD 2/1.mp3" }, Relative(list));
            }
        }

        [Fact]
        public void MoveAndRemove_ThenSortReplacesManualMoves()
        {
            using (var dir = new TempDir())
            {
                Touch(dir, "1.mp3");
                Touch(dir, "2.mp3");
                Touch(dir, "3.mp3");
                Touch(dir, "4.mp3");
                var list = NewList();
                list.Add(new[] { dir.Path });

                Assert.True(list.Move(new[] { list.Tracks[2], list.Tracks[3] }, -1));
                Assert.Equal(new[] { "1.mp3", "3.mp3", "4.mp3", "2.mp3" }, Relative(list));
                Assert.False(list.Move(new[] { list.Tracks[0] }, -1));
                Assert.False(list.Move(new Track[0], 1));

                list.Remove(new[] { list.Tracks[1] });
                Assert.Equal(new[] { "1.mp3", "4.mp3", "2.mp3" }, Relative(list));

                list.Sort(TrackSortCriterion.FolderThenName);
                Assert.Equal(new[] { "1.mp3", "2.mp3", "4.mp3" }, Relative(list));
                Assert.Equal(list.Tracks.Select(t => t.File).ToArray(), list.Files);
            }
        }

        [Fact]
        public void RelativePath_SingleFileIsItsName()
        {
            using (var dir = new TempDir())
            {
                var list = NewList();
                list.Add(new[] { Touch(dir, "x", "Folge 1.mp3") });
                Assert.Equal(new[] { "Folge 1.mp3" }, Relative(list));
            }
        }

        [Fact]
        public void DefaultTagReader_ReadsId3TrackNumber_IgnoresBrokenFiles()
        {
            using (var dir = new TempDir())
            {
                string broken = Touch(dir, "broken.mp3");
                var list = new TrackList();
                list.Add(new[] { Fixtures.Mp3, broken });

                Assert.Equal(1, list.Tracks.Single(t => t.File == Fixtures.Mp3).Tags.TrackNumber);
                Assert.Null(list.Tracks.Single(t => t.File == broken).Tags.TrackNumber);
            }
        }

        [Theory]
        [InlineData("a.mp3", true)]
        [InlineData("A.OGG", true)]
        [InlineData("a.wav", false)]
        [InlineData("mp3", false)]
        public void IsAudioFile(string name, bool expected)
        {
            Assert.Equal(expected, TrackList.IsAudioFile(name));
        }
    }
}
