using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.IO;
using System.Data;
using Mono.Data.SqliteClient;
using UnityEngine.Networking;
using System;
using AnotherFileBrowser.Windows;

[RequireComponent(typeof(AudioSource))]

static class PlayerDataBase
{
    private static string DBPath;
    private const string dbName = "base.sqlite";
    private static SqliteConnection connection;
    
    static PlayerDataBase()//�����������
    {
        #if UNITY_EDITOR
            DBPath = "URI=file:" + Application.dataPath + "/StreamingAssets/base.sqlite";/*Path.Combine(Application.streamingAssetsPath, fileName)*/
        #endif

        #if UNITY_STANDALONE
            DBPath = "URI=file:" + Application.dataPath + "/StreamingAssets/base.sqlite";
        #endif
    }

    /// <summary> ����� ��������� ����������� � ��. </summary>
    private static void OpenConnection()
    {
        connection = new SqliteConnection(DBPath);
        connection.Open();
    }

    /// <summary> ����� ��������� ����������� � ��. </summary>
    public static void CloseConnection()
    {
        connection.Close();
    }

    /// <summary> ����� ��������� ������ query. </summary>
    /// <param name="query"> ������. </param>
    public static void ExecuteQueryWithoutAnswer(string query)
    {
       OpenConnection();
       using (SqliteCommand command= new SqliteCommand())
        {
            command.Connection = connection;
            command.CommandText = query;
            command.ExecuteNonQuery();
        }
        CloseConnection();
    }

    /// <summary> ����� ��������� ������ query � ���������� ����� �������. </summary>
    /// <param name="query"> ������. </param>
    /// <returns> ���������� �������� 1 ������ 1 �������, ���� ��� �������. </returns>
    public static string ExecuteQueryWithAnswer(string query)
    {
        OpenConnection();
        object answer = null ;
        using (SqliteCommand command = new SqliteCommand())
        {
            command.Connection = connection;
            command.CommandText = query;
            answer = command.ExecuteScalar();
        }

        CloseConnection();

        if (answer != null) return answer.ToString();
        else return null;
    }
       
    /// <summary> M���� ���������� �������, ������� �������� ����������� ������� ������� query. </summary>
    /// <param name="query"> ������. </param>
    public static DataTable GetTableView(string query)
    {
        OpenConnection();
        DataSet DS;
        using (SqliteDataAdapter adapter = new SqliteDataAdapter(query, connection))
        {
            DS = new DataSet();
            adapter.Fill(DS);
        }
        CloseConnection();

        return DS.Tables[0];
    }
}

public class AudioManager : MonoBehaviour//����� �������� - ������ ������ � �������� ������ � �������� �� �������
{
    public enum SeekDirection { Forward, Backward };//0 ������, 1 �����
    public AudioSource source;// ����� private?//��������
   
    public List<AudioClip> clips = new List<AudioClip>();//�������� - ��������� ������
    List<string> playlists = new List<string>();

    public Text clipTitleText;//��� ������ �������� �����
    public Text clipTimeText;
    public Text playlistText;
    public Text InfoText;
    
    private int fullLength;
    string fullPath;
    string fileName;
    public string playlistTitle;

    [SerializeField] [HideInInspector]public int currentIndex = 0;//������ ������ ����� �� ��������� 0

    public int maxId;
    private int playTime;//��� ������� ����� ������� �����
    private int seconds;
    private int minutes;

    public GameObject InputTitlePlaylist;
    [SerializeField] GameObject DropDownButtonPrefab;
    public InputField InputTargetPlaylist;
    [SerializeField] Transform DropBoxPanel;
    [SerializeField] Slider progressSlider;

    bool flagNext;//���� ��� ����� ��������
    bool flagRandomPlay = false;//���� ��� ���������� ������������ �������� ���������
    
    void Start()
    {
        if (source == null) source = gameObject.GetComponent<AudioSource>();
        
        clips.Clear();//�������� ������ ��� ������

        //������ � ���� �� ��������� �� ID ������ � ��������� �� ��������� 1 - �����������������

        DataTable SongsPaths = PlayerDataBase.GetTableView($"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.ID_clip = Soundtracks.ID_clip WHERE ID_list = 1");//"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.FilePath_clip = Soundtracks.FilePath_clip WHERE ID_list = 4"

        foreach (DataRow row in SongsPaths.Rows) //����������� ��� �P3 ����� from chosen playlist
        {
            string filename = (string)row["FilePath_clip"];
            StartCoroutine(LoadFile(filename));
        }
        //ReloadSounds();

        StopCoroutine("LoadFile");

        FillDropDown();
    }

