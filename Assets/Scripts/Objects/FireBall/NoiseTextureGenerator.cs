using UnityEngine;

/// <summary>
/// Generates a procedural noise texture for VFX shaders
/// Attach to a GameObject and call GenerateNoiseTexture() to create the texture
/// </summary>
public class NoiseTextureGenerator : MonoBehaviour
{
    [Header("Noise Settings")]
    [SerializeField] public int textureSize = 256;
    [SerializeField] public float noiseScale = 20f;
    [SerializeField] public int octaves = 4;
    [SerializeField] public float persistence = 0.5f;
    [SerializeField] public float lacunarity = 2f;

    [Header("Output")]
    [SerializeField] public bool generateOnStart = true;
    [SerializeField] public string textureName = "NoiseTexture";

    private Texture2D noiseTexture;

    void Start()
    {
        if (generateOnStart)
        {
            GenerateNoiseTexture();
        }
    }

    [ContextMenu("Generate Noise Texture")]
    public void GenerateNoiseTexture()
    {
        noiseTexture = GeneratePerlinNoise(textureSize, textureSize);
        noiseTexture.name = textureName;

        Debug.Log($"Generated noise texture: {textureName} ({textureSize}x{textureSize})");

#if UNITY_EDITOR
        SaveTextureAsAsset();
#endif
    }

    private Texture2D GeneratePerlinNoise(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;

        // Random offset for variation
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);

        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float noise = GenerateFBM(x, y, offsetX, offsetY);
                int index = y * width + x;
                pixels[index] = new Color(noise, noise, noise, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    private float GenerateFBM(float x, float y, float offsetX, float offsetY)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (x / textureSize * noiseScale * frequency) + offsetX;
            float sampleY = (y / textureSize * noiseScale * frequency) + offsetY;

            float perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
            value += perlinValue * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

#if UNITY_EDITOR
    private void SaveTextureAsAsset()
    {
        if (noiseTexture == null) return;

        string path = $"Assets/{textureName}.png";
        byte[] bytes = noiseTexture.EncodeToPNG();

        System.IO.File.WriteAllBytes(path, bytes);
        UnityEditor.AssetDatabase.Refresh();

        Debug.Log($"Saved noise texture to: {path}");
    }
#endif

    public Texture2D GetNoiseTexture()
    {
        return noiseTexture;
    }
}
