using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 锚点图：6 个可寻路锚点 + player 伪锚点，边由场景里手工配置。
/// move 指令的路径 = 这张图上的 BFS 最短路。
/// </summary>
public class AnchorGraph : MonoBehaviour
{
    [System.Serializable]
    public class Edge
    {
        public string a;
        public string b;
    }

    public List<Anchor> anchors = new List<Anchor>();

    [Tooltip("按几何可见性自动连边：两点之间没被碰撞体挡住就直连")]
    public bool autoConnect = true;

    [Tooltip("探测高度（米）：从锚点地面往上抬，避开地面")]
    public float probeHeight = 1f;

    [Tooltip("手工补充的边（自动连边之外额外加，一般留空）")]
    public List<Edge> edges = new List<Edge>();

    readonly Dictionary<string, Anchor> _byId = new Dictionary<string, Anchor>();
    readonly Dictionary<string, List<string>> _neighbours = new Dictionary<string, List<string>>();
    bool _built;

    void Awake()
    {
        Build();
    }

    /// <summary>重建索引与连边。场景里增删锚点后调用一次。</summary>
    public void Build()
    {
        _byId.Clear();
        _neighbours.Clear();

        EnsureFrontAnchor();

        foreach (var anchor in anchors)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.id))
            {
                continue;
            }

            _byId[anchor.id] = anchor;
            _neighbours[anchor.id] = new List<string>();
        }

        if (autoConnect)
        {
            AutoConnect();
        }

        foreach (var edge in edges)
        {
            AddNeighbour(edge.a, edge.b);
        }

        _built = true;
    }

    /// <summary>
    /// 可见性连边：两两锚点做一次射线检测，
    /// 没被墙/家具挡住就直连（挡住才绕路）。
    /// NPC 自身的碰撞体不算遮挡。
    /// </summary>
    void AutoConnect()
    {
        var ids = new List<string>();
        foreach (var pair in _byId)
        {
            if (pair.Key != "player")
            {
                ids.Add(pair.Key);
            }
        }

        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                var from = _byId[ids[i]].transform.position + Vector3.up * probeHeight;
                var to = _byId[ids[j]].transform.position + Vector3.up * probeHeight;

                if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    // 打到 NPC 不算遮挡；打到墙 / 家具才算。
                    if (hit.collider.GetComponentInParent<NpcAgent>() == null)
                    {
                        continue;
                    }
                }

                AddNeighbour(ids[i], ids[j]);
            }
        }
    }

    /// <summary>
    /// 运行时补一个“身前”锚点，让 NPC 能走到镜头前，方便近景对话/表情。
    /// 不动场景文件；每次 Build 幂等。
    /// </summary>
    void EnsureFrontAnchor()
    {
        foreach (var anchor in anchors)
        {
            if (anchor != null && anchor.id == "front")
            {
                return;
            }
        }

        var bed = anchors.Find(a => a != null && a.id == "bed");
        var position = bed != null
            ? bed.transform.position + new Vector3(0f, 0f, -2.8f)
            : new Vector3(0f, 0f, -0.8f);

        var go = new GameObject("Anchor_front");
        go.transform.SetParent(transform, false);
        go.transform.position = position;

        var front = go.AddComponent<Anchor>();
        front.id = "front";
        front.displayName = "身前";
        front.description = "玩家面前，近距离说话的位置";

        anchors.Add(front);
        Debug.Log($"[AnchorGraph] 自动添加 front 锚点 @ {position}");
    }

    void AddNeighbour(string a, string b)
    {
        if (!_neighbours.ContainsKey(a) || !_neighbours.ContainsKey(b))
        {
            Debug.LogWarning($"[AnchorGraph] 边引用了不存在的锚点：{a} - {b}");
            return;
        }

        if (!_neighbours[a].Contains(b))
        {
            _neighbours[a].Add(b);
        }

        if (!_neighbours[b].Contains(a))
        {
            _neighbours[b].Add(a);
        }
    }

    public bool Has(string id)
    {
        EnsureBuilt();
        return _byId.ContainsKey(id);
    }

    public Anchor Get(string id)
    {
        EnsureBuilt();
        return _byId.TryGetValue(id, out var anchor) ? anchor : null;
    }

    public Vector3 Position(string id)
    {
        var anchor = Get(id);
        return anchor != null ? anchor.transform.position : Vector3.zero;
    }

    public List<string> Neighbours(string id)
    {
        EnsureBuilt();
        return _neighbours.TryGetValue(id, out var list) ? list : new List<string>();
    }

    /// <summary>BFS 最短路，返回包含起点和终点的 id 列表；不可达返回空列表。</summary>
    public List<string> FindPath(string from, string to)
    {
        EnsureBuilt();

        var result = new List<string>();
        if (!_byId.ContainsKey(from) || !_byId.ContainsKey(to))
        {
            Debug.LogWarning($"[AnchorGraph] FindPath 失败：{from} -> {to}");
            return result;
        }

        if (from == to)
        {
            result.Add(from);
            return result;
        }

        var previous = new Dictionary<string, string>();
        var queue = new Queue<string>();
        queue.Enqueue(from);
        previous[from] = null;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in _neighbours[current])
            {
                if (previous.ContainsKey(next))
                {
                    continue;
                }

                previous[next] = current;
                if (next == to)
                {
                    queue.Clear();
                    break;
                }

                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(to))
        {
            return result;
        }

        for (var node = to; node != null; node = previous[node])
        {
            result.Add(node);
        }

        result.Reverse();
        return result;
    }

    void EnsureBuilt()
    {
        if (!_built)
        {
            Build();
        }
    }
}
