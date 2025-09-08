using UnityEngine;
using System.Collections.Generic;

public class AudioChunkListener : MonoBehaviour
{
    [Header("Settings")]
    public RecordManager recordManager; // Inspector'da bağlayın
    public bool debugLogs = true;

    private List<float[]> audioChunks = new List<float[]>();
    private int totalSamples = 0;

    // Transcriber referansı
    private WhisperTranscriber transcriber;

    void Start()
    {
        // RecordManager'ın OnChunkReady16k event'ine abone ol
        if (recordManager != null)
        {
            recordManager.OnChunkReady16k += OnAudioChunkReceived;
            if (debugLogs) Debug.Log("[AudioChunkListener] OnChunkReady16k event'ine abone olundu.");
        }
        else
        {
            Debug.LogWarning("[AudioChunkListener] RecordManager referansı bulunamadı!");
        }

        // Transcriber'ı bul
#if UNITY_2023_1_OR_NEWER
        transcriber = UnityEngine.Object.FindFirstObjectByType<WhisperTranscriber>();
#else
        transcriber = UnityEngine.Object.FindObjectOfType<WhisperTranscriber>();
#endif
        if (transcriber == null)
            Debug.LogWarning("[AudioChunkListener] WhisperTranscriber sahnede yok. Lütfen ekleyin ve WhisperManager bağlı olsun.");
    }

    private void OnAudioChunkReceived(float[] audioData)
    {
        if (debugLogs) 
            Debug.Log($"[AudioChunkListener] Ses chunk'ı alındı: {audioData.Length} örnek, toplam: {totalSamples + audioData.Length}");

        // Ses verisini kopyala ve listeye ekle
        float[] chunkCopy = new float[audioData.Length];
        System.Array.Copy(audioData, chunkCopy, audioData.Length);
        audioChunks.Add(chunkCopy);
        totalSamples += audioData.Length;

        // Amplitüd kontrolü (ses seviyesi)
        float averageAmplitude = 0f;
        foreach (float sample in audioData) averageAmplitude += Mathf.Abs(sample);
        averageAmplitude /= audioData.Length;

        if (debugLogs && averageAmplitude > 0.01f) // Aktif ses algılandığında
            Debug.Log($"[AudioChunkListener] Aktif ses algılandı, amplitüd: {averageAmplitude:F4}");

        // 3 saniye ses toplandığında işle
        if (totalSamples >= 16000 * 3) // 16kHz * 3 saniye = 48000 örnek
            ProcessAccumulatedAudio();
    }

    private void ProcessAccumulatedAudio()
    {
        if (audioChunks.Count == 0) return;

        // Tüm chunk'ları birleştir
        float[] combinedAudio = new float[totalSamples];
        int currentIndex = 0;

        foreach (var chunk in audioChunks)
        {
            System.Array.Copy(chunk, 0, combinedAudio, currentIndex, chunk.Length);
            currentIndex += chunk.Length;
        }

        if (debugLogs) 
            Debug.Log($"[AudioChunkListener] Birleştirilmiş ses verisi hazır: {combinedAudio.Length} örnek ({combinedAudio.Length / 16000f:F1} saniye)");

        // Whisper'a gönder
        SendToWhisper(combinedAudio);

        // Buffer'ı temizle
        ClearAudioBuffer();
    }

    private void SendToWhisper(float[] audioData)
    {
        if (debugLogs) 
            Debug.Log($"[AudioChunkListener] Whisper'a gönderilmeye hazır: {audioData.Length} örnek");

        if (transcriber == null)
        {
            Debug.LogWarning("[AudioChunkListener] Transcriber yok, transkripsiyon atlandı.");
            return;
        }

        transcriber.TranscribeAsync(
            audioData,
            onDone: text => Debug.Log($"[AudioChunkListener] Transkript: {text}"),
            onError: err => Debug.LogError($"[AudioChunkListener] Transkripsiyon hatası: {err}")
        );
    }

    public void ClearAudioBuffer()
    {
        audioChunks.Clear();
        totalSamples = 0;
        if (debugLogs) Debug.Log("[AudioChunkListener] Ses buffer'ı temizlendi.");
    }

    // Manuel tetikleme
    public void SendCurrentBufferToWhisper()
    {
        if (audioChunks.Count > 0) ProcessAccumulatedAudio();
        else if (debugLogs) Debug.Log("[AudioChunkListener] Gönderilecek ses verisi yok.");
    }

    void OnDestroy()
    {
        // Event aboneliğini iptal et
        if (recordManager != null)
        {
            recordManager.OnChunkReady16k -= OnAudioChunkReceived;
            if (debugLogs) Debug.Log("[AudioChunkListener] Event aboneliği iptal edildi.");
        }
    }
}
