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
    
    static PlayerDataBase()//конструктор
    {
        #if UNITY_EDITOR
            DBPath = "URI=file:" + Application.dataPath + "/StreamingAssets/base.sqlite";/*Path.Combine(Application.streamingAssetsPath, fileName)*/
        #endif

        #if UNITY_STANDALONE
            DBPath = "URI=file:" + Application.dataPath + "/StreamingAssets/base.sqlite";
        #endif
    }

    /// <summary> Метод открывает подключение к БД. </summary>
    private static void OpenConnection()
    {
        connection = new SqliteConnection(DBPath);
        connection.Open();
    }

    /// <summary> Метод закрывает подключение к БД. </summary>
    public static void CloseConnection()
    {
        connection.Close();
    }

    /// <summary> Метод выполняет запрос query. </summary>
    /// <param name="query"> запрос. </param>
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

    /// <summary> Метод выполняет запрос query и возвращает ответ запроса. </summary>
    /// <param name="query"> запрос. </param>
    /// <returns> Возвращает значение 1 строки 1 столбца, если оно имеется. </returns>
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
       
    /// <summary> Mетод возвращает таблицу, которая является результатом выборки запроса query. </summary>
    /// <param name="query"> запрос. </param>
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

public class AudioManager : MonoBehaviour//Аудио менеджер - логика работы с массивом треков и порядком их запуска
{
    public enum SeekDirection { Forward, Backward };//0 вперед, 1 назад
    public AudioSource source;// может private?//источник
   
    public List<AudioClip> clips = new List<AudioClip>();//Плейлист - контейнер клипов
    List<string> playlists = new List<string>();

    public Text clipTitleText;//для вывода названия трека
    public Text clipTimeText;
    public Text playlistText;
    public Text InfoText;
    
    private int fullLength;
    string fullPath;
    string fileName;
    public string playlistTitle;

    [SerializeField] [HideInInspector]public int currentIndex = 0;//индекс первой песни по умолчанию 0

    public int maxId;
    private int playTime;//для расчета длины дорожки трека
    private int seconds;
    private int minutes;

    public GameObject InputTitlePlaylist;
    [SerializeField] GameObject DropDownButtonPrefab;
    public InputField InputTargetPlaylist;
    [SerializeField] Transform DropBoxPanel;
    [SerializeField] Slider progressSlider;

    bool flagNext;//флаг для паузы корутины
    bool flagRandomPlay = false;//флаг для рандомного проигрывания текущего плейлиста
    
    void Start()
    {
        if (source == null) source = gameObject.GetComponent<AudioSource>();
        
        clips.Clear();//зачищаем массив для клипов

        //запрос к базе по умолчанию на ID клипов с плейлиста по умолчанию 1 - неотсортированный

        DataTable SongsPaths = PlayerDataBase.GetTableView($"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.ID_clip = Soundtracks.ID_clip WHERE ID_list = 1");//"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.FilePath_clip = Soundtracks.FilePath_clip WHERE ID_list = 4"

        foreach (DataRow row in SongsPaths.Rows) //вытаскиваем все МP3 файлы from chosen playlist
        {
            string filename = (string)row["FilePath_clip"];
            StartCoroutine(LoadFile(filename));
        }
        //ReloadSounds();

        StopCoroutine("LoadFile");

        FillDropDown();
    }

    /// <summary> Метод заполняет список плейлистов и создает к ним кнопки. </summary>
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

    /// <summary> Метод делегата. </summary>   
    private void SetPlaylist (string stringListTitle)
    {
        playlistTitle = stringListTitle;
        playlistText.text = playlistTitle;
        print(playlistTitle);
        ReloadSounds();
    }

    /// <summary> Метод выбора предыдущего в списке трека. </summary>   
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

    /// <summary> Метод проигрывания текущего в списке трека. </summary>  
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

    /// <summary> Метод выбора следующего в списке трека. </summary>
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

