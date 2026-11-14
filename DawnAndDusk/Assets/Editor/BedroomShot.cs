using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 无头出图：把卧室 demo 场景按几个机位渲染成 PNG，用来肉眼确认
/// "窗帘后面到底有没有窗""开了是什么样"。
/// 菜单：Tools/DawnAndDusk/Shoot Bedroom
/// </summary>
public static class BedroomShot
{
    const string ScenePath = "Assets/_LocalTest/Bedroom/Scene/BedroomDemo.unity";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-shots");

    [MenuItem("Tools/DawnAndDusk/Shoot Bedroom")]
    public static void Shoot()
    {
        Directory.CreateDirectory(OutDir);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var cam = Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.CompareTag("MainCamera"))
                  ?? Object.FindObjectsOfType<Camera>().FirstOrDefault();
        if (cam == null)
        {
            Debug.LogError("[Shot] 场景里没有相机");
            return;
        }

        var curtain = Object.FindObjectsOfType<Transform>()
            .Where(t => t.name.StartsWith("Curtain"))
            .ToArray();
        Debug.Log("[Shot] 窗帘对象：" + string.Join(", ", curtain.Select(c => $"{c.name}@{c.position}")));

        // 1) 玩家机位朝床
        Render(cam, new Vector3(0f, 1.55f, -2.02f), new Vector3(2f, 0f, 0f), "01-bed-from-player.png");

        // 2) 朝左侧窗帘墙（-x）
        Render(cam, new Vector3(0.6f, 1.5f, -0.2f), new Vector3(0f, -80f, 0f), "02-curtain-wall.png");

        // 3) 更近的窗帘特写
        Render(cam, new Vector3(-0.6f, 1.5f, 0.2f), new Vector3(0f, -85f, 0f), "03-curtain-close.png");

        // 4) 把窗帘藏起来，看后面到底是什么（墙？窗？）
        foreach (var c in curtain)
        {
            c.gameObject.SetActive(false);
        }

        Render(cam, new Vector3(0.6f, 1.5f, -0.2f), new Vector3(0f, -80f, 0f), "04-curtain-hidden.png");

        foreach (var c in curtain)
        {
            c.gameObject.SetActive(true);
        }

        // 5) 沿 Z 轴滑开（真正的"拉开"方向）后再拍
        var curtainRoots = curtain.Where(c => c.name == "Curtain").ToArray();
        foreach (var c in curtainRoots)
        {
            c.position += new Vector3(0f, 0f, -1.25f);
        }

        Render(cam, new Vector3(0.6f, 1.5f, -0.2f), new Vector3(0f, -80f, 0f), "05-curtain-slide-z.png");

        foreach (var c in curtainRoots)
        {
            c.position -= new Vector3(0f, 0f, -1.25f);
        }

        // 6) 门和电视柜方向
        Render(cam, new Vector3(0f, 1.55f, 0.6f), new Vector3(0f, 175f, 0f), "06-door-tv.png");

        Debug.Log($"[Shot] 输出目录 {OutDir}");
        EditorSceneManager.SaveScene(scene);
    }

    static void Render(Camera cam, Vector3 pos, Vector3 euler, string file)
    {
        const int w = 1280;
        const int h = 720;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var texture = new Texture2D(w, h, TextureFormat.RGB24, false);

        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        var path = Path.Combine(OutDir, file);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(texture);

        Debug.Log("[Shot] " + path);
    }
}
