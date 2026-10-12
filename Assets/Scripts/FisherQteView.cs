using UnityEngine;
using UnityEngine.UI;

public class FisherQteView : MonoBehaviour
{
    [Header("프리팹 UI 참조")]
    [SerializeField] private GameObject _readyGroup;
    [SerializeField] private GameObject _timingGroup;
    [SerializeField] private RectTransform _successZone;
    [SerializeField] private RectTransform _symbol;
    [SerializeField] private Slider _timeSlider;
    [SerializeField] private Image _timeFill;
    [SerializeField] private Text _wins;

    [Header("시간 막대 색상")]
    [SerializeField] private Color _timeColor = new Color(0.1f, 0.9f, 1f);
    [SerializeField] private Color _lowTimeColor = new Color(1f, 0.3f, 0.2f);

    /// <summary>
    /// 입력값 없이 Ready 글자만 표시하고 타이밍 막대를 숨긴다.
    /// 카메라 이동이 끝난 준비 단계의 Canvas 활성 상태를 변경한다.
    /// </summary>
    public void ShowReady()
    {
        _readyGroup.SetActive(true);
        _timingGroup.SetActive(false);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// zoneWidth와 successes로 중앙 성공 영역의 비율과 누적 기록을 설정한다.
    /// Ready를 숨기고 타이밍 UI를 표시하며 심볼과 남은 시간을 초기화한다.
    /// </summary>
    public void ShowTiming(float zoneWidth, int successes)
    {
        _successZone.anchorMin = new Vector2(0.5f - zoneWidth * 0.5f, 0f);
        _successZone.anchorMax = new Vector2(0.5f + zoneWidth * 0.5f, 1f);
        _successZone.offsetMin = Vector2.zero;
        _successZone.offsetMax = Vector2.zero;
        _wins.text = $"QTE 성공 횟수: {successes}";
        SetTiming(0f, 1f);
        _readyGroup.SetActive(false);
        _timingGroup.SetActive(true);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// symbolPosition과 remainingRatio로 심볼 위치와 제한 시간 슬라이더를 갱신한다.
    /// 값은 전체 막대의 0~1 비율이며 시간이 25퍼센트 이하이면 경고 색을 표시한다.
    /// </summary>
    public void SetTiming(float symbolPosition, float remainingRatio)
    {
        _symbol.anchorMin = _symbol.anchorMax = new Vector2(Mathf.Clamp01(symbolPosition), 0.5f);
        _symbol.anchoredPosition = Vector2.zero;
        _timeSlider.SetValueWithoutNotify(Mathf.Clamp01(remainingRatio));
        _timeFill.color = remainingRatio <= 0.25f ? _lowTimeColor : _timeColor;
    }

    /// <summary>
    /// 입력값 없이 QTE Canvas를 숨기고 다음 진입에서 재사용할 UI 참조를 유지한다.
    /// Canvas GameObject의 활성 상태를 변경한다.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
