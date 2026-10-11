using UnityEngine;
using UnityEngine.SceneManagement;

public class Managers : MonoBehaviour
{
    private static Managers _instance;

    [Header("매니저")]
    private readonly InputManager _inputManager = new InputManager();

    public static InputManager Input
    {
        get
        {
            if (_instance == null) new GameObject("@Managers").AddComponent<Managers>();
            return _instance._inputManager;
        }
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        _inputManager.Init();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        _inputManager.Clear();
        _instance = null;
    }

    /// <summary>
    /// 새 scene의 UI 입력 모듈을 중앙 액션에 연결한다.
    /// scene과 mode는 사용하지 않으며 UI 모듈의 액션 참조를 변경한다.
    /// </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _inputManager.BindUI();
    }
}