    /// <summary> ����� ��������� ������ ���������� � ������� � ��� ������. </summary>
    public void FillDropDown()
    {
        maxId = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer(("SELECT MAX(ID_list) FROM Playlists")));
        for (int i = 1; i < maxId + 1; i++)
        {
            playlists.Add(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT Title_playlist FROM Playlists WHERE ID_list = {i}"));
        }
        playlists.ForEach(print);

        for (int i = 0; i < maxId; i++)
        {
            GameObject DropDownButton = (GameObject)Instantiate(DropDownButtonPrefab);
            DropDownButton.GetComponentInChildren<Text>().text = playlists[i];
            int index = i;
            DropDownButton.GetComponent<Button>().onClick.AddListener(delegate { SetPlaylist(playlists[index].ToString()); });
            DropDownButton.transform.SetParent(DropBoxPanel, false);
        }
    }

    /// <summary> ����� ��������. </summary>   
    private void SetPlaylist (string stringListTitle)
    {
        playlistTitle = stringListTitle;
        playlistText.text = playlistTitle;
        print(playlistTitle);
        ReloadSounds();
    }

    /// <summary> ����� ������ ����������� � ������ �����. </summary>   
    public void Previous()
    {
        Seek(SeekDirection.Backward);
        PlayCurrent();
        ShowCurrentTitle();
        StartCoroutine(TimeTrackCounter());
        ShowPlayTime();
        SliderProgressBar();
        if (AudioListener.pause == true)
        {
            AudioListener.pause = false;
            flagNext = false;
            StartCoroutine(TimeTrackCounter());
        }
    }

    /// <summary> ����� ������������ �������� � ������ �����. </summary>  
    public void Play_current()
    {
        PlayCurrent();
        ShowCurrentTitle();
        StartCoroutine(TimeTrackCounter());
        ShowPlayTime();
        SliderProgressBar();
        if (AudioListener.pause == true)
        {
            AudioListener.pause = false;
            flagNext = false;
            StartCoroutine(TimeTrackCounter());
        }
    }

    /// <summary> ����� ������ ���������� � ������ �����. </summary>
    public void Next()
    {
        Seek(SeekDirection.Forward);
        PlayCurrent();
        ShowCurrentTitle();
        StartCoroutine(TimeTrackCounter());
        ShowPlayTime();
        SliderProgressBar();
        if (AudioListener.pause == true)
        {
            AudioListener.pause = false;
            flagNext = false;
            StartCoroutine(TimeTrackCounter());
        }
    }

    /// <summary> ����� ������������ ������ �������� ���������. </summary>
    public void Reload()
    {
        ReloadSounds();
        ShowCurrentTitle();
    }

    /// <summary> ����� �����. </summary>
    public void PauseMusic()
    {
        if (AudioListener.pause == true)
        {
            AudioListener.pause = false;
            flagNext = false;
            StartCoroutine(TimeTrackCounter());
        }
        else
        {
            AudioListener.pause = true;
            flagNext = true;
            StopCoroutine(TimeTrackCounter());
        }
    }

    /// <summary> ����� ��������� ������������ �������� �����. </summary>
    public void StopMusic()
    {
        source.Stop();
        StopAllCoroutines();
        if (AudioListener.pause == true)
        {
            AudioListener.pause = false;
            flagNext = false;
            StartCoroutine(TimeTrackCounter());
        }
    }

    /// <summary> ����� ����� �� ����������. </summary>
    public void ExitApp()
    {
        Application.Quit();
    }
    /// <summary> ���� ��� ���������� ������������ ���������. </summary>
    public void SetRandomPlay()
    {
        flagRandomPlay = !flagRandomPlay;
    }

    /// <summary> ��������� ������ ������� �������������� ����� - ��������. </summary>
    public void ChangeProgress()
    {      
        source.time = (float)progressSlider.value;
    }

