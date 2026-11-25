using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Id3;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using TeddyBench.Core;
using TeddyBench.Core.Tracks;

namespace TeddyBench.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Collects and orders the tracks of a new tonie: more files/folders can be added, the whole list sorted by a
    /// criterion (default: folder, then name), and single tracks moved or removed.
    /// </summary>
    public class TrackSortDialog : DialogBase
    {
        public class Row
        {
            public Track Track { get; set; }
            public string Display { get; set; }
            public string File => Track.File;
        }

        /// <summary>Order of the "Sort by" entries.</summary>
        public static readonly (TrackSortCriterion Criterion, string Label)[] Criteria =
        {
            (TrackSortCriterion.FolderThenName, "Folder, then name"),
            (TrackSortCriterion.Name, "Name"),
            (TrackSortCriterion.Id3TrackNumber, "ID3 track number"),
            (TrackSortCriterion.OrderAdded, "Order added")
        };

        private readonly TrackList Tracks;
        private readonly ObservableCollection<Row> Rows = new ObservableCollection<Row>();
        private readonly ListBox List;
        private readonly ComboBox SortBox;
        private readonly TextBlock Note;
        private readonly Button Ok;

        public string[] Result { get; private set; }

        /// <summary>Plain list of files (e.g. for tests); same as adding them to a new <see cref="TrackList"/>.</summary>
        public TrackSortDialog(string[] files) : this(NewList(files), null)
        {
        }

        public TrackSortDialog(TrackList tracks, string note) : base("Sort tracks")
        {
            Tracks = tracks;
            CanResize = true;
            SizeToContent = SizeToContent.Manual;
            Width = 820;
            Height = 520;

            List = new ListBox
            {
                ItemsSource = Rows,
                SelectionMode = SelectionMode.Multiple,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace"),
                ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<Row>((r, _) => new TextBlock { Text = r?.Display })
            };

            SortBox = new ComboBox
            {
                ItemsSource = Criteria.Select(c => c.Label).ToArray(),
                SelectedIndex = Array.FindIndex(Criteria, c => c.Criterion == tracks.Criterion),
                MinWidth = 180
            };
            SortBox.SelectionChanged += (s, e) =>
            {
                if (SortBox.SelectedIndex >= 0)
                {
                    Tracks.Sort(Criteria[SortBox.SelectedIndex].Criterion);
                    Refresh(new Track[0]);
                }
            };

            var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            sortRow.Children.Add(new TextBlock { Text = "Sort by", VerticalAlignment = VerticalAlignment.Center });
            sortRow.Children.Add(SortBox);

            var hint = Text("The tracks are written in this order. Select tracks and move them with Up/Down; choosing a sort order again replaces manual moves.", 760);
            hint.HorizontalAlignment = HorizontalAlignment.Left;
            hint.Margin = new Thickness(0, 0, 0, 8);

            var top = new StackPanel();
            top.Children.Add(hint);
            top.Children.Add(sortRow);
            DockPanel.SetDock(top, Dock.Top);

            var side = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };
            side.Children.Add(MakeButton("Add files...", AddFiles));
            side.Children.Add(MakeButton("Add folder...", AddFolder));
            side.Children.Add(new Separator());
            side.Children.Add(MakeButton("Up", () => Move(-1)));
            side.Children.Add(MakeButton("Down", () => Move(1)));
            side.Children.Add(MakeButton("Remove", Remove));
            foreach (var b in side.Children.OfType<Button>())
            {
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            DockPanel.SetDock(side, Dock.Right);

            Ok = MakeButton("OK", () => { Result = Tracks.Files; Close(); }, isDefault: true);
            var buttons = ButtonRow(Ok, MakeButton("Cancel", Close, isCancel: true));
            Note = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 16, 16, 0), Opacity = 0.75 };
            var bottom = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            bottom.Children.Add(Note);
            DockPanel.SetDock(bottom, Dock.Bottom);

            var panel = new DockPanel { Margin = new Thickness(16) };
            panel.Children.Add(top);
            panel.Children.Add(bottom);
            panel.Children.Add(side);
            panel.Children.Add(List);
            Content = panel;

            SetNote(note);
            Refresh(new Track[0]);
        }

        private static TrackList NewList(string[] files)
        {
            var list = new TrackList();
            list.Add(files);
            return list;
        }

        /// <summary>Adds files/folders (sorted in by the current criterion); public for tests.</summary>
        public void AddPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }
            var result = Tracks.Add(paths);
            SetNote(ViewModels.MainWindowViewModel.SkippedNote(result));
            Refresh(List.SelectedItems.Cast<Row>().Select(r => r.Track).ToList());
        }

        private async void AddFiles()
        {
            AddPaths(await Services.DialogService.PickFiles(this, "Add files", true, "MP3/Ogg files", "*.mp3", "*.MP3", "*.ogg", "*.OGG"));
        }

        private async void AddFolder()
        {
            string folder = await Services.DialogService.PickFolder(this, "Add folder");
            if (folder != null)
            {
                AddPaths(new[] { folder });
            }
        }

        private void SetNote(string note)
        {
            Note.Text = note ?? "";
        }

        private void Move(int direction)
        {
            var selected = List.SelectedItems.Cast<Row>().Select(r => r.Track).ToList();
            if (Tracks.Move(selected, direction))
            {
                Refresh(selected);
            }
        }

        private void Remove()
        {
            Tracks.Remove(List.SelectedItems.Cast<Row>().Select(r => r.Track).ToList());
            Refresh(new Track[0]);
        }

        /// <summary>Rebuilds the rows (numbers and relative paths change) and keeps the given tracks selected.</summary>
        private void Refresh(IEnumerable<Track> select)
        {
            var keep = new HashSet<Track>(select);
            Rows.Clear();
            for (int i = 0; i < Tracks.Tracks.Count; i++)
            {
                var t = Tracks.Tracks[i];
                string id3 = string.IsNullOrEmpty(t.Tags.Description) ? "" : "   (" + t.Tags.Description + ")";
                Rows.Add(new Row { Track = t, Display = (i + 1).ToString().PadLeft(3) + "   " + Tracks.RelativePath(t) + id3 });
            }
            foreach (var row in Rows.Where(r => keep.Contains(r.Track)))
            {
                List.SelectedItems.Add(row);
            }
            Ok.IsEnabled = Rows.Count > 0;
        }
    }

    public class AboutDialog : DialogBase
    {
        public AboutDialog(string version) : base("About")
        {
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            using (var stream = AssetLoader.Open(new Uri("avares://TeddyBench/Assets/custom.png")))
            {
                panel.Children.Add(new Image { Source = Bitmap.DecodeToWidth(stream, 96), Width = 96, Height = 96 });
            }
            panel.Children.Add(new TextBlock { Text = "TeddyBench", FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = "v" + version, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = "by Team RevvoX", HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(Link("Icon source", "https://www.flaticon.com/de/kostenloses-icon/hund_2829818", "https://www.flaticon.com/de/kostenloses-icon/fragen_1923795"));
            panel.Children.Add(Link("visit our telegram channel", "https://t.me/toniebox_reverse_engineering"));
            panel.Children.Add(ButtonRow(MakeButton("OK", Close, isDefault: true, isCancel: true)));
            Content = panel;
        }

        private static Control Link(string text, params string[] urls)
        {
            var button = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#1E88E5")),
                Padding = new Thickness(4, 0),
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand)
            };
            button.Click += (s, e) =>
            {
                foreach (var url in urls)
                {
                    try
                    {
                        PlatformServices.Shell.OpenUrl(url);
                    }
                    catch (Exception)
                    {
                    }
                }
            };
            return button;
        }
    }

    /// <summary>Proxmark3 antenna measurement: LF sweep plot and the HF voltage rating.</summary>
    public class AntennaPlotWindow : DialogBase
    {
        public AntennaPlotWindow(Proxmark3.MeasurementResult result) : base("Proxmark 3 Antenna Plot")
        {
            CanResize = true;
            SizeToContent = SizeToContent.Manual;
            Width = 820;
            Height = 520;

            var plot = new AvaPlot();
            plot.Plot.Add.Scatter(result.GetFrequencieskHz(), result.GetVoltages());
            plot.Plot.XLabel("Frequency [kHz]");
            plot.Plot.YLabel("Amplitude [V]");
            plot.Plot.Title("LF Antenna Plot (no relevance)");
            plot.Plot.Axes.SetLimits(46.0, 600.0, 0, 65.0);
            plot.Refresh();

            var (rating, color) = RateHf(result.vHF);
            var values = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), Margin = new Thickness(16, 16, 8, 16) };
            int row = 0;
            void Add(string label, string value, IBrush background = null)
            {
                values.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = new TextBlock { Text = label, Margin = new Thickness(0, 2, 12, 2) };
                var valueText = new TextBlock { Text = value };
                if (background != null)
                {
                    /* readable on the rating colors in light and dark theme */
                    valueText.Foreground = Brushes.Black;
                }
                var v = new Border { Background = background, Padding = new Thickness(4, 0), Child = valueText };
                Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
                values.Children.Add(l); values.Children.Add(v);
                row++;
            }
            string V(double v) => v.ToString("0.00", CultureInfo.InvariantCulture) + " V";

            Add("HF:", "");
            Add("Voltage at 13.56 MHz:", V(result.vHF) + " (" + rating + ")", color);
            Add("", "");
            Add("LF:", "");
            Add("Voltage at 125 kHz:", V(result.vLF125));
            Add("Voltage at 134 kHz:", V(result.vLF134));
            Add("Optimal LF Frequency:", (result.GetPeakFrequency() / 1000.0f).ToString("0.00", CultureInfo.InvariantCulture) + " kHz");
            Add("Antenna voltage:", V(result.peakV));

            var note = new TextBlock { Text = "TeddyBench uses the HF (13.56 MHz) antenna only.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 8, 16), MaxWidth = 300, Classes = { "hint" } };
            var left = new StackPanel();
            left.Children.Add(values);
            left.Children.Add(note);
            DockPanel.SetDock(left, Dock.Left);
            var panel = new DockPanel();
            panel.Children.Add(left);
            panel.Children.Add(plot);
            Content = panel;
        }

        /// <summary>Same thresholds as the WinForms version.</summary>
        public static (string rating, IBrush color) RateHf(double vHF)
        {
            if (vHF > 45) return ("critical", Brushes.Red);
            if (vHF > 40) return ("danger", Brushes.Yellow);
            if (vHF > 33) return ("wow", Brushes.Green);
            if (vHF > 25) return ("good", Brushes.GreenYellow);
            if (vHF > 20) return ("weak", Brushes.Yellow);
            if (vHF > 10) return ("meh", Brushes.Orange);
            return ("bad", Brushes.Red);
        }
    }

    /// <summary>Debug log (Options / Debug window). Hides instead of closing.</summary>
    public class LogWindow : Window
    {
        private readonly TextBox Box;
        private DateTime LastShown = DateTime.MinValue;
        private readonly DispatcherTimer Timer;

        public bool AllowClose { get; set; }

        public LogWindow()
        {
            Title = "TeddyBench Log";
            Width = 760;
            Height = 420;
            Box = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace"),
                FontSize = 12
            };
            Content = Box;

            Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (s, e) => Refresh());
            Timer.Start();
            Closing += (s, e) =>
            {
                if (!AllowClose)
                {
                    e.Cancel = true;
                    Hide();
                }
                else
                {
                    Timer.Stop();
                }
            };
        }

        private void Refresh()
        {
            DateTime last = Logger.LastUpdate;
            if (!IsVisible || last <= LastShown)
            {
                return;
            }
            Box.Text = Logger.GetText();
            Box.CaretIndex = Box.Text.Length;
            LastShown = last;
        }
    }
}
