using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LighterSpotify
{
    public class SpotifyAuth
    {
        public const string RedirectUri = "http://127.0.0.1:8888/callback";
        const int Port = 8888;
        const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        static string TokenPath
        {
            get { return Path.Combine(Settings.Folder, "token.bin"); }
        }

        string clientId;
        string accessToken;
        string refreshToken;
        DateTime expiresAt = DateTime.MinValue;

        // Only one token refresh at a time, so two requests can't burn the same refresh token
        SemaphoreSlim tokenLock = new SemaphoreSlim(1, 1);

        public string LastError = "";

        public SpotifyAuth(string clientId)
        {
            this.clientId = clientId;
            refreshToken = LoadRefreshToken();
        }

        public bool HasToken
        {
            get { return !string.IsNullOrEmpty(refreshToken); }
        }

        public async Task<string> GetAccessTokenAsync(bool forceRefresh)
        {
            await tokenLock.WaitAsync();
            try
            {
                if (!forceRefresh && accessToken != null && DateTime.Now < expiresAt) return accessToken;
                if (!HasToken) return null;

                var form = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", refreshToken },
                    { "client_id", clientId }
                };
                bool ok = await RequestTokenAsync(form);
                return ok ? accessToken : null;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        public async Task<bool> LoginAsync()
        {
            LastError = "";
            string verifier = RandomBase64Url(64);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = RandomBase64Url(16);

            string url = "https://accounts.spotify.com/authorize"
                + "?response_type=code"
                + "&client_id=" + Uri.EscapeDataString(clientId)
                + "&scope=" + Uri.EscapeDataString(Scopes)
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&code_challenge_method=S256"
                + "&code_challenge=" + challenge
                + "&state=" + state;

            TcpListener listener = new TcpListener(IPAddress.Loopback, Port);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                LastError = "Port " + Port + " is busy. Close the app using it and try again.";
                return false;
            }

            string code = null;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Task timeout = Task.Delay(TimeSpan.FromMinutes(3));

                while (code == null)
                {
                    Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
                    if (await Task.WhenAny(acceptTask, timeout) == timeout)
                    {
                        LastError = "Timed out.";
                        return false;
                    }

                    using (TcpClient client = await acceptTask)
                    {
                        NetworkStream stream = client.GetStream();
                        StreamReader reader = new StreamReader(stream);
                        string requestLine = await reader.ReadLineAsync();
                        string query = GetCallbackQuery(requestLine);

                        if (query == null)
                        {
                            await WriteResponseAsync(stream, "404 Not Found", "");
                            continue;
                        }

                        string error = GetParam(query, "error");
                        if (error != null || GetParam(query, "state") != state)
                        {
                            await WriteResponseAsync(stream, "200 OK", Page("Login was cancelled. You can close this tab."));
                            LastError = error ?? "State mismatch.";
                            return false;
                        }

                        code = GetParam(query, "code");
                        await WriteResponseAsync(stream, "200 OK", Page("Logged in. You can close this tab."));
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
            finally
            {
                listener.Stop();
            }

            var form = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", RedirectUri },
                { "client_id", clientId },
                { "code_verifier", verifier }
            };
            return await RequestTokenAsync(form);
        }

        public static void DeleteSavedToken()
        {
            try
            {
                if (File.Exists(TokenPath)) File.Delete(TokenPath);
            }
            catch { }
        }

        async Task<bool> RequestTokenAsync(Dictionary<string, string> form)
        {
            try
            {
                HttpResponseMessage response = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastError = "Spotify login error (" + (int)response.StatusCode + ").";
                    if ((int)response.StatusCode == 400 && form["grant_type"] == "refresh_token")
                    {
                        refreshToken = null;
                        DeleteSavedToken();
                    }
                    return false;
                }

                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement root = doc.RootElement;
                    accessToken = root.GetProperty("access_token").GetString();
                    int expiresIn = root.GetProperty("expires_in").GetInt32();
                    expiresAt = DateTime.Now.AddSeconds(expiresIn - 60);

                    JsonElement newRefresh;
                    if (root.TryGetProperty("refresh_token", out newRefresh) && newRefresh.ValueKind == JsonValueKind.String)
                    {
                        refreshToken = newRefresh.GetString();
                        SaveRefreshToken(refreshToken);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                LastError = "Can't reach Spotify.";
                return false;
            }
        }

        static string GetCallbackQuery(string requestLine)
        {
            if (string.IsNullOrEmpty(requestLine)) return null;
            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2) return null;

            string target = parts[1];
            int questionMark = target.IndexOf('?');
            string path = questionMark >= 0 ? target.Substring(0, questionMark) : target;
            if (path != "/callback") return null;
            return questionMark >= 0 ? target.Substring(questionMark + 1) : "";
        }

        static string GetParam(string query, string name)
        {
            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0) continue;
                if (pair.Substring(0, equals) == name) return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            return null;
        }

        static async Task WriteResponseAsync(NetworkStream stream, string status, string html)
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            string header = "HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        static string Page(string message)
        {
            return "<html><body style=\"background:#101010;color:#f2f2f2;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">"
                + "<p style=\"font-size:18px\">" + message + "</p></body></html>";
        }

        static string LoadRefreshToken()
        {
            try
            {
                if (!File.Exists(TokenPath)) return null;
                byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }

        static void SaveRefreshToken(string token)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(TokenPath, data);
            }
            catch { }
        }

        static string RandomBase64Url(int byteCount)
        {
            return Base64Url(RandomNumberGenerator.GetBytes(byteCount));
        }

        static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