    /// <summary> ����� ����������/��������� �����. </summary>
    public void MuteMusic()
    {
        source.mute = !source.mute;
    }

    /// <summary> ����� ��������� ������������� �������� �������� ��������. </summary>
    void SliderProgressBar()
    {
        progressSlider.maxValue = (float)source.clip.length;
    }

    /// <summary> ����� ����������� ����� ������� � ��������� ���������. </summary>
    void Seek(SeekDirection d)
    {
        if (flagRandomPlay == false)
        {
            if (d == SeekDirection.Forward)
                currentIndex = (currentIndex + 1) % clips.Count;
            else
            {
                currentIndex--;
                if (currentIndex < 0) currentIndex = clips.Count - 1;
            }
        }
        else
        {
            System.Random rand = new System.Random();
            if (d == SeekDirection.Forward)
            {
                currentIndex = rand.Next(0, clips.Count);
            }
            else
            {
                currentIndex -= rand.Next(currentIndex, clips.Count);
                if (currentIndex < 0) currentIndex = clips.Count - 1;
            }
        }
    }

    /// <summary> ����� ������ ���������� �������� �������������� �����. </summary>
    public void PlayCurrent()
    {
        progressSlider.maxValue = 0.01f;//����� �������� �� ����������� ����� - ����� ������ �������
        source.clip = clips[currentIndex];
        source.Play();
    }

    /// <summary> ����� ������������ ���������. </summary>
    void ReloadSounds()
    {
        clips.Clear();//�������� ������ ��� ������
        currentIndex = 0;//�������� ������ ����� ������������ ���� ������
        //������ � ���� �� ��������� �� ID ������ � ���������

        string nameList = playlistTitle;// InputTargetPlaylist.text;//���� �� ������ - � ����� �� ������������� ���������
        print("nameList " + playlistTitle);
        InfoText.text = "Selected playlist: " + playlistTitle;
        int idList = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}'"));
        print(idList);

       DataTable SongsPaths = PlayerDataBase.GetTableView($"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.ID_clip = Soundtracks.ID_clip WHERE ID_list = {idList}");//"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.FilePath_clip = Soundtracks.FilePath_clip WHERE ID_list = 4"

        foreach (DataRow row in SongsPaths.Rows) //����������� ��� �P3 ����� from chosen playlist
        {
            string filename = (string)row["FilePath_clip"];
            StartCoroutine(LoadFile(filename));
        }
    }

    /// <summary> ��������� �������� ���� ����������. </summary>
    public void OpenFileBrowser()
    {
        var brprop = new BrowserProperties();
        brprop.filter = "Sound files (*.mp3) | *.mp3";
        brprop.filterIndex = 0;

        new FileBrowser().OpenFileBrowser(brprop, path =>
        {
            fullPath = Path.GetFullPath(path);
            fileName = Path.GetFileNameWithoutExtension(path);
        });
    }

    /// <summary> ����� ���������� ����� � ������� �������� � � �������� �� ���������. </summary>
    public void AddTrackToPlaylist()//��������� ���� � �������(���������) �������� � � �������� �� ��������� 1 - Unsorted
    {
        string nameList = playlistTitle;// �� ������������� ��������� �������� ��������
        int idList = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}'"));//����������� ID ���������
        print($"Selected playlist ID {idList}, name {nameList}");//��������
        InfoText.text = $"Selected playlist ID {idList}, name {nameList}";
#if UNITY_STANDALONE
        OpenFileBrowser();
#endif
#if UNITY_EDITOR
        string selectFile = EditorUtility.OpenFilePanel("Select track for add", "", "mp3");
        if (selectFile == "") return;
        string fullPath = Path.GetFullPath(selectFile);
        string fileName = Path.GetFileNameWithoutExtension(selectFile);
        print(fullPath);
        print(fileName);
