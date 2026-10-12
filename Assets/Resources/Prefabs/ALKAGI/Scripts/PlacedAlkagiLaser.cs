using System;
using System.Collections.Generic;

using UnityEngine;

public class PlacedAlkagiLaser : MonoBehaviour
{
    private const float HORIZONTAL_CUT_THRESHOLD = 0.0001f;
    private const float MIN_CUT_NORMAL_Y = 0.001f;

    [Header("레이저")]
    [SerializeField] private Transform _eye;
    [SerializeField] private GameObject _beamPrefab;
    [Tooltip("쓸고 지나가는 동안 접촉을 검사하는 각도 간격. 빔이 길수록 작게 잡아야 먼 곳에 빈틈이 생기지 않는다")]
    [SerializeField, Min(0.001f)] private float _sweepStepAngle = 0.5f;
    private GameObject _beam;
    private CapsuleCollider _shape;
    private Vector3 _previousPosition;
    private Quaternion _previousRotation;
    private float _range;
    private readonly HashSet<CuttableWall> _cutWalls = new HashSet<CuttableWall>();
    public float Width => _shape.radius * 2f * Mathf.Max(_shape.transform.lossyScale.x, _shape.transform.lossyScale.z);
    public event Action<Vector3> OnWaterContact;

    void Awake()
    {
        _beam = Instantiate(_beamPrefab);
        _shape = _beam.GetComponentInChildren<CapsuleCollider>();
        foreach (Collider collider in _beam.GetComponentsInChildren<Collider>()) collider.enabled = false;
        _beam.SetActive(false);
    }

    void OnDestroy()
    {
        if (_beam != null) Destroy(_beam);
    }

    /// <summary>
    /// 탐지 거리 range에 맞춰 빔 길이를 설정하고 눈의 현재 자세로 빔을 켠다.
    /// 빔 길이와 이전 검사 자세를 변경하고 이번 공격의 절단 기록을 초기화한다.
    /// </summary>
    public void BeginFire(float range)
    {
        float length = Mathf.Max(0.01f, range);
        _range = length;
        Vector3 scale = _shape.transform.localScale;
        scale.y = length / _shape.height;
        _shape.transform.localScale = scale;
        _shape.transform.localPosition = Vector3.forward * (length * 0.5f);
        _cutWalls.Clear();
        _previousPosition = _eye.position;
        _previousRotation = _eye.rotation;
        _beam.transform.SetPositionAndRotation(_previousPosition, _previousRotation);
        _beam.SetActive(true);
    }

    /// <summary>
    /// 이 컴포넌트가 만든 빔 인스턴스의 두께를 width(m)로 바꾼다. 빔 프리팹 원본은 변경하지 않는다.
    /// width를 사용하며 빔 원통의 X, Z 배율을 변경해 판정 반경과 Width도 함께 바뀐다.
    /// </summary>
    public void SetWidth(float width)
    {
        Vector3 scale = _shape.transform.localScale;
        scale.x = width / (_shape.radius * 2f);
        scale.z = scale.x;
        _shape.transform.localScale = scale;
    }

