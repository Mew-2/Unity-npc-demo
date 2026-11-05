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
    public List<Edge> edges = new List<Edge>();

    readonly Dictionary<string, Anchor> _byId = new Dictionary<string, Anchor>();
    readonly Dictionary<string, List<string>> _neighbours = new Dictionary<string, List<string>>();
    bool _built;

    void Awake()
    {
        Build();
    }

    /// <summary>重建索引。场景里增删锚点后调用一次。</summary>
    public void Build()
    {
        _byId.Clear();
        _neighbours.Clear();

        foreach (var anchor in anchors)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.id))
            {
                continue;
            }

            _byId[anchor.id] = anchor;
            _neighbours[anchor.id] = new List<string>();
        }

        foreach (var edge in edges)
        {
            if (!_neighbours.ContainsKey(edge.a) || !_neighbours.ContainsKey(edge.b))
            {
                Debug.LogWarning($"[AnchorGraph] 边引用了不存在的锚点：{edge.a} - {edge.b}");
                continue;
            }

            _neighbours[edge.a].Add(edge.b);
            _neighbours[edge.b].Add(edge.a);
        }

        _built = true;
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
