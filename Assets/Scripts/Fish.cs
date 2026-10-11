using UnityEngine;

// 수면 아래에서 기다렸다가 포물선으로 튀어 오르는 물고기(또는 게) 한 마리. 물고기 프리팹의 루트에 붙는다.
// 던진 물고기에 맞거나 다시 물에 들어가면 스포너에 알린다.
public class Fish : MonoBehaviour
{
    private const float WIGGLE_SPEED = 14f;
    private const float WIGGLE_ANGLE = 12f;
    private const float TIRE_ROLL_SPEED = 360f;
    private const float CAN_FLIP_SPEED = 620f;
    private const float FALL_SWAY_SPEED = 1.8f;
    private const float FALL_SWAY_DISTANCE = 1.2f;
    private const float FALL_TILT_ANGLE = 18f;

    [Header("종류")]
    [SerializeField]
    private FishType _type;

    [Header("참조")]
    [Tooltip("메시를 가진 자식 몸 오브젝트. 복어는 이 오브젝트를 부풀린다")]
    [SerializeField]
    private Transform _body;

    [Header("상태")]
    private FishSpawner _spawner;
    private Vector3 _startPosition;
    private Vector3 _position;
    private Vector3 _velocity;
    private float _gravity;
    private float _delay;
    private float _airTime;
    private float _waterY;
    private float _previousY;
    private bool _jumped;
    private bool _caught;
    private bool _landed;
    private bool _isWall;
    private Mesh _wallMesh;
    private bool _isEjected;
    private bool _isFalling;
    // 하늘에서 떨어질 때 기준으로 삼는 프리팹 자세와 바라볼 카메라
    private Quaternion _fallRotation;
    private Transform _view;
    private FisherQteController _fisherQte;

    public FishType Type => _type;
    public Transform Body => _body;
    public bool IsEjected => _isEjected;
    public bool IsCutPiece => _isWall;

    void Update()
    {
        if (_landed || Time.timeScale == 0f) return;
        if (_isWall)
        {
            if (_caught) return;
            float currentY = transform.position.y;
            bool enteredWater = _previousY >= _waterY - 0.5f && currentY < _waterY - 0.5f;
            _previousY = currentY;
            if (enteredWater)
            {
                _landed = true;
                _spawner.HandleFishLanded(this);
            }
            return;
        }
        if (!_jumped)
        {
            _delay -= Time.deltaTime;
            if (_delay > 0f) return;

            _jumped = true;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }
            _spawner.HandleFishSurfaced(_startPosition);
        }

        float dt = Time.deltaTime;
        _airTime += dt;
        _velocity += Vector3.down * (_gravity * dt);
        _position += _velocity * dt;
        transform.SetPositionAndRotation(_isFalling ? _position + FallSway() : _position, BodyRotation());

        if (_velocity.y < 0f && _position.y < _waterY - 0.5f)
        {
            _landed = true;
            _spawner.HandleFishLanded(this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_isWall || _isEjected || _caught || _landed || !_jumped || !_spawner.CanCollectFish
            || Time.timeScale == 0f || other.attachedRigidbody != _spawner.PlayerBody) return;

        if (_fisherQte != null)
        {
            if (_fisherQte.TryBegin()) _caught = true;
            return;
        }

        _caught = true;
        _spawner.HandleFishCaught(this);
    }

    void OnDestroy()
    {
        if (_wallMesh != null) Destroy(_wallMesh);
    }

    /// <summary>
    /// spawner, type와 소유할 mesh로 절단된 벽 또는 유빙의 입수 대기 조각을 준비한다.
    /// 점프 이동과 즉시 포획은 막고 실제 입수 시 다음 run의 모양 및 출현 등록을 허용한다.
    /// </summary>
    public void InitializeWall(FishSpawner spawner, FishType type, Mesh mesh)
    {
        _spawner = spawner;
        _type = type;
        _body = transform;
        _isWall = true;
        _jumped = true;
        _wallMesh = mesh;
        _waterY = spawner.WaterY;
        _previousY = transform.position.y;
    }

    /// <summary>
    /// sourceMesh를 복제해 자연 출현 개체의 몸과 포획용 볼록 트리거를 준비한다.
    /// 복제 메시를 소유하여 도감 및 장애물의 공유 메시와 독립적으로 해제한다.
    /// </summary>
    public void PrepareMeshSpawn(Mesh sourceMesh)
    {
        _wallMesh = Instantiate(sourceMesh);
        GetComponent<MeshFilter>().sharedMesh = _wallMesh;
        MeshCollider collider = gameObject.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = _wallMesh;
        collider.isTrigger = true;
    }

    /// <summary>
    /// type을 사용해 이미지 패널을 선택 템플릿으로 초기화한다.
    /// 종류와 몸 참조를 설정하고 자연 출현용 이동을 끈다.
    /// </summary>
    public void InitializeScreen(FishType type)
    {
        _type = type;
        _body = transform;
        enabled = false;
    }

