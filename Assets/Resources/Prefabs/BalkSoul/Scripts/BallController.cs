using System;
using System.Collections.Generic;

using UnityEngine;

public class BallController : FishAbility
{
    // 실제로 닿기 전에 미리 자르고 충돌을 끄도록 몸 반지름보다 넓게 살핀다.
    private const float CUT_REACH_SCALE = 1.5f;

    private static readonly int[] ATTACK_STATE_HASHES =
    {
        Animator.StringToHash("Attack_Light_1"),
        Animator.StringToHash("Attack_Light_2"),
        Animator.StringToHash("Attack_Light_3"),
    };

    [Header("참조")]
    [SerializeField] private Animator _animator;

    [Header("상태")]
    private int _nextAttackIndex = 1;
    private int _queuedAttacks;
    private TrailRenderer[] _trails;
    // 던질 때만 채워진다. 공격 중 자른 벽은 이번 투척 동안 부딪히지 않고 뚫고 지나간다.
    private PlayerController _player;
    private Collider[] _playerColliders = Array.Empty<Collider>();
    private readonly HashSet<CuttableWall> _cutWalls = new HashSet<CuttableWall>();
    private readonly List<Collider> _ignoredColliders = new List<Collider>();

    void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>();
        UpdateTrailWidths();
    }

    void FixedUpdate()
    {
        if (_player == null || !_player.IsThrown || _player.IsGameOver || !IsAttacking()) return;

        CutWallsAhead();
    }

    void Update()
    {
        // 앞 공격이 끝난 뒤에만 쌓인 다음 공격을 재생해 연타해도 모션이 끊기지 않게 한다.
        if (_queuedAttacks > 0 && IsAttackFinished()) PlayNextAttack();
    }

    void LateUpdate()
    {
        UpdateTrailWidths();
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
    /// 던질 개체가 될 때 벽을 자르는 데 쓸 플레이어와 그 충돌체들을 저장한다.
    /// 부모 PlayerController를 사용하며, _player와 _playerColliders를 변경한다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        _player = GetComponentInParent<PlayerController>();
        _playerColliders = _player.GetComponentsInChildren<Collider>(true);
    }

    /// <summary>
    /// 몸 전체를 절단면으로 삼아 이번 물리 단계에 닿을 벽을 원판 평면으로 자르고, 그 벽과의 충돌을 꺼서 뚫고 지나가게 한다.
    /// 원판(_animator가 붙은 몸)의 위치, 크기, 윗방향과 플레이어 속도를 사용하며, 벽 모양과 충돌 무시 상태, _cutWalls와 _ignoredColliders를 변경한다.
    /// </summary>
    private void CutWallsAhead()
    {
        Transform blade = _animator.transform;
        Vector3 center = blade.position;
        float radius = Mathf.Max(blade.lossyScale.x, blade.lossyScale.z) * 0.5f * CUT_REACH_SCALE;
        Vector3 next = center + _player.Velocity * Time.fixedDeltaTime;
        foreach (Collider collider in Physics.OverlapCapsule(center, next, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!collider.TryGetComponent(out CuttableWall wall) || !_cutWalls.Add(wall)) continue;

            wall.Cut(center, blade.up);
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

    /// <summary>
    /// 공격 1, 2, 3 중 하나를 재생하거나 그쪽으로 넘어가는 중인지 확인한다.
    /// _animator의 현재 상태와 다음 상태를 사용하며, 공격 중이면 true를 반환한다.
    /// </summary>
    private bool IsAttacking()
    {
        return IsAttackState(_animator.GetCurrentAnimatorStateInfo(0)) || IsAttackState(_animator.GetNextAnimatorStateInfo(0));
    }

    /// <summary>
    /// state가 공격 1, 2, 3 중 하나인지 확인한다.
    /// state의 이름 해시와 ATTACK_STATE_HASHES를 사용하며, 공격 상태면 true를 반환한다.
    /// </summary>
    private static bool IsAttackState(AnimatorStateInfo state)
    {
        return Array.IndexOf(ATTACK_STATE_HASHES, state.shortNameHash) >= 0;
    }

    /// <summary>
    /// 현재 오브젝트 크기에 맞춰 공격 궤적의 폭을 갱신한다.
    /// transform.localScale.x를 사용하며, 각 TrailRenderer의 widthMultiplier를 변경한다.
    /// </summary>
    private void UpdateTrailWidths()
    {
        float width = transform.localScale.x * 0.2f;
        foreach (TrailRenderer trail in _trails)
        {
            trail.widthMultiplier = width;
        }
    }

    /// <summary>
    /// 던진 뒤 SPACE를 누를 때마다 판정 결과와 관계없이 공격 하나를 대기열에 쌓는다.
    /// context는 쓰지 않으며, PlayAttackSequence로 _queuedAttacks를 늘린다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        PlayAttackSequence();
    }

    /// <summary>
    /// 공격 하나를 대기열에 쌓는다. 재생은 Update에서 앞 공격이 끝난 뒤 차례로 한다.
    /// 입력값은 없으며, _queuedAttacks를 늘린다.
    /// </summary>
    public void PlayAttackSequence()
    {
        _queuedAttacks++;
    }

    /// <summary>
    /// 대기한 공격 하나를 꺼내 공격 1, 2, 3 중 다음 애니메이션을 재생한다.
    /// _nextAttackIndex를 사용하며, _animator 상태와 다음 공격 번호, _queuedAttacks를 변경한다.
    /// </summary>
    private void PlayNextAttack()
    {
        _queuedAttacks--;
        _animator.CrossFadeInFixedTime($"Attack_Light_{_nextAttackIndex}", 0.05f, 0, 0f);
        _nextAttackIndex = _nextAttackIndex % 3 + 1;
    }

    /// <summary>
    /// 다음 공격을 재생해도 되는지 확인한다. 공격으로 넘어가는 중이거나 공격이 끝까지 재생되지 않았으면 기다린다.
    /// _animator의 전환 상태와 현재 상태의 재생 위치를 사용하며, 재생해도 되면 true를 반환한다.
    /// </summary>
    private bool IsAttackFinished()
    {
        if (_animator.IsInTransition(0)) return !IsAttackState(_animator.GetNextAnimatorStateInfo(0));
        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        return !IsAttackState(current) || current.normalizedTime >= 1f;
    }

    void OnDisable()
    {
        _nextAttackIndex = 1;
        _queuedAttacks = 0;
    }
}
