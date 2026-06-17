#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemySpawnPoint))]
public class EnemySpawnPointEditor : Editor
{
    private SerializedProperty enemyTypeProp;
    private SerializedProperty overridesProp;

    private void OnEnable()
    {
        enemyTypeProp = serializedObject.FindProperty("enemyType");
        overridesProp = serializedObject.FindProperty("overrides");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(enemyTypeProp);
        EditorGUILayout.Space(6);

        DrawCommonOverrides();

        EditorGUILayout.Space(8);
        DrawSpecificOverrides((EnemyType)enemyTypeProp.enumValueIndex);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCommonOverrides()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Common Overrides", EditorStyles.boldLabel);

        DrawOverridePair("overrideSpeed", "speed", "Speed");
        DrawOverridePair("overrideAttackRange", "attackRange", "Attack Range");
        DrawOverridePair("overrideAttackCooldown", "attackCooldown", "Attack Cooldown");
        DrawOverridePair("overrideAttackDamage", "attackDamage", "Attack Damage");
        DrawOverridePair("overrideMaxHealth", "maxHealth", "Max Health");
        DrawOverridePair("overrideIsBoss", "isBoss", "Is Boss");

        EditorGUILayout.EndVertical();
    }

    private void DrawSpecificOverrides(EnemyType type)
    {
        switch (type)
        {
            case EnemyType.Zalayty:
                DrawZalaytyOverrides();
                break;

            case EnemyType.Bee:
            case EnemyType.BeeEretic:
                DrawBeeOverrides();
                break;

            default:
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Specific Overrides", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No enemy-specific overrides for this type yet.", MessageType.Info);
                EditorGUILayout.EndVertical();
                break;
        }
    }

    private void DrawZalaytyOverrides()
    {
        SerializedProperty zalaytyProp = overridesProp.FindPropertyRelative("zalayty");
        if (zalaytyProp == null)
            return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Zalayty Overrides", EditorStyles.boldLabel);

        DrawNestedOverridePair(zalaytyProp, "overrideSqueezeCheckInterval", "squeezeCheckInterval", "Squeeze Check Interval");
        DrawNestedOverridePair(zalaytyProp, "overrideRepathInterval", "repathInterval", "Repath Interval");

        EditorGUILayout.EndVertical();
    }

    private void DrawBeeOverrides()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Bee Overrides", EditorStyles.boldLabel);

        // Flat fields on EnemySpawnOverrides (read by the Bee's spark, SparkShieldAwareClamp2D).
        DrawOverridePair("overrideSparkStun", "sparkStunSeconds", "Spark Stun Seconds");
        DrawOverridePair("overrideSparkKnockback", "sparkKnockbackVel", "Spark Knockback Vel");

        EditorGUILayout.EndVertical();
    }

    private void DrawOverridePair(string boolName, string valueName, string label)
    {
        SerializedProperty overrideProp = overridesProp.FindPropertyRelative(boolName);
        SerializedProperty valueProp = overridesProp.FindPropertyRelative(valueName);

        if (overrideProp == null || valueProp == null)
            return;

        EditorGUILayout.BeginHorizontal();
        overrideProp.boolValue = EditorGUILayout.ToggleLeft($"Override {label}", overrideProp.boolValue, GUILayout.Width(160));

        using (new EditorGUI.DisabledScope(!overrideProp.boolValue))
        {
            EditorGUILayout.PropertyField(valueProp, GUIContent.none, true);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawNestedOverridePair(SerializedProperty root, string boolName, string valueName, string label)
    {
        SerializedProperty overrideProp = root.FindPropertyRelative(boolName);
        SerializedProperty valueProp = root.FindPropertyRelative(valueName);

        if (overrideProp == null || valueProp == null)
            return;

        EditorGUILayout.BeginHorizontal();
        overrideProp.boolValue = EditorGUILayout.ToggleLeft($"Override {label}", overrideProp.boolValue, GUILayout.Width(200));

        using (new EditorGUI.DisabledScope(!overrideProp.boolValue))
        {
            EditorGUILayout.PropertyField(valueProp, GUIContent.none, true);
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif