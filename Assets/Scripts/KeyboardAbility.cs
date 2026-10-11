using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

// 키보드 물고기의 특수 동작. 실제 키보드(Keyboard.current)에서 누르고 있는 키와 짝인 키캡이 눌려 내려간다.
// 던진 뒤 물이나 벽에 닿을 때마다 아직 붙어 있는 키캡 몇 개가 떨어져 나가 강하게 튀어 날아간다.
// 모델은 정점이 많아 던질 물고기를 만들 때 잠시 떼어 두고, 생성기는 꺼진 물리 상자 하나로만 수면 판정을 만든다.
public class KeyboardAbility : FishAbility
{
    // 키캡 하나와 그 키캡을 누르는 실제 키보드 키의 짝.
    [Serializable]
    private struct KeycapBinding
    {
        [SerializeField]
        private Transform _keycap;
        [SerializeField]
        private Key _key;

        public Transform Keycap => _keycap;
        public Key Key => _key;
    }

    [Header("키 누름")]
    [Tooltip("키캡과 실제 키보드 키의 짝. Key가 None이면 눌리지 않는다")]
    [SerializeField]
    private KeycapBinding[] _keycaps;
    [Tooltip("키캡이 눌려 내려가는 깊이. 키캡 부모 좌표 기준이며 키캡 높이는 약 0.53이다")]
    [SerializeField]
    private float _pressDepth = 0.2f;
    [Tooltip("누를 때 내려가는 속도 (초당 깊이)")]
    [SerializeField]
    private float _pressSpeed = 8f;
    [Tooltip("뗄 때 올라오는 속도 (초당 깊이)")]
    [SerializeField]
    private float _releaseSpeed = 4f;
    private Vector3[] _restPositions;
    private float[] _depths;

    [Header("꾸물거림 (던지기 전까지)")]
    [Tooltip("초당 꾸물거리는 횟수. 기본 물고기와 같다")]
    [SerializeField]
    private float _flopFrequency = 6f;
    [Tooltip("양 끝 키캡이 C자로 오르내리는 높이. 키캡 부모 좌표 기준이며, 누름 깊이보다 작아야 누른 키가 묻히지 않는다")]
    [SerializeField]
    private float _flopKeyLift = 0.06f;
    [Tooltip("몸통 전체가 들썩이는 높이 (키보드 길이 비율). 기본 물고기와 같다")]
    [SerializeField]
    private float _flopHop = 0.08f;
    [Tooltip("몸통이 좌우로 기우는 최대 각도")]
    [SerializeField]
    private float _flopTilt = 6f;
    private float[] _flopWeights;
    private float _flopLength;
    private bool _isFlopping;

    [Header("키캡 터짐")]
    [Tooltip("물에 닿을 때 떨어져 나가는 키캡 수 범위")]
    [SerializeField]
    private Vector2Int _waterBurstCount = new Vector2Int(2, 5);
    [Tooltip("벽에 부딪힐 때 떨어져 나가는 키캡 수 범위")]
    [SerializeField]
    private Vector2Int _wallBurstCount = new Vector2Int(6, 12);
    [Tooltip("떨어져 나가는 속도 범위")]
    [SerializeField]
    private Vector2 _burstSpeedRange = new Vector2(10f, 20f);
    [Tooltip("튀는 방향을 위쪽으로 기울이는 정도. 클수록 위로 솟는다")]
    [SerializeField]
    private float _burstUpBias = 1.2f;
    [Tooltip("키보드의 현재 속도를 이어받는 비율")]
    [SerializeField]
    private float _inheritVelocity = 0.6f;
    [Tooltip("떨어져 나간 키캡의 최대 회전 속도 (rad/s)")]
    [SerializeField]
    private float _burstSpin = 25f;
    [Tooltip("떨어져 나간 키캡이 사라지기까지의 시간 (초)")]
    [SerializeField]
    private float _debrisLifetime = 4f;
    private readonly List<int> _attached = new List<int>();
    private readonly List<GameObject> _debris = new List<GameObject>();
    private PlayerController _player;

    [Header("물리 대체")]
    [Tooltip("던질 물고기를 만들 때 생성기에서 잠시 떼어 둘 모델 루트. 프리팹에는 꺼진 물리 상자 메쉬 하나만 남겨 둔다")]
    [SerializeField]
    private Transform[] _visualRoots;
    private Vector3[] _visualPositions;
    private Quaternion[] _visualRotations;
    private Vector3[] _visualScales;
    private bool _visualsDetached;

