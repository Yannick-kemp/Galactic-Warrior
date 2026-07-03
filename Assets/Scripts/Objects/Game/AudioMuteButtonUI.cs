using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AudioMuteButtonUI : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Color unmutedColor = Color.white;
    [SerializeField] private Color mutedColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    private void Awake()
    {
#if UNITY_STANDALONE
        // Desktop build (Steam): the touch mute button is a mobile affordance. Muting is
        // available from the Settings popup instead, so hide this on-screen button.
        gameObject.SetActive(false);
#else
        if (icon == null) icon = GetComponent<Image>();
        AudioMute.Apply();
        Refresh();
#endif
    }

    // Block world (gameplay) input the instant the button is pressed, before the
    // warrior's HandleInput can act on the same tap. Mirrors PauseButtonUI /
    // RelicUIButton / attack button convention. Covers mouse and touch via the
    // EventSystem pointer event.
    public void OnPointerDown(PointerEventData eventData)
    {
        Warrior warrior = Warrior.Instance;
        if (warrior == null)
            warrior = FindFirstObjectByType<Warrior>();

        if (warrior != null)
            warrior.NotifyUIConsumedInput();
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