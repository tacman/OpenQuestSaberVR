/*
 * The spawner code and also the correct timing stuff was taken from the project:
 * BeatSaver Viewer (https://github.com/supermedium/beatsaver-viewer) and ported to C#.
 * 
 * To be more precisly most of the code in the Update() method was ported to C# by me 
 * from their project.
 * 
 * Without that project this project won't exist, so thank you very much for releasing 
 * the source code under MIT license!
 */

using Boomlagoon.JSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;

[RequireComponent(typeof(AudioSource))]
public class NotesSpawner : MonoBehaviour
{
    [SerializeField]
    GameObject[] Cubes;
    [SerializeField]
    private SongData songData;
    [SerializeField]
    GameObject Wall;

    private string jsonString;
    private string audioFilePath;
    private List<Note> NotesToSpawn = new List<Note>();
    private List<Obstacle> ObstaclesToSpawn = new List<Obstacle>();
    private double BeatsPerMinute;

    public int _noteIndex = 0;
    public int _eventIndex = 0;
    public int _obstacleIndex = 0;

    public float _BeatPerMin;
    public float _BeatPerSec;
    public float _SecPerBeat;
    public float _spawnOffset;
    public float _noteSpeed;
    public float BeatsTime;

    SceneHandling sceneHandling;
    AudioSource audioSource;
    GameObject cameraHead;

    private bool menuLoadInProgress = false;
    private bool audioLoaded = false;
    private bool paused = false;
    bool songPlaying;


    public AudioSource AudioSource {
        get {
            return audioSource;
        }
    }

    void Start() {
        cameraHead = GameObject.FindGameObjectWithTag("MainCamera");
        sceneHandling = GameObject.FindGameObjectWithTag("SceneHandling").GetComponent<SceneHandling>();
        audioSource = GetComponent<AudioSource>();
    }

    private IEnumerator LoadAudio() {
        yield return null;
        audioSource.clip = OggClipLoader.LoadClip(audioFilePath);
        audioLoaded = true;
        yield return null;
    }

    public void PlaySongWithDifficulty(string songHash, string difficulty, string playingMethod)
    {
        var song = songData.Songs.First(x => x.Hash == songHash);

        // Still using Songsettings to pass song data to the song summary scene
        // TODO: Change
        var songSettings = GameObject.FindGameObjectWithTag("SongSettings").GetComponent<SongSettings>();
        songSettings.CurrentSong = song;
        songSettings.SelectedPlayingMethod = playingMethod;
        songSettings.SelectedDifficulty = difficulty;
        
        string path = song.Path;
        if (Directory.Exists(path))
        {
            if (Directory.GetFiles(path, "info.dat").Length > 0)
            {
                JSONObject infoFile = JSONObject.Parse(File.ReadAllText(Path.Combine(path, "info.dat")));

                JSONArray beatmapSets = null;
                foreach (var obj in infoFile.GetArray("_difficultyBeatmapSets")) {
                    if (obj.Obj.GetString("_beatmapCharacteristicName") == playingMethod) {
                        beatmapSets = obj.Obj.GetArray("_difficultyBeatmaps");
                        break;
                    }
                }

                // TODO: Fix. Null pointer here if the playing method doesn't exist.
                foreach (var difficultyBeatmaps in beatmapSets)
                {
                    if (difficultyBeatmaps.Obj.GetString("_difficulty") == difficulty)
                    {
                        _noteSpeed = (float)difficultyBeatmaps.Obj.GetNumber("_noteJumpMovementSpeed");
                        audioFilePath = Path.Combine(path, infoFile.GetString("_songFilename"));
                        jsonString = File.ReadAllText(Path.Combine(path, difficultyBeatmaps.Obj.GetString("_beatmapFilename")));
                        break;
                    }
                }
            }
        }

        StartCoroutine(LoadAudio());

        JSONObject json = JSONObject.Parse(jsonString);

        var bpm = Convert.ToDouble(song.BPM);

        //Notes
        var notes = json.GetArray("_notes");
        foreach (var note in notes)
        {
            var type = note.Obj.GetNumber("_type");

            // ignore bombs, will lead to a bug in GenerateNote which spawns a blue note
            if (type > 1)
            {
                continue;
            }

            var n = new Note
            {
                Hand = (NoteType)type,
                CutDirection = (CutDirection)note.Obj.GetNumber("_cutDirection"),
                LineIndex = (int)note.Obj.GetNumber("_lineIndex"),
                LineLayer = (int)note.Obj.GetNumber("_lineLayer"),
                TimeInSeconds = (note.Obj.GetNumber("_time") / bpm) * 60,
                Time = (note.Obj.GetNumber("_time"))
            };

            NotesToSpawn.Add(n);
        }
        
        var obstacles = json.GetArray("_obstacles");
        foreach (var obstacle in obstacles)
        {
            var o = new Obstacle
            {
                Type = (ObstacleType)obstacle.Obj.GetNumber("_type"),
                Duration = obstacle.Obj.GetNumber("_duration"),
                LineIndex = (int)obstacle.Obj.GetNumber("_lineIndex"),
                TimeInSeconds = (obstacle.Obj.GetNumber("_time") / bpm) * 60,
                Time = (obstacle.Obj.GetNumber("_time")),
                Width = (obstacle.Obj.GetNumber("_width"))
            };

            ObstaclesToSpawn.Add(o);
        }

        Comparison<Note> NoteCompare = (x, y) => x.Time.CompareTo(y.Time);
        NotesToSpawn.Sort(NoteCompare);

        Comparison<Obstacle> ObsticaleCompare = (x, y) => x.Time.CompareTo(y.Time);
        ObstaclesToSpawn.Sort(ObsticaleCompare);

        BeatsPerMinute = bpm;
        
        UpdateBeats();

        songPlaying = true;
    }