    /// <summary>
    /// delay 뒤 start에서 launchVelocity로 튀어 오르게 준비하고, 튀어 오르기 전까지 몸을 숨긴다.
    /// 각 인자를 사용하며, 이동 상태와 몸 표시 여부를 변경한다.
    /// </summary>
    public void Launch(FishSpawner spawner, Vector3 start, Vector3 launchVelocity, float delay, float waterY)
    {
        _spawner = spawner;
        _startPosition = start;
        _position = start;
        _velocity = launchVelocity;
        _delay = delay;
        _waterY = waterY;
        _gravity = -Physics.gravity.y;
        _airTime = 0f;
        _jumped = false;
        _caught = false;
        _landed = false;
        _isWall = false;
        _isEjected = false;
        _isFalling = false;
        transform.position = start;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }
        if (TryGetComponent(out _fisherQte)) _fisherQte.Initialize(this, spawner);
    }

    /// <summary>
    /// 공중의 start에서 launchVelocity로 즉시 사출하고 입수 전까지 포획을 막는다.
    /// spawner와 waterY를 사용하며, 표시 상태와 사출 이동 상태를 초기화한다.
    /// </summary>
    public void Eject(FishSpawner spawner, Vector3 start, Vector3 launchVelocity, float waterY)
    {
        Launch(spawner, start, launchVelocity, 0f, waterY);
        _jumped = true;
        _isEjected = true;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    /// <summary>
    /// 하늘의 start에서 fallSpeed로 흔들리며 떨어지게 준비하고 바로 보이게 한다. 떨어지는 동안에도 잡을 수 있다.
    /// spawner, start, fallSpeed, waterY, 바라볼 카메라 view와 프리팹 자세를 사용하며, 낙하 이동 상태와 몸 표시 여부를 변경한다.
    /// </summary>
    public void Drop(FishSpawner spawner, Vector3 start, float fallSpeed, float waterY, Transform view)
    {
        _fallRotation = transform.rotation;
        _view = view;
        Launch(spawner, start, Vector3.down * fallSpeed, 0f, waterY);
        _gravity = 0f;
        _jumped = true;
        _isFalling = true;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    /// <summary>
    /// 떨어지는 동안 카메라에서 볼 때 좌우로 흔들리는 거리를 구한다.
    /// _airTime과 _view를 사용하며, 낙하 위치에 더할 수평 이동량을 반환한다.
    /// </summary>
    private Vector3 FallSway()
    {
        Vector3 right = _view.right;
        right.y = 0f;
        return right.normalized * (Mathf.Sin(_airTime * FALL_SWAY_SPEED) * FALL_SWAY_DISTANCE);
    }

    /// <summary>
    /// 날아가는 동안의 몸 방향을 구한다. 머리가 진행 방향을 보며 몸을 흔들고, 타이어와 캔은 굴러가거나 뒤집힌다.
    /// 하늘에서 떨어질 때는 카메라를 보며 흔들림에 맞춰 기운다.
    /// _velocity, _airTime, 종류 모양, _fallRotation과 _view를 사용하며, 회전을 반환한다.
    /// </summary>
    private Quaternion BodyRotation()
    {
        if (_isFalling)
        {
            // 프리팹 자세의 X축 쪽 면을 카메라로 돌리고, 흔들리는 쪽으로 아래가 따라가도록 기울인다.
            Vector3 toView = _view.position - _position;
            toView.y = 0f;
            float tilt = Mathf.Sin(_airTime * FALL_SWAY_SPEED) * FALL_TILT_ANGLE;
            return Quaternion.LookRotation(toView) * Quaternion.Euler(0f, 0f, tilt)
                * Quaternion.Euler(0f, -90f, 0f) * _fallRotation;
        }
        if (_fisherQte != null && !_isEjected)
            return _fisherQte.IsResolving ? transform.rotation : Quaternion.identity;

        if (Type.Shape == FishShape.Tire)
        {
            // 굴러가듯 세워서 돌면서 좌우로 털썩털썩 흔들린다.
            return Quaternion.Euler(_airTime * TIRE_ROLL_SPEED, Mathf.Sin(_airTime * 9f) * 25f, 90f + Mathf.Sin(_airTime * 13f) * 30f);
        }
        if (Type.Shape == FishShape.Can)
        {
            // 앞뒤로 뒤집히면서 불규칙하게 파닥인다.
            return Quaternion.Euler(_airTime * CAN_FLIP_SPEED, Mathf.Sin(_airTime * 11f) * 40f, Mathf.Sin(_airTime * 17f) * 35f);
        }

        Quaternion wiggle = Quaternion.Euler(0f, Mathf.Sin(_airTime * WIGGLE_SPEED) * WIGGLE_ANGLE, 0f);
        return Quaternion.LookRotation(_velocity) * wiggle;
    }
}
