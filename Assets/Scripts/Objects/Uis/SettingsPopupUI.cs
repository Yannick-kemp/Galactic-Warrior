using UnityEngine;
using UnityEngine.UI;

public class SettingsPopupUI : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private Image muteIcon;

    [Header("Icon colors")]
    [SerializeField] private Color unmutedColor = Color.white;
    [SerializeField] private Color mutedColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    private bool muted;

    private void Awake()
    {
        closeButton.onClick.AddListener(Hide);
        muteButton.onClick.AddListener(ToggleMute);

        AudioMute.Apply();
        RefreshIcon();
        Hide();
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);

    private void ToggleMute()
    {
        AudioMute.Toggle();
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (muteIcon != null)
            muteIcon.color = AudioMute.IsMuted ? mutedColor : unmutedColor;
    }
}