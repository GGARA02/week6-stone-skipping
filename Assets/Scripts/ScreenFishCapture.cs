using System;
using System.Collections;
using System.IO;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 블랙홀의 최초 착수 화면을 천처럼 흡입하고 저장된 16:9 이미지를 SCREEN 투척물로 제공한다.
/// </summary>
public class ScreenFishCapture : MonoBehaviour
{
    private const int IMAGE_WIDTH = 1280;
    private const int IMAGE_HEIGHT = 720;

    [Header("Panel Size")]
    [Tooltip("패널의 생성 크기 (가로 X, 두께 Y, 세로 Z | 16: 9 비율)")]
    [SerializeField] private Vector3 _panelSize = new Vector3(3.2f, 0.08f, 1.8f);
#if UNITY_EDITOR
    private Vector3 _lastPanelSize = new Vector3(3.2f, 0.08f, 1.8f);
#endif

    [Header("Screen Suction")]
    [SerializeField, Range(1f, 2f)] private float _duration = 1.7f;
    [SerializeField, Range(0.5f, 4f)] private float _suctionStrength = 2.4f;
    [SerializeField, Range(0.1f, 2f)] private float _suctionRadius = 0.65f;
    [SerializeField, Range(0.5f, 4f)] private float _falloff = 1.8f;
    [SerializeField, Range(0f, 3f)] private float _stretchStrength = 1.6f;
    [SerializeField, Range(0f, 0.15f)] private float _wrinkleStrength = 0.065f;
    [SerializeField, Range(0f, 0.3f)] private float _twistStrength = 0.045f;
    [SerializeField, Range(1f, 6f)] private float _finalCollapseSpeed = 3.5f;

    [Header("캡처 상태")]
    private PlayerProgress _progress;
    private Fish _template;
    private Mesh _mesh;
    private Material _material;
    private RenderTexture _frame;
    private Material _suctionMaterial;
    private GameObject _overlay;
    private Transform _hole;
    private Vector2 _holePosition;
    private float _absorbProgress;
    private float _previousTimeScale;
    private string _imagePath;
    public Texture2D Image { get; private set; }
    public bool IsCapturing { get; private set; }
    public bool HasFrame => _frame != null;
    public Vector3 PanelSize
    {
        get => _panelSize;
        set => _panelSize = value;
    }
    public event Action OnUnlocked;

