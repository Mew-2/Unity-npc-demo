using System.Collections;
using UnityEngine;

/// <summary>
/// 灯 / 窗帘 / 换装的状态切换。
/// 灯是一盏程序拼的吊灯（金属环 + 6 根蜡烛 + 暖色点光），不是魔法球；
/// 窗帘在 window 锚点运行时生成；换装暂时改胶囊颜色，等 VRM 接上后换材质。
/// </summary>
public class StateSwitcher : MonoBehaviour
{
    [Header("依赖")]
    public AnchorGraph graph;
    public NpcAgent npc;

    [Header("道具来源")]
    [Tooltip("勾选 = 程序生成吊灯和窗帘（灰盒场景）；取消 = 用下面两个引用（真实卧室场景）")]
    public bool buildProps = true;

    [Tooltip("真实场景里要被开关的灯")]
    public Light[] roomLights;

    [Tooltip("真实场景里的窗帘（拉/关按局部偏移平移）")]
    public Transform curtain;

    Light _chandelierLight;
    Renderer[] _flames;
    Transform _curtainLeft;
    Transform _curtainRight;
    Renderer _npcRenderer;
    Coroutine _curtainRoutine;
    float[] _roomLightBase;
    Vector3 _curtainBase;

    readonly Color _nightgownColor = new Color(0.22f, 0.3f, 0.55f);
    readonly Color _daydressColor = new Color(0.95f, 0.9f, 0.82f);
    readonly Color _flameColor = new Color(1f, 0.65f, 0.3f);

    // 相对 window 锚点的局部坐标（锚点在 (-1.8, 0, 2.3)，墙内表面 x≈-2.45、z≈2.95）
    readonly Vector3 _curtainClosedLeft = new Vector3(-0.19f, 1.35f, 0.52f);
    readonly Vector3 _curtainClosedRight = new Vector3(0.19f, 1.35f, 0.52f);
    readonly Vector3 _curtainOpenLeft = new Vector3(-0.45f, 1.35f, 0.52f);
    readonly Vector3 _curtainOpenRight = new Vector3(0.45f, 1.35f, 0.52f);

    void Awake()
    {
        BuildProps();
    }

    void BuildProps()
    {
        if (graph == null)
        {
            graph = FindObjectOfType<AnchorGraph>();
        }

        if (buildProps)
        {
            BuildChandelier();
            BuildCurtains();
        }

        if (roomLights != null)
        {
            _roomLightBase = new float[roomLights.Length];
            for (var i = 0; i < roomLights.Length; i++)
            {
                _roomLightBase[i] = roomLights[i] != null ? roomLights[i].intensity : 0f;
            }
        }

        if (curtain != null)
        {
            _curtainBase = curtain.localPosition;
        }

        if (npc != null)
        {
            _npcRenderer = npc.GetComponentInChildren<Renderer>();
        }
    }

