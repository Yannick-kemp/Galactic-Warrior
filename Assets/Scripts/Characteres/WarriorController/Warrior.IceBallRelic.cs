using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using Assets.Scripts.Relics.Projectiles;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
	public partial class Warrior : CharacterController
	{
		[Header("Ice Ball Relic")]
		[SerializeField] private Transform iceBallSpawnSocket;
		[SerializeField] private float iceTouchGuardDuration = 0.12f;
		[SerializeField] private float minIceAimDistance = 0.12f;
		[SerializeField] private bool faceIceTargetWhenCasting = true;

		private bool _iceBallArmed;
		private string _iceBallRelicId;
		private bool _iceBallConsumeOnCast;
		private IceBallRelic _armedIceBallDef;

		public bool IsIceBallArmed => _iceBallArmed;

		public bool TryArmIceBallRelic(IceBallRelic def, bool consumeOnCast)
		{
			if (def == null) return false;
			if (def.projectilePrefab == null)
			{
				Debug.LogWarning("[Warrior] IceBallRelic has no projectilePrefab assigned.", this);
				return false;
			}

			if (IsDead || CanDie) return false;
			if (_sprintActive) return false;
			if (_iceBallArmed) return false;

			_armedIceBallDef = def;
			_iceBallRelicId = !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
			_iceBallConsumeOnCast = consumeOnCast;
			_iceBallArmed = true;

			NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, iceTouchGuardDuration));
			return true;
		}

		private bool TryHandleArmedIceBallTouch()
		{
			if (!_iceBallArmed)
				return false;

			// IMPORTANT:
			// This touch is reserved for the relic.
			// So normal move / jump / attack must not happen from this touch.
			FireArmedIceBall();
			return true;
		}

		private void FireArmedIceBall()
		{
			if (!_iceBallArmed || _armedIceBallDef == null)
			{
				ClearArmedIceBall();
				return;
			}

			if (IsDead || CanDie)
				return;

			if (_iceBallConsumeOnCast)
			{
				var rm = GetComponent<RelicManager>();
				if (rm == null || !rm.TryConsumeById(_iceBallRelicId, 1))
				{
					ClearArmedIceBall();
					return;
				}
			}

			Vector3 spawnPos = GetIceBallSpawnPosition(_armedIceBallDef);
			Vector2 shootDir = GetIceBallDirection(spawnPos);

			if (faceIceTargetWhenCasting && Mathf.Abs(shootDir.x) > 0.01f)
				SetDirectionVariables(transform.position.x + shootDir.x);

			GameObject go = Instantiate(_armedIceBallDef.projectilePrefab, spawnPos, Quaternion.identity);

			IceBallProjectile projectile = go.GetComponent<IceBallProjectile>();
			if (projectile != null)
			{
				projectile.Init(
					this,
					shootDir,
					_armedIceBallDef.projectileSpeed,
					_armedIceBallDef.damage,
					_armedIceBallDef.stunSeconds,
					_armedIceBallDef.lifeTime
				);
			}
			else
			{
				Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
				if (rb != null)
				{
					rb.gravityScale = 0f;
					rb.linearVelocity = shootDir * _armedIceBallDef.projectileSpeed;
				}

				Destroy(go, _armedIceBallDef.lifeTime);
			}

			NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, iceTouchGuardDuration));
			ClearArmedIceBall();
		}

		private Vector3 GetIceBallSpawnPosition(IceBallRelic def)
		{
			Transform socket = iceBallSpawnSocket != null ? iceBallSpawnSocket : transform;
			Vector3 offset = def != null ? def.spawnLocalOffset : Vector3.zero;

			offset.x = rightFacing ? Mathf.Abs(offset.x) : -Mathf.Abs(offset.x);

			return socket.position + offset;
		}

		private Vector2 GetIceBallDirection(Vector3 spawnPos)
		{
			Vector2 dir = (Vector2)InputMgr.Instance.TouchedVector - (Vector2)spawnPos;

			if (dir.sqrMagnitude <= minIceAimDistance * minIceAimDistance)
				dir = rightFacing ? Vector2.right : Vector2.left;

			return dir.normalized;
		}

		private void ClearArmedIceBall()
		{
			_iceBallArmed = false;
			_iceBallRelicId = null;
			_iceBallConsumeOnCast = false;
			_armedIceBallDef = null;
		}
	}
}