    public void UpdateBeats()
    {
        _BeatPerMin = (float)BeatsPerMinute;
        _BeatPerSec = 60 / _BeatPerMin;
        _SecPerBeat = _BeatPerMin / 60;

        UpdateSpawnTime();
    }

    public void UpdateSpawnTime()
    {
        _spawnOffset = BeatsConstants.BEAT_WARMUP_SPEED / _BeatPerMin + BeatsConstants.BEAT_WARMUP_OFFSET * 0.5f / _noteSpeed;
    }

    public void UpdateNotes()
    {
        if (audioSource.isPlaying) {
            for (int i = _noteIndex; i < NotesToSpawn.Count; i++) {
                if ((NotesToSpawn[i].Time * _BeatPerSec) - _spawnOffset < BeatsTime) {
                    SetupNoteData(NotesToSpawn[i]);

                    _noteIndex++;
                } else
                    break;
            }
        }
    }

    public void UpdateObstilcles()
    {
        if (audioSource.isPlaying) {
            for (int i = _obstacleIndex; i < ObstaclesToSpawn.Count; i++) {
                if ((ObstaclesToSpawn[_obstacleIndex].Time * _BeatPerSec) - _spawnOffset < BeatsTime) {
                    SetupObstacleData(ObstaclesToSpawn[_obstacleIndex]);
                    _obstacleIndex++;
                } else
                    break;
            }
        }
    }

    private void SetupObstacleData(Obstacle _obstacle)
    {
        float x = GetX(_obstacle.LineIndex);
        float y = 1.0f; // @todo: was this GetY(_obstacle.LineLayer);
        float z = BeatsConstants.BEAT_WARMUP_SPEED + BeatsConstants.BEAT_WARMUP_OFFSET * 0.5f;
                
        Vector3 _startZ = new Vector3(x, y, z);
        Vector3 _midZ = new Vector3(x, y, z - BeatsConstants.BEAT_WARMUP_SPEED);
        Vector3 _endZ = new Vector3(x, y, z - (BeatsConstants.BEAT_WARMUP_OFFSET + BeatsConstants.BEAT_WARMUP_SPEED));

        GenerateObstacle(_obstacle, _startZ, _midZ, _endZ);
    }

    private void SetupNoteData(Note _note)
    {
        float x = GetX(_note.LineIndex);
        float y = GetY(_note.LineLayer);
        float z = BeatsConstants.BEAT_WARMUP_SPEED + BeatsConstants.BEAT_WARMUP_OFFSET * 0.5f;
                
        Vector3 _startZ = new Vector3(x, y, z);
        Vector3 _midZ = new Vector3(x, y, z - BeatsConstants.BEAT_WARMUP_SPEED);
        Vector3 _endZ = new Vector3(x, y, z - (BeatsConstants.BEAT_WARMUP_OFFSET + BeatsConstants.BEAT_WARMUP_SPEED));
                
        GenerateNote(_note, _startZ, _midZ, _endZ);
    }

    void Update()
    {
        if (songPlaying) {
            if (audioLoaded) {
                audioLoaded = false;
                audioSource.Play();
            }

            BeatsTime = audioSource.time;
            UpdateNotes();
            UpdateObstilcles();

            if (_noteIndex > 0 && !audioSource.isPlaying && !paused) {
                if (!menuLoadInProgress) {
                    menuLoadInProgress = true;
                    StartCoroutine(LoadMenu());
                }
            }
        }
    }