    void Awake()
    {
        _restPositions = new Vector3[_keycaps.Length];
        _depths = new float[_keycaps.Length];
        for (int i = 0; i < _keycaps.Length; i++)
        {
            _restPositions[i] = _keycaps[i].Keycap.localPosition;
            _attached.Add(i);
        }
        CacheFlopWeights();

        // 생성기는 생성 직후 자식 메쉬를 모두 모아 물리에 쓴다. 정점이 너무 많아 모델을 떼어 두고 물리 상자만 보이게 한다.
        // 생성기의 파닥임도 그 물리 상자에만 걸리므로, 던지기 전까지 모델을 직접 꾸물거리게 한다.
        if (transform.parent != null && transform.parent.TryGetComponent(out FishMeshGenerator _))
        {
            DetachVisuals();
            _isFlopping = true;
        }
    }

    void Start()
    {
        if (!_visualsDetached) return;

        for (int i = 0; i < _visualRoots.Length; i++)
        {
            _visualRoots[i].SetParent(transform, false);
            _visualRoots[i].SetLocalPositionAndRotation(_visualPositions[i], _visualRotations[i]);
            _visualRoots[i].localScale = _visualScales[i];
        }
        _visualsDetached = false;
    }

    void Update()
    {
        float flopLift = _isFlopping ? Flop(Time.time) : 0f;
        float dt = Time.deltaTime;
        foreach (int i in _attached)
        {
            Key key = _keycaps[i].Key;
            bool pressed = Managers.Input.IsKeyHeld(key);
            float speed = pressed ? _pressSpeed : _releaseSpeed;
            _depths[i] = Mathf.MoveTowards(_depths[i], pressed ? _pressDepth : 0f, speed * dt);
            // 눌린 키캡은 물결을 따르지 않고 끝까지 내려가 있어야 꾸물거리는 중에도 누른 것이 보인다.
            float wave = flopLift * _flopWeights[i] * (1f - Mathf.InverseLerp(0f, _pressDepth, _depths[i]));
            _keycaps[i].Keycap.localPosition = _restPositions[i] + Vector3.up * (wave - _depths[i]);
        }
    }

    void OnDestroy()
    {
        if (_player != null)
        {
            _player.OnWaterContact -= HandleWaterContact;
            _player.OnObstacleHit -= HandleObstacleHit;
        }
        foreach (GameObject debris in _debris)
        {
            // 수명이 다해 먼저 사라진 조각은 건너뛴다.
            if (debris != null)
            {
                Destroy(debris);
            }
        }
        // 다시 붙이기 전에 사라지면 떼어 둔 모델이 장면에 남으므로 함께 없앤다.
        if (_visualsDetached)
        {
            foreach (Transform visualRoot in _visualRoots)
            {
                Destroy(visualRoot.gameObject);
            }
        }
    }

    /// <summary>
    /// 모델 루트들의 로컬 자세를 기억한 뒤 이 오브젝트에서 떼어 둔다. Start에서 다시 붙인다.
    /// _visualRoots를 사용하며, 기억한 자세 배열과 모델 루트의 부모, _visualsDetached를 변경한다.
    /// </summary>
    private void DetachVisuals()
    {
        _visualPositions = new Vector3[_visualRoots.Length];
        _visualRotations = new Quaternion[_visualRoots.Length];
        _visualScales = new Vector3[_visualRoots.Length];
        for (int i = 0; i < _visualRoots.Length; i++)
        {
            _visualRoots[i].GetLocalPositionAndRotation(out _visualPositions[i], out _visualRotations[i]);
            _visualScales[i] = _visualRoots[i].localScale;
            _visualRoots[i].SetParent(null, true);
        }
        _visualsDetached = true;
    }

    /// <summary>
    /// 키캡마다 키보드 길이 방향(x) 위치로 꾸물거림 세기를 정한다. 가운데는 0, 양 끝은 1이라 C자로 휜다.
    /// 키캡의 현재 위치를 사용하며, _flopWeights와 _flopLength를 변경한다.
    /// </summary>
    private void CacheFlopWeights()
    {
        _flopWeights = new float[_keycaps.Length];
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        for (int i = 0; i < _keycaps.Length; i++)
        {
            _flopWeights[i] = transform.InverseTransformPoint(_keycaps[i].Keycap.position).x;
            minX = Mathf.Min(minX, _flopWeights[i]);
            maxX = Mathf.Max(maxX, _flopWeights[i]);
        }
        _flopLength = maxX - minX;

        float centerX = (minX + maxX) * 0.5f;
        for (int i = 0; i < _keycaps.Length; i++)
        {
            float along = (_flopWeights[i] - centerX) / _flopLength * 2f;
            _flopWeights[i] = along * along;
        }
    }

