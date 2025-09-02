using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class RecordManager : MonoBehaviour
{
    [Header("Mic Settings")]
    public string microphoneDevice = null; // null => varsayılan cihaz [1]
    public int targetSampleRate = 16000;   // Whisper hedefi 16 kHz [2]
    public int recordLengthSec = 10;       // Döngüsel mic buffer uzunluğu (saniye) [1]
    public bool debugLogs = true;

    private AudioSource _audioSource;
    private AudioClip _micClip;
    private int _micChannels = 1;          // Çoğu cihazda 1; değilse mono’ya indirgeriz [1]
    private int _micSampleRate;            // Gerçek mic örnekleme hızı (44.1k/48k vs) [3]
    private int _lastReadPos = 0;          // Döngüsel klip için okuma imleci [4]

    // Ring buffer (mic SR, mono)
    private float[] _monoRing;
    private int _monoWrite;                // yazma imleci
    private int _monoRead;                 // okuma imleci

    // Çıkış: 16k mono dilimler
    public Action<float[]> OnChunkReady16k; // Dışa: Whisper’a beslemek için [2]

    // 0.5 sn @ 16k => 8000 örnek hedef çıkış [2]
    private int TargetChunkSamples16k => targetSampleRate / 2;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.mute = true; // geri beslemeyi önler [1]
    }

    public void StartRecording()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            if (debugLogs) Debug.LogWarning("No microphone devices found."); // [1]
            return;
        }

        // Gerçek SR tespiti (bazı cihazlar verilen SR'yi uygulamaz) [3]
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(microphoneDevice, out minFreq, out maxFreq); // bilgi amaçlı [1]
        _micSampleRate = AudioSettings.outputSampleRate; // pratik yaklaşım [3]
        if (_micSampleRate <= 0) _micSampleRate = 48000;

        // Mic’i başlat (looping clip) [1]
        _micClip = Microphone.Start(microphoneDevice, true, recordLengthSec, _micSampleRate);
        // İlk örnekler gelene kadar bekle (kısa spin) [1]
        int safety = 0;
        while (Microphone.GetPosition(microphoneDevice) <= 0 && safety++ < 200) { }

        _audioSource.clip = _micClip;
        _audioSource.loop = true;
        _audioSource.mute = true;
        _audioSource.Play(); // sessiz çal [1]

        _micChannels = Mathf.Max(1, _micClip.channels); // interleaved kanallar olabilir [2]
        _lastReadPos = 0;

        // Ring buffer boyutu: mic SR * birkaç saniye (burada recordLengthSec kadar) [4]
        int ringLen = Mathf.NextPowerOfTwo(_micSampleRate * Mathf.Max(1, recordLengthSec));
        _monoRing = new float[ringLen];
        _monoWrite = 0;
        _monoRead = 0;

        if (debugLogs) Debug.Log($"Mic started: dev='{microphoneDevice ?? "default"}', rate={_micSampleRate}Hz, len={recordLengthSec}s, ch={_micChannels}");
    }

    public void StopRecording()
    {
        if (_micClip != null && Microphone.IsRecording(microphoneDevice))
            Microphone.End(microphoneDevice); // [1]

        if (_audioSource.isPlaying)
            _audioSource.Stop();

        _micClip = null;
        _monoRing = null;

        if (debugLogs) Debug.Log("Mic stopped.");
    }

    void Update()
    {
        if (_micClip == null) return;

        // Döngüsel mic klibinden yeni yazılmış kısmı çek [4]
        int micPos = Microphone.GetPosition(microphoneDevice);
        if (micPos < 0 || micPos > _micClip.samples) return;

        int totalSamples = _micClip.samples;
        // Yeni veri uzunluğu (frame cinsinden)
        int newFrames = micPos - _lastReadPos;
        if (newFrames < 0)
            newFrames += totalSamples; // wrap-around [5]
        if (newFrames == 0) return;

        // Okumayı iki parçaya böl (wrap olabilir)
        ReadMicSegment(_lastReadPos, Mathf.Min(newFrames, totalSamples - _lastReadPos));
        int remaining = newFrames - (totalSamples - _lastReadPos);
        if (remaining > 0)
            ReadMicSegment(0, remaining);

        _lastReadPos = micPos;

        // Ring buffer’dan downsample için yeterli input var mı? [2]
        int needInput = Mathf.CeilToInt(TargetChunkSamples16k * (_micSampleRate / (float)targetSampleRate));
        while (AvailableMonoSamples() >= needInput)
        {
            // Input’u kopyala
            var tempIn = RentArray(needInput);
            DequeueMono(tempIn, needInput);

            // 16k downsample (lineer enterpolasyon) [2]
            var down = DownsampleLinear(tempIn, _micSampleRate, targetSampleRate);
            OnChunkReady16k?.Invoke(down); // dışa ver [2]
        }
    }

    // Mic klipten bir segmenti oku, mono’ya indir ve ring buffer’a yaz [2]
    private void ReadMicSegment(int startSample, int frames)
    {
        if (frames <= 0) return;

        int samplesToRead = frames * _micChannels;
        var temp = RentArray(samplesToRead);
        _micClip.GetData(temp, startSample); // interleaved PCM [2]

        // Interleaved -> mono ortalaması [2]
        if (_micChannels == 1)
        {
            EnqueueMono(temp, frames);
        }
        else
        {
            var monoTemp = RentArray(frames);
            for (int i = 0, o = 0; i < samplesToRead; i += _micChannels, o++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _micChannels; ch++) sum += temp[i + ch];
                monoTemp[o] = sum / _micChannels;
            }
            EnqueueMono(monoTemp, frames);
        }
    }

    // Ring buffer yardımcıları [4]
    private void EnqueueMono(float[] src, int count)
    {
        int n = _monoRing.Length;
        for (int i = 0; i < count; i++)
        {
            _monoRing[_monoWrite] = src[i];
            _monoWrite = (_monoWrite + 1) & (n - 1);
            // Overrun halinde read'i ileri al
            if (_monoWrite == _monoRead)
                _monoRead = (_monoRead + 1) & (n - 1);
        }
    }

    private void DequeueMono(float[] dst, int count)
    {
        int n = _monoRing.Length;
        for (int i = 0; i < count; i++)
        {
            if (_monoRead == _monoWrite) { dst[i] = 0f; continue; } // underflow koruması
            dst[i] = _monoRing[_monoRead];
            _monoRead = (_monoRead + 1) & (n - 1);
        }
    }

    private int AvailableMonoSamples()
    {
        int n = _monoRing.Length;
        int avail = _monoWrite - _monoRead;
        if (avail < 0) avail += n;
        return avail;
    }

    // Basit lineer enterpolasyonla downsample [2]
    public static float[] DownsampleLinear(float[] input, int srcRate, int dstRate)
    {
        if (srcRate == dstRate) return (float[])input.Clone();
        double ratio = (double)dstRate / srcRate;
        int outLen = (int)Math.Floor(input.Length * ratio);
        var output = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i / ratio;
            int i0 = (int)Math.Floor(srcPos);
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double t = srcPos - i0;
            output[i] = (float)((1.0 - t) * input[i0] + t * input[i1]);
        }
        return output;
    }

    // Basit array havuzu: GC tahsisini azaltır
    private static readonly Stack<float[]> _pool = new Stack<float[]>();
    private static float[] RentArray(int size)
    {
        if (_pool.Count > 0)
        {
            var arr = _pool.Pop();
            if (arr.Length >= size) return arr;
        }
        return new float[size];
    }
    private static void ReturnArray(float[] arr)
    {
        if (arr != null) _pool.Push(arr);
    }

    void OnDisable()
    {
        StopRecording();
    }
}
