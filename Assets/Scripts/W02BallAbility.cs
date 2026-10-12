using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class W02BallAbility : FishAbility
{
    private const string EVOLUTION_LAYER = "Evolution";
    private const int ATTACK_MOTION_COUNT = 3;
    private const int BEAM_SEGMENTS = 24;
    private const float BEAM_BOTTOM_RADIUS_SCALE = 1.6f;
    private const float BEAM_TOP_RADIUS_SCALE = 1.1f;

    private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

    [Header("크기")]
    [Min(0.01f)]
    [SerializeField] private float _ejectedScaleMultiplier = 10f;
    [Min(0.01f)]
    [SerializeField] private float _waterSpawnScaleMultiplier = 20f;
    [Min(0.01f)]
    [SerializeField] private float _throwScaleMultiplier = 20f;

    [Header("사출 방향")]
    [Tooltip("수평 사출 방향을 카메라 정면으로 보정하는 비율. 위쪽 도약 속도는 유지한다")]
    [Range(0f, 1f)]
    [SerializeField] private float _cameraDirectionCorrection = 0.5f;

    [Header("진화")]
    [Tooltip("한 번 던진 동안 Perfect를 이 횟수만큼 성공하면 진화한다")]
    [Min(1)]
    [SerializeField] private int _perfectCountToEvolve = 3;
    [Tooltip("진화할 볼크소울 프리팹. 진화하는 순간 도감에 등록되어 선택할 수 있게 된다")]
    [SerializeField] private Fish _evolvedPrefab;
    private int _perfectCount;

    [Header("진화 연출")]
    [Tooltip("진화하는 동안 화면을 흑백으로 바꾸는 볼륨 프로필")]
    [SerializeField] private VolumeProfile _grayscaleProfile;
    [Tooltip("카메라가 다가가며 화면이 흑백이 되는 시간(초)")]
    [Min(0.01f)]
    [SerializeField] private float _approachDuration = 0.5f;
    [Tooltip("진화 전 모습이 점점 빨라지며 도는 시간(초)")]
    [Min(0.01f)]
    [SerializeField] private float _spinUpDuration = 1.2f;
    [Tooltip("진화 후 모습이 점점 느려지며 멈추는 시간(초)")]
    [Min(0.01f)]
    [SerializeField] private float _spinDownDuration = 1f;
    [Tooltip("카메라가 돌아가며 색이 돌아오는 시간(초)")]
    [Min(0.01f)]
    [SerializeField] private float _returnDuration = 0.4f;
    [Tooltip("빨라지는 동안과 느려지는 동안 각각 도는 바퀴 수")]
    [Min(1)]
    [SerializeField] private int _spinTurns = 4;
    [Tooltip("외형 크기에 곱해 카메라가 다가갈 거리")]
    [Min(0.5f)]
    [SerializeField] private float _closeUpDistance = 2f;
    [Tooltip("도는 동안 배경을 검게 만드는 볼륨 프로필. 빨라지며 어두워지고 느려지며 밝아진다")]
    [SerializeField] private VolumeProfile _blackoutProfile;

    [Header("하늘 빛")]
    [SerializeField] private Color _skyLightColor = new Color(1f, 0.95f, 0.8f);
    [Min(0f)]
    [SerializeField] private float _skyLightIntensity = 500f;
    [Tooltip("진화하는 개체 위 몇 m에서 빛을 비출지")]
    [Min(1f)]
    [SerializeField] private float _skyLightHeight = 15f;
    [Tooltip("빛 기둥 머티리얼. 기본 색의 알파로 밝기를 조절한다")]
    [SerializeField] private Material _beamMaterial;
    [Min(1f)]
    [SerializeField] private float _beamHeight = 40f;

    [Header("진화 후 돌진")]
    [Tooltip("시간이 다시 흐를 때 진행 방향으로 더하는 속도")]
    [Min(0f)]
    [SerializeField] private float _burstForwardSpeed = 20f;
    [Tooltip("시간이 다시 흐를 때 위로 튀어 오르는 속도")]
    [Min(0f)]
    [SerializeField] private float _burstUpSpeed = 15f;
    [Tooltip("공격 모션 사이 간격(초). 볼크소울 공격 클립 길이에 맞춘다")]
    [Min(0.05f)]
    [SerializeField] private float _attackInterval = 1f;

    /// <summary>
    /// 던질 개체의 원래 크기를 설정 배율로 확대한다.
    /// _throwScaleMultiplier를 사용하며, 충돌체 생성 전 개체의 스케일을 변경한다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        transform.localScale *= _throwScaleMultiplier;
    }

    /// <summary>
    /// 최초 사출 크기를 설정 배율로 확대하고 카메라의 수평 방향으로 속도를 보정한다.
    /// velocity와 확대 및 보정 설정을 사용하며, 스케일을 변경하고 보정된 속도를 반환한다.
    /// </summary>
    public override Vector3 OnEjected(Vector3 velocity)
    {
        transform.localScale *= _ejectedScaleMultiplier;
        Camera camera = Camera.main;
        if (camera == null || _cameraDirectionCorrection <= 0f) return velocity;

        Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 cameraForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (horizontal.sqrMagnitude <= 0.0001f || cameraForward.sqrMagnitude <= 0.0001f) return velocity;

        Vector3 direction = Vector3.Slerp(horizontal.normalized, cameraForward.normalized, _cameraDirectionCorrection);
        return direction * horizontal.magnitude + Vector3.up * velocity.y;
    }

    /// <summary>
    /// 수면 출현용 프리팹의 원래 크기를 설정 배율로 확대한다.
    /// _waterSpawnScaleMultiplier를 사용하며, 이 개체의 스케일을 변경한다.
    /// </summary>
    public override void OnWaterSpawn()
    {
        transform.localScale *= _waterSpawnScaleMultiplier;
    }

    /// <summary>
    /// 던진 동안 Perfect 성공을 세고, 설정 횟수에 도달하면 진화한다. 진화는 볼크소울을 처음 획득할 때만 일어난다.
    /// context, judge, _perfectCountToEvolve와 볼크소울 도감 등록 여부를 사용하며, _perfectCount를 변경하고 진화 연출을 시작한다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        if (judge != SkipJudge.Perfect) return;
        _perfectCount++;
        if (_perfectCount < _perfectCountToEvolve) return;

        // 이미 획득했으면 진화 연출을 한 번 본 것이므로 공 모습을 그대로 유지한다.
        if (context.Spawner.Progress.IsFishRegistered(_evolvedPrefab.Type)) return;
        // 진화하면 이 개체가 제거되므로 연출은 플레이어에서 돌린다.
        context.Player.StartCoroutine(PlayEvolution(context, _evolvedPrefab, true));
    }

    /// <summary>
    /// 시간을 멈추고 카메라 접근, 흑백 배경, 빛 기둥과 회전 연출로 evolvedPrefab으로 진화하고 도감에 등록한다.
    /// context, evolvedPrefab과 연출 설정을 사용하며 카메라, 외형과 시간 배율을 복원한다. burstAfterEvolution이 참이면 돌진과 공격 모션도 재생한다.
    /// </summary>
    public IEnumerator PlayEvolution(ThrowContext context, Fish evolvedPrefab, bool burstAfterEvolution)
    {
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // 시간이 멈춘 동안 CameraController는 카메라를 움직이지 않으므로 여기서 직접 옮긴다.
        // 메인 카메라는 CinemachineBrain이 덮어쓰므로 추적용 시네머신 카메라를 옮긴다.
        Camera mainCamera = Camera.main;
        Transform view = FindFirstObjectByType<CameraController>().transform;
        Vector3 startPosition = view.position;
        Quaternion startRotation = view.rotation;
        Renderer[] bodyRenderers = context.Body.GetComponentsInChildren<Renderer>();
        Bounds bounds = bodyRenderers[0].bounds;
        foreach (Renderer renderer in bodyRenderers) bounds.Encapsulate(renderer.bounds);
        Vector3 closePosition = bounds.center + (startPosition - bounds.center).normalized * (bounds.extents.magnitude * _closeUpDistance);
        Quaternion closeRotation = Quaternion.LookRotation(bounds.center - closePosition);

        // 흑백과 암전은 메인 카메라 후처리로 건다. 진화하는 개체와 빛 기둥은 후처리 없는 오버레이 카메라로 위에 다시 그려 색을 지킨다.
        int layer = LayerMask.NameToLayer(EVOLUTION_LAYER);
        Camera overlay = CreateOverlayCamera(mainCamera, layer);
        Volume grayscale = CreateVolume(_grayscaleProfile, 100f);
        Volume blackout = CreateVolume(_blackoutProfile, 101f);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * BEAM_BOTTOM_RADIUS_SCALE;
        Light skyLight = CreateSkyLight(bounds.center, radius);
        MeshRenderer beam = CreateBeam(bounds, radius, layer);
        MaterialPropertyBlock beamBlock = new MaterialPropertyBlock();
        List<Renderer> movedRenderers = new List<Renderer>();
        List<int> originalLayers = new List<int>();
        MoveToLayer(context.Shape, layer, movedRenderers, originalLayers);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / _approachDuration)
        {
            float blend = Mathf.SmoothStep(0f, 1f, t);
            view.SetPositionAndRotation(Vector3.Lerp(startPosition, closePosition, blend), Quaternion.Slerp(startRotation, closeRotation, blend));
            grayscale.weight = blend;
            FadeSkyLight(skyLight, beam, beamBlock, blend);
            yield return null;
        }
        view.SetPositionAndRotation(closePosition, closeRotation);
        grayscale.weight = 1f;
        FadeSkyLight(skyLight, beam, beamBlock, 1f);

        yield return Spin(context.Shape.transform, _spinUpDuration, true, blackout);
        context.Spawner.HandlePlacedFishCaught(evolvedPrefab.Type, bounds.center);
        context.Shape.GenerateFish(evolvedPrefab);
        // 이전 개체는 제거되므로 새 개체만 옮겼다가 되돌린다.
        MoveToLayer(context.Shape, layer, movedRenderers, originalLayers);
        if (burstAfterEvolution)
        {
            // 시간이 멈춘 암전 구간에서 돌진 속도를 준비하고 경로의 장애물을 배치한다.
            context.Player.Burst(_burstForwardSpeed, _burstUpSpeed);
            context.Spawner.GetComponent<ProceduralObstacleSpawner>()
                .SpawnEvolutionObstacles(context.Player.transform.position, context.Player.Velocity);
        }
        yield return Spin(context.Shape.transform, _spinDownDuration, false, blackout);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / _returnDuration)
        {
            float blend = Mathf.SmoothStep(0f, 1f, t);
            view.SetPositionAndRotation(Vector3.Lerp(closePosition, startPosition, blend), Quaternion.Slerp(closeRotation, startRotation, blend));
            grayscale.weight = 1f - blend;
            FadeSkyLight(skyLight, beam, beamBlock, 1f - blend);
            yield return null;
        }
        view.SetPositionAndRotation(startPosition, startRotation);

        for (int i = 0; i < movedRenderers.Count; i++)
        {
            movedRenderers[i].gameObject.layer = originalLayers[i];
        }
        mainCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(overlay);
        Destroy(overlay.gameObject);
        Destroy(grayscale.gameObject);
        Destroy(blackout.gameObject);
        Destroy(skyLight.gameObject);
        Destroy(beam.GetComponent<MeshFilter>().sharedMesh);
        Destroy(beam.gameObject);
        Time.timeScale = previousTimeScale;

        if (!burstAfterEvolution) yield break;

        BallController ball = context.Shape.GetComponentInChildren<BallController>();
        for (int i = 0; i < ATTACK_MOTION_COUNT; i++)
        {
            // 공격 도중 판이 다시 시작되면 볼크소울이 제거된다.
            if (ball == null) yield break;
            ball.PlayAttackSequence();
            yield return new WaitForSeconds(_attackInterval);
        }
    }

    /// <summary>
    /// target을 수직축으로 duration 동안 _spinTurns 바퀴 돌리며 암전 볼륨을 함께 바꾼다. speedUp이면 점점 빨라지며 어두워지고, 아니면 점점 느려지며 밝아진다.
    /// target, duration, speedUp, blackout과 _spinTurns를 사용하며, 도는 동안 target 회전과 blackout 가중치를 바꾸고 끝나면 처음 회전으로 되돌린다.
    /// </summary>
    private IEnumerator Spin(Transform target, float duration, bool speedUp, Volume blackout)
    {
        Quaternion startRotation = target.rotation;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            // 정수 바퀴를 돌게 해 끝 회전이 처음과 같아지므로 진화하는 순간과 끝에서 튀지 않는다.
            float turn = speedUp ? t * t : 1f - (1f - t) * (1f - t);
            target.rotation = Quaternion.AngleAxis(turn * _spinTurns * 360f, Vector3.up) * startRotation;
            blackout.weight = speedUp ? t : 1f - t;
            yield return null;
        }
        target.rotation = startRotation;
        blackout.weight = speedUp ? 1f : 0f;
    }

    /// <summary>
    /// mainCamera를 따라다니며 layer만 후처리 없이 그리는 오버레이 카메라를 만들어 메인 카메라 스택에 넣는다.
    /// mainCamera의 시야 설정과 layer를 사용하며, 메인 카메라의 카메라 스택을 변경하고 만든 카메라를 반환한다.
    /// </summary>
    private static Camera CreateOverlayCamera(Camera mainCamera, int layer)
    {
        Camera overlay = new GameObject("EvolutionOverlayCamera").AddComponent<Camera>();
        overlay.transform.SetParent(mainCamera.transform, false);
        overlay.fieldOfView = mainCamera.fieldOfView;
        overlay.nearClipPlane = mainCamera.nearClipPlane;
        overlay.farClipPlane = mainCamera.farClipPlane;
        overlay.cullingMask = 1 << layer;
        UniversalAdditionalCameraData overlayData = overlay.GetUniversalAdditionalCameraData();
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;
        mainCamera.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
        return overlay;
    }

    /// <summary>
    /// profile을 priority로 화면 전체에 거는 볼륨을 가중치 0으로 만든다.
    /// profile과 priority를 사용하며, 만든 볼륨을 반환한다.
    /// </summary>
    private static Volume CreateVolume(VolumeProfile profile, float priority)
    {
        Volume volume = new GameObject(profile.name).AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = priority;
        volume.sharedProfile = profile;
        volume.weight = 0f;
        return volume;
    }

    /// <summary>
    /// center 위 _skyLightHeight에서 아래로 반지름 radius만큼 비추는 스포트 조명을 꺼진 밝기로 만든다.
    /// center, radius와 하늘 빛 설정을 사용하며, 만든 조명을 반환한다.
    /// </summary>
    private Light CreateSkyLight(Vector3 center, float radius)
    {
        Light light = new GameObject("EvolutionSkyLight").AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = _skyLightColor;
        light.range = _skyLightHeight * 2f;
        light.spotAngle = Mathf.Atan(radius / _skyLightHeight) * Mathf.Rad2Deg * 2f;
        light.intensity = 0f;
        light.transform.SetPositionAndRotation(center + Vector3.up * _skyLightHeight, Quaternion.Euler(90f, 0f, 0f));
        return light;
    }

    /// <summary>
    /// bounds 바닥에서 하늘로 올라가는 빛 기둥을 layer에 만든다.
    /// bounds, 아래 반지름 radius, layer와 _beamHeight, _beamMaterial을 사용하며, 만든 빛 기둥 렌더러를 반환한다.
    /// </summary>
    private MeshRenderer CreateBeam(Bounds bounds, float radius, int layer)
    {
        GameObject beam = new GameObject("EvolutionBeam");
        beam.layer = layer;
        beam.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        beam.AddComponent<MeshFilter>().sharedMesh = BuildBeamMesh(radius, radius / BEAM_BOTTOM_RADIUS_SCALE * BEAM_TOP_RADIUS_SCALE, _beamHeight);
        MeshRenderer renderer = beam.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _beamMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>
    /// 아래 반지름 bottomRadius, 위 반지름 topRadius, 높이 height인 뚜껑 없는 원뿔대 메시를 만든다. 정점 알파는 아래가 1이고 위로 갈수록 0이 된다.
    /// 각 인자와 BEAM_SEGMENTS를 사용하며, 새 메시를 반환한다.
    /// </summary>
    private static Mesh BuildBeamMesh(float bottomRadius, float topRadius, float height)
    {
        Vector3[] vertices = new Vector3[(BEAM_SEGMENTS + 1) * 2];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[BEAM_SEGMENTS * 6];
        for (int i = 0; i <= BEAM_SEGMENTS; i++)
        {
            float angle = i * Mathf.PI * 2f / BEAM_SEGMENTS;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * bottomRadius;
            vertices[i * 2 + 1] = direction * topRadius + Vector3.up * height;
            colors[i * 2] = Color.white;
            colors[i * 2 + 1] = new Color(1f, 1f, 1f, 0f);
            if (i == BEAM_SEGMENTS) break;

            int triangle = i * 6;
            triangles[triangle] = i * 2;
            triangles[triangle + 1] = i * 2 + 1;
            triangles[triangle + 2] = i * 2 + 2;
            triangles[triangle + 3] = i * 2 + 2;
            triangles[triangle + 4] = i * 2 + 1;
            triangles[triangle + 5] = i * 2 + 3;
        }

        Mesh mesh = new Mesh { name = "EvolutionBeam" };
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        return mesh;
    }

    /// <summary>
    /// 하늘 빛 조명과 빛 기둥을 amount 비율로 밝힌다.
    /// light, beam, block, amount와 _skyLightIntensity, _beamMaterial의 기본 색을 사용하며, 조명 밝기와 빛 기둥의 색 알파를 변경한다.
    /// </summary>
    private void FadeSkyLight(Light light, MeshRenderer beam, MaterialPropertyBlock block, float amount)
    {
        light.intensity = _skyLightIntensity * amount;
        Color color = _beamMaterial.GetColor(BASE_COLOR_ID);
        color.a *= amount;
        block.SetColor(BASE_COLOR_ID, color);
        beam.SetPropertyBlock(block);
    }

    /// <summary>
    /// shape 아래 던진 외형의 렌더러들을 layer로 옮기고 원래 레이어를 기록한다.
    /// shape와 layer를 사용하며, renderers와 layers를 새로 채우고 렌더러 오브젝트의 레이어를 변경한다.
    /// </summary>
    private static void MoveToLayer(FishMeshGenerator shape, int layer, List<Renderer> renderers, List<int> layers)
    {
        renderers.Clear();
        layers.Clear();
        foreach (Renderer renderer in shape.GetComponentsInChildren<Renderer>())
        {
            // 생성기 자신에는 충돌체가 있어 레이어를 바꾸지 않는다.
            if (renderer.gameObject == shape.gameObject) continue;

            renderers.Add(renderer);
            layers.Add(renderer.gameObject.layer);
            renderer.gameObject.layer = layer;
        }
    }
}
