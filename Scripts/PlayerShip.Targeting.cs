using Godot;
using System.Collections.Generic;

public partial class PlayerShip
{
	// Targeting
	private GimbalTarget _lockedTarget;
	private const float TARGET_MAX_RANGE = 2000f;
	// Missile lock, and switching away from a target, both need it this close to the nose.
	private const float TargetingConeDeg = 20f;
	public GimbalTarget LockedTarget => _lockedTarget;

	public const float MissileLockTime = 2f;
	private float _missileLockTimer = 0f;
	private const float MissileLockLossTime = 2f;
	private float _outOfConeTimer = 0f;
	public float MissileLockProgress => _lockedTarget != null ? Mathf.Clamp(_missileLockTimer / MissileLockTime, 0f, 1f) : 0f;
	public bool IsMissileLocked => _lockedTarget != null && _missileLockTimer >= MissileLockTime;

	// Target HUD elements
	private CockpitBar _targetHealthBar;
	private CockpitReadout _targetNameReadout;
	private CockpitReadout _targetDistReadout;

	private void InitTargeting()
	{
		_targetHealthBar = GetNodeOrNull<CockpitBar>("TargetHealthBar");
		_targetNameReadout = GetNodeOrNull<CockpitReadout>("TargetNameReadout");
		_targetDistReadout = GetNodeOrNull<CockpitReadout>("TargetDistReadout");
	}

