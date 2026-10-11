using System;

using UnityEngine;
using UnityEngine.Rendering;

public enum FishShape
{
    Fish,
    Crab,
    Tire,
    Can,
    Starfish,
}

// 던질 거리로 골랐을 때 바뀌는 날씨와 후처리 볼륨. 인스펙터에서 접을 수 있게 따로 묶는다.
[Serializable]
public class FishSelectEffects
{
    [Header("날씨")]
    [Tooltip("고르면 비가 온다")]
    [SerializeField]
    private bool _causesRain;
    [Tooltip("고르면 밤이 된다")]
    [SerializeField]
    private bool _causesNight;
    [Tooltip("고르면 그 판에 비가 오기로 했어도 비가 그친다")]
    [SerializeField]
    private bool _preventsRain;
    [Tooltip("고르면 그 판이 밤이기로 했어도 낮이 된다")]
    [SerializeField]
    private bool _preventsNight;

    [Header("볼륨")]
    [Tooltip("고르면 서서히 켜지는 후처리 볼륨 프로필. 다른 것을 고르면 서서히 꺼진다")]
    [SerializeField]
    private VolumeProfile _volumeProfile;

    public bool CausesRain => _causesRain;
    public bool CausesNight => _causesNight;
    public bool PreventsRain => _preventsRain;
    public bool PreventsNight => _preventsNight;
    public VolumeProfile VolumeProfile => _volumeProfile;
}

// 자연 출현에 붙는 조건과 출현 방식. 인스펙터에서 접을 수 있게 따로 묶는다.
[Serializable]
public class FishSpawnConditions
{
    [Tooltip("켜면 비가 올 때만 자연 출현한다")]
    [SerializeField]
    private bool _onlyInRain;
    [Tooltip("켜면 밤일 때만 자연 출현한다")]
    [SerializeField]
    private bool _onlyAtNight;
    [Tooltip("지정하면 플레이어가 이 물고기를 골라 던졌을 때만 자연 출현한다. 물고기 프리팹을 넣는다")]
    [SerializeField]
    private GameObject _requiredPlayerFish;
    [Tooltip("켜면 수면에서 튀어 오르지 않고 하늘에서 흔들리며 떨어진다. 프리팹 자세에서 X축 쪽 면이 카메라를 본다")]
    [SerializeField]
    private bool _fallsFromSky;

    public bool OnlyInRain => _onlyInRain;
    public bool OnlyAtNight => _onlyAtNight;
    public GameObject RequiredPlayerFish => _requiredPlayerFish;
    public bool FallsFromSky => _fallsFromSky;
}

// 물고기 한 종류의 값, 생김새, 던질 때의 물리 특성.
// 물고기는 옆모습 실루엣 기준으로 길이는 z, 높이는 x, 두께는 y로 만든다. 게는 위에서 본 모양이라 높이가 좌우 폭이다.
[Serializable]
public class FishType
{
    [Header("기본")]
    [SerializeField]
    private string _id = "mackerel";
    [SerializeField]
    private string _displayName = "MACKEREL";
    [SerializeField]
    private int _value = 10;
    [SerializeField]
    private Color _color = new Color(0.35f, 0.5f, 0.62f);

    [Header("사출 및 개체 제한")]
    [Tooltip("바다에 등록된 뒤에도 다시 사출할 수 있다")]
    [SerializeField] private bool _allowRepeatEjection = true;
    [Tooltip("출수 대기와 사출 중인 개체도 포함한다. 0이면 제한하지 않는다")]
    [Min(0)]
    [SerializeField] private int _maxConcurrent;
    [Tooltip("획득한 뒤 자연 출현과 사출을 중단한다")]
    [SerializeField] private bool _stopAfterCatch;

    [Header("선택 미리보기")]
    [Tooltip("공개하면 회색 반투명 모델, 공개하지 않으면 물음표로 표시한다. 도감 등록 여부와는 별개다")]
    [SerializeField]
    private bool _isRevealed = true;

    [Header("몸")]
    [SerializeField]
    private FishShape _shape = FishShape.Fish;
    [SerializeField]
    private float _length = 1.6f;
    [Tooltip("물고기는 몸 높이, 게는 좌우 폭")]
    [SerializeField]
    private float _height = 0.45f;
    [SerializeField]
    private float _thickness = 0.3f;
    [Tooltip("물고기는 꼬리 길이, 게는 집게 길이")]
    [SerializeField]
    private float _tailLength = 0.45f;
    [Tooltip("등지느러미 크기. 0이면 없다 (상어용)")]
    [SerializeField]
    private float _dorsalFin = 0f;

