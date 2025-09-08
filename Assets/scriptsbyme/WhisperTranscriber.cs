using UnityEngine;
using System;
using System.Threading.Tasks;
using Whisper; // com.whisper.unity paketinin namespace'i

public class WhisperTranscriber : MonoBehaviour
{
    public WhisperManager whisperManager; // Inspector’da atayın
    public bool debugLogs = true;

    void Awake()
    {
#if UNITY_2023_1_OR_NEWER
        if (whisperManager == null) whisperManager = UnityEngine.Object.FindFirstObjectByType<WhisperManager>();
#else
        if (whisperManager == null) whisperManager = UnityEngine.Object.FindObjectOfType<WhisperManager>();
#endif
        if (debugLogs)
            Debug.Log(whisperManager != null
                ? "[WhisperTranscriber] WhisperManager sahnede bulundu."
                : "[WhisperTranscriber] WhisperManager bulunamadı! Sahneye ekleyin ve/veya Inspector’da atayın.");
    }

    // 16 kHz, mono float[] ile çağırın
    public async void TranscribeAsync(float[] pcm16k, Action<string> onDone, Action<string> onError = null)
    {
        if (whisperManager == null)
        {
            onError?.Invoke("WhisperManager atanmadı/bulunamadı.");
            if (debugLogs) Debug.LogWarning("[WhisperTranscriber] WhisperManager yok.");
            return;
        }

        try
        {
            if (debugLogs) Debug.Log("[WhisperTranscriber] Transcription (float[]) başlatılıyor...");

            WhisperResult res = await whisperManager.GetTextAsync(pcm16k, 16000, 1);
            string text = res != null ? res.Result : null;

            if (string.IsNullOrEmpty(text))
            {
                onError?.Invoke("Boş transkript döndü veya işlem başarısız.");
                if (debugLogs) Debug.LogWarning("[WhisperTranscriber] Boş transkript.");
                return;
            }

            if (debugLogs) Debug.Log($"[WhisperTranscriber] Transcription tamam: {text}");
            onDone?.Invoke(text);
        }
        catch (Exception ex)
        {
            if (debugLogs) Debug.LogError($"[WhisperTranscriber] Hata: {ex.Message}");
            onError?.Invoke(ex.Message);
        }
    }

    // Alternatif: 16 kHz mono AudioClip ile kullanmak isterseniz
    public async void TranscribeAsync(AudioClip clip16kMono, Action<string> onDone, Action<string> onError = null)
    {
        if (whisperManager == null)
        {
            onError?.Invoke("WhisperManager atanmadı/bulunamadı.");
            if (debugLogs) Debug.LogWarning("[WhisperTranscriber] WhisperManager yok.");
            return;
        }

        if (clip16kMono == null)
        {
            onError?.Invoke("AudioClip null.");
            return;
        }

        try
        {
            if (debugLogs) Debug.Log("[WhisperTranscriber] Transcription (AudioClip) başlatılıyor...");

            WhisperResult res = await whisperManager.GetTextAsync(clip16kMono);
            string text = res != null ? res.Result : null;

            if (string.IsNullOrEmpty(text))
            {
                onError?.Invoke("Boş transkript döndü veya işlem başarısız.");
                if (debugLogs) Debug.LogWarning("[WhisperTranscriber] Boş transkript.");
                return;
            }

            if (debugLogs) Debug.Log($"[WhisperTranscriber] Transcription tamam: {text}");
            onDone?.Invoke(text);
        }
        catch (Exception ex)
        {
            if (debugLogs) Debug.LogError($"[WhisperTranscriber] Hata: {ex.Message}");
            onError?.Invoke(ex.Message);
        }
    }
}
