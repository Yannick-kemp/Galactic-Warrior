using UnityEngine;

[CreateAssetMenu(menuName = "Galactic Warrior/Platform Theme")]
public class PlatformTheme : ScriptableObject
{
    [Header("Main Platform Sprite")]
    public Sprite mainSprite;

    [Header("Optional Overlay")]
    public Sprite overlaySprite;

    [Header("Materials")]
    public Material mainMaterial;
    public Material overlayMaterial;

    [Header("Tint")]
    public Color mainColor = Color.white;
    public Color overlayColor = Color.white;
}