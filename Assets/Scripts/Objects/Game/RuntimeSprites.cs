using UnityEngine;

/// <summary>
/// Runtime-generated UI sprites, so on-screen controls do not depend on Unity's
/// builtin resource sprites (UI/Skin/*.psd), which are not loadable via
/// Resources.GetBuiltinResource in every Unity version / build.
/// </summary>
public static class RuntimeSprites
{
    private static Sprite _circle;
    private static Sprite _solid;

    /// <summary>Soft-edged filled white circle (for joysticks and round buttons).</summary>
    public static Sprite Circle()
    {
        if (_circle != null)
            return _circle;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float radius = size * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float a = Mathf.Clamp01((radius - d) / 2f); // ~2px anti-aliased edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _circle;
    }

    /// <summary>Plain opaque white sprite (tint it for flat rectangles).</summary>
    public static Sprite Solid()
    {
        if (_solid != null)
            return _solid;

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        _solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        return _solid;
    }
}
