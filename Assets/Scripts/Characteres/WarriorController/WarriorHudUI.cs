using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WarriorHudUI : MonoBehaviour
{
    [SerializeField] private Assets.Scripts.Characteres.WarriorController.Warrior warrior;

    [Header("HP")]
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;

    //[Header("Shield")]
    //[SerializeField] private Image shieldFill;

    [Header("Cooldowns")]
    [SerializeField] private Image sprintCdFill;
    [SerializeField] private Image attack2CdFill;
    [SerializeField] private Image shieldCdFill;        

    [SerializeField] private float sprintCooldownTotal = 6f;
    [SerializeField] private float attack2CooldownTotal = 6f;
    [SerializeField] private float shieldCooldownTotal = 8f; 

    // cached values to avoid useless rebuild requests
    private float _lastShieldCd = -1f;

    private float _lastHp = -1f;
    //private float _lastShield = -1f;
    private float _lastSprintCd = -1f;
    private float _lastAtk2Cd = -1f;

    private bool _dirty;

    private void OnEnable()
    {
        // Don’t touch UI immediately in OnEnable → defer to next frame
        StartCoroutine(BindAndRefreshNextFrame());
    }

    private IEnumerator BindAndRefreshNextFrame()
    {
        yield return null; // wait one frame (avoids rebuild-loop timing)

        // bind warrior safely
        warrior = warrior ?? Assets.Scripts.Characteres.WarriorController.Warrior.Instance
                          ?? GameMgr.Instance?.WarriorInstance;

        _dirty = true; // mark for UI apply
    }

    private void LateUpdate()
    {
        if (warrior == null)
        {
            // keep trying to bind if Warrior spawns late
            warrior = Assets.Scripts.Characteres.WarriorController.Warrior.Instance
                   ?? GameMgr.Instance?.WarriorInstance;
            if (warrior == null) return;
            _dirty = true;
        }

        // If Unity is rebuilding UI right now, don't change graphics this frame
        if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
        {
            _dirty = true;
            return;
        }

        ApplyUI();
    }

    private void ApplyUI()
    {
        // HP
        float hp01 = (warrior.MaxHealth <= 0f) ? 0f : Mathf.Clamp01(warrior.CurrentHealth / warrior.MaxHealth);
        if (hpFill != null && !Mathf.Approximately(hp01, _lastHp))
        {
            _lastHp = hp01;
            hpFill.fillAmount = hp01;
        }

        if (hpText != null)
        {
            // update only if needed (optional)
            hpText.text = $"{Mathf.CeilToInt(warrior.CurrentHealth)}/{Mathf.CeilToInt(warrior.MaxHealth)}";
        }


        // Sprint CD (0 ready → fill 0, in CD → fill > 0)
        if (sprintCdFill != null)
        {
            float sprint01 = Mathf.Clamp01(warrior.SprintCooldownRemaining / Mathf.Max(0.01f, sprintCooldownTotal));
            if (!Mathf.Approximately(sprint01, _lastSprintCd))
            {
                _lastSprintCd = sprint01;
                sprintCdFill.fillAmount = sprint01;
            }
        }
        // Shield CD (0 ready → fill 0, in CD → fill > 0)
        if (shieldCdFill != null)
        {
            float sh01 = Mathf.Clamp01(warrior.ShieldRelicCooldownRemaining / Mathf.Max(0.01f, shieldCooldownTotal));
            if (!Mathf.Approximately(sh01, _lastShieldCd))
            {
                _lastShieldCd = sh01;
                shieldCdFill.fillAmount = sh01;
            }
        }

        // Attack2 CD
        if (attack2CdFill != null)
        {
            float atk201 = Mathf.Clamp01(warrior.Attack2CooldownRemaining / Mathf.Max(0.01f, attack2CooldownTotal));
            if (!Mathf.Approximately(atk201, _lastAtk2Cd))
            {
                _lastAtk2Cd = atk201;
                attack2CdFill.fillAmount = atk201;
            }
        }

        _dirty = false;
    }
}