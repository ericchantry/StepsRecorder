using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace OpenSteps.App;

/// <summary>
/// Plays a session's video next to its keyboard/mouse timeline. Clicking a timeline row seeks the video;
/// while playing, the row for the current moment is highlighted.
/// </summary>
public partial class RecordingReviewWindow : Window
{
    private readonly RecordingSession _session;
    private readonly List<TimelineEntry> _entries;
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _positionTimer;
    private RecordingSegment? _segment;
    private (int Segment, double Offset)? _pendingSeek;
    private string _filter = "All";
    private bool _isPlaying;
    private bool _updatingSlider;
    private bool _updatingSelection;
    private bool _mediaReady;

    public RecordingReviewWindow(RecordingSession session)
    {
        _session = session;
        InitializeComponent();

        SessionTitleText.Text = session.Title;
        _entries = InputTimelineBuilder.BuildWithSteps(session.InputEvents, session.Steps).ToList();
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = item => item is TimelineEntry entry && MatchesFilter(entry);
        TimelineList.ItemsSource = _view;

        var segments = session.Segments.OrderBy(s => s.Number).ToList();
        if (segments.Count == 0 && _entries.Count > 0)
        {
            segments.Add(new RecordingSegment { Number = 1 });
        }

        SegmentPicker.ItemsSource = segments.Select(s => new SegmentOption(s)).ToList();
        SegmentPicker.Visibility = segments.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        var typedTextNote = session.Segments.Any(s => s.TypedTextCaptured)
            ? "Typed text is shown where it was recorded; password and other sensitive fields are always masked."
            : "Typed characters were not recorded for this session; only key counts, keys and shortcuts are shown.";
        TimelineFooter.Text = $"{session.InputEvents.Count} input events. {typedTextNote} Click a row to jump to that moment.";

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _positionTimer.Tick += (_, _) => SyncFromPlayer();

        PreviewKeyDown += RecordingReviewWindow_PreviewKeyDown;
        Closed += (_, _) =>
        {
            _positionTimer.Stop();
            Player.Close();
        };

        if (segments.Count > 0)
        {
            SegmentPicker.SelectedIndex = 0;
        }
    }

    public void SeekTo(int segmentNumber, double offsetSeconds)
    {
        _pendingSeek = (segmentNumber, offsetSeconds);
        if (_segment?.Number != segmentNumber)
        {
            SelectSegment(segmentNumber);
            return;
        }

        ApplyPendingSeek();
    }

    private void SelectSegment(int number)
    {
        if (SegmentPicker.ItemsSource is not IEnumerable<SegmentOption> options)
        {
            return;
        }

        var match = options.FirstOrDefault(o => o.Segment.Number == number);
        if (match is not null)
        {
            SegmentPicker.SelectedItem = match;
        }
    }

