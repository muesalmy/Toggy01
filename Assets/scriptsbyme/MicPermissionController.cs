using UnityEngine;
using UnityEngine.Android;

public class MicPermissionController : MonoBehaviour
{
    // Unity sabiti: "android.permission.RECORD_AUDIO"
    private const string RECORD_AUDIO = Permission.Microphone;

    // Kullanım: RequestMicPermissionIfNeeded(
    //   onGranted: () => { /* StartRecording(); */ },
    //   onDenied:  () => { /* ShowRationalePanel(); */ }
    // );
    public void RequestMicPermissionIfNeeded(System.Action onGranted = null, System.Action onDenied = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 1) Zaten verilmiş mi?
        if (Permission.HasUserAuthorizedPermission(RECORD_AUDIO))
        {
            onGranted?.Invoke();
            return;
        }

        // 2) Callbacks hazırla
        var callbacks = new PermissionCallbacks();

        callbacks.PermissionGranted += perm =>
        {
            if (perm == RECORD_AUDIO)
            {
                Debug.Log("[Perm] RECORD_AUDIO granted.");
                onGranted?.Invoke();
            }
        };

        callbacks.PermissionDenied += perm =>
        {
            if (perm == RECORD_AUDIO)
            {
                Debug.LogWarning("[Perm] RECORD_AUDIO denied.");
                onDenied?.Invoke();
            }
        };

        callbacks.PermissionDeniedAndDontAskAgain += perm =>
        {
            if (perm == RECORD_AUDIO)
            {
                Debug.LogWarning("[Perm] RECORD_AUDIO denied with 'Don't ask again'. Guide user to Settings.");
                // Burada Ayarlar’a yönlendirme veya bilgilendirme gösterilebilir.
                onDenied?.Invoke();
            }
        };

        // 3) Gerekçe (rationale) gerekiyorsa önce kullanıcıya neden istendiğini anlatan UI gösterilebilir
        if (Permission.ShouldShowRequestPermissionRationale(RECORD_AUDIO))
        {
            Debug.Log("[Perm] ShouldShowRequestPermissionRationale = true, explaining to user...");
            // Kendi UI’nı gösterdikten sonra iste:
            Permission.RequestUserPermission(RECORD_AUDIO, callbacks);
        }
        else
        {
            // İlk kez isteniyordur veya 'bir daha sorma' tetiktir; yine de isteği gönder
            Permission.RequestUserPermission(RECORD_AUDIO, callbacks);
        }
#else
        // Editor ve diğer platformlarda izin diyaloğu yok: granted varsayımı
        Debug.Log("[Perm] Non-Android or Editor: treating as granted.");
        onGranted?.Invoke();
#endif
    }
}
