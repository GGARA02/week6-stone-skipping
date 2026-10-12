using UnityEngine;

using UnityEditor;

public static class SkyboxProgressResetTool
{
    private const string MENU_PATH = "Tools/Skybox/Reset Acquisition";
    private const string ACQUISITION_KEY = "SkipStoneV2.Fish.skybox";
    private const string NEW_FISH_KEY = "SkipStoneV2.NewFish.skybox";

    /// <summary>
    /// 입력값 없이 스카이박스의 획득 기록과 "새로운!" 표시를 삭제하고 PlayerPrefs에 저장한다.
    /// 다음 플레이에서 스카이박스를 미획득 상태로 되돌려, 물 밖으로 떨어지면 다시 얻게 한다.
    /// </summary>
    [MenuItem(MENU_PATH)]
    private static void ResetAcquisition()
    {
        if (!CanResetAcquisition()) return;

        PlayerPrefs.DeleteKey(ACQUISITION_KEY);
        PlayerPrefs.DeleteKey(NEW_FISH_KEY);
        PlayerPrefs.Save();
        Debug.Log("스카이박스 획득 상태를 초기화했습니다.");
    }

    /// <summary>
    /// 현재 에디터 재생 상태를 사용해 획득 초기화 메뉴의 활성 여부를 반환한다.
    /// 재생 중이거나 재생 모드 전환 중이면 초기화를 차단한다.
    /// </summary>
    [MenuItem(MENU_PATH, true)]
    private static bool CanResetAcquisition()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
