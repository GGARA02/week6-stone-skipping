using System;
using System.Collections.Generic;

using UnityEngine;

public class KingHopakAbility : FishAbility
{
    // SPACE 한 번이 절단 상태를 이어 주는 시간. 연타하면 끊기지 않고 계속 자른다.
    private const float ACTIVE_DURATION = 0.5f;

    [Header("상태")]
    // 던질 때만 채워진다. 절단 상태에서 자른 벽은 이번 투척 동안 부딪히지 않고 뚫고 지나간다.
    private PlayerController _player;
    private Collider[] _playerColliders = Array.Empty<Collider>();
    private float _activeUntil = float.NegativeInfinity;
    private readonly HashSet<CuttableWall> _cutWalls = new HashSet<CuttableWall>();
    private readonly List<Collider> _ignoredColliders = new List<Collider>();

    void FixedUpdate()
    {
        if (_player == null || !_player.IsThrown || _player.IsGameOver || Time.time > _activeUntil) return;

        CutWallsAhead();
    }

    void OnDestroy()
    {
        // 다음 투척에서는 벽과 다시 부딪히게 한다. 씬을 닫는 중이면 벽이 먼저 사라졌을 수 있다.
        foreach (Collider wallCollider in _ignoredColliders)
        {
            if (wallCollider == null || !IsActive(wallCollider)) continue;
            SetIgnored(wallCollider, false);
        }
    }

    /// <summary>
    /// 던질 개체가 될 때 벽을 자르는 데 쓸 플레이어와 그 충돌체들을 저장하고, 양손 끝에 손이 닿는 벽을 자르는 커터를 붙인다.
    /// 부모 PlayerController와 mixamorig 손 뼈를 사용하며, _player, _playerColliders와 손의 WallCuttingTip을 변경한다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        _player = GetComponentInParent<PlayerController>();
        _playerColliders = _player.GetComponentsInChildren<Collider>(true);
        foreach (Transform tip in GetComponentsInChildren<Transform>())
        {
            if (tip.name == "mixamorig:LeftHand" || tip.name == "mixamorig:RightHand")
            {
                tip.gameObject.AddComponent<WallCuttingTip>().Initialize(_player);
            }
        }
    }

    /// <summary>
    /// SPACE 입력마다 절단 상태를 ACTIVE_DURATION만큼 이어 준다.
    /// context는 쓰지 않으며, _activeUntil을 변경한다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        _activeUntil = Time.time + ACTIVE_DURATION;
    }

    /// <summary>
    /// 몸 전체를 감싸는 범위에서 이번 물리 단계에 닿을 벽을 몸 중심의 납작한 평면으로 자르고, 그 벽과의 충돌을 꺼서 뚫고 지나가게 한다.
    /// 플레이어 충돌체 범위, 던질 거리의 윗방향과 플레이어 속도를 사용하며, 벽 모양과 충돌 무시 상태, _cutWalls와 _ignoredColliders를 변경한다.
    /// </summary>
    private void CutWallsAhead()
    {
        // 던질 때 팔까지 포함해 만든 볼록 충돌체가 몸 전체를 감싸므로 그 범위로 잰다.
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (Collider playerCollider in _playerColliders)
        {
            if (playerCollider == null || !IsActive(playerCollider) || playerCollider.isTrigger) continue;
            bounds.Encapsulate(playerCollider.bounds);
        }
        Vector3 center = bounds.center;
        Vector3 next = center + _player.Velocity * Time.fixedDeltaTime;
        foreach (Collider collider in Physics.OverlapCapsule(center, next, bounds.extents.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!collider.TryGetComponent(out CuttableWall wall) || !_cutWalls.Add(wall)) continue;

            // 던질 거리는 생성기 위에 눕혀져 있으므로 생성기의 윗방향이 몸의 납작한 면에 수직이다.
            wall.Cut(center, transform.parent.up);
            // 자르면 벽의 충돌체가 바뀌므로 자른 뒤에 남은 충돌체와의 충돌을 끈다.
            foreach (Collider wallCollider in wall.GetComponents<Collider>())
            {
                if (!IsActive(wallCollider)) continue;
                SetIgnored(wallCollider, true);
                _ignoredColliders.Add(wallCollider);
            }
        }
    }

    /// <summary>
    /// 켜져 있는 플레이어 충돌체들과 wallCollider 사이의 충돌을 끄거나 다시 켠다.
    /// wallCollider, ignore와 _playerColliders를 사용하며, 물리 충돌 무시 상태를 변경한다.
    /// </summary>
    private void SetIgnored(Collider wallCollider, bool ignore)
    {
        foreach (Collider playerCollider in _playerColliders)
        {
            // 던질 때 꺼 둔 물고기 자신의 충돌체는 건너뛴다.
            if (playerCollider == null || !IsActive(playerCollider)) continue;
            Physics.IgnoreCollision(playerCollider, wallCollider, ignore);
        }
    }

    /// <summary>
    /// collider가 켜져 있고 활성 오브젝트에 붙어 있는지 확인한다.
    /// collider의 enabled와 오브젝트 활성 상태를 사용하며, 충돌 무시를 걸 수 있으면 true를 반환한다.
    /// </summary>
    private static bool IsActive(Collider collider)
    {
        return collider.enabled && collider.gameObject.activeInHierarchy;
    }
}
