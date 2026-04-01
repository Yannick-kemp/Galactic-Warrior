using UnityEngine;
using UnityEngine.UI;

public class AudioMuteButtonUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Color unmutedColor = Color.white;
    [SerializeField] private Color mutedColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    private void Awake()
    {
        if (icon == null) icon = GetComponent<Image>();
        AudioMute.Apply();
        Refresh();
    }

    public void ToggleMute()
    {
        AudioMute.Toggle();
        Refresh();
    }

    private void Refresh()
    {
        icon.color = AudioMute.IsMuted ? mutedColor : unmutedColor;
    }
}