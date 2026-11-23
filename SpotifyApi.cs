using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LighterSpotify
{
    public class SpotifyApi
    {
        const string BaseUrl = "https://api.spotify.com/v1/";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        SpotifyAuth auth;
        public string LastError = "";
        public DateTime BlockedUntil = DateTime.MinValue;

        public SpotifyApi(SpotifyAuth auth)
        {
            this.auth = auth;
        }

        public bool IsBlocked
        {
            get { return DateTime.Now < BlockedUntil; }
        }

        public Task<bool> PauseAsync() { return SendSimpleAsync(HttpMethod.Put, "me/player/pause", null); }
        public Task<bool> ResumeAsync() { return SendSimpleAsync(HttpMethod.Put, "me/player/play", null); }
        public Task<bool> NextAsync() { return SendSimpleAsync(HttpMethod.Post, "me/player/next", null); }
        public Task<bool> PreviousAsync() { return SendSimpleAsync(HttpMethod.Post, "me/player/previous", null); }
        public Task<bool> SeekAsync(int ms) { return SendSimpleAsync(HttpMethod.Put, "me/player/seek?position_ms=" + ms, null); }
        public Task<bool> SetVolumeAsync(int percent) { return SendSimpleAsync(HttpMethod.Put, "me/player/volume?volume_percent=" + percent, null); }
        public Task<bool> AddToQueueAsync(string uri) { return SendSimpleAsync(HttpMethod.Post, "me/player/queue?uri=" + Uri.EscapeDataString(uri), null); }

        public Task<bool> TransferAsync(string deviceId, bool play)
        {
            string json = JsonSerializer.Serialize(new { device_ids = new[] { deviceId }, play = play });
            return SendSimpleAsync(HttpMethod.Put, "me/player", json);
        }

        public Task<bool> PlayUrisAsync(string deviceId, List<string> uris)
        {
            string json = JsonSerializer.Serialize(new { uris = uris });
            return SendSimpleAsync(HttpMethod.Put, "me/player/play?device_id=" + Uri.EscapeDataString(deviceId), json);
        }

        public async Task<PlaybackState> GetPlaybackAsync()
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, "me/player", null);
            if (response == null) return null;

            PlaybackState state = new PlaybackState();
            if (response.StatusCode == HttpStatusCode.NoContent) return state;

            string text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return state;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement root = doc.RootElement;
                    state.IsPlaying = GetBool(root, "is_playing");
                    state.ProgressMs = GetInt(root, "progress_ms");

                    JsonElement device;
                    if (root.TryGetProperty("device", out device) && device.ValueKind == JsonValueKind.Object)
                    {
                        state.DeviceId = GetString(device, "id");
                        state.DeviceName = GetString(device, "name");
                        JsonElement volume;
                        if (device.TryGetProperty("volume_percent", out volume) && volume.ValueKind == JsonValueKind.Number)
                            state.Volume = volume.GetInt32();
                    }

                    JsonElement item;
                    if (root.TryGetProperty("item", out item) && item.ValueKind == JsonValueKind.Object)
                    {
                        state.HasItem = true;
                        state.Title = GetString(item, "name") ?? "";
                        state.DurationMs = GetInt(item, "duration_ms");
                        state.Artists = JoinArtists(item);
                        JsonElement album;
                        if (item.TryGetProperty("album", out album)) state.ImageUrl = PickImage(album, 150);
                    }
                }
            }
            catch (JsonException)
            {
                LastError = "Couldn't read player state.";
                return null;
            }
            return state;
        }

        public async Task<List<DeviceInfo>> GetDevicesAsync()
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, "me/player/devices", null);
            if (response == null) return null;

            List<DeviceInfo> devices = new List<DeviceInfo>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    JsonElement list;
                    if (doc.RootElement.TryGetProperty("devices", out list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement d in list.EnumerateArray())
                        {
                            devices.Add(new DeviceInfo
                            {
                                Id = GetString(d, "id"),
                                Name = GetString(d, "name"),
                                IsActive = GetBool(d, "is_active")
                            });
                        }
                    }
                }
            }
            catch (JsonException) { }
            return devices;
        }

        public async Task<List<TrackItem>> SearchTracksAsync(string query, int offset)
        {
            // Development Mode caps search at 10 results per request
            string path = "search?type=track&limit=10&offset=" + offset + "&q=" + Uri.EscapeDataString(query);
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null);
            if (response == null) return null;

            List<TrackItem> tracks = new List<TrackItem>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    JsonElement trackBlock;
                    JsonElement items;
                    if (doc.RootElement.TryGetProperty("tracks", out trackBlock) && trackBlock.TryGetProperty("items", out items))
                    {
                        foreach (JsonElement t in items.EnumerateArray())
                        {
                            if (t.ValueKind != JsonValueKind.Object) continue;
                            TrackItem track = new TrackItem
                            {
                                Uri = GetString(t, "uri"),
                                Title = GetString(t, "name") ?? "",
                                Artists = JoinArtists(t)
                            };
                            JsonElement album;
                            if (t.TryGetProperty("album", out album)) track.ImageUrl = PickImage(album, 60);
                            if (track.Uri != null) tracks.Add(track);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                LastError = "Couldn't read search results.";
                return null;
            }
            return tracks;
        }

        async Task<bool> SendSimpleAsync(HttpMethod method, string path, string json)
        {
            HttpResponseMessage response = await SendAsync(method, path, json);
            return response != null;
        }

        // Returns null on any failure and sets LastError
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string json)
        {
            LastError = "";
            if (IsBlocked)
            {
                LastError = "Spotify asked to slow down. Retrying in " + (int)(BlockedUntil - DateTime.Now).TotalSeconds + "s.";
                return null;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                string token = await auth.GetAccessTokenAsync(attempt == 1);
                if (token == null)
                {
                    LastError = "Not logged in.";
                    return null;
                }

                HttpRequestMessage request = new HttpRequestMessage(method, BaseUrl + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                else if (method != HttpMethod.Get) request.Content = new StringContent("");

                HttpResponseMessage response;
                try
                {
                    response = await http.SendAsync(request);
                }
                catch (Exception)
                {
                    LastError = "Can't reach Spotify. Check your connection.";
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;

                if ((int)response.StatusCode == 429)
                {
                    TimeSpan wait = TimeSpan.FromSeconds(30);
                    if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                        wait = response.Headers.RetryAfter.Delta.Value;
                    BlockedUntil = DateTime.Now + wait;
                    LastError = "Spotify asked to slow down. Waiting " + (int)wait.TotalSeconds + "s.";
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LastError = await ReadErrorAsync(response);
                    return null;
                }
                return response;
            }

            LastError = "Login expired. Log in again.";
            return null;
        }

        static async Task<string> ReadErrorAsync(HttpResponseMessage response)
        {
            try
            {
                string text = await response.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement error;
                    if (doc.RootElement.TryGetProperty("error", out error))
                    {
                        if (error.ValueKind == JsonValueKind.String) return error.GetString();
                        string reason = GetString(error, "reason");
                        if (reason == "NO_ACTIVE_DEVICE") return "No active device. Press the speaker button to play on this PC.";
                        if (reason == "PREMIUM_REQUIRED") return "Spotify Premium is required.";
                        string message = GetString(error, "message");
                        if (!string.IsNullOrEmpty(message)) return message;
                    }
                }
            }
            catch { }
            return "Spotify error " + (int)response.StatusCode + ".";
        }

        static string JoinArtists(JsonElement item)
        {
            JsonElement artists;
            if (!item.TryGetProperty("artists", out artists) || artists.ValueKind != JsonValueKind.Array) return "";

            List<string> names = new List<string>();
            foreach (JsonElement a in artists.EnumerateArray())
            {
                string name = GetString(a, "name");
                if (name != null) names.Add(name);
            }
            return string.Join(", ", names);
        }

        // Picks the smallest image that is at least minWidth wide
        static string PickImage(JsonElement album, int minWidth)
        {
            JsonElement images;
            if (!album.TryGetProperty("images", out images) || images.ValueKind != JsonValueKind.Array) return null;

            string best = null;
            int bestWidth = int.MaxValue;
            string fallback = null;
            foreach (JsonElement img in images.EnumerateArray())
            {
                string url = GetString(img, "url");
                if (url == null) continue;
                if (fallback == null) fallback = url;
                int width = GetInt(img, "width");
                if (width >= minWidth && width < bestWidth)
                {
                    best = url;
                    bestWidth = width;
                }
            }
            return best ?? fallback;
        }

        static string GetString(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String) return value.GetString();
            return null;
        }

        static int GetInt(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number) return value.GetInt32();
            return 0;
        }

        static bool GetBool(JsonElement element, string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.True;
        }
    }
}
