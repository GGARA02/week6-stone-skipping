using System.Collections.Generic;

using UnityEngine;

// 도감 투척물의 특수 동작. SPACE 입력 및 벽 충돌 시 지금까지 해금된 물고기들을 정규화된 크기로 흩뿌린다.
public class CatalogAbility : FishAbility
{
    [Header("충돌 분출 설정")]
    [Tooltip("장애물(벽) 충돌 시 뿜어낼 해금 물고기 수")]
    [SerializeField] private int _collisionScatterCount = 8;
    [Tooltip("분출될 물고기들의 정규화 목표 크기 (가로 X, 두께 Y, 세로 Z)")]
    [SerializeField] private Vector3 _targetDebrisSize = new Vector3(2.5f, 2.5f, 2.5f);
    [Tooltip("충돌 시 사방으로 튀어나가는 폭발 속도")]
    [SerializeField] private float _scatterSpeed = 12f;
    [Tooltip("위쪽으로 솟구치는 기본 속도")]
    [SerializeField] private float _upwardSpeed = 8f;
    [Tooltip("분출된 물고기가 회전하는 속도")]
    [SerializeField] private float _spinSpeed = 15f;
    [Tooltip("분출된 물고기가 사라지기까지의 수명(초)")]
    [SerializeField] private float _debrisLifetime = 3.5f;

    [Header("판정 설정")]
    [Tooltip("SPACE 입력 시 생성할 수량")]
    [SerializeField] private int _judgeScatterCount = 1;

    /// <summary>
    /// 외부 인스펙터 설정값으로부터 크기와 분출 수량을 동적으로 주입받는다.
    /// targetSize와 scatterCount를 사용하며, _targetDebrisSize와 _collisionScatterCount를 변경한다.
    /// </summary>
    public void Configure(Vector3 targetSize, int scatterCount)
    {
        _targetDebrisSize = targetSize;
        if (scatterCount > 0)
        {
            _collisionScatterCount = scatterCount;
        }
    }

