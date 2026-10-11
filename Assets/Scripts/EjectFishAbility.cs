using UnityEngine;

public class EjectFishAbility : FishAbility
{
    [Header("사출 대상")]
    [SerializeField] private Fish _fishPrefab;
    [SerializeField] private Transform _origin;

    [Header("성공 조건")]
    [SerializeField] private bool _perfectOnly;
    [Tooltip("한 번 던지는 동안의 최대 사출 횟수. 0이면 제한하지 않는다")]
    [Min(0)]
    [SerializeField] private int _maxEjections;
    private int _ejectionCount;

    [Header("사출 속도")]
    [Tooltip("X/Z는 사출 기준의 방향이며, Y는 수면 기준 위쪽 속도다")]
    [SerializeField] private Vector3 _localVelocity = new Vector3(0f, 12f, 0f);
    [Tooltip("Spacebar 사출 시 진행(전방) 방향으로 부여할 추가 수평 속도")]
    [SerializeField] private float _forwardSpeed = 15f;
    [Tooltip("Spacebar 사출 시 좌우 등으로 퍼지는 무작위 수평 편차 속도")]
    [SerializeField] private float _horizontalSpreadSpeed = 4f;
    [SerializeField] private float _inheritVelocity = 0.35f;

    [Header("충돌 사출")]
    [Tooltip("장애물(벽) 충돌 시 사출할 개수 (0이면 충돌 시 미사출)")]
    [SerializeField] private int _collisionEjectCount = 6;
    [Tooltip("충돌 시 사방으로 튀어나갈 무작위 수평 속도")]
    [SerializeField] private float _collisionScatterSpeed = 30f;

    public Fish FishPrefab => _fishPrefab;

    /// <summary>
    /// 성공 판정과 사출 횟수 조건을 만족하면 기준 위치에서 물고기를 사출한다.
    /// context와 judge, Inspector 속도를 사용하며, 진행 방향 수평 속도와 수직 속도를 조합해 사출한다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        Transform origin = _origin != null ? _origin : transform;

        // 플레이어 진행 방향의 수평 벡터를 구하고, 없으면 플레이어 정면을 사용한다.
        Vector3 forward = Vector3.ProjectOnPlane(context.Player.Velocity, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.ProjectOnPlane(context.Player.transform.forward, Vector3.up);
        }
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // 진행 방향 수평 속도와 좌우 무작위 편차를 계산한다.
        Vector3 horizontalVelocity = forward * _forwardSpeed
            + right * Random.Range(-_horizontalSpreadSpeed, _horizontalSpreadSpeed)
            + Vector3.ProjectOnPlane(context.Player.Velocity, Vector3.up) * _inheritVelocity;

        Vector3 velocity = horizontalVelocity
            + origin.TransformDirection(new Vector3(_localVelocity.x, 0f, _localVelocity.z))
            + Vector3.up * _localVelocity.y;

        TryEjectFrom(context, judge, origin, velocity, origin.lossyScale);
    }

    /// <summary>
    /// 장애물(벽)과 충돌했을 때 기준 위치에서 사방 수평 및 위쪽으로 여러 개의 물고기를 사출한다.
    /// context와 collision을 사용하며, _collisionEjectCount만큼 사방으로 분산된 수평 속도를 부여해 사출한다.
    /// </summary>
    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        base.OnObstacleCollision(context, collision);

        Transform origin = _origin != null ? _origin : transform;
        if (_collisionEjectCount <= 0) return;

        float angleStep = 360f / _collisionEjectCount;
        float baseAngle = Random.Range(0f, 360f);

        // 충돌 시 사방(360도 수평)으로 튀어나가도록 각도와 수평 속도를 분산하여 연속 사출한다.
        for (int i = 0; i < _collisionEjectCount; i++)
        {
            float angle = (baseAngle + i * angleStep + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
            Vector3 horizontalDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float scatterSpeed = Random.Range(_collisionScatterSpeed * 0.7f, _collisionScatterSpeed);

            float upSpeed = _localVelocity.y + Random.Range(2f, 8f);
            Vector3 velocity = horizontalDir * scatterSpeed + Vector3.up * upSpeed;

            TryEjectFrom(context, SkipJudge.Good, origin, velocity, origin.lossyScale);
        }
    }

    /// <summary>
    /// 성공 판정과 횟수 제한을 만족하면 지정한 위치에서 공용 시스템으로 물고기를 사출한다.
    /// context, judge, origin, velocity, scale을 사용하며, 성공 시 횟수를 늘리고 true를 반환한다.
    /// </summary>
    public bool TryEjectFrom(ThrowContext context, SkipJudge judge, Transform origin, Vector3 velocity, Vector3 scale)
    {
        if (judge != SkipJudge.Good && judge != SkipJudge.Perfect) return false;
        if (_perfectOnly && judge != SkipJudge.Perfect && !context.Player.IsSliding) return false;
        if (_maxEjections > 0 && _ejectionCount >= _maxEjections) return false;

        if (context.Spawner.TryEject(_fishPrefab, origin.position, origin.rotation, scale, velocity))
        {
            _ejectionCount++;
            return true;
        }
        return false;
    }
}
