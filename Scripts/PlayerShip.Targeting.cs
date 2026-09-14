using Godot;
using System.Collections.Generic;

public partial class PlayerShip
{
	// Targeting
	private GimbalTarget _lockedTarget;
	private const float TARGET_MAX_RANGE = 2000f;
	public GimbalTarget LockedTarget => _lockedTarget;

	public const float MissileLockTime = 2f;
	private float _missileLockTimer = 0f;
	public float MissileLockProgress => _lockedTarget != null ? Mathf.Clamp(_missileLockTimer / MissileLockTime, 0f, 1f) : 0f;
	public bool IsMissileLocked => _lockedTarget != null && _missileLockTimer >= MissileLockTime;

	// Target HUD elements
	private Control _targetPanel;
	private ProgressBar _targetHealthBar;
	private CockpitReadout _targetNameReadout;
	private CockpitReadout _targetDistReadout;

	private void InitTargeting()
	{
		_targetPanel = canvasLayer.GetNodeOrNull<Control>("TargetPanel");
		if (_targetPanel != null)
		{
			_targetHealthBar = _targetPanel.GetNode<ProgressBar>("MarginContainer/VBoxContainer/TargetHealthBar");
			_targetPanel.Visible = false;
		}
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
			// Has a target - find a different one closer to center, or untarget
			GimbalTarget best = null;
			foreach (var (gt, _) in validTargets)
			{
				if (gt != _lockedTarget)
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
		if (_targetPanel == null) return;

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
			_targetPanel.Visible = false;
			if (_targetNameReadout != null) _targetNameReadout.Value = "---";
			if (_targetDistReadout != null) _targetDistReadout.Value = "---";
			return;
		}

		if (_missileLockTimer < MissileLockTime)
		{
			if (IsTargetInGimbalCone())
				_missileLockTimer += delta;
			else
				_missileLockTimer = 0f;
		}

		_targetPanel.Visible = true;
		float distance = GlobalPosition.DistanceTo(_lockedTarget.GlobalPosition);
		if (_targetNameReadout != null) _targetNameReadout.Value = _lockedTarget.GetDisplayName();
		if (_targetDistReadout != null) _targetDistReadout.Value = $"{distance:F0}m";

		_targetHealthBar.Visible = _lockedTarget.HasHealthData();
		if (_lockedTarget.HasHealthData())
		{
			_targetHealthBar.MaxValue = _lockedTarget.GetMaxHealth();
			_targetHealthBar.Value    = _lockedTarget.GetCurrentHealth();
		}
	}

	private bool IsTargetInGimbalCone()
	{
		if (_lockedTarget == null || barrel == null) return false;
		Vector3 toTarget = (_lockedTarget.GlobalPosition - barrel.GlobalPosition).Normalized();
		Vector3 forward = (-barrel.GlobalTransform.Basis.Z).Normalized();
		return forward.AngleTo(toTarget) <= Mathf.DegToRad(GimbalAngle);
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
		Vector3 targetVel = _lockedTarget.GetVelocity();

		float bulletWorldSpeed = BulletSpeed + Mathf.Max(0, Velocity.Dot((-GlobalTransform.Basis.Z).Normalized()));
		Vector3 leadPos = AimUtils.PredictIntercept(GlobalPosition, targetPos, targetVel, bulletWorldSpeed);

		if (camera.IsPositionBehind(leadPos))
			return null;

		return camera.UnprojectPosition(leadPos);
	}
}
