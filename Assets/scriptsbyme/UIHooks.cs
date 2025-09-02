using UnityEngine;

public class UIHooks : MonoBehaviour
{
    [Header("References")]
    public MicPermissionController micPerm;  // Inspector: PermissionManager (MicPermissionController) sürükle
    public GameObject rationalePanel;        // Inspector: RationalePanel sürükle

    void Start()
    {
        // Panel başlangıçta kapalı
        if (rationalePanel != null)
            rationalePanel.SetActive(false);
    }

    // “Kaydı Başlat” butonu
    public void OnPressStart()
    {
        if (rationalePanel != null)
            rationalePanel.SetActive(false);

        if (micPerm == null)
        {
            Debug.LogWarning("MicPermissionController reference is missing on UIHooks.");
            return;
        }

        micPerm.RequestMicPermissionIfNeeded(
            onGranted: () =>
            {
                Debug.Log("Mic permission granted (OnPressStart)");
                // TODO (Aşama 3.2): StartRecording(); // mikrofon yakalama + 16 kHz mono downsample
            },
            onDenied: () =>
            {
                Debug.Log("Mic permission denied (OnPressStart)");
                if (rationalePanel != null)
                    rationalePanel.SetActive(true);
            }
        );
    }

    // “Tekrar Dene” butonu (panel üzerindeyken)
    public void OnPressRetry()
    {
        if (micPerm == null)
        {
            Debug.LogWarning("MicPermissionController reference is missing on UIHooks.");
            return;
        }

        micPerm.RequestMicPermissionIfNeeded(
            onGranted: () =>
            {
                Debug.Log("Mic permission granted (OnPressRetry)");
                if (rationalePanel != null)
                    rationalePanel.SetActive(false);
                // TODO (Aşama 3.2): StartRecording();
            },
            onDenied: () =>
            {
                Debug.Log("Mic permission still denied (OnPressRetry)");
                // Gerekirse burada “Ayarlar’dan izin verin” bilgisini göster
                if (rationalePanel != null)
                    rationalePanel.SetActive(true);
            }
        );
    }
}