    /// <summary> Метод перезагрузки треков текущего плейлиста. </summary>
    public void Reload()
    {
        ReloadSounds();
        ShowCurrentTitle();
    }

    /// <summary> Метод паузы. </summary>
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

    /// <summary> Метод остановки проигрывания текущего трека. </summary>
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

    /// <summary> Метод выход из приложения. </summary>
    public void ExitApp()
    {
        Application.Quit();
    }
    /// <summary> Флаг для рандомного проигрывания плейлиста. </summary>
    public void SetRandomPlay()
    {
        flagRandomPlay = !flagRandomPlay;
    }

    /// <summary> Процедура выбора времени проигрываемого трека - промотка. </summary>
    public void ChangeProgress()
    {      
        source.time = (float)progressSlider.value;
    }

    /// <summary> Метод выключения/включения звука. </summary>
    public void MuteMusic()
    {
        source.mute = !source.mute;
    }

    /// <summary> Метод установки максимального значения ползунка промотки. </summary>
    void SliderProgressBar()
    {
        progressSlider.maxValue = (float)source.clip.length;
    }

    /// <summary> Метод перемещения между треками в выбранном плейлисте. </summary>
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

    /// <summary> Метод выбора источником текущего проигрываемого трека. </summary>
    public void PlayCurrent()
    {
        progressSlider.maxValue = 0.01f;//сброс величины от предыдущего трека - иначе ошибка индекса
        source.clip = clips[currentIndex];
        source.Play();
    }

    /// <summary> Метод перезагрузки плейлиста. </summary>
    void ReloadSounds()
    {
        clips.Clear();//зачищаем массив для клипов
        currentIndex = 0;//зануляем всегда когда перезгружаем лист треков
        //запрос к базе по умолчанию на ID клипов с плейлиста

        string nameList = playlistTitle;// InputTargetPlaylist.text;//пока из инпута - а нужно из динамического дропдауна
        print("nameList " + playlistTitle);
        InfoText.text = "Selected playlist: " + playlistTitle;
        int idList = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}'"));
        print(idList);

       DataTable SongsPaths = PlayerDataBase.GetTableView($"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.ID_clip = Soundtracks.ID_clip WHERE ID_list = {idList}");//"SELECT Soundtracks.FilePath_clip FROM Soundtracks JOIN Listing ON Listing.FilePath_clip = Soundtracks.FilePath_clip WHERE ID_list = 4"

