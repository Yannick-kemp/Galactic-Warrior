using UnityEngine;
using Assets.Scripts.Characteres.WarriorController;

namespace Assets.Scripts.Platforms
{
    public class MovingVerticalPlatform : PlatFormPlfColliderTrigger
    {
        [Header("Relative Limits (Offset from Start)")]
        [SerializeField] private float relativeMinY = -2f; // Downward distance
        [SerializeField] private float relativeMaxY = 5f;  // Upward distance
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private bool startsMovingUp = true;

        [Header("System")]
        [SerializeField] private string respawnId;

        private float _worldMinY;
        private float _worldMaxY;
        private bool _isMovingUp;

        public string RespawnId => respawnId;
        public bool IsMovingUpNow => _isMovingUp;

        protected override void Start()
        {
            base.Start();

            // Calculate the absolute world positions based on where you placed it
            float startY = transform.position.y;
            _worldMinY = startY + relativeMinY;
            _worldMaxY = startY + relativeMaxY;

            // Simple safety: Ensure Min is actually the lower value
            if (_worldMinY > _worldMaxY)
            {
                float temp = _worldMinY;
                _worldMinY = _worldMaxY;
                _worldMaxY = temp;
            }

            _isMovingUp = startsMovingUp;

            if (string.IsNullOrEmpty(respawnId))
                respawnId = $"VP_{name}_{transform.position.x:F1}_{_worldMinY:F1}";
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            HandleMovement();
        }

        private void HandleMovement()
        {
            float targetY = _isMovingUp ? _worldMaxY : _worldMinY;
            Vector3 currentPos = transform.position;

            float newY = Mathf.MoveTowards(currentPos.y, targetY, moveSpeed * Time.fixedDeltaTime);
            transform.position = new Vector3(currentPos.x, newY, currentPos.z);

            if (Mathf.Abs(newY - targetY) < 0.001f)
            {
                _isMovingUp = !_isMovingUp;
            }
        }

        // --- Character Parenting (The "Hook") ---

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            base.OnCollisionEnter2D(collision);

            if (collision.contactCount > 0 && collision.contacts[0].normal.y < -0.5f)
            {
                var character = collision.collider.GetComponent<CharacterController>();
                if (character != null)
                {
                    character.transform.SetParent(transform);
                }
            }
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            var character = collision.collider.GetComponent<CharacterController>();
            if (character != null && character.transform.parent == transform)
            {
                character.transform.SetParent(null);
            }

            base.OnCollisionExit2D(collision);
        }
        // Inside MovingVerticalPlatform.cs

        public Vector3 GetSurfacePosition()
        {
            // Force the physics engine to acknowledge the current transform.position 
            // before GameMgr reads the bounds.
            Physics2D.SyncTransforms();

            if (platformCollider == null) return transform.position;

            return new Vector3(
                platformCollider.bounds.center.x,
                platformCollider.bounds.max.y,
                transform.position.z
            );
        }
        // --- Editor Visualizer ---
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 center = transform.position;

            // In the Editor, show the path relative to current position
            // Note: If you move the platform in the editor, the gizmo follows it
            Vector3 top = new Vector3(center.x, center.y + relativeMaxY, center.z);
            Vector3 bottom = new Vector3(center.x, center.y + relativeMinY, center.z);

            Gizmos.DrawLine(top, bottom);
            Gizmos.DrawCube(top, new Vector3(1, 0.1f, 0.1f));
            Gizmos.DrawCube(bottom, new Vector3(1, 0.1f, 0.1f));
        }
    }
}