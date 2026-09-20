using TMPro;
using UnityEngine;

/// <summary>
/// Drives the RelicMemory slot's CountText from the PERSISTENT boss-relic counter
/// held by GameMgr (distinct from the gameplay RelicManager counts).
///
/// In Variante 2 the RelicMemory slot is detached from RelicManager: disable/remove the
/// RelicUIButton on that slot (so it stops overwriting CountText from RelicManager.GetCount)
/// and add this component instead. The CountText then shows GameMgr.BossRelicCount at every
/// scene, and updates live via GameMgr.OnBossRelicCountChanged.
/// </summary>
[DisallowMultipleComponent]
public class BossMemoryCountDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text countText;

    private bool _subscribed;

    private void Awake()
    {
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        EnsureSubscribed();
        Refresh();
    }

    // GameMgr (DontDestroyOnLoad) may finish its Awake after this object's OnEnable on the very
    // first scene, so re-attempt the subscription/refresh in Start.
    private void Start()
    {
        EnsureSubscribed();
        Refresh();
    }

    private void OnDisable()
    {
        if (GameMgr.Instance != null)
            GameMgr.Instance.OnBossRelicCountChanged -= HandleChanged;

        _subscribed = false;
    }

    private void EnsureSubscribed()
    {
        if (_subscribed || GameMgr.Instance == null)
            return;

        GameMgr.Instance.OnBossRelicCountChanged -= HandleChanged; // guard against double-subscribe
        GameMgr.Instance.OnBossRelicCountChanged += HandleChanged;
        _subscribed = true;
    }

    private void HandleChanged(int newCount) => SetText(newCount);

    private void Refresh()
    {
        int count = GameMgr.Instance != null ? GameMgr.Instance.BossRelicCount : 0;
        SetText(count);
    }

    private void SetText(int count)
    {
        if (countText == null)
            return;

        countText.SetText("x{0}", Mathf.Max(0, count));
        countText.ForceMeshUpdate();
    }
}