    /// <summary>
    /// progress, type과 종류별 저장 PNG를 사용해 비활성 이미지 패널 템플릿을 만든다.
    /// 16:9 패널 메시와 이미지 머티리얼을 준비하고 선택 목록에 넣을 Fish를 반환한다.
    /// </summary>
    public Fish Initialize(PlayerProgress progress, FishType type)
    {
        _progress = progress;
        _imagePath = Path.Combine(Application.persistentDataPath, $"{type.Id}-fish.png");
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = $"{type.DisplayName} Fish Catalog";
        panel.SetActive(false);
        panel.transform.SetParent(transform, false);
        Destroy(panel.GetComponent<Collider>());
        _mesh = Instantiate(panel.GetComponent<MeshFilter>().sharedMesh);
        Vector3[] vertices = _mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = Vector3.Scale(vertices[i], _panelSize);   //패널 크기 설정
        _mesh.vertices = vertices;
        Vector2[] uvs = _mesh.uv;
        Vector3[] normals = _mesh.normals;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (Mathf.Abs(normals[i].y) > 0.5f)
                uvs[i] = new Vector2(vertices[i].x / _panelSize.x + 0.5f, vertices[i].z / _panelSize.z + 0.5f);
        }
        _mesh.uv = uvs;
        _mesh.RecalculateBounds();
        panel.GetComponent<MeshFilter>().sharedMesh = _mesh;
        _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        panel.GetComponent<MeshRenderer>().sharedMaterial = _material;
        panel.AddComponent<Rigidbody>().isKinematic = true;
        _template = panel.AddComponent<Fish>();
        _template.InitializeScreen(type);
        if (File.Exists(_imagePath))
        {
            Image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (Image.LoadImage(File.ReadAllBytes(_imagePath))) _material.mainTexture = Image;
            else
            {
                Destroy(Image);
                Image = null;
                PlayerPrefs.DeleteKey("SkipStoneV2.Fish." + type.Id);
            }
        }
        else PlayerPrefs.DeleteKey("SkipStoneV2.Fish." + type.Id);
        return _template;
    }

    /// <summary>
    /// hole의 화면 위치를 목표로 전체 게임 화면 캡처와 흡입을 시작한다.
    /// 게임 시간을 멈추며 캡처 중 상태를 변경한다.
    /// </summary>
    public void Capture(Transform hole)
    {
        if (IsCapturing) return;
        _previousTimeScale = Time.timeScale;
        IsCapturing = true;
        Time.timeScale = 0f;
        _hole = hole;
        _absorbProgress = 0f;
        StartCoroutine(AbsorbScreen());
    }

    /// <summary>
    /// 현재 UI가 그려진 프레임을 기다려 흡입 연출 없이 16:9 패널로 캡처한다.
    /// 입력값 없이 종류별 이미지와 도감 등록을 저장하고 캡처 상태를 해제한다.
    /// </summary>
    public IEnumerator CapturePanel()
    {
        _previousTimeScale = Time.timeScale;
        IsCapturing = true;
        try
        {
            yield return new WaitForEndOfFrame();
            CaptureFrame();
            PrepareImage();
            File.WriteAllBytes(_imagePath, Image.EncodeToPNG());
            _progress.AddFish(_template.Type);
        }
        finally
        {
            ReleaseCapture();
        }
    }

    /// <summary>
    /// 렌더 완료된 현재 게임 화면을 HUD까지 포함해 캡처하고 그래픽 API의 상하 방향을 보정한다.
    /// 화면 크기를 사용하며 _frame에 정방향 캡처 텍스처를 저장한다.
    /// </summary>
    private void CaptureFrame()
    {
        _frame = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Screen Panel Capture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _frame.Create();
        RenderTexture rawFrame = RenderTexture.GetTemporary(_frame.width, _frame.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            UnityEngine.ScreenCapture.CaptureScreenshotIntoRenderTexture(rawFrame);
            Vector2 scale = SystemInfo.graphicsUVStartsAtTop ? new Vector2(1f, -1f) : Vector2.one;
            Vector2 offset = SystemInfo.graphicsUVStartsAtTop ? Vector2.up : Vector2.zero;
            Graphics.Blit(rawFrame, _frame, scale, offset);
        }
        finally
        {
            RenderTexture.ReleaseTemporary(rawFrame);
        }
    }

    /// <summary>
    /// 접촉 프레임의 렌더 완료를 기다려 HUD 포함 화면을 한 번 캡처하고 _duration 동안 흡입한다.
    /// _hole과 Inspector 값을 사용하며 SCREEN 해금, 임시 리소스 해제 및 이전 게임 시간 복원을 수행한다.
    /// </summary>
    private IEnumerator AbsorbScreen()
    {
        try
        {
            yield return new WaitForEndOfFrame();
            CaptureFrame();
            Vector3 viewport = Camera.main.WorldToViewportPoint(_hole.position);
            _holePosition = viewport.z > 0f
                ? new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y))
                : new Vector2(0.5f, 0.5f);
            PrepareImage();
            CreateOverlay();
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < _duration)
            {
                _absorbProgress = Mathf.Clamp01((Time.realtimeSinceStartup - start) / _duration);
                _suctionMaterial.SetFloat("_Progress", _absorbProgress);
                yield return null;
            }
            _absorbProgress = 1f;
            _suctionMaterial.SetFloat("_Progress", 1f);
            yield return new WaitForEndOfFrame();
            _progress.AddFish(_template.Type);
            OnUnlocked?.Invoke();
        }
        finally
        {
            ReleaseCapture();
        }
        yield return null;
        File.WriteAllBytes(_imagePath, Image.EncodeToPNG());
    }

    /// <summary>
    /// 전체 캡처 프레임을 비율 유지해 1280x720 패널 이미지로 만든다.
    /// _frame을 사용하며 Image와 SCREEN 머티리얼을 변경한다.
    /// </summary>
    private void PrepareImage()
    {
        RenderTexture target = RenderTexture.GetTemporary(IMAGE_WIDTH, IMAGE_HEIGHT, 0);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, IMAGE_WIDTH, IMAGE_HEIGHT, 0);
            float scale = Mathf.Min((float)IMAGE_WIDTH / _frame.width, (float)IMAGE_HEIGHT / _frame.height);
            float width = _frame.width * scale;
            float height = _frame.height * scale;
            Graphics.DrawTexture(new Rect((IMAGE_WIDTH - width) * 0.5f, (IMAGE_HEIGHT - height) * 0.5f, width, height), _frame);
            GL.PopMatrix();
            Texture2D image = new Texture2D(IMAGE_WIDTH, IMAGE_HEIGHT, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, IMAGE_WIDTH, IMAGE_HEIGHT), 0, 0);
            image.Apply();
            if (Image != null) Destroy(Image);
            Image = image;
            _material.mainTexture = Image;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>
    /// 캡처 텍스처와 고정된 Viewport 중심으로 원본 화면 배경과 단일 GPU 천 메시를 만든다.
    /// Inspector 튜닝값을 머티리얼에 전달하며 캡처 중에만 존재하는 Overlay Canvas를 생성한다.
    /// </summary>
    private void CreateOverlay()
    {
        _suctionMaterial = new Material(Resources.Load<Shader>("Shader/ScreenSuction"));
        _suctionMaterial.SetTexture("_MainTex", _frame);
        _suctionMaterial.SetVector("_SuctionCenter", new Vector4(_holePosition.x, _holePosition.y, 0f, 0f));
        _suctionMaterial.SetFloat("_Aspect", (float)_frame.width / _frame.height);
        _suctionMaterial.SetFloat("_SuctionStrength", _suctionStrength);
        _suctionMaterial.SetFloat("_SuctionRadius", _suctionRadius);
        _suctionMaterial.SetFloat("_Falloff", _falloff);
        _suctionMaterial.SetFloat("_StretchStrength", _stretchStrength);
        _suctionMaterial.SetFloat("_WrinkleStrength", _wrinkleStrength);
        _suctionMaterial.SetFloat("_TwistStrength", _twistStrength);
        _suctionMaterial.SetFloat("_FinalCollapseSpeed", _finalCollapseSpeed);
        _overlay = new GameObject("Screen Suction Overlay", typeof(Canvas));
        Canvas canvas = _overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        GameObject background = new GameObject("Original Frame", typeof(RectTransform), typeof(RawImage));
        background.transform.SetParent(_overlay.transform, false);
        StretchOverlay(background.GetComponent<RectTransform>());
        background.GetComponent<RawImage>().texture = _frame;
        background.GetComponent<RawImage>().raycastTarget = false;
        GameObject cloth = new GameObject("Captured Cloth", typeof(RectTransform), typeof(CanvasRenderer), typeof(ScreenSuctionGraphic));
        cloth.transform.SetParent(_overlay.transform, false);
        StretchOverlay(cloth.GetComponent<RectTransform>());
        ScreenSuctionGraphic graphic = cloth.GetComponent<ScreenSuctionGraphic>();
        graphic.material = _suctionMaterial;
        graphic.raycastTarget = false;
        graphic.SetTexture(_frame);
    }

    /// <summary>
    /// rect를 부모 전체에 맞춰 캡처와 배경이 화면을 덮도록 앵커와 여백을 변경한다.
    /// </summary>
    private void StretchOverlay(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 입력값 없이 캡처의 Canvas, 머티리얼, RenderTexture를 해제한다.
    /// 캡처 상태를 종료하고 시작 전에 기록한 timeScale을 복원한다.
    /// </summary>
    private void ReleaseCapture()
    {
        if (_overlay != null)
        {
            _overlay.SetActive(false);
            Destroy(_overlay);
        }
        if (_suctionMaterial != null) Destroy(_suctionMaterial);
        if (_frame != null)
        {
            _frame.Release();
            Destroy(_frame);
        }
        _overlay = null;
        _suctionMaterial = null;
        _frame = null;
        Time.timeScale = _previousTimeScale;
        IsCapturing = false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 패널 크기 조절 시 자동으로 16:9 비율로 계산해주는 메서드
    /// </summary>
    void OnValidate()
    {
        // 가로(X)가 변경된 경우 세로(Z)를 16:9 비율로 자동 보정한다.
        if (!Mathf.Approximately(_panelSize.x, _lastPanelSize.x))
        {
            _panelSize.z = _panelSize.x * (9f / 16f);
        }
        // 세로(Z)가 변경된 경우 가로(X)를 16:9 비율로 자동 보정한다.
        else if (!Mathf.Approximately(_panelSize.z, _lastPanelSize.z))
        {
            _panelSize.x = _panelSize.z * (16f / 9f);
        }

        _lastPanelSize = _panelSize;
    }
#endif

    void OnDisable()
    {
        if (!IsCapturing) return;
        StopAllCoroutines();
        if (IsCapturing) ReleaseCapture();
    }

    void OnDestroy()
    {
        if (Image != null) Destroy(Image);
        if (_material != null) Destroy(_material);
        if (_mesh != null) Destroy(_mesh);
    }
}