    [Header("던질 때")]
    [SerializeField]
    private float _speedMultiplier = 1f;
    [SerializeField]
    private float _spinMultiplier = 1f;
    [Tooltip("프리팹 물고기는 기본 물고기와 면적이 달라서 양력을 보정한다")]
    [SerializeField]
    private float _liftMultiplier = 3f;
    [SerializeField]
    private float _frictionMultiplier = 1f;
    [Tooltip("공중에서 받는 중력 배율. 1보다 작으면 활공한다")]
    [SerializeField]
    private float _airGravityScale = 1f;

    [Header("선택 시 효과")]
    [Tooltip("던질 거리로 골랐을 때 바뀌는 날씨와 후처리 볼륨")]
    [SerializeField]
    private FishSelectEffects _selectEffects = new FishSelectEffects();

    [Header("출현 조건")]
    [Tooltip("자연 출현에 붙는 날씨와 플레이어 물고기 조건")]
    [SerializeField]
    private FishSpawnConditions _spawnConditions = new FishSpawnConditions();

    public string Id => _id;
    public string DisplayName => _displayName;
    public int Value => _value;
    public Color Color => _color;
    public bool IsRevealed => _isRevealed;
    public bool AllowRepeatEjection => _allowRepeatEjection;
    public int MaxConcurrent => _maxConcurrent;
    public bool StopAfterCatch => _stopAfterCatch;
    public FishShape Shape => _shape;
    public float Length => _length;
    public float Height => _height;
    public float Thickness => _thickness;
    public float TailLength => _tailLength;
    public float DorsalFin => _dorsalFin;
    public bool SelectCausesRain => _selectEffects.CausesRain;
    public bool SelectCausesNight => _selectEffects.CausesNight;
    public bool SelectPreventsRain => _selectEffects.PreventsRain;
    public bool SelectPreventsNight => _selectEffects.PreventsNight;
    public VolumeProfile SelectVolumeProfile => _selectEffects.VolumeProfile;
    public bool SpawnOnlyInRain => _spawnConditions.OnlyInRain;
    public bool SpawnOnlyAtNight => _spawnConditions.OnlyAtNight;
    public GameObject RequiredPlayerFish => _spawnConditions.RequiredPlayerFish;
    public bool SpawnFallsFromSky => _spawnConditions.FallsFromSky;

    /// <summary>
    /// 입력값 없이 절단 조각의 Wall 종류를 생성해 반환한다.
    /// 미등록 외형과 이름을 숨기는 고정 도감 ID 및 투척 배율을 설정한다.
    /// </summary>
    public static FishType CreateWall()
    {
        return new FishType
        {
            _id = "wall",
            _displayName = "Wall",
            _value = 0,
            _isRevealed = false,
            _liftMultiplier = 1f,
        };
    }

    /// <summary>
    /// 입력값 없이 절단된 유빙의 ICE FLOE 종류를 생성해 반환한다.
    /// 섭취 전에는 미등록 항목으로 숨기는 고정 도감 ID 및 투척 배율을 설정한다.
    /// </summary>
    public static FishType CreateIce()
    {
        return new FishType
        {
            _id = "ice",
            _displayName = "ICE FLOE",
            _value = 0,
            _color = new Color(0.65f, 0.9f, 1f),
            _isRevealed = false,
            _liftMultiplier = 1f,
        };
    }

    /// <summary>
    /// 입력값 없이 캡처 이미지를 사용하는 SCREEN 종류를 반환한다.
    /// 자연 출현을 끄고 16:9 패널 크기와 도감 ID를 설정한다.
    /// </summary>
    public static FishType CreateScreen()
    {
        return new FishType
        {
            _id = "screen",
            _displayName = "SCREEN",
            _value = 0,
            _isRevealed = false,
            _height = 3.2f,
            _length = 1.8f,
            _thickness = 0.08f,
            _liftMultiplier = 1f,
        };
    }

    /// <summary>
    /// 입력값 없이 도감 화면 이미지를 사용하는 비공개 패널 종류를 반환한다.
    /// SCREEN과 동일한 16:9 크기에 독립된 도감 ID와 이름을 설정한다.
    /// </summary>
    public static FishType CreateCatalog()
    {
        FishType type = CreateScreen();
        type._id = "catalog";
        type._displayName = "도감";
        return type;
    }

    /// <summary>
    /// 이 물고기를 던질 때 적용할 배율을 만든다. 던지는 힘과 스핀 업그레이드 배율을 곱한다.
    /// powerMultiplier, spinMultiplier를 사용하며, ThrowModifiers를 반환한다.
    /// </summary>
    public ThrowModifiers ToThrowModifiers(float powerMultiplier, float spinMultiplier)
    {
        return new ThrowModifiers(
            _speedMultiplier * powerMultiplier,
            _spinMultiplier * spinMultiplier,
            _liftMultiplier,
            _frictionMultiplier,
            _airGravityScale);
    }
}