    /// <summary>
    /// 기본 물고기의 파닥임처럼 몸통을 들썩이며 좌우로 기울인다. 정점은 건드리지 않고 모델 루트 자세만 바꾼다.
    /// time과 꾸물거림 설정, 기억한 모델 루트 자세를 사용하며, 모델 루트의 로컬 자세를 변경하고 양 끝 키캡이 오르내릴 높이를 반환한다.
    /// </summary>
    private float Flop(float time)
    {
        float phase = time * _flopFrequency * Mathf.PI * 2f;
        // 기본 물고기와 같이 세기를 노이즈로 흔들어 기계 같은 박자를 피한다.
        float strength = 0.35f + 0.65f * Mathf.PerlinNoise(time * 1.7f, 0.37f);
        float hop = Mathf.Abs(Mathf.Sin(phase * 0.5f)) * strength * _flopHop * _flopLength;
        Quaternion tilt = Quaternion.AngleAxis(Mathf.Sin(phase * 0.5f) * strength * _flopTilt, Vector3.forward);
        for (int i = 0; i < _visualRoots.Length; i++)
        {
            _visualRoots[i].SetLocalPositionAndRotation(tilt * _visualPositions[i] + Vector3.up * hop, tilt * _visualRotations[i]);
        }
        return Mathf.Sin(phase) * strength * _flopKeyLift;
    }

    /// <summary>
    /// 꾸물거림을 멈추고 모델 루트를 원래 자세로 되돌린다. 키캡은 다음 Update에서 제자리로 돌아간다.
    /// 기억한 모델 루트 자세를 사용하며, _isFlopping과 모델 루트의 로컬 자세를 변경한다.
    /// </summary>
    private void StopFlop()
    {
        _isFlopping = false;
        for (int i = 0; i < _visualRoots.Length; i++)
        {
            _visualRoots[i].SetLocalPositionAndRotation(_visualPositions[i], _visualRotations[i]);
        }
    }

    /// <summary>
    /// 던진 직후 한 번 꾸물거림을 멈추고 플레이어의 착수와 충돌 이벤트를 구독한다.
    /// context의 플레이어를 사용하며, 꾸물거림 상태, _player와 이벤트 구독 상태를 변경한다. dt는 쓰지 않는다.
    /// </summary>
    public override void UpdateFlight(ThrowContext context, float dt)
    {
        if (_player != null) return;

        StopFlop();
        _player = context.Player;
        _player.OnWaterContact += HandleWaterContact;
        _player.OnObstacleHit += HandleObstacleHit;
    }

    /// <summary>
    /// 물에 닿으면 키캡 몇 개를 떨어뜨린다.
    /// _waterBurstCount를 사용하며, point와 speed는 쓰지 않는다. 붙어 있는 키캡 목록을 변경한다.
    /// </summary>
    private void HandleWaterContact(Vector3 point, float speed)
    {
        Burst(_waterBurstCount);
    }

    /// <summary>
    /// 벽이나 장애물에 부딪히면 키캡을 더 많이 떨어뜨린다.
    /// _wallBurstCount를 사용하며, speed는 쓰지 않는다. 붙어 있는 키캡 목록을 변경한다.
    /// </summary>
    private void HandleObstacleHit(float speed)
    {
        Burst(_wallBurstCount);
    }

    /// <summary>
    /// 아직 붙어 있는 키캡 중 무작위로 골라 떼어 날린다.
    /// countRange(최소, 최대 포함)를 사용하며, _attached에서 고른 키캡을 뺀다.
    /// </summary>
    private void Burst(Vector2Int countRange)
    {
        int count = Mathf.Min(UnityEngine.Random.Range(countRange.x, countRange.y + 1), _attached.Count);
        for (int n = 0; n < count; n++)
        {
            int pick = UnityEngine.Random.Range(0, _attached.Count);
            Launch(_keycaps[_attached[pick]].Keycap);
            _attached.RemoveAt(pick);
        }
    }

    /// <summary>
    /// 키캡을 키보드에서 떼어 물리로 날려 보내고 잠시 뒤 없앤다.
    /// keycap과 터짐 설정, 플레이어 속도를 사용하며, 키캡의 부모와 Rigidbody, _debris를 변경한다.
    /// </summary>
    private void Launch(Transform keycap)
    {
        keycap.SetParent(null, true);

        // 키보드 중심에서 바깥쪽, 위쪽으로 흩어지게 한다.
        Vector3 outward = (keycap.position - transform.position).normalized;
        Vector3 direction = (outward + Vector3.up * _burstUpBias + UnityEngine.Random.insideUnitSphere * 0.5f).normalized;
        Rigidbody body = keycap.gameObject.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.maxAngularVelocity = _burstSpin;
        body.linearVelocity = _player.Velocity * _inheritVelocity
            + direction * UnityEngine.Random.Range(_burstSpeedRange.x, _burstSpeedRange.y);
        body.angularVelocity = UnityEngine.Random.insideUnitSphere * _burstSpin;

        _debris.Add(keycap.gameObject);
        Destroy(keycap.gameObject, _debrisLifetime);
    }
}
