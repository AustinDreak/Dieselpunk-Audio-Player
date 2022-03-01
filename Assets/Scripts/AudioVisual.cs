using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AudioManager;

public class AudioVisual : MonoBehaviour
{
    private const int sampleSize = 1024;
    private const float coefFreq = 0.5f;
    private const float coefDB = 0.1f;

    public float rmsValue;//средн€€ мощность вывода звука
    public float dbValue;//значение громкости на кадр в децибелах
    public float pitchValue;//высота тона

    public float backgroundIntesity;
    public Material backgroundMaterial;
    public Color minColor;
    public Color maxColor;

    public float maxVisualScale = 20.0f;//макс скейл кубов
    public float visualModifier = 150.0f;
    public float smoothSpeed = 10.0f;//плавность смены скейла
    public float keepPercentage = 0.25f;// по умолчанию 50% - дл€ урезани€ неактивных анимаций

    private AudioSource vSource;//сурс един на проект
    private float[] samples;//массив выборки//переделать массивы в списки??
    private float[] spectrum;//массив спектра
    private float sampleRate;//частота дискретизации

    private Transform[] visualList;
    private float[] visualScale;
    public int visualCount = 64;

    void Start()
    {
        vSource = gameObject.GetComponent<AudioSource>();//цепл€юсь к аудиоисточнику на аудио менеджере
        samples = new float[sampleSize];
        spectrum = new float[sampleSize];
        sampleRate = AudioSettings.outputSampleRate;//фактическа€ частота дискретизации
        SpawnCircle();
    }
    /// <summary> ћетод визуализации звука линией. </summary>
    private void SpawnLine()//визуализаци€ через линию
    {
        visualScale = new float[visualCount];
        visualList = new Transform[visualCount];

        for (int i = 0; i < visualCount; i++)
        {
            GameObject lineGo = GameObject.CreatePrimitive(PrimitiveType.Cube) as GameObject;//создаем игровой объект с примитивом
            visualList[i] = lineGo.transform;
            visualList[i].position = Vector3.right * i;//на один метр смещение
        }
    }

    private void Update()
    {
        AnalyzeSound();
        UpdateVisual();
        UpdateVisualBackground();
    }

    /// <summary> ћетод визуализации звука кругом. </summary>
    private void SpawnCircle()
    {
        visualScale = new float[visualCount];
        visualList = new Transform[visualCount];

        Vector3 center = Vector3.zero;//центр круга - задаем координаты
        float radius = 5.0f;//10 метров

        for(int i = 0; i < visualCount; i++)
        {
            float angle = i * 1.0f / visualCount;
            angle = angle * Mathf.PI * 2;
            float x = center.x + Mathf.Cos(angle) * radius;
            float y = center.y + Mathf.Sin(angle) * radius;

            Vector3 position =center +  new Vector3(x,y,0);
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube) as GameObject;//создаем кубы по кругу
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward, position);
            visualList[i] = go.transform;
        }
    }

    /// <summary> ћетод изменени€ объектов визуализации. </summary>
    private void UpdateVisual()
    {
        int visualIndex = 0;
        int spectrumIndex = 0;
        int averageSize = (int)((sampleSize * keepPercentage)/ visualCount);

        while(visualIndex < visualCount)
        {
            int j = 0;
            float sum = 0;
            while(j<averageSize)
            {
                sum += spectrum[spectrumIndex];
                spectrumIndex++;
                j++;
            }
            float scaleY = sum / averageSize * visualModifier;
            visualScale[visualIndex] -= Time.deltaTime * smoothSpeed;

            if (visualScale[visualIndex] > maxVisualScale) visualScale[visualIndex] = maxVisualScale;//ограничиваем мертвые визуализации

            if (visualScale[visualIndex] < scaleY) visualScale[visualIndex] = scaleY;
            visualList[visualIndex].localScale = Vector3.one + Vector3.up * visualScale[visualIndex];
            visualIndex++;
        }
    }

    /// <summary> ћетод визуализации завднего фона визуализации. </summary>
    private void UpdateVisualBackground()
    {
        backgroundIntesity -= Time.deltaTime * 0.5f * pitchValue;//может на другой коеф заменить?
        if (backgroundIntesity < dbValue/40) backgroundIntesity = dbValue/40;//либо на 20
        backgroundMaterial.color = Color.Lerp(maxColor, minColor, -backgroundIntesity);
    }

    /// <summary> ћетод анализа звука на кадр. </summary>
    private void AnalyzeSound()
    {
        vSource.GetOutputData(samples, 0);//берем из массива семплировани€ на кадр из канала 0

        int i = 0;
        float sum = 0;
        for(; i< sampleSize; i++)
        {
            sum = samples[i] * samples[i];
        }
        rmsValue = Mathf.Sqrt(sum / sampleSize);//вычисл€ем среднюю мощность вывода

        dbValue = 20 * Mathf.Log10(rmsValue / coefDB);//вычисл€ем вывод на кадр

        //получаем спектр звука
        vSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //получаем тональность
        float maxV = 0;
        var maxN = 0;
        for(i=0; i<sampleSize;i++)
        {
            if (!(spectrum[i] > maxV) || !(spectrum[i] > coefDB)) continue;
            maxV = spectrum[i];
            maxN = i;
        }

        float freqN = maxN;
        if(maxN>0 && maxN <sampleSize-1)
        {
            var dL = spectrum[maxN - 1] / spectrum[maxN];
            var dR = spectrum[maxN +1] / spectrum[maxN];
            freqN += coefFreq * (dR * dR - dL * dL);
        }
        pitchValue = freqN * (sampleRate / 2) / sampleSize;
    }
}