    void BuildChandelier()
    {
        var root = new GameObject("Chandelier");
        root.transform.position = new Vector3(0f, 2.55f, 0f);

        var metal = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.18f, 0.14f, 0.1f)
        };
        metal.SetFloat("_Metallic", 0.8f);
        metal.SetFloat("_Smoothness", 0.6f);

        var wax = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.95f, 0.92f, 0.85f)
        };

        var flameMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        flameMaterial.EnableKeyword("_EMISSION");
        flameMaterial.SetColor("_EmissionColor", _flameColor * 3f);

        // 吊杆
        CreatePrimitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.22f, 0f),
            new Vector3(0.05f, 0.22f, 0.05f), metal);

        // 金属环（24 段，段长略大于弧长，避免缝隙）
        for (var i = 0; i < 24; i++)
        {
            var angle = i * Mathf.PI * 2f / 24f;
            var segment = CreatePrimitive(PrimitiveType.Cube, root.transform,
                new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f),
                new Vector3(0.14f, 0.03f, 0.03f), metal);
            segment.localRotation = Quaternion.Euler(0f, 90f - angle * Mathf.Rad2Deg, 0f);
        }

        // 中心毂，遮住支臂接头
        CreatePrimitive(PrimitiveType.Sphere, root.transform, Vector3.zero,
            Vector3.one * 0.12f, metal);

        // 6 根蜡烛 + 火苗
        _flames = new Renderer[6];
        for (var i = 0; i < 6; i++)
        {
            var angle = i * Mathf.PI * 2f / 6f;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            // 支臂
            var arm = CreatePrimitive(PrimitiveType.Cube, root.transform,
                direction * 0.25f, new Vector3(0.03f, 0.02f, 0.5f), metal);
            arm.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            // 蜡烛
            CreatePrimitive(PrimitiveType.Cylinder, root.transform,
                direction * 0.5f + Vector3.up * 0.06f, new Vector3(0.04f, 0.06f, 0.04f), wax);

            // 火苗
            var flame = CreatePrimitive(PrimitiveType.Sphere, root.transform,
                direction * 0.5f + Vector3.up * 0.16f, Vector3.one * 0.05f, flameMaterial);
            _flames[i] = flame.GetComponent<Renderer>();
        }

        var lightGo = new GameObject("ChandelierLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, -0.1f, 0f);

        _chandelierLight = lightGo.AddComponent<Light>();
        _chandelierLight.type = LightType.Point;
        _chandelierLight.range = 9f;
        _chandelierLight.intensity = 2f;
        _chandelierLight.color = new Color(1f, 0.75f, 0.48f);
        _chandelierLight.shadows = LightShadows.Soft;
    }

    Transform CreatePrimitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        Destroy(go.GetComponent<Collider>());

        go.GetComponent<Renderer>().material = material;
        return go.transform;
    }

    void BuildCurtains()
    {
        var windowAnchor = graph.Get("window");
        if (windowAnchor == null)
        {
            return;
        }

        _curtainLeft = CreateCurtainPanel("CurtainLeft", windowAnchor.transform, _curtainClosedLeft);
        _curtainRight = CreateCurtainPanel("CurtainRight", windowAnchor.transform, _curtainClosedRight);
    }

    Transform CreateCurtainPanel(string name, Transform parent, Vector3 localPosition)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = localPosition;
        panel.transform.localScale = new Vector3(0.38f, 2.4f, 0.05f);
        Destroy(panel.GetComponent<Collider>());

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.16f, 0.22f, 0.36f)
        };
        panel.GetComponent<Renderer>().material = material;
        return panel.transform;
    }

    public void Apply(string target, string state)
    {
        switch (target)
        {
            case "light":
                SetLight(state == "on");
                break;
            case "curtain":
                SetCurtain(state == "open");
                break;
            case "outfit":
                SetOutfit(state);
                break;
            default:
                Debug.LogWarning($"[StateSwitcher] 未知 set target：{target}");
                break;
        }
    }

    public void SetLight(bool on)
    {
        if (roomLights != null)
        {
            for (var i = 0; i < roomLights.Length; i++)
            {
                var light = roomLights[i];
                if (light == null)
                {
                    continue;
                }

                light.enabled = on;
                light.intensity = on && _roomLightBase != null && i < _roomLightBase.Length
                    ? _roomLightBase[i]
                    : 0f;
            }
        }

        if (_chandelierLight != null)
        {
            _chandelierLight.enabled = on;
            _chandelierLight.intensity = on ? 2f : 0f;
        }

        if (_flames == null)
        {
            return;
        }

        foreach (var flame in _flames)
        {
            if (flame != null)
            {
                flame.material.SetColor("_EmissionColor", on ? _flameColor * 3f : Color.black);
            }
        }
    }

    public void SetCurtain(bool open)
    {
        if (curtain != null)
        {
            if (_curtainRoutine != null)
            {
                StopCoroutine(_curtainRoutine);
            }

            _curtainRoutine = StartCoroutine(SlideTransform(
                curtain,
                _curtainBase + (open ? new Vector3(0.42f, 0f, 0f) : Vector3.zero)));
            return;
        }

        if (_curtainLeft == null || _curtainRight == null)
        {
            return;
        }

        if (_curtainRoutine != null)
        {
            StopCoroutine(_curtainRoutine);
        }

        _curtainRoutine = StartCoroutine(SlideCurtains(
            open ? _curtainOpenLeft : _curtainClosedLeft,
            open ? _curtainOpenRight : _curtainClosedRight));
    }

    IEnumerator SlideTransform(Transform target, Vector3 to)
    {
        var from = target.localPosition;
        const float duration = 0.45f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            target.localPosition = Vector3.Lerp(from, to, t);
            yield return null;
        }

        target.localPosition = to;
        _curtainRoutine = null;
    }

    IEnumerator SlideCurtains(Vector3 leftTarget, Vector3 rightTarget)
    {
        var leftStart = _curtainLeft.localPosition;
        var rightStart = _curtainRight.localPosition;
        const float duration = 0.35f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            _curtainLeft.localPosition = Vector3.Lerp(leftStart, leftTarget, t);
            _curtainRight.localPosition = Vector3.Lerp(rightStart, rightTarget, t);
            yield return null;
        }

        _curtainLeft.localPosition = leftTarget;
        _curtainRight.localPosition = rightTarget;
        _curtainRoutine = null;
    }

    public void SetOutfit(string state)
    {
        if (_npcRenderer == null)
        {
            return;
        }

        _npcRenderer.material.color = state == "daydress" ? _daydressColor : _nightgownColor;
    }
}