    /// <summary>
    /// 던진 상태에서 SPACE를 누를 때마다 판정 성공 때와 같이 해금된 물고기 중 하나를 팝업 연출로 생성한다.
    /// context를 사용하며, 플레이어 위치에서 물고기 파편을 사출한다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        ScatterItems(context, _judgeScatterCount);
    }

    /// <summary>
    /// 장애물(벽)과 충돌했을 때 충돌 지점에서 해금된 물고기들을 대량으로 사방에 흩뿌린다.
    /// context와 collision을 사용하며, 충돌 지점과 반사 방향을 기반으로 물고기 파편들을 분출한다.
    /// </summary>
    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        base.OnObstacleCollision(context, collision);

        ScatterItemsOnCollision(context, collision, _collisionScatterCount);
    }

    /// <summary>
    /// SPACE 입력 시 지정된 수량만큼 해금된 물고기를 팝업 사출한다.
    /// context, count를 사용하며, SpawnDebrisFish를 호출한다.
    /// </summary>
    private void ScatterItems(ThrowContext context, int count)
    {
        List<Fish> unlockedPrefabs = GetUnlockedFishPrefabs(context);
        if (unlockedPrefabs.Count == 0) return;

        Vector3 spawnPos = context.Player != null
            ? context.Player.transform.position + Vector3.up * 0.5f
            : transform.position + Vector3.up * 0.5f;

        Vector3 playerVelocity = context.Player != null ? context.Player.Velocity : Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Fish selectedPrefab = unlockedPrefabs[Random.Range(0, unlockedPrefabs.Count)];
            Vector3 randomSpread = Random.insideUnitSphere * (_scatterSpeed * 0.4f);
            randomSpread.y = Mathf.Abs(randomSpread.y);

            Vector3 launchVelocity = playerVelocity * 0.3f + Vector3.up * _upwardSpeed + randomSpread;
            SpawnDebrisFish(selectedPrefab, spawnPos, launchVelocity);
        }
    }

    /// <summary>
    /// 장애물 충돌 시 지정된 수량만큼 해금된 물고기들을 충돌 지점에서 대량으로 사방 분출한다.
    /// context, collision, count를 사용하며, SpawnDebrisFish를 반복 호출한다.
    /// </summary>
    private void ScatterItemsOnCollision(ThrowContext context, Collision collision, int count)
    {
        List<Fish> unlockedPrefabs = GetUnlockedFishPrefabs(context);
        if (unlockedPrefabs.Count == 0) return;

        Vector3 hitPoint = collision.contacts.Length > 0
            ? collision.contacts[0].point
            : transform.position;

        Vector3 normal = collision.contacts.Length > 0
            ? collision.contacts[0].normal
            : Vector3.up;

        // 충돌 지점에서 해금된 물고기들을 벽 바깥 방향과 사방으로 폭발 분출한다.
        for (int i = 0; i < count; i++)
        {
            Fish selectedPrefab = unlockedPrefabs[Random.Range(0, unlockedPrefabs.Count)];

            Vector3 randomDir = (normal + Vector3.up * 0.5f + Random.insideUnitSphere * 0.7f).normalized;
            Vector3 launchVelocity = randomDir * Random.Range(_scatterSpeed * 0.7f, _scatterSpeed * 1.3f)
                + Vector3.up * _upwardSpeed;

            SpawnDebrisFish(selectedPrefab, hitPoint, launchVelocity);
        }
    }

    /// <summary>
    /// Spawner와 Progress를 조회하여 현재 해금된 물고기 프리팹 목록을 반환한다.
    /// context를 사용하며, 해금된 Fish 프리팹 리스트를 생성해 반환한다.
    /// </summary>
    private List<Fish> GetUnlockedFishPrefabs(ThrowContext context)
    {
        List<Fish> result = new List<Fish>();
        if (context.Spawner == null || context.Spawner.FishPrefabs == null) return result;

        PlayerProgress progress = context.Spawner.Progress;
        IReadOnlyList<Fish> prefabs = context.Spawner.FishPrefabs;

        for (int i = 0; i < prefabs.Count; i++)
        {
            Fish prefab = prefabs[i];
            if (prefab == null || prefab.Type == null) continue;

            // 도감 자신과 화면 캡처는 분출 대상에서 제외한다.
            if (prefab.Type.Id == "catalog" || prefab.Type.Id == "screen") continue;

            // 도감 등록 여부를 확인한다.
            if (progress != null && progress.IsFishRegistered(prefab.Type))
            {
                result.Add(prefab);
            }
        }

        // 해금된 물고기가 전혀 없는 경우 도감/화면 외 첫 번째 프리팹을 기본으로 제공한다.
        if (result.Count == 0)
        {
            foreach (Fish prefab in prefabs)
            {
                if (prefab != null && prefab.Type != null && prefab.Type.Id != "catalog" && prefab.Type.Id != "screen")
                {
                    result.Add(prefab);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 물고기 프리팹을 복제하고 크기를 정규화한 뒤 물리 속도를 주어 파편으로 날려 보낸다.
    /// prefab, spawnPos, launchVelocity를 사용하며, Debris 오브젝트를 생성하고 일정 시간 후 파괴한다.
    /// </summary>
    private void SpawnDebrisFish(Fish prefab, Vector3 spawnPos, Vector3 launchVelocity)
    {
        GameObject debris = Instantiate(prefab.gameObject, spawnPos, Random.rotation);
        debris.SetActive(true);

        // 파편으로만 동작하도록 콜라이더와 비주얼 외 일반 스크립트를 끈다.
        foreach (MonoBehaviour mb in debris.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is BlackHoleLensing) continue;
            mb.enabled = false;
        }
        foreach (Collider col in debris.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // 큰 모델은 줄이고 작은 모델은 키워 목표 크기에 맞춘다.
        NormalizeObjectSize(debris);

        Rigidbody rb = debris.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = debris.AddComponent<Rigidbody>();
        }
        rb.isKinematic = false;
        rb.detectCollisions = false;
        rb.linearVelocity = launchVelocity;
        rb.angularVelocity = Random.insideUnitSphere * _spinSpeed;

        Destroy(debris, _debrisLifetime);
    }

    /// <summary>
    /// 오브젝트 렌더러의 바운드를 측정하여 목표 크기 박스에 맞게 등비율로 스케일링한다.
    /// targetObj와 _targetDebrisSize를 사용하며, targetObj의 localScale을 변경한다.
    /// </summary>
    private void NormalizeObjectSize(GameObject targetObj)
    {
        Renderer[] renderers = targetObj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float maxBoundDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float maxTargetDimension = Mathf.Max(_targetDebrisSize.x, _targetDebrisSize.y, _targetDebrisSize.z);

        if (maxBoundDimension > 0.001f)
        {
            float scaleFactor = maxTargetDimension / maxBoundDimension;
            targetObj.transform.localScale *= scaleFactor;
        }
    }
}