	private void CycleTarget()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) { ClearTarget(); return; }

		Vector2 screenCenter = GetViewport().GetVisibleRect().Size / 2f;

		var gimbalTargets = GetTree().GetNodesInGroup("gimbal_targets");
		var validTargets = new List<(GimbalTarget target, float screenDist)>();

		foreach (var node in gimbalTargets)
		{
			if (node is not GimbalTarget gt || !gt.IsValid())
				continue;
			// Own ship now carries one of these too.
			if (gt.TargetOwner == this) continue;

			float worldDist = GlobalPosition.DistanceTo(gt.GlobalPosition);
			if (worldDist > TARGET_MAX_RANGE) continue;
			if (camera.IsPositionBehind(gt.GlobalPosition)) continue;

			Vector2 screenPos = camera.UnprojectPosition(gt.GlobalPosition);
			float screenDist = screenPos.DistanceTo(screenCenter);
			validTargets.Add((gt, screenDist));
		}

		if (validTargets.Count == 0)
		{
			ClearTarget();
			return;
		}

		// Sort by distance from screen center
		validTargets.Sort((a, b) => a.screenDist.CompareTo(b.screenDist));

		if (_lockedTarget == null || !IsInstanceValid(_lockedTarget))
		{
			SetTarget(validTargets[0].target);
		}
		else
		{
			// Has a target: the next nearest the centre if another sits in the cone, or untarget
			GimbalTarget best = null;
			foreach (var (gt, _) in validTargets)
			{
				if (gt != _lockedTarget && IsInTargetingCone(gt))
				{
					best = gt;
					break;
				}
			}

			if (best != null)
				SetTarget(best);
			else
				ClearTarget();
		}
	}

	// Steps through every target in range, nearest first, whichever way it lies.
	private void CycleAllTargets()
	{
		var targets = new List<GimbalTarget>();
		foreach (var node in GetTree().GetNodesInGroup("gimbal_targets"))
		{
			if (node is not GimbalTarget gt || !gt.IsValid() || gt.TargetOwner == this) continue;
			if (GlobalPosition.DistanceTo(gt.GlobalPosition) <= TARGET_MAX_RANGE) targets.Add(gt);
		}

		if (targets.Count == 0)
		{
			ClearTarget();
			return;
		}

		targets.Sort((a, b) => GlobalPosition.DistanceSquaredTo(a.GlobalPosition)
			.CompareTo(GlobalPosition.DistanceSquaredTo(b.GlobalPosition)));
		// Not in the list, or nothing locked, gives -1: the cycle starts at the nearest.
		int current = targets.IndexOf(_lockedTarget);
		SetTarget(targets[(current + 1) % targets.Count]);
	}

	private void SetTarget(GimbalTarget target)
	{
		if (_lockedTarget == target) return;

		if (_lockedTarget != null && IsInstanceValid(_lockedTarget))
			_lockedTarget.Died -= OnTargetDied;

		_lockedTarget = target;
		_missileLockTimer = 0f;

		if (_lockedTarget != null)
			_lockedTarget.Died += OnTargetDied;
	}

	private void ClearTarget()
	{
		if (_lockedTarget != null && IsInstanceValid(_lockedTarget))
			_lockedTarget.Died -= OnTargetDied;

		_lockedTarget = null;
		_missileLockTimer = 0f;
	}

	private void OnTargetDied()
	{
		_lockedTarget = null;
		_missileLockTimer = 0f;
	}

	private void UpdateTargetHUD(float delta)
	{
		// Validate target is still alive and in range
		if (_lockedTarget != null)
		{
			if (!IsInstanceValid(_lockedTarget) || !_lockedTarget.IsValid())
			{
				ClearTarget();
			}
			else
			{
				float dist = GlobalPosition.DistanceTo(_lockedTarget.GlobalPosition);
				if (dist > TARGET_MAX_RANGE)
					ClearTarget();
			}
		}

		if (_lockedTarget == null)
		{
			_targetHealthBar?.SetFraction(null);
			if (_targetNameReadout != null) _targetNameReadout.Value = "---";
			if (_targetDistReadout != null) _targetDistReadout.Value = "---";
			return;
		}

		bool inCone = IsInTargetingCone(_lockedTarget);
		if (!IsMissileLocked)
		{
			_missileLockTimer = inCone ? _missileLockTimer + delta : 0f;
			_outOfConeTimer = 0f;
		}
		else if (inCone)
		{
			_outOfConeTimer = 0f;
		}
		else if ((_outOfConeTimer += delta) >= MissileLockLossTime)
		{
			_missileLockTimer = 0f;
		}

		float distance = GlobalPosition.DistanceTo(_lockedTarget.GlobalPosition);
		if (_targetNameReadout != null) _targetNameReadout.Value = _lockedTarget.GetDisplayName();
		if (_targetDistReadout != null) _targetDistReadout.Value = $"{distance:F0}m";

		_targetHealthBar?.SetFraction(_lockedTarget.HasHealthData()
			? _lockedTarget.GetCurrentHealth() / _lockedTarget.GetMaxHealth()
			: null);
	}

	private bool IsInTargetingCone(GimbalTarget target)
	{
		Vector3 toTarget = target.GlobalPosition - GlobalPosition;
		return (-GlobalTransform.Basis.Z).AngleTo(toTarget) <= Mathf.DegToRad(TargetingConeDeg);
	}

	public Vector2? GetLockedTargetScreenPos()
	{
		if (_lockedTarget == null || !IsInstanceValid(_lockedTarget))
			return null;

		var camera = GetViewport().GetCamera3D();
		if (camera == null) return null;

		if (camera.IsPositionBehind(_lockedTarget.GlobalPosition))
			return null;

		return camera.UnprojectPosition(_lockedTarget.GlobalPosition);
	}

	public Vector2? GetLeadTargetScreenPos()
	{
		if (_lockedTarget == null || !IsInstanceValid(_lockedTarget))
			return null;

		var camera = GetViewport().GetCamera3D();
		if (camera == null) return null;

		Vector3 targetPos = _lockedTarget.GlobalPosition;
		// Bullets leave with this ship's velocity, so the lead is on the target's relative motion.
		Vector3 relativeVel = _lockedTarget.GetVelocity() - Velocity;
		Vector3 leadPos = AimUtils.PredictIntercept(GlobalPosition, targetPos, relativeVel, BulletSpeed);

		if (camera.IsPositionBehind(leadPos))
			return null;

		return camera.UnprojectPosition(leadPos);
	}
}
