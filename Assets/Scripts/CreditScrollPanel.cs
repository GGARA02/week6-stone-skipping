using System;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

using TMPro;

public class CreditScrollPanel : MonoBehaviour
{
    private const string DEFAULT_TITLE = "Special Thanks";

    [Header("Font")]
    [SerializeField] private TMP_FontAsset _fontAsset;

    [Header("Data")]
    [SerializeField] private ContributorList _contributor;

    [Header("UI Reference")]
    [Tooltip("스크롤될 CreditPanel (미할당 시 본 오브젝트의 RectTransform)")]
    [SerializeField] private RectTransform _creditPanel;
    [SerializeField] private TMP_Text _creditText;

    [Header("Display Mode (Text Size)")]
    [Tooltip("3D 월드 아이템(Fish) 모드 여부. 체크 시 3D 공간에서 잘 보이도록 폰트 크기를 확대합니다.")]
    [SerializeField] private bool _is3DMode;
    [Tooltip("2D UI 모드 폰트 크기")]
    private const float UIFONTSIZE = 20f;
    [Tooltip("3D 월드 모드 폰트 크기")]
    private const float WORLDFONTSIZE = 43f;

    [Header("Mask Settings")]
    [Tooltip("부모 패널에 RectMask2D를 자동 적용하여 보드 크기 밖으로 나가는 글자를 자를지 여부")]
    [SerializeField] private bool _autoApplyRectMask2D = true;
    [Tooltip("2D UI 모드에서 하단 스킵 문구와 겹치지 않도록 마스크를 띄울 여백(px)")]
    private float _maskPaddingBottom = 80f;
    [Tooltip("2D UI 모드에서 상단 경계와 여백을 둘 마스크 여백(px)")]
    private float _maskPaddingTop = 50f;
    [Tooltip("마스크 상하단 경계면의 부드러운 페이드아웃 효과 크기(px)")]
    private int _maskSoftnessY = 30;

    [Header("Scroll Settings")]
    [Tooltip("위로 스크롤되는 속도 (px/초)")]
    private float _scrollSpeed = 120f;
    [Tooltip("바닥 시작 및 상단 종료 시 추가 여백(px)")]
    [SerializeField] private float _marginOffset = 140f;

    private bool _isInitialized;

    public event Action OnScrollFinished;

    void Awake()
    {
        if (_creditPanel == null)
        {
            _creditPanel = GetComponent<RectTransform>();
        }
        HidePanelBackground();
        EnsureRectMask();
        EnsureCreditText();
    }

    /// <summary>
    /// 기울어진 CreditPanel 자체의 배경 Image가 3D 공간에 노출되지 않도록 비활성화한다.
    /// _creditPanel의 Image 컴포넌트를 탐색하며, enabled를 false로 변경한다.
    /// </summary>
    private void HidePanelBackground()
    {
        // 본 스크립트가 부모(CreditBackground)에 붙어있고 _creditPanel이 자식인 경우, 자식의 Image만 끈다.
        if (_creditPanel != transform && _creditPanel.TryGetComponent<Image>(out var panelImage))
        {
            panelImage.enabled = false;
        }
    }

    /// <summary>
    /// 부모 패널에 RectMask2D를 적용하고 2D UI 모드 시 상하단 패딩 및 소프트니스 페이드를 설정한다.
    /// _autoApplyRectMask2D와 _is3DMode를 사용하며, RectMask2D 컴포넌트의 padding과 softness를 변경한다.
    /// </summary>
    private void EnsureRectMask()
    {
        if (!_autoApplyRectMask2D || _creditPanel == null || _creditPanel.parent == null) return;

        RectMask2D mask = _creditPanel.parent.GetComponent<RectMask2D>();
        if (mask == null && _creditPanel.parent.GetComponent<Mask>() == null)
        {
            mask = _creditPanel.parent.gameObject.AddComponent<RectMask2D>();
        }

        if (mask != null && !_is3DMode)
        {
            // 하단 스킵 문구와 상단 화면 경계에 여백을 두고 경계면을 부드럽게 페이드 처리한다.
            mask.padding = new Vector4(0f, _maskPaddingBottom, 0f, _maskPaddingTop);
            mask.softness = new Vector2Int(0, _maskSoftnessY);
        }
    }

    void OnEnable()
    {
        SetupCreditContent();
        ResetPosition();
    }

    void Update()
    {
        UpdateScroll();
    }

    /// <summary>
    /// 크레딧 패널을 위로 스크롤하고 화면 상단을 벗어나면 바닥으로 리셋하여 무한 반복한다.
    /// _scrollSpeed와 Time.unscaledDeltaTime을 사용하며, _creditPanel의 anchoredPosition을 변경한다.
    /// </summary>
    private void UpdateScroll()
    {
        if (_creditPanel == null) return;

        float startY = GetStartY();
        float endY = GetEndY();

        Vector2 pos = _creditPanel.anchoredPosition;
        pos.y += _scrollSpeed * Time.unscaledDeltaTime;

        // 화면 위로 완전히 벗어나면 바닥 위치로 되돌려 무한 반복한다.
        if (pos.y >= endY)
        {
            pos.y = startY;
            OnScrollFinished?.Invoke();
        }

        _creditPanel.anchoredPosition = pos;
    }