        foreach (DataRow row in SongsPaths.Rows) //вытаскиваем все МP3 файлы from chosen playlist
        {
            string filename = (string)row["FilePath_clip"];
            StartCoroutine(LoadFile(filename));
        }
    }

    /// <summary> Процедура открытия окна проводника. </summary>
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

    /// <summary> Метод добавления трека в текущий плейлист и в плейлист по умолчанию. </summary>
    public void AddTrackToPlaylist()//добавляем трек в текущий(указанный) плейлист и в плейлист по умолчанию 1 - Unsorted
    {
        string nameList = playlistTitle;// из динамического дропдауна выютраем плейлист
        int idList = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"SELECT ID_list FROM Playlists WHERE Title_playlist = '{nameList}'"));//вытаскиваем ID плейлиста
        print($"Selected playlist ID {idList}, name {nameList}");//проверка
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
                lastInsertID_clip = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"INSERT INTO Soundtracks (FilePath_clip,Title_clip) VALUES('{fullPath}','{fileName}');SELECT last_insert_rowid()"));//а если файл уже есть в базе? иначе ошибка логики
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
                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_list,ID_clip) SELECT (SELECT ID_list FROM Playlists WHERE Title_playlist = 'Unsorted') AS ID_list, {lastInsertID_clip} AS ID_clip");//всегда добавляем в Unsorted// а если есть уже в Unsorted? - иначе ошибка логики БД
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
                lastInsertID_clip = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer($"INSERT INTO Soundtracks (FilePath_clip,Title_clip) VALUES('{fullPath}','{fileName}');SELECT last_insert_rowid()"));//а если файл уже есть в базе? иначе ошибка логики
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
                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_list,ID_clip) SELECT (SELECT ID_list FROM Playlists WHERE Title_playlist = 'Unsorted') AS ID_list, {lastInsertID_clip} AS ID_clip");//всегда добавляем в Unsorted// а если есть уже в Unsorted? - иначе ошибка логики БД
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

                PlayerDataBase.ExecuteQueryWithoutAnswer($"INSERT INTO Listing (ID_clip, ID_list) SELECT {lastInsertID_clip} AS ID_clip, {idList}");//добавляем в целевой плейлист   $"INSERT INTO Listing (ID_clip, ID_list) SELECT (SELECT ID_clip FROM Soundtracks WHERE FilePath_clip = '{fullPath}') AS ID_clip, ID_list FROM Playlists WHERE ID_list = {idList}"
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
        maxId = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer(("SELECT * FROM Playlists ORDER BY ID_list DESC LIMIT 1")));//для обновления динамического дропдауна
        ReloadSounds();//перезапись списка клипов
    }

    /// <summary> Метод удаления трека из текущего плейлиста. </summary>
    public void DeleteTrackFromPlaylist()//удаляем трек из указанного плейлиста
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
        maxId = Convert.ToInt32(PlayerDataBase.ExecuteQueryWithAnswer(("SELECT MAX(ID_list) FROM Playlists")));//для обновления динамического дропдауна
        ReloadSounds();//перезапись списка клипов
    }

    /// <summary> Метод создания нового плейлиста. </summary>
    public void CreatePlaylist()
        //создаем новый плейлист
    {
        try
        {
            //InputTitlePlaylist.SetActive(true);
            string nameList = InputTargetPlaylist.text;//принимаем от пользователя название нового плейлиста // по дефолту имя должно быть NewPlayList
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

    /// <summary> Метод перезаписи элементов(кнопок) списка плейлистов. </summary>
    private void ClearDropBox()
    {
        var clones = GameObject.FindGameObjectsWithTag("CloneBox");
        foreach (var clone in clones)
        {
            Destroy(clone);
        }
    }

    /// <summary> Метод удаления плейлиста. </summary>
    public void DropPlaylist()
        //удаляем выбранный плейлист
        //Плейлист Unsorted удалять нельзя!
    {
        try
        {
            //InputTitlePlaylist.SetActive(true);
            string nameList = InputTargetPlaylist.text;//как пример ввода от пользователя
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

    IEnumerator LoadFile(string path)//корутина для проигрывания аудиофайлов
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

    /// <summary> Метод отображения названия текущего трека. </summary>
    void ShowCurrentTitle()//для отображения названия трека во время проигрывания
    {
        if (source.isPlaying)
        {
            clipTitleText.text = source.clip.name;//вытаскиваем имя текущего проигрываемого клипа из компонента в строку
            fullLength = (int)source.clip.length;
        }
    }

    IEnumerator TimeTrackCounter()//корутина для счетчика времени проигрывания трека
    {
        while (source.isPlaying)//пока играет
        {
            playTime = (int)source.time;//читаем время трека, преобразую в целое для удобства создания счетчика
            ShowPlayTime();//вызываем функцию отображения времени и с каждым тиком обновляет строку времени
            yield return null;//бездействуем пока выполняется условие - трек играет
            progressSlider.value = (float)source.time;
        }
        if (flagNext == false) Next();
    }

    /// <summary> Метод отображения текущего времени проигрываемого трека. </summary>
    void ShowPlayTime()
    {
        seconds = playTime % 60;//секунды - остаток от деления длина трека на 60 секунд
        minutes = (playTime / 60) % 60;//минуты - нацело делим на 60 минут и остаток от деления 60 секунд 
        clipTimeText.text = minutes + ":" + seconds.ToString("D2") + "/" + ((fullLength / 60) % 60) + ":" + (fullLength % 60).ToString("D2");//вывод в строку времени
    }
}