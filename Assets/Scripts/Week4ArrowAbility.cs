using System.Collections;
using UnityEngine;

// Week4 화살의 특수 동작. 던질 거리로 고른 동안에는 위아래로 천천히 흔들리고,
// 던진 뒤 SPACE를 누르면 불씨 파티클 4개가 터져 나왔다가 화살로 빨려 들어간다.
public class Week4ArrowAbility : FishAbility
{
    private const int SPACE_EMBER_COUNT = 4;

    [Header("고르기")]
    [Tooltip("위아래로 흔들리는 높이")]
    [SerializeField]
    private float _bobHeight = 0.2f;
    [Tooltip("초당 흔들리는 횟수")]
    [SerializeField]
    private float _bobFrequency = 0.5f;
    public float BobHeight => _bobHeight;

    [Header("불씨")]
    [Tooltip("SPACE 입력과 벽 충돌 때 화살 위치에 만드는 불씨 파티클")]
    [SerializeField]
    private Week4ArrowGainEffect _gainEffectPrefab;

    [Header("발사")]
    [Tooltip("던진 뒤 남는 시간을 바꿀 트레일")]
    [SerializeField]
    private TrailRenderer _trail;
    [Tooltip("던진 뒤 트레일이 남는 시간(초)")]
    [SerializeField]
    private float _thrownTrailTime = 1f;

    [Header("미끄러짐")]
    [Tooltip("미끄러지는 동안 끄는 파티클")]
    [SerializeField]
    private ParticleSystem _particle;
    [Tooltip("충돌 처리용 코루틴")]
    private Coroutine _crashParticleCoroutine;
    private const int CRASHPARTICLECOUNT = 6;

    [Header("상태")]
    private bool _held;
    private bool _thrown;

    void Start()
    {
        // 던질 거리로 만들어지면 모양 생성기 아래에 붙는다. 물에서 튀어 오르는 화살은 흔들지 않는다.
        _held = GetComponentInParent<FishMeshGenerator>() != null;
    }

    void Update()
    {
        if (!_held || _thrown) return;

        float offset = GetBobOffset(Time.time);
        // 조준 자세로 물고기가 기울어도 화면 기준 위아래로 흔들리게 월드 위쪽을 부모 로컬로 바꾼다.
        transform.localPosition = transform.parent.InverseTransformVector(Vector3.up * offset);
    }

    /// <summary>
    /// time 시각의 위아래 흔들림 높이를 계산한다. 고르기 상태와 선택 미리보기가 같은 아이들 모션을 쓴다.
    /// time과 _bobFrequency, _bobHeight를 사용하며, 월드 위쪽 기준 높이를 반환한다.
    /// </summary>
    public float GetBobOffset(float time)
    {
        return Mathf.Sin(time * _bobFrequency * Mathf.PI * 2f) * _bobHeight;
    }

    /// <summary>
    /// 던진 뒤 처음 불리면 흔들림을 멈추고 화살을 제자리로 돌린 뒤 트레일 시간을 던진 뒤 값으로 바꾼다.
    /// _thrownTrailTime을 사용하며, context와 dt는 쓰지 않는다. _thrown, 로컬 위치, 트레일 시간을 변경한다.
    /// </summary>
    public override void UpdateFlight(ThrowContext context, float dt)
    {
        if (_thrown) return;

        _thrown = true;
        transform.localPosition = Vector3.zero;
        _trail.time = _thrownTrailTime;
    }

    /// <summary>
    /// 미끄러지기 시작하면 비행 중 계속 나오는 파티클의 방출을 중단한다.
    /// _particle을 사용하며, context는 쓰지 않는다. 파티클 재생 상태를 변경한다.
    /// </summary>
    public override void OnSlideStart(ThrowContext context)
    {
        // 이미 나온 파티클은 자연스럽게 사라지도록 새로 만드는 것만 멈춘다.
        _particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>
    /// SPACE를 누를 때마다 불씨 파티클 SPACE_EMBER_COUNT개를 터뜨리고 화살로 빨려 들어오게 한다.
    /// _gainEffectPrefab과 현재 위치를 사용하며, context는 쓰지 않는다. 새 불씨 파티클을 만든다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        Week4ArrowGainEffect effect = Instantiate(_gainEffectPrefab, transform.position, Quaternion.identity);
        effect.SetBurstCount(SPACE_EMBER_COUNT);
        effect.SetTarget(transform);
    }


    /// <summary>
    /// 장애물(벽)과 충돌했을 때 코루틴을 통해 연속적으로 불씨 파티클을 생성한다.
    /// context와 collision을 사용하며, _crashParticleCoroutine을 시작한다.
    /// </summary>
    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        if (_crashParticleCoroutine != null)
            StopCoroutine(_crashParticleCoroutine);
        _crashParticleCoroutine = StartCoroutine(SpreadParticles());
    }

    /// <summary>
    /// 짧은 시간 간격을 두고 불씨 파티클을 순차적으로 생성하여 화살로 흡수되게 한다.
    /// _gainEffectPrefab과 CRASHPARTICLECOUNT를 사용하며, 불씨 파티클 인스턴스를 생성한다.
    /// </summary>
    private IEnumerator SpreadParticles()
    {
        for (int i = 0; i < CRASHPARTICLECOUNT; i++)
        {
            if (_gainEffectPrefab != null)
            {
                Week4ArrowGainEffect effect = Instantiate(_gainEffectPrefab, transform.position, Quaternion.identity);
                effect.SetTarget(transform);
            }
            yield return new WaitForSeconds(0.05f);
        }
        _crashParticleCoroutine = null;
    }
}