    IEnumerator LoadMenu()
    {
        yield return new WaitForSeconds(5);

        yield return sceneHandling.LoadScene(SceneConstants.SCORE_SUMMARY, LoadSceneMode.Additive);
        yield return sceneHandling.UnloadScene(SceneConstants.GAME);
    }

    void GenerateNote(Note note, Vector3 moveStartPos, Vector3 moveEndPos, Vector3 jumpEndPos)
    {
        if (note.CutDirection == CutDirection.NONDIRECTION)
        {
            // the nondirection cubes are stored at the index+2 in the array
            note.Hand += 2;
        }

        GameObject cube = Instantiate(Cubes[(int)note.Hand], transform);
        var handling = cube.GetComponent<CubeHandling>();
        handling.SetupNote(moveStartPos, moveEndPos, jumpEndPos, this, note);
    }

    public void GenerateObstacle(Obstacle obstacle, Vector3 moveStartPos, Vector3 moveEndPos, Vector3 jumpEndPos)
    {
        GameObject wall = Instantiate(Wall, transform);
        var wallHandling = wall.GetComponent<ObstacleHandling>();
        wallHandling.SetupObstacle(obstacle, this, moveStartPos ,moveEndPos, jumpEndPos);
    }

    private float GetY(float lineLayer)
    {
        float delta = (1.9f - 1.4f);

        if ((int)lineLayer >= 1000 || (int)lineLayer <= -1000)
        {
            return 1.4f - delta - delta + (((int)lineLayer) * (delta / 1000f));
        }

        if ((int)lineLayer > 2)
        {

            return 1.4f - delta + ((int)lineLayer * delta);
        }

        if ((int)lineLayer < 0)
        {
            return 1.4f - delta + ((int)lineLayer * delta);
        }

        if (lineLayer == 0)
        {
            return 0.85f;
        }
        if (lineLayer == 1)
        {
            return 1.4f;
        }

        return 1.9f;
    }
    public float GetX(float noteindex)
    {
        float num = -1.5f;

        if (noteindex >= 1000 || noteindex <= -1000)
        { 
            if (noteindex <= -1000)
                noteindex += 2000;

            num = (num + ((noteindex) * (0.6f / 1000)));
        }
        else
        {
            num = (num + noteindex) * 0.6f;
        }

        return num;
    }    

    public class Note
    {
        public double Time { get; set; }
        public double TimeInSeconds { get; set; }
        public int LineIndex { get; set; }
        public int LineLayer { get; set; }
        public NoteType Hand { get; set; }
        public CutDirection CutDirection { get; set; }

        public override bool Equals(object obj)
        {
            return Time == ((Note)obj).Time && LineIndex == ((Note)obj).LineIndex && LineLayer == ((Note)obj).LineLayer;
        }

        public override int GetHashCode()
        {
            var hashCode = -702342995;
            hashCode = hashCode * -1521134295 + Time.GetHashCode();
            hashCode = hashCode * -1521134295 + TimeInSeconds.GetHashCode();
            hashCode = hashCode * -1521134295 + LineIndex.GetHashCode();
            hashCode = hashCode * -1521134295 + LineLayer.GetHashCode();
            hashCode = hashCode * -1521134295 + Hand.GetHashCode();
            hashCode = hashCode * -1521134295 + CutDirection.GetHashCode();
            return hashCode;
        }
    }

    public enum NoteType
    {
        LEFT = 0,
        RIGHT = 1
    }

    public enum CutDirection
    {
        TOP = 1,
        BOTTOM = 0,
        LEFT = 2,
        RIGHT = 3,
        TOPLEFT = 6,
        TOPRIGHT = 7,
        BOTTOMLEFT = 4,
        BOTTOMRIGHT = 5,
        NONDIRECTION = 8
    }

    public class Obstacle
    {
        internal double TimeInSeconds;
        internal double Time;
        internal int LineIndex;
        internal double Duration;
        internal ObstacleType Type;
        internal double Width;
    }

    public enum ObstacleType
    {
        WALL = 0,
        CEILING = 1
    }

    public enum Mode
    {
        preciseHeight,
        preciseHeightStart
    };

    public static GameObject GetChildByName(GameObject parent, string childName)
    {
        GameObject _childObject = null;

        Transform[] _Children = parent.transform.GetComponentsInChildren<Transform>(true);
        foreach (Transform _child in _Children)
        {
            if (_child.gameObject.name == childName)
            {
                return _child.gameObject;
            }
        }

        return _childObject;
    }
}



