namespace LighterSpotify
{
    public class PlaybackState
    {
        public bool HasItem;
        public bool IsPlaying;
        public int ProgressMs;
        public int DurationMs;
        public string Title = "";
        public string Artists = "";
        public string ImageUrl;
        public string DeviceId;
        public string DeviceName;
        public int Volume = -1;
    }

    public class TrackItem
    {
        public string Uri { get; set; }
        public string Title { get; set; }
        public string Artists { get; set; }
        public string ImageUrl { get; set; }
    }

    public class DeviceInfo
    {
        public string Id;
        public string Name;
        public bool IsActive;
    }
}
