using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class RecordManager : MonoBehaviour
{
    [Header("Mic Settings")]
    public string microphoneDevice = null; // null => varsayılan cihaz
    public int targetRate = 16000;         // hedef örnekleme hızı (Hz)
    public int recordLength = 10;          // kayıt tamponu uzunluğu (saniye)
    public bool debugLogs = true;

    private AudioSource _audioSource;
    private AudioClip _micClip;
    private int _micChannels = 1;
    private int _micSampleRate;
    private int _lastReadPos;

    private float[] _monoRing;
    private int _monoWrite;
    private int _monoRead;

    public Action<float[]> OnChunkReady16k;

    private int TargetSamplesPerChunk => targetRate / 2; // 0.5s chunk = 8000 örnek
    private static Stack<float[]> _pool = new Stack<float[]>();

    private bool permissionGranted = false;

    // Log spamini azaltmak için
    private int _lastLoggedMicPos = -1;
    private float _lastNoSampleLogTime = 0f;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.volume = 0f;
        _audioSource.mute = true;
        _audioSource.loop = false;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        if (debugLogs) Debug.Log("[RecordManager] Awake: AudioSource hazır.");
    }

    void Start()
    {
        StartCoroutine(CheckAndRequestMicPermission());
    }

    IEnumerator CheckAndRequestMicPermission()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            if (debugLogs) Debug.Log("[RecordManager] Mikrofon izni kontrol ediliyor...");
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            permissionGranted = true;
            if (debugLogs) Debug.Log("[RecordManager] Mikrofon izni verildi.");
            StartRecording();
        }
        else
        {
            permissionGranted = false;
            if (debugLogs) Debug.LogWarning("[RecordManager] Mikrofon izni reddedildi.");
        }
    }

    public void StartRecording()
    {
        if (!permissionGranted)
        {
            if (debugLogs) Debug.LogWarning("[RecordManager] Mikrofon izni yok.");
            return;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            if (debugLogs) Debug.LogWarning("[RecordManager] Mikrofon bulunamadı.");
            return;
        }

        // Cihazları logla ve gerekirse ilkini seç
        if (debugLogs)
        {
            Debug.Log("[RecordManager] Bulunan mikrofon cihazları:");
            for (int i = 0; i < Microphone.devices.Length; i++)
                Debug.Log($"  - {i}: {Microphone.devices[i]}");
        }
        if (string.IsNullOrEmpty(microphoneDevice))
        {
            microphoneDevice = Microphone.devices[0];
            if (debugLogs) Debug.Log($"[RecordManager] Varsayılan mikrofon seçildi: {microphoneDevice}");
        }

        if (_micClip != null)
        {
            if (debugLogs) Debug.Log("[RecordManager] Mevcut kayıt durduruluyor.");
            StopRecording();
        }

        // Cihaz kapasiteleri ve güvenli örnekleme hızı
        Microphone.GetDeviceCaps(microphoneDevice, out int minFreq, out int maxFreq);
        // min/max bazı cihazlarda 0,0 dönebilir => 44100 kullan
        if (minFreq == 0 && maxFreq == 0)
        {
            _micSampleRate = 44100;
        }
        else
        {
            // Hedef: output sample rate veya aralıktaki güvenli bir değer
            int desired = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            _micSampleRate = Mathf.Clamp(desired, minFreq == 0 ? desired : minFreq, maxFreq == 0 ? desired : maxFreq);
        }

        if (debugLogs) Debug.Log($"[RecordManager] Mikrofon başlatılıyor: {microphoneDevice}, {_micSampleRate}Hz");

        _micClip = Microphone.Start(microphoneDevice, true, recordLength, _micSampleRate);

        // Başlatma beklemesi: pozisyon ilerliyor mu kontrol et
        int safety = 0;
        int lastPos = -1;
        while (safety < 500)
        {
            int pos = Microphone.GetPosition(microphoneDevice);
            if (pos > 0 && pos != lastPos)
                break;
            lastPos = pos;
            System.Threading.Thread.Sleep(2);
            safety++;
        }

        // AudioSource ata (oynatmak şart değil; GetData ile okuyacağız)
        if (_micClip != null)
        {
            _audioSource.clip = _micClip;
            _audioSource.loop = false;
            _audioSource.mute = true;
            _audioSource.volume = 0f;
            // Oynatmak isterseniz açabilirsiniz; FMOD sorunlarında genelde gerekmez:
            // _audioSource.Play();
        }

        _micChannels = Mathf.Max(1, _micClip.channels);
        _lastReadPos = 0;

        int ringLen = Mathf.NextPowerOfTwo(_micSampleRate * Mathf.Max(1, recordLength));
        _monoRing = new float[ringLen];
        _monoWrite = 0;
        _monoRead = 0;

        if (debugLogs) Debug.Log($"[RecordManager] Kayıt başladı. Kanal: {_micChannels}, Buffer: {ringLen}");
    }

    public void StopRecording()
    {
        if (debugLogs) Debug.Log("[RecordManager] Kayıt durduruluyor...");

        if (_audioSource != null && _audioSource.isPlaying)
        {
            _audioSource.Stop();
            _audioSource.clip = null;
            if (debugLogs) Debug.Log("[RecordManager] AudioSource durduruldu.");
        }

        if (_micClip != null && Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
            if (debugLogs) Debug.Log("[RecordManager] Mikrofon kapatıldı.");
        }

        _micClip = null;
        _monoRing = null;

        if (debugLogs) Debug.Log("[RecordManager] Kayıt durduruldu.");
    }

    void Update()
    {
        if (_micClip == null) return;

        int micPos = Microphone.GetPosition(microphoneDevice);

        // Pozisyon değiştiğinde seyrek log
        if (debugLogs && micPos != _lastLoggedMicPos)
        {
            Debug.Log($"[RecordManager] Update: Mikrofon pozisyonu: {micPos}/{_micClip.samples}");
            _lastLoggedMicPos = micPos;
        }

        if (micPos < 0 || micPos > _micClip.samples) return;

        int totalSamples = _micClip.samples;
        int newSamples = micPos - _lastReadPos;
        if (newSamples < 0) newSamples += totalSamples;

        if (newSamples == 0)
        {
            // 0.5 saniyede bir kez bilgilendirme
            if (debugLogs && Time.realtimeSinceStartup - _lastNoSampleLogTime > 0.5f)
            {
                Debug.Log("[RecordManager] Update: Yeni örnek yok (newSamples=0)");
                _lastNoSampleLogTime = Time.realtimeSinceStartup;
            }
            return;
        }

        if (debugLogs) Debug.Log($"[RecordManager] Update: {newSamples} yeni örnek.");

        // Dairesel buffer'a yeni veriyi oku
        ReadSegment(_lastReadPos, Mathf.Min(newSamples, totalSamples - _lastReadPos));
        int overflow = newSamples - Mathf.Min(newSamples, totalSamples - _lastReadPos);
        if (overflow > 0) ReadSegment(0, overflow);

        _lastReadPos = micPos;

        // Downsample için gereken input
        int requiredInput = Mathf.CeilToInt(TargetSamplesPerChunk * (_micSampleRate / (float)targetRate));

        while (AvailableSamples() >= requiredInput)
        {
            var audioData = RentArray(requiredInput);
            DequeueMono(audioData, requiredInput);

            var downsampled = Downsample(audioData, _micSampleRate, targetRate);

            if (debugLogs) Debug.Log($"[RecordManager] Chunk hazır: {downsampled.Length} örnek.");

            OnChunkReady16k?.Invoke(downsampled);
        }
    }

    private void ReadSegment(int start, int length)
    {
        if (length <= 0) return;

        int sampleCount = length * _micChannels;
        var buffer = RentArray(sampleCount);
        _micClip.GetData(buffer, start);

        if (_micChannels == 1)
        {
            EnqueueMono(buffer, length);
        }
        else
        {
            var monoBuffer = RentArray(length);
            for (int i = 0, o = 0; i < sampleCount; i += _micChannels, o++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _micChannels; ch++) sum += buffer[i + ch];
                monoBuffer[o] = sum / _micChannels;
            }
            EnqueueMono(monoBuffer, length);
            ReturnArray(monoBuffer);
        }
        ReturnArray(buffer);
    }

    private void EnqueueMono(float[] data, int count)
    {
        int size = _monoRing.Length;
        for (int i = 0; i < count; i++)
        {
            _monoRing[_monoWrite] = data[i];
            _monoWrite = (_monoWrite + 1) & (size - 1);
            if (_monoWrite == _monoRead)
                _monoRead = (_monoRead + 1) & (size - 1);
        }
    }

    private void DequeueMono(float[] dest, int count)
    {
        int size = _monoRing.Length;
        for (int i = 0; i < count; i++)
        {
            if (_monoRead == _monoWrite) dest[i] = 0f;
            else
            {
                dest[i] = _monoRing[_monoRead];
                _monoRead = (_monoRead + 1) & (size - 1);
            }
        }
    }

    private int AvailableSamples()
    {
        int diff = _monoWrite - _monoRead;
        if (diff < 0) diff += _monoRing.Length;
        return diff;
    }

    private static float[] Downsample(float[] input, int src, int dst)
    {
        if (src == dst) return (float[])input.Clone();

        double ratio = (double)dst / src;
        int len = (int)(input.Length * ratio);
        var output = new float[len];
        for (int i = 0; i < len; i++)
        {
            double pos = i / ratio;
            int i0 = (int)pos;
            int i1 = Mathf.Min(i0 + 1, input.Length - 1);
            double t = pos - i0;
            output[i] = (float)((1 - t) * input[i0] + t * input[i1]);
        }
        return output;
    }

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
        if (debugLogs) Debug.Log("[RecordManager] OnDisable: Mikrofon durduruluyor.");
        StopRecording();
    }
}