    /// <summary>
    /// Special Thanks 제목과 기여자 목록을 영화 크레딧 형태로 조합하여 텍스트 상단부터 반영한다.
    /// _contributor의 목록을 사용하며, _creditText의 내용과 상단 정렬을 변경한다.
    /// </summary>
    private void SetupCreditContent()
    {
        if (_creditText == null) return;

        _creditText.fontSize = _is3DMode ? WORLDFONTSIZE : UIFONTSIZE;

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("<align=center><size=140%><b>" + DEFAULT_TITLE + "</b></size></align>");
        builder.AppendLine();
        builder.AppendLine();

        if (_contributor != null && _contributor.contributorList != null)
        {
            for (int i = 0; i < _contributor.contributorList.Count; i++)
            {
                string entry = _contributor.contributorList[i];
                if (string.IsNullOrWhiteSpace(entry)) continue;

                entry = entry.Trim();
                int dashIndex = entry.IndexOf('-');
                if (dashIndex >= 0)
                {
                    string asset = entry.Substring(0, dashIndex).Trim();
                    string names = entry.Substring(dashIndex + 1).Trim();

                    // 왼쪽에는 에셋 이름(우측 정렬), 중앙에는 구분선(|), 오른쪽에는 기여자 명단(좌측 정렬)을 배치한다.
                    builder.Append("<line-height=0><margin-right=53%><align=right>");
                    builder.Append(asset);
                    builder.Append("</align></margin>\n");
                    builder.Append("<align=center>|</align>\n</line-height>");
                    builder.Append("<margin-left=53%><align=left>");
                    builder.Append(names);
                    builder.Append("</align></margin>\n\n");
                }
                else
                {
                    builder.Append("<align=center>");
                    builder.Append(entry);
                    builder.Append("</align>\n\n");
                }
            }
        }

        _creditText.text = builder.ToString();
        _creditText.alignment = TextAlignmentOptions.Top;
    }

    /// <summary>
    /// 크레딧 패널의 크기를 맞추고 회전 없이 패널이 화면 바닥 아래에 오도록 초기화한다.
    /// GetStartY()를 사용하며, _creditPanel의 회전, 크기, 위치를 변경한다.
    /// </summary>
    public void ResetPosition()
    {
        if (_creditPanel == null) return;

        // 독립 보드 형태로 동작할 수 있도록 앵커와 피벗을 중앙으로 맞춘다.
        _creditPanel.anchorMin = new Vector2(0.5f, 0.5f);
        _creditPanel.anchorMax = new Vector2(0.5f, 0.5f);
        _creditPanel.pivot = new Vector2(0.5f, 0.5f);

        float contentHeight = GetContentHeight();
        float panelHeight = Mathf.Max(1200f, contentHeight + 400f);
        float panelWidth = _is3DMode ? 1800f : 1000f;
        _creditPanel.sizeDelta = new Vector2(panelWidth, panelHeight);

        _creditPanel.localRotation = Quaternion.identity;
        _creditPanel.localScale = Vector3.one;
        _creditPanel.anchoredPosition = new Vector2(0f, GetStartY());
    }

    /// <summary>
    /// 크레딧 텍스트 컴포넌트가 없으면 패널 자식에 상단 정렬 형태로 새로 생성한다.
    /// _creditPanel을 탐색하며, _creditText를 초기화한다.
    /// </summary>
    private void EnsureCreditText()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        if (_creditText == null)
        {
            _creditText = _creditPanel.GetComponentInChildren<TMP_Text>();
        }

        if (_creditText == null)
        {
            GameObject textObj = new GameObject("CreditText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(_creditPanel, false);

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.05f, 0f);
            rect.anchorMax = new Vector2(0.95f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            _creditText = textObj.GetComponent<TextMeshProUGUI>();
            _creditText.font = _fontAsset;
            _creditText.fontSize = _is3DMode ? WORLDFONTSIZE : UIFONTSIZE;
            _creditText.color = Color.white;
            _creditText.alignment = TextAlignmentOptions.Top;
        }
        else
        {
            _creditText.font = _fontAsset;
            _creditText.fontSize = _is3DMode ? WORLDFONTSIZE : UIFONTSIZE;
            _creditText.alignment = TextAlignmentOptions.Top;
        }
    }

    /// <summary>
    /// 패널의 최상단이 화면 바닥 아래에 완전히 숨겨진 시작 Y 좌표를 계산한다.
    /// 패널 높이와 뷰 높이를 사용하며, 시작 Y 좌표(픽셀)를 반환한다.
    /// </summary>
    private float GetStartY()
    {
        float viewHeight = GetViewHeight();
        float panelHeight = _creditPanel != null ? _creditPanel.sizeDelta.y : 1200f;
        return -viewHeight * 0.5f - panelHeight * 0.5f - _marginOffset;
    }

    /// <summary>
    /// 패널의 최하단이 화면 상단 위로 완전히 벗어난 종료 Y 좌표를 계산한다.
    /// 패널 높이와 뷰 높이를 사용하며, 종료 Y 좌표(픽셀)를 반환한다.
    /// </summary>
    private float GetEndY()
    {
        float viewHeight = GetViewHeight();
        float panelHeight = _creditPanel != null ? _creditPanel.sizeDelta.y : 1200f;
        return viewHeight * 0.5f + panelHeight * 0.5f + _marginOffset;
    }

    /// <summary>
    /// 부모 RectTransform 또는 Screen의 높이를 반환한다.
    /// 부모 Transform을 사용하며, 화면 기준 높이를 반환한다.
    /// </summary>
    private float GetViewHeight()
    {
        if (_creditPanel != null && _creditPanel.parent is RectTransform parentRect && parentRect.rect.height > 0f)
        {
            return parentRect.rect.height;
        }
        return Screen.height > 0 ? Screen.height : 1080f;
    }

    /// <summary>
    /// 크레딧 텍스트의 전체 세로 길이를 계산한다.
    /// _creditText를 사용하며, 계산된 텍스트 높이(픽셀)를 반환한다.
    /// </summary>
    private float GetContentHeight()
    {
        if (_creditText != null)
        {
            return _creditText.preferredHeight;
        }
        return 1000f;
    }
}
