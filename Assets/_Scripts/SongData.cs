using Boomlagoon.JSON;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;

public class SongData : MonoBehaviour
{
    string[] songPaths = new string[0];

    public IEnumerable<Song> Songs { get; private set; } = new List<Song>();

    IEnumerable<PlayingMethod> ParsePlayingMethods(JSONArray difficultyBeatmapSets) {
        return
            difficultyBeatmapSets
                .Select(
                    beatmapSets => {
                        var difficulties =
                            beatmapSets.Obj.GetArray("_difficultyBeatmaps")
                                .Select(difficultyBeatmaps => difficultyBeatmaps.Obj.GetString("_difficulty"))
                                .ToList();

                        return new PlayingMethod(beatmapSets.Obj.GetString("_beatmapCharacteristicName"), difficulties);
                    });
    }

    public async Task<IEnumerable<Song>> LoadSongsAsync() {
        async Task<Song> loadSong(string dir) {
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "info.dat").Length > 0) {
                using (var fs = File.OpenRead(Path.Combine(dir, "info.dat"))) {
                    var reader = new StreamReader(fs);
                    var text = await reader.ReadToEndAsync();

                    JSONObject infoFile = JSONObject.Parse(text);
                    var song = new Song(
                        dir,
                        infoFile.GetString("_songName"),
                        infoFile.GetString("_songAuthorName"),
                        infoFile.GetString("_levelAuthorName"),
                        infoFile.GetNumber("_beatsPerMinute").ToString(),
                        Path.Combine(dir, infoFile.GetString("_coverImageFilename")),
                        Path.Combine(dir, infoFile.GetString("_songFilename")),
                        ParsePlayingMethods(infoFile.GetArray("_difficultyBeatmapSets")));

                    return song;
                }
            } else
                return null;
        }

        async Task<IEnumerable<Song>> loadSongs(string[] paths) {
            // Process zip files first
            foreach (var path in paths) {
                if (Directory.Exists(path)) {
                    foreach (var f in Directory.GetFiles(path, "*.zip")) {
                        var outputFolder = Path.Combine("/sdcard/Playlists", Path.GetFileNameWithoutExtension(f));
                        if (!Directory.Exists(outputFolder))
                            Directory.CreateDirectory(outputFolder);

                        // System.IO.Compression.ZipFile.ExtractToDirectory(f, outputFolder);
                        File.Delete(f);
                    }
                }
            }

            var songTasks =
                paths
                    .Where(Directory.Exists)
                    .SelectMany(Directory.GetDirectories)
                    .Select(loadSong);
            var songsX = await Task.WhenAll(songTasks);
            return songsX.Where(x => x != null).OrderBy(song => song.Name);
        }

        var songs = await loadSongs(this.songPaths);
        return songs;
    }

    public IEnumerable<Song> LoadSongs() {
        return Task.Run(LoadSongsAsync).Result;
    }

    void Awake() {
        this.songPaths = new string[]
            {
    #if UNITY_ANDROID
                Path.Combine(Application.persistentDataPath, "Playlists"),
                "/sdcard/Playlists",
                "/sdcard/Download"
    #else
                Path.Combine(Application.dataPath + "/Playlists")
    #endif
            };

        Songs = LoadSongs();
    }
}

public class Song
{
    public string Path { get; }
    public string AudioFilePath { get; }
    public string Name { get; }
    public string AuthorName { get; }
    public string LevelAuthor { get; }
    public string BPM { get; }
    public string CoverImagePath { get; }
    public ImmutableList<PlayingMethod> PlayingMethods { get; }

    public Song(string path, string name, string authorName, string levelAuthor, string bpm, string coverImagePath, string audioFilePath, IEnumerable<PlayingMethod> playingMethods) {
        this.Path = path;
        this.AudioFilePath = audioFilePath;
        this.Name = name;
        this.AuthorName = authorName;
        this.LevelAuthor = levelAuthor;
        this.BPM = bpm;
        this.CoverImagePath = coverImagePath;
        this.PlayingMethods = ImmutableList<PlayingMethod>.Empty.AddRange(playingMethods);
    }

    public string Hash
    {
        get
        {
            using (SHA1 hashGen = SHA1.Create()) {
                var hash = hashGen.ComputeHash(Encoding.UTF8.GetBytes(Name + AuthorName + BPM));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hash.Length; i++) {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}

public class PlayingMethod
{
    public string CharacteristicName { get; }
    public ImmutableList<string> Difficulties { get; }

    public PlayingMethod(string name, IEnumerable<string> difficulties) {
        CharacteristicName = name;
        Difficulties = ImmutableList<string>.Empty.AddRange(difficulties);
    }
}