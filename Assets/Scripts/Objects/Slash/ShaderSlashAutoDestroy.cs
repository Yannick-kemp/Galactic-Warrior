using System.Collections;
using UnityEngine;

public class ShaderSlashAutoDestroy : MonoBehaviour
{
    [Header("Shader Settings")]
    [SerializeField] private string fadeProperty = "_Fade";
    [SerializeField] private float lifeTime = 0.25f;
    [SerializeField]
    private AnimationCurve fadeCurve =
        AnimationCurve.EaseInOut(0, 1, 1, 0);

    private MaterialPropertyBlock mpb;
    private Renderer rend;
    private float timer;

    void Awake()
    {
        rend = GetComponentInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        timer = 0f;
        StartCoroutine(LifeRoutine());
    }

    IEnumerator LifeRoutine()
    {
        while (timer < lifeTime)
        {
            timer += Time.deltaTime;
            float t = timer / lifeTime;

            rend.GetPropertyBlock(mpb);
            mpb.SetFloat(fadeProperty, fadeCurve.Evaluate(t));
            rend.SetPropertyBlock(mpb);

            yield return null;
        }

        Destroy(gameObject);
    }
}
