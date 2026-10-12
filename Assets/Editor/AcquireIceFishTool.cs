using UnityEngine;

using UnityEditor;

// 유빙(ICE FLOE)을 도감에 획득 처리하는 메뉴.
// 플레이 중이면 진행 상황을 거쳐 획득 팝업까지 띄우고, 아니면 PlayerPrefs의 획득 수만 올린다.
public static class AcquireIceFishTool
{
    private const string FISH_KEY_PREFIX = "SkipStoneV2.Fish.";
    private const string ICE_FISH_ID = "ice";

    /// <summary>
    /// 유빙의 누적 획득 수를 1 올려 도감에 획득 상태로 저장한다.
    /// 플레이 중인 FishSpawner가 있으면 그 진행 상황을, 없으면 PlayerPrefs를 직접 변경한다.
    /// </summary>
    [MenuItem("Tools/Acquire Ice Floe")]
    public static void AcquireIceFish()
    {
        FishSpawner spawner = Application.isPlaying ? Object.FindFirstObjectByType<FishSpawner>() : null;
        if (spawner != null)
        {
            spawner.Progress.AddFish(spawner.IceFish.Type);
        }
        else
        {
            string key = FISH_KEY_PREFIX + ICE_FISH_ID;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
            PlayerPrefs.Save();
        }
        Debug.Log("유빙(ICE FLOE)을 도감에 획득 처리했습니다.");
    }
}
