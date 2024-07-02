using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace SpotifyToText
{
    class SpotifyCall
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly string redirectUri = "http://localhost:8888/callback/";

        #region user and process functions
        public static string GetAuthorizationUrl()
        {
            JsonCredentials credentials = Program.GetCredentials();
            string scopes = "playlist-modify-public playlist-modify-private";  // Add other scopes as needed
            return $"https://accounts.spotify.com/authorize?response_type=code&client_id={credentials.ClientId}&scope={Uri.EscapeDataString(scopes)}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        public static string ListenForCode()
        {
            using (HttpListener listener = new HttpListener())
            {
                string uri = redirectUri;
                // Ensure the uri ends with a slash
                if (!uri.EndsWith("/"))
                    uri += "/";

                listener.Prefixes.Add(uri);
                listener.Start();
                Console.WriteLine("Listening for OAuth callback...");

                HttpListenerContext context = listener.GetContext();  // Block until a request arrives
                HttpListenerRequest request = context.Request;
                string code = request.QueryString["code"];  // Extract the code from the query parameter

                // Send an HTTP response to the browser to inform the user they can close the window
                HttpListenerResponse response = context.Response;
                string responseString = "<html><head><meta charset='UTF-8'></head><body>Please return to the app.</body></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();

                listener.Stop();
                return code;
            }
        }

        public static async Task<string> GetAccessToken(string code)
        {
            JsonCredentials credentials = Program.GetCredentials();

            var values = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", redirectUri },
                { "client_id", credentials.ClientId },
                { "client_secret", credentials.ClientSecret }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync("https://accounts.spotify.com/api/token", content);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            var responseJson = JsonConvert.DeserializeObject<dynamic>(responseBody);
            return responseJson.access_token;
        }

        public static async Task<string> GetActiveUserID(string accessToken)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var response = await client.GetAsync("https://api.spotify.com/v1/me");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error fetching user profile: {response.StatusCode} - {response.ReasonPhrase}");
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Response body: " + errorBody);
                response.EnsureSuccessStatusCode(); // This will throw an exception with more details
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            var responseJson = JsonConvert.DeserializeObject<dynamic>(responseBody);
            return responseJson.id;
        }
        #endregion
        #region Playlist functions

        public static string ExtractPlaylistId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/');
                // The playlist ID is the last segment of the path before any query string
                var playlistSegment = segments[^1];
                // Handle cases where the URL might contain a query string
                var questionMarkIndex = playlistSegment.IndexOf('?');
                if (questionMarkIndex != -1)
                {
                    playlistSegment = playlistSegment.Substring(0, questionMarkIndex);
                }
                return playlistSegment;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to extract playlist ID: " + ex.Message);
                return null;
            }
        }

        public static async Task<PlaylistData> GetPlaylistData(string playlistId, string accessToken)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var response = await client.GetAsync($"https://api.spotify.com/v1/playlists/{playlistId}");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            //Console.WriteLine("API Response: " + responseBody);
            return JsonConvert.DeserializeObject<PlaylistData>(responseBody);
        }

        public static string GetFormattedPlaylistInfo(PlaylistData playlistData)
        {
            var sb = new StringBuilder();

            foreach (var track in playlistData.tracks.items)
            {
                sb.AppendLine($"Song: {track.track.name}, Artist: {track.track.artists[0].name}, Album: {track.track.album.name}");
            }

            return sb.ToString();
        }

        public static async Task<string> CreatePlaylist(string playlistName, string playlistDescription, string access)
        {
            var playlistDetails = new
            {
                name = playlistName,
                description = playlistDescription,
                @public = true // Make it public
            };

            var activeUserID = await GetActiveUserID(access);

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            var content = new StringContent(JsonConvert.SerializeObject(playlistDetails), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"https://api.spotify.com/v1/users/{activeUserID}/playlists", content);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Response body: " + errorBody);
                response.EnsureSuccessStatusCode(); // This will throw an exception with more details
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            var responseJson = JsonConvert.DeserializeObject<dynamic>(responseBody);
            return responseJson.id;
        }

        public static async Task AddTracksToPlaylist(string playlistId, List<string> trackUris, string access)
        {
            var trackDetails = new
            {
                uris = trackUris
            };

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            var content = new StringContent(JsonConvert.SerializeObject(trackDetails), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"https://api.spotify.com/v1/playlists/{playlistId}/tracks", content);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
        }
        #endregion


        #region Track functions
        public static async Task<List<string>> GetTrackUris(List<string> songs, string access)
        {
            var uris = new List<string>();

            foreach (var song in songs)
            {
                Console.WriteLine("Looking for " + song);
                string sanitizedSong = SanitizeString(song);
                string uri = await GetTrackUri(sanitizedSong, access);
                if (uri != null)
                {
                    uris.Add(uri);
                }
            }

            return uris;
        }

        public static async Task<string> GetTrackUri(string song, string access)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            var response = await client.GetAsync($"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(song)}&type=track&limit=1");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            var responseJson = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (responseJson.tracks.items.Count > 0)
            {
                return responseJson.tracks.items[0].uri;
            }
            return null;
        }
        #endregion

        static string SanitizeString(string input)
        {
            // Remove or replace any characters that are not valid in a URL query string
            string sanitized = Regex.Replace(input, @"[^\w\s\-:,'&]", "");
            return sanitized;
        }
    }
}
