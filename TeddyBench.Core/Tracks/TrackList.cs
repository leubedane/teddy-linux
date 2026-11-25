using Id3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TeddyBench.Core.Tracks
{
    /// <summary>How <see cref="TrackList.Sort"/> orders the whole list. See GLOSSARY.md "Sort criterion".</summary>
    public enum TrackSortCriterion
    {
        FolderThenName,
        Name,
        Id3TrackNumber,
        OrderAdded
    }

    /// <summary>The ID3 information shown for and sorted by a track.</summary>
    public class TrackTags
    {
        public int? TrackNumber { get; set; }
        /// <summary>"Artist - Title", or null.</summary>
        public string Description { get; set; }
    }

    /// <summary>One MP3/Ogg file of a tonie.</summary>
    public class Track
    {
        public string File { get; internal set; }
        /// <summary>Position in the order the tracks were added.</summary>
        public int AddedIndex { get; internal set; }
        public TrackTags Tags { get; internal set; }
    }

    public class TrackAddResult
    {
        public int Added { get; internal set; }
        /// <summary>Non-audio files and files already in the list.</summary>
        public int Skipped { get; internal set; }
        /// <summary>Folders that contained no MP3/Ogg files at all.</summary>
        public List<string> EmptyFolders { get; } = new List<string>();
    }

    /// <summary>
    /// The tracks of one tonie that is about to be encoded: collects files and folders (recursively, MP3/Ogg only,
    /// no duplicates) and holds their order.
    /// </summary>
    public class TrackList
    {
        private readonly List<Track> Items = new List<Track>();
        private readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<string, TrackTags> ReadTags;
        private int NextIndex;

        /// <param name="readTags">Reads the ID3 tags of a file; defaults to the ID3 library (MP3 only).</param>
        public TrackList(Func<string, TrackTags> readTags = null)
        {
            ReadTags = readTags ?? ReadId3Tags;
        }

        public IReadOnlyList<Track> Tracks => Items;

        public string[] Files => Items.Select(t => t.File).ToArray();

        /// <summary>The criterion last applied; new tracks are sorted in by it.</summary>
        public TrackSortCriterion Criterion { get; private set; } = TrackSortCriterion.FolderThenName;

        public static bool IsAudioFile(string file)
        {
            string ext = Path.GetExtension(file);
            return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds files and folders, then re-applies <see cref="Criterion"/>.</summary>
        public TrackAddResult Add(IEnumerable<string> paths)
        {
            var result = new TrackAddResult();
            foreach (string path in paths.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsAudioFile).ToList();
                    if (files.Count == 0)
                    {
                        result.EmptyFolders.Add(path);
                        continue;
                    }
                    files.Sort(CompareFolderThenName);
                    foreach (string file in files)
                    {
                        AddFile(file, result);
                    }
                }
                else if (IsAudioFile(path))
                {
                    AddFile(path, result);
                }
                else
                {
                    result.Skipped++;
                }
            }
            Sort(Criterion);
            return result;
        }

        private void AddFile(string file, TrackAddResult result)
        {
            string full = Path.GetFullPath(file);
            if (!Known.Add(full))
            {
                result.Skipped++;
                return;
            }
            Items.Add(new Track { File = full, AddedIndex = NextIndex++, Tags = ReadTags(full) ?? new TrackTags() });
            result.Added++;
        }

        public void Remove(IEnumerable<Track> tracks)
        {
            foreach (var track in tracks.ToList())
            {
                if (Items.Remove(track))
                {
                    Known.Remove(track.File);
                }
            }
        }

        /// <summary>Moves the given tracks one step up (-1) or down (+1); false if nothing moved.</summary>
        public bool Move(IEnumerable<Track> tracks, int direction)
        {
            var indices = tracks.Select(t => Items.IndexOf(t)).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
            if (indices.Count == 0 || (direction < 0 && indices.First() == 0) || (direction > 0 && indices.Last() == Items.Count - 1))
            {
                return false;
            }
            foreach (int index in direction < 0 ? indices : Enumerable.Reverse(indices))
            {
                var track = Items[index];
                Items.RemoveAt(index);
                Items.Insert(index + direction, track);
            }
            return true;
        }

        /// <summary>Orders the whole list (replaces manual moves).</summary>
        public void Sort(TrackSortCriterion criterion)
        {
            Criterion = criterion;
            Comparison<Track> compare;
            switch (criterion)
            {
                case TrackSortCriterion.Name:
                    compare = (a, b) => Chain(NaturalStringComparer.Instance.Compare(Path.GetFileName(a.File), Path.GetFileName(b.File)), CompareFolderThenName(a.File, b.File));
                    break;
                case TrackSortCriterion.Id3TrackNumber:
                    compare = (a, b) => Chain(CompareTrackNumbers(a.Tags.TrackNumber, b.Tags.TrackNumber),
                        Chain(NaturalStringComparer.Instance.Compare(Path.GetFileName(a.File), Path.GetFileName(b.File)), CompareFolderThenName(a.File, b.File)));
                    break;
                case TrackSortCriterion.OrderAdded:
                    compare = (a, b) => a.AddedIndex.CompareTo(b.AddedIndex);
                    break;
                default:
                    compare = (a, b) => CompareFolderThenName(a.File, b.File);
                    break;
            }
            /* List.Sort is not stable, but all comparisons above are total orders on distinct files */
            Items.Sort(compare);
        }

        private static int Chain(int first, int second) => first != 0 ? first : second;

        private static int CompareTrackNumbers(int? a, int? b)
        {
            if (a.HasValue && b.HasValue)
            {
                return a.Value.CompareTo(b.Value);
            }
            return a.HasValue ? -1 : b.HasValue ? 1 : 0;
        }

        /// <summary>Folder path segment by segment (natural order; a folder's own files before its subfolders), then file name.</summary>
        private static int CompareFolderThenName(string a, string b)
        {
            string[] dirA = Segments(Path.GetDirectoryName(a));
            string[] dirB = Segments(Path.GetDirectoryName(b));
            for (int i = 0; i < Math.Min(dirA.Length, dirB.Length); i++)
            {
                int result = NaturalStringComparer.Instance.Compare(dirA[i], dirB[i]);
                if (result != 0)
                {
                    return result;
                }
            }
            if (dirA.Length != dirB.Length)
            {
                return dirA.Length.CompareTo(dirB.Length);
            }
            return NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b));
        }

        private static string[] Segments(string dir)
        {
            return (dir ?? "").Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The track's path below the deepest folder that contains all tracks (just the file name for a single folder).</summary>
        public string RelativePath(Track track)
        {
            string[] common = null;
            foreach (var t in Items)
            {
                string[] dir = Segments(Path.GetDirectoryName(t.File));
                if (common == null)
                {
                    common = dir;
                    continue;
                }
                int n = 0;
                while (n < common.Length && n < dir.Length && common[n] == dir[n])
                {
                    n++;
                }
                if (n < common.Length)
                {
                    common = common.Take(n).ToArray();
                }
            }

            string[] own = Segments(Path.GetDirectoryName(track.File));
            int skip = common == null ? own.Length : Math.Min(common.Length, own.Length);
            return string.Join(Path.DirectorySeparatorChar.ToString(), own.Skip(skip).Concat(new[] { Path.GetFileName(track.File) }));
        }

        private static TrackTags ReadId3Tags(string file)
        {
            try
            {
                using (var mp3 = new Mp3(file, Mp3Permissions.Read))
                {
                    var tag = mp3.GetAllTags().FirstOrDefault(t => t.Track.IsAssigned);
                    if (tag == null)
                    {
                        return new TrackTags();
                    }
                    return new TrackTags { TrackNumber = tag.Track.Value, Description = tag.Artists + " - " + tag.Title };
                }
            }
            catch (Exception)
            {
                return new TrackTags();
            }
        }
    }
}
