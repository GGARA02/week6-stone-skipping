using System;

using UnityEngine;

// 물고기 도감과 조각 모양을 저장하고 최대 강화 기준의 기본 능력치를 제공한다.
public class PlayerProgress : MonoBehaviour
{
    private const string FISH_KEY_PREFIX = "SkipStoneV2.Fish.";
    private const string WALL_MESH_KEY = "SkipStoneV2.WallMesh";
    private const string ICE_MESH_KEY = "SkipStoneV2.IceMesh";
    private const string SEA_MESH_KEY_PREFIX = "SkipStoneV2.SeaMesh.";

    public float PowerMultiplier => 1.4f;
    public float SpinMultiplier => 2f;
    public event Action<FishType> OnFishRegistered;

    /// <summary>
    /// 물고기가 도감에 등록되었는지 확인한다.
    /// fish를 사용하며, 한 번이라도 잡았으면 true를 반환한다.
    /// </summary>
    public bool IsFishRegistered(FishType fish)
    {
        return GetFishCount(fish) > 0;
    }

    /// <summary>
    /// fish의 ID에 저장된 누적 획득 수를 읽어 반환한다.
    /// 기존 등록 값 1도 획득 1개로 유지하며 미획득 종류는 0을 반환한다.
    /// </summary>
    public int GetFishCount(FishType fish)
    {
        return PlayerPrefs.GetInt(FISH_KEY_PREFIX + fish.Id, 0);
    }

    /// <summary>
    /// fish의 누적 획득 수를 올려 영구 저장하고 최초 획득일 때만 등록 이벤트를 보낸다.
    /// 기존 획득 수를 사용하며 PlayerPrefs와 도감 등록 상태를 변경한다.
    /// </summary>
    public void AddFish(FishType fish)
    {
        int count = GetFishCount(fish);
        PlayerPrefs.SetInt(FISH_KEY_PREFIX + fish.Id, count + 1);
        PlayerPrefs.Save();
        if (count == 0) OnFishRegistered?.Invoke(fish);
    }

    /// <summary>
    /// mesh와 scale을 사용해 마지막으로 획득한 Wall의 모양을 재보정 없이 영구 저장한다.
    /// 독립된 메시를 반환하며 다음 선택과 재실행에서 획득한 형상을 그대로 사용한다.
    /// </summary>
    public Mesh SaveWallMesh(Mesh mesh, Vector3 scale)
    {
        WallFishMeshData data = new WallFishMeshData(mesh, scale, false);
        PlayerPrefs.SetString(WALL_MESH_KEY, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        return data.ToMesh();
    }

    /// <summary>
    /// 저장된 Wall 메시 데이터를 읽어 독립된 메시로 반환한다.
    /// 입력값은 없으며 아직 접촉한 조각이 없으면 null을 반환한다.
    /// </summary>
    public Mesh LoadWallMesh()
    {
        if (!PlayerPrefs.HasKey(WALL_MESH_KEY)) return null;
        return JsonUtility.FromJson<WallFishMeshData>(PlayerPrefs.GetString(WALL_MESH_KEY)).ToMesh();
    }

    /// <summary>
    /// mesh와 scale을 사용해 마지막으로 먹은 유빙 조각의 투척용 모양을 저장한다.
    /// 벽과 독립된 저장 키를 갱신하고 재보정 없이 획득한 형상을 가진 새 메시를 반환한다.
    /// </summary>
    public Mesh SaveIceMesh(Mesh mesh, Vector3 scale)
    {
        WallFishMeshData data = new WallFishMeshData(mesh, scale, false);
        PlayerPrefs.SetString(ICE_MESH_KEY, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        return data.ToMesh();
    }

    /// <summary>
    /// 입력값 없이 저장된 유빙 조각 데이터를 읽어 독립된 투척용 메시를 반환한다.
    /// 아직 먹은 유빙 조각이 없으면 null을 반환하며 벽 저장 데이터는 변경하지 않는다.
    /// </summary>
    public Mesh LoadIceMesh()
    {
        if (!PlayerPrefs.HasKey(ICE_MESH_KEY)) return null;
        return JsonUtility.FromJson<WallFishMeshData>(PlayerPrefs.GetString(ICE_MESH_KEY)).ToMesh();
    }

    /// <summary>
    /// type의 마지막 입수 모양을 mesh와 scale로 저장하고 독립된 스폰용 메시를 반환한다.
    /// prepareForThrow로 최초 절단 조각만 보정하며 획득 및 투척용 저장 모양은 변경하지 않는다.
    /// </summary>
    public Mesh SaveSeaMesh(FishType type, Mesh mesh, Vector3 scale, bool prepareForThrow)
    {
        WallFishMeshData data = new WallFishMeshData(mesh, scale, prepareForThrow);
        PlayerPrefs.SetString(SEA_MESH_KEY_PREFIX + type.Id, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        return data.ToMesh();
    }

    /// <summary>
    /// type의 마지막 입수 메시 데이터를 읽어 새 run에 사용할 독립된 메시를 반환한다.
    /// 아직 입수 모양을 저장하지 않았으면 null을 반환하며 획득 기록은 변경하지 않는다.
    /// </summary>
    public Mesh LoadSeaMesh(FishType type)
    {
        string key = SEA_MESH_KEY_PREFIX + type.Id;
        if (!PlayerPrefs.HasKey(key)) return null;
        return JsonUtility.FromJson<WallFishMeshData>(PlayerPrefs.GetString(key)).ToMesh();
    }

}
