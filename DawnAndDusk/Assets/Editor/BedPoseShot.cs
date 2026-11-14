using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 卧姿机位候选：同一场景渲 4 个"躺在床上"的机位，挑一个最像的。
/// 菜单：Tools/DawnAndDusk/Shoot Bed Poses
/// </summary>
public static class BedPoseShot
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";
    const string VrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-bed-poses");

    [MenuItem("Tools/DawnAndDusk/Shoot Bed Poses")]
    public static void Shoot()
    {
        Directory.CreateDirectory(OutDir);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var cam = Object.FindObjectsOfType<Camera>().OrderByDescending(c => c.depth).First();
        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);

        // 鲸鲸站在窗前（北墙 z≈3.55）
        GameObject standIn = null;
        if (vrm != null)
        {
            standIn = Object.Instantiate(vrm);
            standIn.transform.position = new Vector3(3.65f, 0f, 3.55f);
            standIn.transform.localScale = Vector3.one * 0.82f;
            standIn.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // 床的范围 x[2.23,4.83] z[0.90,3.14]，床头在北（z≈3.1，旁边有床头柜）
        Render(cam, new Vector3(3.30f, 1.05f, 1.30f), new Vector3(-2f, 0f, 0f), "A-半躺看向窗.png");
        Render(cam, new Vector3(3.55f, 0.90f, 2.55f), new Vector3(-2f, 0f, 0f), "B-枕边平躺.png");
        Render(cam, new Vector3(4.10f, 0.95f, 2.35f), new Vector3(-4f, -68f, 6f), "C-侧躺看向窗.png");
        Render(cam, new Vector3(3.05f, 1.15f, 1.55f), new Vector3(-10f, 8f, 0f), "D-坐起斜靠.png");
        Render(cam, new Vector3(3.30f, 1.55f, 0.62f), new Vector3(-4f, 0f, 0f), "E-床尾站立.png");

        if (standIn != null)
        {
            Object.DestroyImmediate(standIn);
        }

        Debug.Log("[BedPose] 输出 " + OutDir);
    }

    static void Render(Camera cam, Vector3 pos, Vector3 euler, string file)
    {
        const int w = 1600;
        const int h = 900;
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

        File.WriteAllBytes(Path.Combine(OutDir, file), texture.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(texture);
        Debug.Log("[BedPose] " + file);
    }
}
