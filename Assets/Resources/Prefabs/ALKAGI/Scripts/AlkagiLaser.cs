using System.Collections.Generic;

using UnityEngine;

public class AlkagiLaser : FishAbility
{
    [Header("레이저")]
    [SerializeField] private Transform _eye;
    [SerializeField] private GameObject _beamPrefab;
    [SerializeField] private float _duration = 0.35f;
    [Tooltip("공유 빔 프리팹 대비 이 물고기 빔의 길이 배율. 눈 쪽 끝은 그대로 두고 앞으로 늘린다")]
    [SerializeField, Min(0.1f)] private float _lengthScale = 3f;

    [Header("상태")]
    private GameObject _beam;
    private CapsuleCollider _beamCollider;
    private float _remainingTime;
    private readonly HashSet<CuttableWall> _cutWalls = new HashSet<CuttableWall>();

    void Update()
    {
        if (_remainingTime <= 0f) return;

        CutWalls();
        _remainingTime -= Time.deltaTime;
        if (_remainingTime <= 0f)
        {
            _beam.SetActive(false);
        }
    }

    void OnDisable()
    {
        _remainingTime = 0f;
        if (_beam != null) _beam.SetActive(false);
    }

    /// <summary>
    /// 던진 뒤 SPACE를 누를 때마다 판정 결과와 관계없이 레이저를 쏜다.
    /// context는 쓰지 않으며, Fire로 레이저 표시 상태를 변경한다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        Fire();
    }

    /// <summary>
    /// 눈의 forward 방향으로 레이저를 표시한다.
    /// _eye와 _beamPrefab, _duration을 사용하며, Beam을 재사용하고 표시 시간을 다시 시작한다.
    /// </summary>
    public void Fire()
    {
        _cutWalls.Clear();
        if (_beam == null)
        {
            _beam = Instantiate(_beamPrefab, _eye, false);
            _beam.transform.localPosition = Vector3.zero;
            _beam.transform.localRotation = Quaternion.identity;
            Vector3 scale = _beamPrefab.transform.localScale;
            Vector3 eyeScale = _eye.localScale;
            _beam.transform.localScale = new Vector3(
                scale.x / eyeScale.x, scale.y / eyeScale.y, scale.z / eyeScale.z);
            foreach (Collider collider in _beam.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }
            _beamCollider = _beam.GetComponentInChildren<CapsuleCollider>();
            StretchBeam();
        }

        _beam.SetActive(true);
        _remainingTime = _duration;
        CutWalls();
    }

    /// <summary>
    /// 빔 원통의 눈 쪽 끝을 고정한 채 길이 축으로 _lengthScale배 늘린다. 절단 범위도 같은 CapsuleCollider를 쓰므로 함께 늘어난다.
    /// _beamCollider의 길이 축, 높이와 원통의 로컬 변환을 사용하며, 원통의 localScale과 localPosition을 변경한다.
    /// </summary>
    private void StretchBeam()
    {
        Transform cylinder = _beamCollider.transform;
        int axis = _beamCollider.direction;
        Vector3 lengthAxis = Vector3.zero;
        lengthAxis[axis] = 1f;
        Vector3 forward = cylinder.localRotation * lengthAxis;
        // 빔은 눈의 앞쪽(+Z)으로 나가므로 그쪽을 앞으로 보고 반대쪽 끝을 고정한다.
        if (forward.z < 0f) forward = -forward;
        float halfLength = _beamCollider.height * Mathf.Abs(cylinder.localScale[axis]) * 0.5f;
        Vector3 back = cylinder.localPosition - forward * halfLength;

        Vector3 scale = cylinder.localScale;
        scale[axis] *= _lengthScale;
        cylinder.localScale = scale;
        cylinder.localPosition = back + forward * (halfLength * _lengthScale);
    }

    /// <summary>
    /// 표시 중인 빔의 CapsuleCollider 형상과 눈 방향을 사용해 닿은 벽을 평면 절단한다.
    /// 빔의 물리 충돌은 켜지 않고, 빔 진행 방향을 포함하는 절단면으로 Wall 조각을 만든다.
    /// </summary>
    private void CutWalls()
    {
        Transform beamTransform = _beamCollider.transform;
        int axis = _beamCollider.direction;
        Vector3 scale = beamTransform.lossyScale;
        float radius = _beamCollider.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
        float halfLength = Mathf.Max(0f, _beamCollider.height * Mathf.Abs(scale[axis]) * 0.5f - radius);
        Vector3 direction = axis == 0 ? beamTransform.right : axis == 1 ? beamTransform.up : beamTransform.forward;
        Vector3 center = beamTransform.TransformPoint(_beamCollider.center);
        foreach (Collider collider in Physics.OverlapCapsule(center - direction * halfLength, center + direction * halfLength,
                     radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (collider.TryGetComponent(out CuttableWall wall) && !_cutWalls.Contains(wall))
            {
                Vector3 contact = collider.ClosestPoint(_eye.position + _eye.forward * Vector3.Dot(collider.bounds.center - _eye.position, _eye.forward));
                if (wall.Cut(contact, _eye.up)) _cutWalls.Add(wall);
            }
        }
    }
}