    private void SegmentPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SegmentPicker.SelectedItem is SegmentOption option)
        {
            LoadSegment(option.Segment);
        }
    }

    private void LoadSegment(RecordingSegment segment)
    {
        PausePlayback();
        _segment = segment;
        _mediaReady = false;
        Player.Close();
        PositionSlider.Value = 0;
        PositionSlider.Maximum = Math.Max(1, segment.DurationSeconds);

        if (segment.HasVideo)
        {
            NoVideoText.Visibility = Visibility.Collapsed;
            Player.Source = new Uri(segment.VideoPath!, UriKind.Absolute);
            Player.Play();
            Player.Pause();
        }
        else
        {
            Player.Source = null;
            NoVideoText.Text = segment.VideoError is { } error
                ? $"No video for this recording.\n{error}"
                : "No video was recorded for this recording. The input timeline is still available.";
            NoVideoText.Visibility = Visibility.Visible;
            ApplyPendingSeek();
        }

        UpdateTimeText();
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _mediaReady = true;
        if (Player.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = Math.Max(0.1, Player.NaturalDuration.TimeSpan.TotalSeconds);
        }

        ApplyPendingSeek();
        UpdateTimeText();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        PausePlayback();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _mediaReady = false;
        NoVideoText.Text = $"This video could not be played: {e.ErrorException?.Message}\nThe file is at {_segment?.VideoPath}";
        NoVideoText.Visibility = Visibility.Visible;
    }

    private void ApplyPendingSeek()
    {
        if (_pendingSeek is not { } seek || _segment?.Number != seek.Segment)
        {
            return;
        }

        if (_segment.HasVideo && !_mediaReady)
        {
            return;
        }

        _pendingSeek = null;
        SeekVideo(Math.Max(0, seek.Offset - 0.3));
        HighlightEntryAt(seek.Segment, seek.Offset);
    }

    private void SeekVideo(double seconds)
    {
        if (_segment?.HasVideo == true && _mediaReady)
        {
            Player.Position = TimeSpan.FromSeconds(seconds);
        }

        _updatingSlider = true;
        PositionSlider.Value = Math.Min(PositionSlider.Maximum, seconds);
        _updatingSlider = false;
        UpdateTimeText();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (_segment?.HasVideo != true || !_mediaReady)
        {
            return;
        }

        if (_isPlaying)
        {
            PausePlayback();
            return;
        }

        if (Player.NaturalDuration.HasTimeSpan && Player.Position >= Player.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(200))
        {
            Player.Position = TimeSpan.Zero;
        }

        Player.Play();
        _isPlaying = true;
        PlayPauseButton.Content = "";
        _positionTimer.Start();
    }

    private void PausePlayback()
    {
        if (_mediaReady)
        {
            Player.Pause();
        }

        _isPlaying = false;
        PlayPauseButton.Content = "";
        _positionTimer.Stop();
        UpdateTimeText();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        SeekVideo(Math.Max(0, PositionSlider.Value - 5));
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider)
        {
            return;
        }

        if (_segment?.HasVideo == true && _mediaReady)
        {
            Player.Position = TimeSpan.FromSeconds(e.NewValue);
        }

        if (_segment is not null)
        {
            HighlightEntryAt(_segment.Number, e.NewValue);
        }

        UpdateTimeText();
    }

    private void SpeedPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedPicker.SelectedItem is ComboBoxItem { Tag: string tag } && double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed) && Player is not null)
        {
            Player.SpeedRatio = speed;
        }
    }

    private void SyncFromPlayer()
    {
        if (!_mediaReady || _segment is null)
        {
            return;
        }

        var seconds = Player.Position.TotalSeconds;
        _updatingSlider = true;
        PositionSlider.Value = Math.Min(PositionSlider.Maximum, seconds);
        _updatingSlider = false;
        HighlightEntryAt(_segment.Number, seconds);
        UpdateTimeText();
    }

    private void HighlightEntryAt(int segment, double seconds)
    {
        var current = _view.Cast<TimelineEntry>()
            .Where(entry => entry.Segment == segment && entry.OffsetSeconds <= seconds + 0.05)
            .LastOrDefault();
        if (current is null || ReferenceEquals(TimelineList.SelectedItem, current))
        {
            return;
        }

        _updatingSelection = true;
        TimelineList.SelectedItem = current;
        TimelineList.ScrollIntoView(current);
        _updatingSelection = false;
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || TimelineList.SelectedItem is not TimelineEntry entry)
        {
            return;
        }

        PausePlayback();
        SeekTo(entry.Segment, entry.OffsetSeconds);
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _filter = tag;
            _view?.Refresh();
        }
    }

    private bool MatchesFilter(TimelineEntry entry)
    {
        return _filter switch
        {
            "Step" => entry.IsStep,
            "Keyboard" => entry.IsKeyboard,
            "Mouse" => entry.IsMouse,
            _ => true
        };
    }

    private void UpdateTimeText()
    {
        var position = PositionSlider.Value;
        var total = PositionSlider.Maximum;
        TimeText.Text = $"{TimeFormat.Format(position)} / {TimeFormat.Format(total)}";
    }

    private void RecordingReviewWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                TogglePlayback();
                e.Handled = true;
                break;
            case Key.Left when Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is not ListBoxItem:
                SeekVideo(Math.Max(0, PositionSlider.Value - 5));
                e.Handled = true;
                break;
            case Key.Right when Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is not ListBoxItem:
                SeekVideo(Math.Min(PositionSlider.Maximum, PositionSlider.Value + 5));
                e.Handled = true;
                break;
        }
    }

    private sealed class SegmentOption(RecordingSegment segment)
    {
        public RecordingSegment Segment { get; } = segment;

        public string Label => $"Recording {Segment.Number} ({TimeFormat.Format(Segment.DurationSeconds)}){(Segment.HasVideo ? string.Empty : " – no video")}";
    }
}