#endif

        int lastInsertID_clip = 0;

        if (playlistText.text == "Unsorted")
        {
            try
            {
                lastInsertID_clip = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"INSERT INTO Soundtracks (FilePath_clip,Title_clip) VALUES('{fullPath}','{fileName}');SELECT last_insert_rowid()"));//� ���� ���� ��� ���� � ����? ����� ������ ������
                print(lastInsertID_clip);
            }
            catch (Exception e)
            {
                print(e.Message);
                print("This track is already in the selected or unsorted playlists ");
                InfoText.text = "This track is already in the selected or unsorted playlists ";
            }
            try
            {
                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_list,ID_clip) SELECT (SELECT ID_list FROM Playlists WHERE Title_playlist = 'Unsorted') AS ID_list, {lastInsertID_clip} AS ID_clip");//������ ��������� � Unsorted// � ���� ���� ��� � Unsorted? - ����� ������ ������ ��
            }
            catch (Exception e)
            {
                print(e.Message);
                print("This track is already in the unsorted playlists ");
                InfoText.text = "This track is already in the unsorted playlists ";
            }
        }
        else
        {
            try
            {
                lastInsertID_clip = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"INSERT INTO Soundtracks (FilePath_clip,Title_clip) VALUES('{fullPath}','{fileName}');SELECT last_insert_rowid()"));//� ���� ���� ��� ���� � ����? ����� ������ ������
                print(lastInsertID_clip);
            }
            catch (Exception e)
            {
                print(e.Message);
                print("This track is already in the selected or unsorted playlists ");
                InfoText.text = "This track is already in the selected or unsorted playlists ";
            }

            try
            {
                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_list,ID_clip) SELECT (SELECT ID_list FROM Playlists WHERE Title_playlist = 'Unsorted') AS ID_list, {lastInsertID_clip} AS ID_clip");//������ ��������� � Unsorted// � ���� ���� ��� � Unsorted? - ����� ������ ������ ��
            }
            catch (Exception e)
            {
                print(e.Message);
                print("This track is already in the unsorted playlists ");
                InfoText.text = "This track is already in the unsorted playlists ";
            }

            try
            {
                //lastInsertID_clip = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer("SELECT last_insert_rowid()"));

                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_clip, ID_list) SELECT {lastInsertID_clip} AS ID_clip, {idList}");//��������� � ������� ��������   $"INSERT INTO Listing (ID_clip, ID_list) SELECT (SELECT ID_clip FROM Soundtracks WHERE FilePath_clip = '{fullPath}') AS ID_clip, ID_list FROM Playlists WHERE ID_list = {idList}"
                print("done insert");
                InfoText.text = "Done insert";
            }
            catch (Exception e)
            {
                print(e.Message);
                print("This track is already in the selected ");
                InfoText.text = "This track is already in the selected playlist";
            }

        }
        maxId = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer(("SELECT * FROM Playlists ORDER BY ID_list DESC LIMIT 1")));//��� ���������� ������������� ���������
        ReloadSounds();//���������� ������ ������
    }

    /// <summary> ����� �������� ����� �� �������� ���������. </summary>
    public void DeleteTrackFromPlaylist()//������� ���� �� ���������� ���������
    {
        string nameList = playlistTitle;//InputTargetPlaylist.text;
        int idList = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}'"));
        print($"Selected playlist ID {idList}, name {nameList}");
#if UNITY_STANDALONE
        OpenFileBrowser();
#endif
#if UNITY_EDITOR
        string selectFile = EditorUtility.OpenFilePanel("Select track for delete", "", "mp3");
        string fullPath = Path.GetFullPath(selectFile);
        string fileName = Path.GetFileNameWithoutExtension(selectFile);
        print(fullPath);
        print(fileName);
