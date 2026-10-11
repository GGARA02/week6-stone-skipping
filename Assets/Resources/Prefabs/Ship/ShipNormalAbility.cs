using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public class ShipNormalAbility : FishAbility
{
    private const float FLAME_DURATION = 2f;

    [Header("화염 포탑")]
    [SerializeField] private GameObject[] _flameEffects = new GameObject[0];
    private Coroutine _flameRoutine;

    [Header("진행 상태")]
    private ShipAideController _controller;
    private bool _throwStarted;

    [Header("톱날 절단")]
    private PlayerController _player;
    private MeshFilter _throwGeometry;
    private Mesh _throwMesh;
    private readonly List<Renderer> _saws = new List<Renderer>();
    private readonly HashSet<CuttableWall> _contactWalls = new HashSet<CuttableWall>();
    private readonly HashSet<CuttableWall> _currentWalls = new HashSet<CuttableWall>();
    public override MeshFilter ThrowGeometry => _throwGeometry;

    void FixedUpdate()
    {
        if (_player == null || !_player.IsThrown || _player.IsGameOver) return;

        CutWallsWithSaws();
    }

    void OnDestroy()
    {
        if (_throwMesh != null) Destroy(_throwMesh);
    }

    void OnDisable()
    {
        if (_flameRoutine != null) StopCoroutine(_flameRoutine);
        _flameRoutine = null;
        SetFlamesActive(false);
    }

    /// <summary>
    /// 현재 투척 선택을 관리하는 controller를 연결한다.
    /// 이후 최초 투척과 판정 결과를 전달할 참조를 저장한다.
    /// </summary>
    public void Bind(ShipAideController controller)
    {
        _controller = controller;
    }

    /// <summary>
    /// 던질 개체가 될 때 플레이어와 톱날을 저장하고, 톱날을 뺀 선체만으로 투척 형상을 만든다.
    /// 부모 PlayerController와 자식 MeshFilter, 이름이 Ice Saw인 자식을 사용하며, _player, _saws와 ThrowGeometry를 변경한다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        _player = GetComponentInParent<PlayerController>();
        foreach (Transform part in GetComponentsInChildren<Transform>(true))
        {
            if (part.name != "Ice Saw" && part.name != "IceSaw") continue;
            Renderer saw = part.GetComponentInChildren<Renderer>(true);
            if (saw != null) _saws.Add(saw);
        }

        // 톱날은 충돌체와 수면 판정에서 빼서 벽을 갈고 지나가고, 선체만 벽에 부딪히게 한다.
        GameObject geometry = new GameObject("Ship Throw Geometry");
        geometry.transform.SetParent(transform, false);
        List<CombineInstance> parts = new List<CombineInstance>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (IsSawPart(filter.transform)) continue;
            for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
            {
                parts.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = i,
                    transform = geometry.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                });
            }
        }
        _throwMesh = new Mesh { name = "Ship_Throw_Geometry" };
        _throwMesh.CombineMeshes(parts.ToArray(), true, true);
        _throwGeometry = geometry.AddComponent<MeshFilter>();
        _throwGeometry.sharedMesh = _throwMesh;
    }

    /// <summary>
    /// part 자신이나 배 안의 부모 중에 톱날이 있는지 확인한다.
    /// part에서 이 오브젝트까지의 이름을 사용하며, 톱날의 일부면 true를 반환한다.
    /// </summary>
    private bool IsSawPart(Transform part)
    {
        for (Transform current = part; current != transform; current = current.parent)
        {
            if (current.name == "Ice Saw" || current.name == "IceSaw") return true;
        }
        return false;
    }

    /// <summary>
    /// 톱날 범위에서 다음 물리 단계까지 닿을 벽과 유빙을 톱날 높이에서 수평으로 갈아 자른다.
    /// _saws의 범위와 플레이어 속도를 사용하며, 장애물 모양과 접촉 중인 벽 목록을 변경한다.
    /// </summary>
    private void CutWallsWithSaws()
    {
        Vector3 step = _player.Velocity * Time.fixedDeltaTime;
        _currentWalls.Clear();
        foreach (Renderer saw in _saws)
        {
            Bounds bounds = saw.bounds;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            foreach (Collider collider in Physics.OverlapCapsule(bounds.center, bounds.center + step, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!collider.TryGetComponent(out CuttableWall wall)) continue;
                // 닿아 있는 동안에는 한 번만 잘라 물리 단계마다 조각이 쏟아지지 않게 한다.
                if (_contactWalls.Contains(wall) || _currentWalls.Contains(wall))
                {
                    _currentWalls.Add(wall);
                    continue;
                }
                if (wall.Cut(bounds.center, Vector3.up)) _currentWalls.Add(wall);
            }
        }
        _contactWalls.Clear();
        _contactWalls.UnionWith(_currentWalls);
    }

    /// <summary>
    /// context의 실제 비행 시작을 확인해 첫 프레임에 ShipNormal 사용을 알린다.
    /// dt와 관계없이 한 번만 시작 상태를 변경하고 연결된 컨트롤러를 호출한다.
    /// </summary>
    public override void UpdateFlight(ThrowContext context, float dt)
    {
        if (_throwStarted || _controller == null || !context.Player.IsThrown) return;
        _throwStarted = true;
        _controller.BeginShipThrow(this);
    }

    /// <summary>
    /// ShipNormal 투척 중 부딪힌 벽이나 유빙을 접촉 높이에서 수평으로 절단한다.
    /// context의 게임오버 상태와 collision의 충돌체, 접촉점을 사용해 장애물과 절단 조각을 변경한다.
    /// </summary>
    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        if (context.Player.IsGameOver) return;

        if (collision.collider.TryGetComponent(out CuttableWall wall))
        {
            wall.Cut(collision.GetContact(0).point, Vector3.up);
        }
    }

    /// <summary>
    /// SPACE 성공 및 Mash Space 입력을 초상화 상호작용에 전달한다.
    /// context와 judge의 성공 통지로 표정과 대사를 변경하고 화염 포탑을 2초 동안 켠다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        if (_controller != null) _controller.ReactSuccess(this);
        if (_flameRoutine != null) StopCoroutine(_flameRoutine);
        _flameRoutine = StartCoroutine(PlayFlames());
    }

    /// <summary>
    /// context에서 발생한 SPACE 실패를 초상화 상호작용에 전달한다.
    /// 연결된 컨트롤러의 실패 누적과 표정, 대사를 변경한다.
    /// </summary>
    public override void OnJudgeMiss(ThrowContext context)
    {
        if (_controller != null) _controller.ReactFailure(this);
    }

    /// <summary>
    /// 연결된 화염 이펙트를 켜고 FLAME_DURATION이 지나면 끄는 코루틴을 반환한다.
    /// 입력값 없이 포탑 표시와 실행 중인 코루틴 참조를 갱신한다.
    /// </summary>
    private IEnumerator PlayFlames()
    {
        SetFlamesActive(true);
        yield return new WaitForSeconds(FLAME_DURATION);
        SetFlamesActive(false);
        _flameRoutine = null;
    }

    /// <summary>
    /// active를 사용해 프리팹에 연결된 모든 화염 이펙트의 활성 상태를 변경한다.
    /// 켜질 때 파티클이 재생되고 꺼질 때 화면에서 제거된다.
    /// </summary>
    private void SetFlamesActive(bool active)
    {
        foreach (GameObject effect in _flameEffects) effect.SetActive(active);
    }
}