    /// <summary>
    /// playerBody와 waterHeight 및 눈 자세로 빔 접촉을 검사하고 cutWalls가 true면 벽도 절단한다.
    /// 수면 접촉점을 알리고 플레이어 접촉 여부를 반환하며 이전 검사 자세를 갱신한다.
    /// </summary>
    public bool Sweep(Rigidbody playerBody, float waterHeight, bool cutWalls = true)
    {
        Vector3 position = _eye.position;
        Quaternion rotation = _eye.rotation;
        Vector3 cutNormal = CalculateCutNormal(position, rotation);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(_previousRotation, rotation) / _sweepStepAngle));
        bool hitPlayer = false;
        for (int i = 0; i <= steps; i++)
        {
            float ratio = (float)i / steps;
            Vector3 previousPosition = _beam.transform.position;
            Quaternion previousRotation = _beam.transform.rotation;
            _beam.transform.SetPositionAndRotation(Vector3.Lerp(_previousPosition, position, ratio),
                Quaternion.Slerp(_previousRotation, rotation, ratio));
            hitPlayer |= CheckContacts(playerBody, cutNormal, cutWalls);
            CheckWaterContact(waterHeight, previousPosition, previousRotation);
        }
        _previousPosition = position;
        _previousRotation = rotation;
        return hitPlayer;
    }

    /// <summary>
    /// 현재 position과 rotation 및 이전 빔 끝점으로 쓸고 지나가는 절단면의 단위 법선을 반환한다.
    /// 움직임이 없으면 빔의 오른쪽을 사용하고 수평 절단 보정에 걸리는 Y 성분만 보충한다.
    /// </summary>
    private Vector3 CalculateCutNormal(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = rotation * Vector3.forward;
        Vector3 previousEnd = _previousPosition + _previousRotation * Vector3.forward * _range;
        Vector3 sweepMotion = position + direction * _range - previousEnd;
        Vector3 normal = Vector3.Cross(direction, sweepMotion.normalized);
        if (normal.sqrMagnitude < 0.000001f) normal = rotation * Vector3.right;
        normal.Normalize();
        if (Mathf.Abs(normal.y) < HORIZONTAL_CUT_THRESHOLD)
        {
            normal.y = normal.y < 0f ? -MIN_CUT_NORMAL_Y : MIN_CUT_NORMAL_Y;
            normal.Normalize();
        }
        return normal;
    }

    /// <summary>
    /// waterHeight의 수면과 현재 빔의 교차점을 알리고 수면을 벗어나는 마지막 접촉도 계산한다.
    /// 이전 빔 위치와 회전을 사용하며 유효한 접촉점마다 OnWaterContact를 보낸다.
    /// </summary>
    private void CheckWaterContact(float waterHeight, Vector3 previousPosition, Quaternion previousRotation)
    {
        Vector3 position = _beam.transform.position;
        Quaternion rotation = _beam.transform.rotation;
        Vector3 direction = rotation * Vector3.forward;
        if (position.y > waterHeight && direction.y < -0.000001f)
        {
            float distance = (waterHeight - position.y) / direction.y;
            if (distance <= _range) OnWaterContact?.Invoke(position + direction * distance);
        }

        Vector3 previousEnd = previousPosition + previousRotation * Vector3.forward * _range;
        Vector3 end = position + direction * _range;
        if (previousPosition.y <= waterHeight || position.y <= waterHeight
            || previousEnd.y >= waterHeight || end.y < waterHeight) return;

        // 수평에 가까운 긴 빔도 마지막 수면 접촉을 건너뛰지 않도록 끝점의 교차 자세를 찾는다.
        float lower = 0f;
        float upper = 1f;
        for (int i = 0; i < 20; i++)
        {
            float ratio = (lower + upper) * 0.5f;
            Vector3 candidate = Vector3.Lerp(previousPosition, position, ratio)
                + Quaternion.Slerp(previousRotation, rotation, ratio) * Vector3.forward * _range;
            if (candidate.y < waterHeight) lower = ratio;
            else upper = ratio;
        }
        Vector3 contact = Vector3.Lerp(previousPosition, position, lower)
            + Quaternion.Slerp(previousRotation, rotation, lower) * Vector3.forward * _range;
        contact.y = waterHeight;
        OnWaterContact?.Invoke(contact);
    }

    /// <summary>
    /// 빔을 비활성화하여 현재 공격의 레이저 표시를 종료한다.
    /// 입력값 없이 빔 활성 상태를 변경한다.
    /// </summary>
    public void EndFire()
    {
        if (_beam != null) _beam.SetActive(false);
    }

    /// <summary>
    /// CapsuleCollider 형상으로 playerBody 충돌체의 접촉 여부를 반환하고 cutWalls가 true면 cutNormal로 벽을 절단한다.
    /// 같은 벽은 공격당 한 번만 절단하며 플레이어의 Trigger도 검사한다.
    /// </summary>
    private bool CheckContacts(Rigidbody playerBody, Vector3 cutNormal, bool cutWalls)
    {
        Transform shapeTransform = _shape.transform;
        Vector3 scale = shapeTransform.lossyScale;
        float radius = _shape.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfLength = Mathf.Max(0f, _shape.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 center = shapeTransform.TransformPoint(_shape.center);
        Vector3 axis = shapeTransform.up * halfLength;
        bool hitPlayer = false;
        foreach (Collider collider in Physics.OverlapCapsule(center - axis, center + axis, radius,
                     ~0, QueryTriggerInteraction.Collide))
        {
            if (collider.attachedRigidbody == playerBody) hitPlayer = true;
            if (!cutWalls || collider.isTrigger || !collider.TryGetComponent(out CuttableWall wall) || _cutWalls.Contains(wall)) continue;
            Vector3 contact = collider.ClosestPoint(_beam.transform.position + _beam.transform.forward
                * Vector3.Dot(collider.bounds.center - _beam.transform.position, _beam.transform.forward));
            if (wall.Cut(contact, cutNormal)) _cutWalls.Add(wall);
        }
        return hitPlayer;
    }
}
