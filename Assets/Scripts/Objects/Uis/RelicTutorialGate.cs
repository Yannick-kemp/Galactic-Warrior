using Assets.Scripts.Relics.Core;
using System.Collections;
using UnityEngine;

public class RelicTutorialGate : MonoBehaviour
{
    [Header("Tutorial Objects (root GameObjects)")]
    [SerializeField] private GameObject tutorialPowerCombo;
    [SerializeField] private GameObject tutorialShield;
    [SerializeField] private GameObject tutorialSprint;
    [SerializeField] private GameObject tutorialIceBall;

    [Header("Relic IDs (must match RelicDefinition.relicId, or SO name if relicId empty)")]
    [SerializeField] private string powerComboId;
    [SerializeField] private string shieldId;
    [SerializeField] private string sprintId;
    [SerializeField] private string iceBallId = "relic-ice-evolve";

    [Header("Rule")]
    [SerializeField] private int showWhenCountAtLeast = 2;

    private RelicManager _rm;

    IEnumerator Start()
    {
        while (_rm == null)
        {
            var w = Assets.Scripts.Characteres.WarriorController.Warrior.Instance
                 ?? GameMgr.Instance?.WarriorInstance;

            if (w != null) _rm = w.GetComponent<RelicManager>();
            yield return null;
        }

        _rm.OnRelicCountChanged += OnRelicCountChanged;
        RefreshAll();
    }

    void OnDestroy()
    {
        if (_rm != null) _rm.OnRelicCountChanged -= OnRelicCountChanged;
    }

    private void OnRelicCountChanged(RelicDefinition def, int newCount)
    {
        string id = (!string.IsNullOrEmpty(def.relicId)) ? def.relicId : def.name;

        if (id == powerComboId) SetVisible(tutorialPowerCombo, newCount >= showWhenCountAtLeast);
        if (id == shieldId) SetVisible(tutorialShield, newCount >= showWhenCountAtLeast);
        if (id == sprintId) SetVisible(tutorialSprint, newCount >= showWhenCountAtLeast);
        if (id == iceBallId) SetVisible(tutorialIceBall, newCount >= showWhenCountAtLeast);
    }

    private void RefreshAll()
    {
        SetVisible(tutorialPowerCombo, _rm.GetCountById(powerComboId) >= showWhenCountAtLeast);
        SetVisible(tutorialShield, _rm.GetCountById(shieldId) >= showWhenCountAtLeast);
        SetVisible(tutorialSprint, _rm.GetCountById(sprintId) >= showWhenCountAtLeast);
        SetVisible(tutorialIceBall, _rm.GetCountById(iceBallId) >= showWhenCountAtLeast);
    }

    private static void SetVisible(GameObject go, bool visible)
    {
        if (!go) return;
        if (go.activeSelf == visible) return;
        go.SetActive(visible);
    }
}