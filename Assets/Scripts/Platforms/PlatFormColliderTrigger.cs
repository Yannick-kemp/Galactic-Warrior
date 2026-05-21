using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class PlatFormColliderTrigger : MonoBehaviour
{
    [Header("Collision")]
    public BoxCollider2D platformCollider;
    public BoxCollider2D platformTrigger;

    [Header("Platform Motion")]
    [Tooltip("True = moving/rotating platform subclasses run their scripted movement. False = this platform stays fixed in place while collision, landing, respawn, and passenger detection remain active.")]
    [SerializeField] private bool platformMotionEnabled = true;

    public bool PlatformMotionEnabled
    {
        get => platformMotionEnabled;
        set => platformMotionEnabled = value;
    }

    public void SetPlatformMotionEnabled(bool enabled)
    {
        PlatformMotionEnabled = enabled;
    }

    protected bool CanRunPlatformMotion => platformMotionEnabled;

    [Header("Visual Theme")]
    [Tooltip("The original SpriteRenderer directly on the platform root object. Example: Plf_bck_rotating SpriteRenderer.")]
    [SerializeField] private SpriteRenderer defaultRenderer;

    [Tooltip("The themed main SpriteRenderer. Example: VisualRoot/PlatformSprite.")]
    [SerializeField] private SpriteRenderer mainRenderer;

    [Tooltip("The themed overlay SpriteRenderer. Example: VisualRoot/OverlaySprite.")]
    [SerializeField] private SpriteRenderer overlayRenderer;

    [SerializeField] private PlatformTheme initialTheme;
    [SerializeField] private bool applyInitialThemeOnStart = true;

    [Tooltip("If true, the original/default SpriteRenderer is hidden when a theme sprite exists.")]
    [SerializeField] private bool hideDefaultRendererWhenThemeApplied = true;

    [Tooltip("If true, the script tries to find the root SpriteRenderer automatically if Default Renderer is not assigned.")]
    [SerializeField] private bool autoBindDefaultRenderer = true;

    [Tooltip("If true, the script tries to find child renderers named PlatformSprite and OverlaySprite when they are not assigned.")]
    [SerializeField] private bool autoBindThemeRenderers = true;

    private bool _defaultRendererInitialEnabled;
    private bool _defaultRendererStateCaptured;

    protected virtual void Start()
    {
        AutoBindVisualRenderersIfNeeded();
        CaptureDefaultRendererState();

        if (platformCollider != null)
            platformCollider.edgeRadius = 0.02f;

        if (applyInitialThemeOnStart && initialTheme != null)
            ApplyVisualTheme(initialTheme);
    }

    protected virtual void Update()
    {
    }

    public virtual void ApplyVisualTheme(PlatformTheme theme)
    {
        AutoBindVisualRenderersIfNeeded();
        CaptureDefaultRendererState();

        if (theme == null)
        {
            RestoreDefaultRenderer();
            return;
        }

        bool hasThemeVisual =
            theme.mainSprite != null ||
            theme.overlaySprite != null;

        if (defaultRenderer != null && hideDefaultRendererWhenThemeApplied)
        {
            if (defaultRenderer == mainRenderer || defaultRenderer == overlayRenderer)
            {
                Debug.LogWarning(
                    $"{name}: Default Renderer is also used as Main Renderer or Overlay Renderer. " +
                    "The script will not disable it because that would also hide the themed sprite.",
                    this);
            }
            else
            {
                // This disables only the root/default SpriteRenderer component.
                // It does NOT disable the platform GameObject, collider, or script.
                defaultRenderer.enabled = !hasThemeVisual;
            }
        }

        ApplyRendererTheme(mainRenderer, theme.mainSprite, theme.mainMaterial, theme.mainColor);
        ApplyRendererTheme(overlayRenderer, theme.overlaySprite, theme.overlayMaterial, theme.overlayColor);
    }

    private void ApplyRendererTheme(SpriteRenderer renderer, Sprite sprite, Material material, Color color)
    {
        if (renderer == null)
            return;

        if (sprite == null)
        {
            renderer.enabled = false;
            return;
        }

        renderer.enabled = true;
        renderer.sprite = sprite;
        renderer.color = color;

        if (material != null)
            renderer.material = material;
    }

    private void RestoreDefaultRenderer()
    {
        if (defaultRenderer != null)
            defaultRenderer.enabled = _defaultRendererInitialEnabled;

        if (mainRenderer != null)
            mainRenderer.enabled = false;

        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
    }

    private void CaptureDefaultRendererState()
    {
        if (_defaultRendererStateCaptured)
            return;

        _defaultRendererStateCaptured = true;

        if (defaultRenderer != null)
            _defaultRendererInitialEnabled = defaultRenderer.enabled;
    }

    private void AutoBindVisualRenderersIfNeeded()
    {
        if (autoBindDefaultRenderer && defaultRenderer == null)
            defaultRenderer = GetComponent<SpriteRenderer>();

        if (autoBindThemeRenderers)
        {
            if (mainRenderer == null)
                mainRenderer = FindChildSpriteRendererByName("PlatformSprite");

            if (overlayRenderer == null)
                overlayRenderer = FindChildSpriteRendererByName("OverlaySprite");
        }
    }

    private SpriteRenderer FindChildSpriteRendererByName(string childName)
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];

            if (renderer == null)
                continue;

            if (renderer.gameObject == gameObject)
                continue;

            if (renderer.name == childName)
                return renderer;
        }

        return null;
    }

    protected virtual void OnCollisionExit2D(Collision2D collision)
    {
        CharacterController character = collision.collider.GetComponentInParent<CharacterController>();

        if (character == null)
            return;

        if (character.CurrentplatForm != this)
            return;

        StartCoroutine(ClearPlatformIfReallyLeft(character));
    }

    protected virtual void OnCollisionEnter2D(Collision2D collision)
    {
        CharacterController character = collision.collider.GetComponentInParent<CharacterController>();

        if (character != null)
        {
            // Derived platform types can reject the landing when the character is
            // in a pass-through state. This prevents artificial seating / stopping
            // while Warrior or Zalayty should naturally continue the jump path.
            if (ShouldSkipArtificialPlatformLanding(character, collision))
                return;

            character.CurrentplatForm = this;

            if (character is Warrior w)
            {
                if (platformCollider != null && collision.transform.position.y > platformCollider.bounds.min.y)
                {
                    if (!Physics2D.GetIgnoreCollision(collision.collider, platformCollider))
                    {
                        w.CanMove = true;
                        w.IsFallingEdge = false;
                        w.IsFallingPlfExit = false;
                        w.IsFallingHitEnemy = false;
                        w.IsFallingGrazesEdge = false;
                        w.StopJumpTowardCoroutine();
                        w.StopMoveTowardCoroutine();
                        w._blockAction = false;
                        w.LastSafePlatform = this;
                    }
                }

                w._blockAction = false;
            }
            else if (character is Enemy enemy)
            {
                if (platformCollider != null && !Physics2D.GetIgnoreCollision(collision.collider, platformCollider))
                {
                    SeatCharacterOnTop(enemy);

                    enemy.StopJumpTowardCoroutine();

                    if (enemy is ZalaytyMonster zalayty)
                        zalayty.SetJumping(false);

                    if (enemy.rigidbody2 != null)
                    {
                        Vector2 v = enemy.rigidbody2.linearVelocity;
                        v.y = 0f;
                        enemy.rigidbody2.linearVelocity = v;

                        enemy.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
                    }

                    enemy.WaitAnimationDisplay();
                }
            }
        }
    }

    protected virtual bool ShouldSkipArtificialPlatformLanding(CharacterController character, Collision2D collision)
    {
        return false;
    }

    protected virtual void OnCollisionStay2D(Collision2D collision)
    {
        CharacterController character = collision.collider.GetComponentInParent<CharacterController>();

        if (character == null)
            return;

        character.CurrentplatForm = this;

        if (character is Warrior)
            return;

        if (character is ZalaytyMonster)
            return;

        if (character is Enemy enemy && enemy.rigidbody2 != null)
        {
            RigidbodyConstraints2D c = enemy.rigidbody2.constraints;
            c |= RigidbodyConstraints2D.FreezeRotation;
            c &= ~RigidbodyConstraints2D.FreezePositionY;
            enemy.rigidbody2.constraints = c;
        }
    }

    private IEnumerator ClearPlatformIfReallyLeft(CharacterController ch)
    {
        yield return new WaitForFixedUpdate();

        if (ch == null)
            yield break;

        if (IsAnyCharacterColliderTouchingPlatform(ch))
            yield break;

        if (ch is Warrior w && w.transform.parent == transform)
            yield break;

        if (ch.CurrentplatForm == this)
            ch.CurrentplatForm = null;
    }

    private bool IsAnyCharacterColliderTouchingPlatform(CharacterController ch)
    {
        if (ch == null || platformCollider == null)
            return false;

        Collider2D[] cols = ch.GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];

            if (col == null || col.isTrigger)
                continue;

            if (Physics2D.GetIgnoreCollision(platformCollider, col))
                continue;

            if (col.IsTouching(platformCollider))
                return true;
        }

        return false;
    }

    protected Collider2D GetStandingCollider(CharacterController character)
    {
        if (character is Enemy e && e.NormalCollider != null && e.NormalCollider.enabled)
            return e.NormalCollider;

        return character.collider2;
    }

    public virtual bool TryPrepareForPredictedTopLanding(CharacterController character, Collider2D predictedPlatformCollider)
    {
        if (character == null || platformCollider == null)
            return false;

        if (predictedPlatformCollider != platformCollider)
            return false;

        Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];

            if (col == null)
                continue;

            Physics2D.IgnoreCollision(platformCollider, col, false);
        }

        return true;
    }

    /// <summary>
    /// Zalayty-only request used when Zalayty intentionally jumps down from this platform
    /// toward a lower platform. The default implementation is intentionally generic:
    /// ignore this platform body now, then restore it as soon as Zalayty is no longer
    /// touching/overlapping this platformCollider.
    ///
    /// PlatFormPlfColliderTrigger overrides this with a stronger lock so its
    /// trigger/body-first rules cannot restore the source platform too early.
    /// </summary>
    public virtual bool RequestZalaytyJumpDownThroughSourcePlatform(ZalaytyMonster zalayty)
    {
        if (zalayty == null || platformCollider == null)
            return false;

        SetPlatformCollisionForCharacter(zalayty, ignore: true);
        StartCoroutine(RestoreZalaytySourcePlatformWhenBodyClear(zalayty));
        return true;
    }

    /// <summary>
    /// Zalayty-only emergency restore used when he lands on Warrior while the
    /// source platform is still temporarily ignored. This never changes Warrior
    /// platform behavior; it only restores this platform body against Zalayty.
    /// </summary>
    public virtual bool ForceRestoreZalaytyJumpDownSourcePlatform(ZalaytyMonster zalayty)
    {
        if (zalayty == null || platformCollider == null)
            return false;

        SetPlatformCollisionForCharacter(zalayty, ignore: false);
        return true;
    }

    private IEnumerator RestoreZalaytySourcePlatformWhenBodyClear(ZalaytyMonster zalayty)
    {
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        // Let the IgnoreCollision state reach the physics engine first.
        yield return wait;

        while (zalayty != null && platformCollider != null)
        {
            if (!IsAnyCharacterColliderTouchingOrOverlappingPlatformBody(zalayty))
                break;

            SetPlatformCollisionForCharacter(zalayty, ignore: true);
            yield return wait;
        }

        if (zalayty != null && platformCollider != null)
            SetPlatformCollisionForCharacter(zalayty, ignore: false);
    }

    protected void SetPlatformCollisionForCharacter(CharacterController character, bool ignore)
    {
        if (character == null || platformCollider == null)
            return;

        Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];

            if (col == null)
                continue;

            Physics2D.IgnoreCollision(platformCollider, col, ignore);
        }
    }

    protected bool IsAnyCharacterColliderTouchingOrOverlappingPlatformBody(CharacterController character, float contactSkin = 0.01f)
    {
        if (character == null || platformCollider == null)
            return false;

        Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];

            if (col == null || col.isTrigger)
                continue;

            ColliderDistance2D distance = Physics2D.Distance(col, platformCollider);

            if (distance.isOverlapped || distance.distance <= contactSkin)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Forces this platform to become pass-through for this character because the
    /// character has really lost the edge and must fall by Rigidbody2D gravity.
    /// This does not move or seat the character.
    /// </summary>
    public virtual void ForceCharacterToFallThroughSourcePlatform(CharacterController character)
    {
        if (character == null || platformCollider == null)
            return;

        SetPlatformCollisionForCharacter(character, ignore: true);
    }

    protected void SeatCharacterOnTop(CharacterController character, float safeMargin = 0.08f, float seatOffset = 0.02f)
    {
        if (character == null || platformCollider == null)
            return;


        Collider2D support = GetStandingCollider(character);

        if (support == null)
            return;

        Bounds pb = platformCollider.bounds;
        Bounds cb = support.bounds;

        float dx = 0f;

        float leftLimit = pb.min.x + safeMargin;
        float rightLimit = pb.max.x - safeMargin;

        if (cb.min.x < leftLimit)
            dx = leftLimit - cb.min.x;
        else if (cb.max.x > rightLimit)
            dx = rightLimit - cb.max.x;

        float dy = (pb.max.y + seatOffset) - cb.min.y;

        Vector2 delta = new Vector2(dx, dy);

        if (character.rigidbody2 != null)
            character.rigidbody2.MovePosition(character.rigidbody2.position + delta);
        else
            character.transform.position += (Vector3)delta;
    }
}