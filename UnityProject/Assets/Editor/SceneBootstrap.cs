using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 生成起始场景：相机 + 冷色方向光（月光）+ 暖色点光 + 地面 + 测试方块。
/// 菜单 Tools/Create Starter Scene；或 batchmode -executeMethod SceneBootstrap.Setup。
/// </summary>
public static class SceneBootstrap
{
    [MenuItem("Tools/Create Starter Scene")]
    public static void Setup()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        camGo.transform.position = new Vector3(0f, 1.6f, -3.5f);
        camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

        var moon = new GameObject("Moon Light");
        var moonLight = moon.AddComponent<Light>();
        moonLight.type = LightType.Directional;
        moonLight.color = new Color(0.55f, 0.65f, 1f);
        moonLight.intensity = 0.45f;
        moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var warm = new GameObject("Warm Point Light");
        var warmLight = warm.AddComponent<Light>();
        warmLight.type = LightType.Point;
        warmLight.color = new Color(1f, 0.72f, 0.42f);
        warmLight.intensity = 3f;
        warmLight.range = 8f;
        warm.transform.position = new Vector3(0f, 2.2f, 0f);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Test Cube";
        cube.transform.position = new Vector3(0f, 0.5f, 0f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(1f, 1f, 1f);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/NightRoom.unity");
        AssetDatabase.Refresh();
        Debug.Log("[SceneBootstrap] Assets/Scenes/NightRoom.unity saved.");
    }
}
