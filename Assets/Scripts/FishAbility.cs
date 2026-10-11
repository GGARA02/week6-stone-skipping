using UnityEngine;

// 물고기 프리팹 루트에 붙어 종류별 특수 동작을 수행한다. 하위 클래스는 필요한 동작만 재정의한다.
// Fish를 참조하지 않으며, 동작에 필요한 대상은 호출하는 쪽(PlayerController)이 넘겨준다.
public abstract class FishAbility : MonoBehaviour
{
    /// <summary>
    /// 투척 시 수면 판정, 충돌체와 카메라 크기 계산에 사용할 메시를 반환한다.
    /// 지정하지 않으면 프리팹의 기존 합성 형상을 사용한다.
    /// </summary>
    public virtual MeshFilter ThrowGeometry => null;

    /// <summary>
    /// 플레이어가 던질 개체의 외형을 충돌체 생성 전에 준비한다. 기본 동작은 없다.
    /// 입력값은 없으며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnPrepareThrow()
    {
    }

    /// <summary>
    /// 사출된 개체의 초기 속도와 외형을 준비한다. 기본 동작은 입력 속도를 유지한다.
    /// velocity를 사용하며, 사출에 사용할 속도를 반환한다.
    /// </summary>
    public virtual Vector3 OnEjected(Vector3 velocity)
    {
        return velocity;
    }

    /// <summary>
    /// 수면에서 출현할 개체의 외형을 준비한다. 기본 동작은 없다.
    /// 입력값은 없으며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnWaterSpawn()
    {
    }

    /// <summary>
    /// 하늘에서 떨어질 개체의 외형을 준비한다. 기본 동작은 없다.
    /// 입력값은 없으며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnSkyFall()
    {
    }

    /// <summary>
    /// 던진 상태에서 SPACE 판정에 성공했을 때 특수 동작을 한다. 기본 동작은 없다.
    /// context와 judge(Perfect 또는 Good)를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
    }

    /// <summary>
    /// 던진 상태에서 판정 대기나 MISS 쿨타임과 관계없이 SPACE를 누를 때마다 특수 동작을 한다. 기본 동작은 없다.
    /// context를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnSpacePressed(ThrowContext context)
    {
    }

    /// <summary>
    /// 던진 상태에서 SPACE 판정에 실패(MISS)했을 때 특수 동작을 한다. 기본 동작은 없다.
    /// context를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnJudgeMiss(ThrowContext context)
    {
    }

    /// <summary>
    /// 던진 뒤 튕겨 나가지 못하고 미끄러지기 시작할 때 특수 동작을 한다. 기본 동작은 없다.
    /// context를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnSlideStart(ThrowContext context)
    {
    }

    /// <summary>
    /// 투척물과 장애물의 충돌을 종류별 특수 동작에 전달한다. 기본 동작은 없다.
    /// context와 collision의 접촉 정보를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void OnObstacleCollision(ThrowContext context, Collision collision)
    {
    }

    /// <summary>
    /// 던진 뒤 게임오버 전까지 매 프레임 특수 동작을 갱신한다. 기본 동작은 없다.
    /// context와 dt를 사용하며, 변경하는 상태는 하위 클래스가 정한다.
    /// </summary>
    public virtual void UpdateFlight(ThrowContext context, float dt)
    {
    }
}
