using System;
using Windows.Media;
using Windows.Storage.Streams;

namespace LighterSpotify
{
    // Shows the player in the Windows media flyout (speaker/volume controls) and receives media keys
    public class MediaSession
    {
        SystemMediaTransportControls smtc;
        string lastTrackKey;

        public event Action PlayPressed;
        public event Action PausePressed;
        public event Action NextPressed;
        public event Action PreviousPressed;

        public bool Start(IntPtr windowHandle)
        {
            try
            {
                smtc = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
            }
            catch
            {
                smtc = null;
                return false;
            }

            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            smtc.ButtonPressed += OnButtonPressed;
            return true;
        }

        public void Update(PlaybackState state)
        {
            if (smtc == null) return;

            if (state == null || !state.HasItem)
            {
                smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                if (lastTrackKey != null)
                {
                    smtc.DisplayUpdater.ClearAll();
                    smtc.DisplayUpdater.Update();
                    lastTrackKey = null;
                }
                return;
            }

            smtc.PlaybackStatus = state.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

            string trackKey = state.Title + "|" + state.Artists + "|" + state.ImageUrl;
            if (trackKey != lastTrackKey)
            {
                lastTrackKey = trackKey;
                SystemMediaTransportControlsDisplayUpdater updater = smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = state.Title;
                updater.MusicProperties.Artist = state.Artists;
                updater.Thumbnail = state.ImageUrl != null ? RandomAccessStreamReference.CreateFromUri(new Uri(state.ImageUrl)) : null;
                updater.Update();
            }

            SystemMediaTransportControlsTimelineProperties timeline = new SystemMediaTransportControlsTimelineProperties();
            timeline.StartTime = TimeSpan.Zero;
            timeline.MinSeekTime = TimeSpan.Zero;
            timeline.Position = TimeSpan.FromMilliseconds(state.ProgressMs);
            timeline.MaxSeekTime = TimeSpan.FromMilliseconds(state.DurationMs);
            timeline.EndTime = TimeSpan.FromMilliseconds(state.DurationMs);
            smtc.UpdateTimelineProperties(timeline);
        }

        public void Stop()
        {
            if (smtc == null) return;
            try
            {
                smtc.ButtonPressed -= OnButtonPressed;
                smtc.DisplayUpdater.ClearAll();
                smtc.DisplayUpdater.Update();
                smtc.IsEnabled = false;
            }
            catch { }
            smtc = null;
        }

        void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            SystemMediaTransportControlsButton button = args.Button;

            // Windows raises this on a background thread, so hop back to the UI thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (button == SystemMediaTransportControlsButton.Play && PlayPressed != null) PlayPressed();
                if (button == SystemMediaTransportControlsButton.Pause && PausePressed != null) PausePressed();
                if (button == SystemMediaTransportControlsButton.Next && NextPressed != null) NextPressed();
                if (button == SystemMediaTransportControlsButton.Previous && PreviousPressed != null) PreviousPressed();
            }));
        }
    }
}
