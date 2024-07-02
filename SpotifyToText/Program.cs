using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using TextCopy;

/// <summary>
/// - Initialize Spotify API client with client ID and client secret.
/// - Authenticate using OAuth and obtain access token.
/// - Make API request to get playlist data using the playlist ID.
/// - Parse the JSON response to extract song names, artist names, and album titles.
/// - Display or save the extracted information.
/// Authorization code: AQAz9Ypdd3UuFWrdx4kTXkQDthv0zT9xAMLLHTV7ZJfKJfpgmU5IQCjN8lmSZvFEz9bB-IuwRxLPNZ2Y9dhWSnmHU7Dfh-nlc8kleRYXQ1qymGPOVdK6jCzg0RzVhj4TY5Swv93FNVRr6gvBM-bkscgWXv8a86xoA6lmJny3ii2QB1k6ftwh393exDFwPIlAbepLDQ3NdlpOtg
/// </summary>

namespace SpotifyToText
{
    class Program
    {
        private static readonly string credentialsPath = "C:/Users/Jonathan/Documents/secretkeyopenai.json";
        private static readonly string systemPrompt = 
            "You are musicGPT. You recommend songs based on input. These are songs in an spotify playlist. Please recommend songs based on this selection. " +
            "Please recommend $$$ songs." +
            "Please consider the second part of the message as requiered for the recommendations." +
            "Please keep the struture shown in this example: " +
            "Song: Fallen leaves, Artist: Billy Talent, Album: Billy Talent 3 Song: Resistance, Artist: Muse, Album: Resistance " +
            "Song: Shut up I Want You to Love Me Back, Artist: Blue October, Album: Black Orchid Song: Big Love, Artist: Blue October, Album: Spinn";

        private static JsonCredentials credentials;
        public static JsonCredentials GetCredentials() { return credentials; }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Loading credentials");
            credentials = LoadCredentialsFromJsonFile(credentialsPath);
            Console.WriteLine("Visit this URL to authorize:");
            string authURL = SpotifyCall.GetAuthorizationUrl();
            Console.WriteLine(authURL);
            ClipboardService.SetText(authURL);
            Console.WriteLine("URL copied to clipboard");

            string code = SpotifyCall.ListenForCode();

            string authorizationCode = code;
            string accessToken = await SpotifyCall.GetAccessToken(authorizationCode);

            //await ValidateTokenScopes(accessToken);

            Console.WriteLine("Please enter the Spotify playlist link or leave empty:");
            string playlistUrl = Console.ReadLine();
            string inspirationPlaylistId;
            string prompt = "";
            if (string.IsNullOrEmpty(playlistUrl))
            {
                prompt = "Pick any songs that you think are right. There are noi songs as input. Please keepm the given structure of Song: Songname, Artist: Artistname, Album: Albumname";
            }
            else
            {
                inspirationPlaylistId = SpotifyCall.ExtractPlaylistId(playlistUrl);

                try
                {
                    var playlistData = await SpotifyCall.GetPlaylistData(inspirationPlaylistId, accessToken);
                    Console.WriteLine(playlistData.ToString());
                    foreach (var track in playlistData.tracks.items)
                    {
                        Console.WriteLine($"Song: {track.track.name}, Artist: {track.track.artists[0].name}, Album: {track.track.album.name}");
                    }

                    prompt += SpotifyCall.GetFormattedPlaylistInfo(playlistData);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            if (string.IsNullOrEmpty(credentials.ApiKey))
            {
                Console.WriteLine("Invalid API key. Please ensure you entered the key correctly and try again.");
                return;
            }

            Console.WriteLine("How many songs are you looking for");
            string ammountRequest = Console.ReadLine();
            string preparedSystemPrompt = "";
            if(IsNumber(ammountRequest, out int ammount))
            {
                preparedSystemPrompt = ReplacePlaceholder(systemPrompt,"$$$", ammount.ToString());
            }
            else
            {
                preparedSystemPrompt = ReplacePlaceholder(systemPrompt, "$$$", "10");
            }

            Console.WriteLine("Any extra wishes?");
            string extraWishes = Console.ReadLine();
            string promptExtra = prompt + " Second part: " + " " + extraWishes;
            //Console.WriteLine(systemPrompt);
            //Console.WriteLine(prompt);

            string response = await GptCall.GetGPT4Response(credentials.ApiKey.Trim(), promptExtra, preparedSystemPrompt);
            Console.WriteLine("Parsing songs");

            List<string> songs = ParseSongs(response);


            // create playlist

            string playlistName = "New Playlist";
            string playlistDescription = "A new playlist created via the Spotify API";
            playlistName += " Songs like " + GetSongName(songs[0]);
            playlistName += " but '" + extraWishes + "'";
            Console.WriteLine("New Playlist: " + playlistName);
            Console.WriteLine("Press any key to create list");
            Console.ReadKey();

            Console.WriteLine("Searching for song uris...");
            // Example list of track URIs (replace with actual URIs)
            List<string> uris = await SpotifyCall.GetTrackUris(songs, accessToken);
            Console.WriteLine("Recieved song uris. Creating playlist...");

            try
            {
                // Step 1: Create a new playlist
                string newPlaylistId = await SpotifyCall.CreatePlaylist(playlistName, playlistDescription, accessToken);
                Console.WriteLine("Created Playlist ID: " + newPlaylistId);

                // Step 2: Add tracks to the new playlist
                await SpotifyCall.AddTracksToPlaylist(newPlaylistId, uris, accessToken);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
            }

        }

        public static JsonCredentials LoadCredentialsFromJsonFile(string filePath)
        {
            string jsonString = File.ReadAllText(filePath);
            JsonCredentials credentials = JsonConvert.DeserializeObject<JsonCredentials>(jsonString);
            return credentials;
        }

        static List<string> ParseSongs(string input)
        {
            var songs = new List<string>(); 
            var parts = input.Split(new[] { "Song: " }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                songs.Add(part.Trim());
            }
            Console.WriteLine("GPT respone was " + songs.Count + " songs long");
            return songs;
        }
        static string ReplacePlaceholder(string input, string placeholder, string target)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (placeholder == null) throw new ArgumentNullException(nameof(placeholder));
            if (target == null) throw new ArgumentNullException(nameof(target));
            return input.Replace(placeholder, target);
        }

        static bool IsNumber(string input, out int result)
        {
            return int.TryParse(input, out result);
        }

        static string GetSongName(string target)
        {
            string keyword = "Artist";

            // Find the starting position of "Album:"
            int index = target.IndexOf(keyword);

            // If "Album:" is found in the string, extract the portion before it
            string result;
            if (index >= 0)
            {
                result = target.Substring(0, index).Trim();
            }
            else
            {
                // If "Album:" is not found, return the original string
                result = target;
            }
            return result;
        }

    }
}