#endif
        try
        {
            PlayerDataBase.ExecuteQueryWithoutAnswer($"DELETE FROM Listing WHERE (SELECT ID_clip FROM Soundtracks WHERE FilePath_clip = '{fullPath}') AS ID_clip AND  (SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}') AS ID_list ");
            PlayerDataBase.ExecuteQueryWithoutAnswer($"DELETE FROM Soundtracks WHERE Title_clip = '{fileName}' AND FilePath_clip = '{fullPath}'");
            print("done drop");
            InfoText.text = "Done drop";
        }
        catch (Exception e)
        {
            print(e.Message);
            print("Cannot drop this track in the selected playlist ");
            InfoText.text = "Cannot drop this track in the selected playlist ";
        }
        maxId = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer(("SELECT MAX(ID_list) FROM Playlists")));//��� ���������� ������������� ���������
        ReloadSounds();//���������� ������ ������
    }

    /// <summary> ����� �������� ������ ���������. </summary>
    public void CreatePlaylist()
        //������� ����� ��������
    {
        try
        {
            //InputTitlePlaylist.SetActive(true);
            string nameList = InputTargetPlaylist.text;//��������� �� ������������ �������� ������ ��������� // �� ������� ��� ������ ���� NewPlayList
            PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Playlists (Title_playlist) VALUES('{nameList}')");
            print($"Playlist {nameList} successfully created");
            InfoText.text = $"Playlist {nameList} successfully created";
        }
        catch (Exception e)
        {
            print(e.Message);
            print("Cannot create this playlist ");
            InfoText.text = "Cannot create this playlist ";
        }

        playlists.Clear();
        var clones = GameObject.FindGameObjectsWithTag("CloneBox");

        foreach (var clone in clones)
        {
            Destroy(clone);
        }
        FillDropDown();
    }

    /// <summary> ����� ���������� ���������(������) ������ ����������. </summary>
    private void ClearDropBox()
    {
        var clones = GameObject.FindGameObjectsWithTag("CloneBox");
        foreach (var clone in clones)
        {
            Destroy(clone);
        }
    }

    /// <summary> ����� �������� ���������. </summary>
    public void DropPlaylist()
        //������� ��������� ��������
        //�������� Unsorted ������� ������!
    {
        try
        {
            //InputTitlePlaylist.SetActive(true);
            string nameList = InputTargetPlaylist.text;//��� ������ ����� �� ������������
            if (nameList == "Unsorted")
            {
                print("Cannot delete this playlist");
                InfoText.text = "Cannot delete this playlist";
            }
            
            PlayerDataBase.ExecuteQueryWithoutAnswer($"DELETE FROM Playlists WHERE ID_list = 4 AND Title_playlist = '{nameList}'");
            print($"Playlist {nameList} successfully dropped");
            InfoText.text = $"Playlist {nameList} successfully dropped";
        }
        catch (Exception e)
        {
            print(e.Message);
            print("Cannot delete this playlist ");
            InfoText.text = "Cannot delete this playlist ";
        }

        playlists.Clear();
        var clones = GameObject.FindGameObjectsWithTag("CloneBox");

        foreach (var clone in clones)
        {
            Destroy(clone);
        }
        FillDropDown();
    }

    IEnumerator LoadFile(string path)//�������� ��� ������������ �����������
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            print("Loading " + path);
            yield return www.SendWebRequest();
            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);//AudioClip clip = www.GetAudioClip(false);    
            print("Done loading");
            InfoText.text = "Done loading";
            clip.name = Path.GetFileName(path);
            clips.Add(clip);
        }
    }

    /// <summary> ����� ����������� �������� �������� �����. </summary>
    void ShowCurrentTitle()//��� ����������� �������� ����� �� ����� ������������
    {
        if (source.isPlaying)
        {
            clipTitleText.text = source.clip.name;//����������� ��� �������� �������������� ����� �� ���������� � ������
            fullLength = (int)source.clip.length;
        }
    }

    IEnumerator TimeTrackCounter()//�������� ��� �������� ������� ������������ �����
    {
        while (source.isPlaying)//���� ������
        {
            playTime = (int)source.time;//������ ����� �����, ���������� � ����� ��� �������� �������� ��������
            ShowPlayTime();//�������� ������� ����������� ������� � � ������ ����� ��������� ������ �������
            yield return null;//������������ ���� ����������� ������� - ���� ������
            progressSlider.value = (float)source.time;
        }
        if (flagNext == false) Next();
    }

    /// <summary> ����� ����������� �������� ������� �������������� �����. </summary>
    void ShowPlayTime()
    {
        seconds = playTime % 60;//������� - ������� �� ������� ����� ����� �� 60 ������
        minutes = (playTime / 60) % 60;//������ - ������ ����� �� 60 ����� � ������� �� ������� 60 ������ 
        clipTimeText.text = minutes + ":" + seconds.ToString("D2") + "/" + ((fullLength / 60) % 60) + ":" + (fullLength % 60).ToString("D2");//����� � ������ �������
    }
}