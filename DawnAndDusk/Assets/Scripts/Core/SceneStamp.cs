using UnityEngine;

/// <summary>
/// 场景构建戳：每次 ApartmentSceneBuilder 生成场景时写入时间戳。
/// 作用：Play 时 GameLog 会把它写进 session 事件，这样"日志说的"和"你看到的"
/// 到底是不是同一版场景，一眼可查——之前吃过"改了代码但 Play 的是旧场景"的亏。
/// </summary>
public class SceneStamp : MonoBehaviour
{
    public string buildId = "";

    void Awake()
    {
        _instance = this;
        GameLog.Event("scene_build", "id", buildId);
        Debug.Log($"[SceneStamp] 场景构建版本 {buildId}");
    }

    static SceneStamp _instance;

    public static string Current
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<SceneStamp>();
            }

            return _instance != null ? _instance.buildId : "<none>";
        }
    }
}
