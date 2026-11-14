using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时栅格寻路：扫描场景里的家具包围盒 → 按角色半径膨胀 → A* 求最短路 → 拉直。
/// 为什么不用 NavMesh：这套公寓是展示场景，没有 Navigation 数据；而且房间通道只有 0.2m 级，
/// NavMesh 的烘焙半径会把路直接封死。栅格方案可以自己控制半径、也方便调试（可画出占用图）。
/// </summary>
public class GridPathfinder : MonoBehaviour
{
    [Header("范围（世界 XZ）")]
    public Vector2 min = new Vector2(1.4f, -0.2f);
    public Vector2 max = new Vector2(5.2f, 4.0f);

    [Header("精度与体型")]
    public float cell = 0.12f;

    [Tooltip("角色半宽（米）。烘焙时把障碍向外扩这么多，保证身体不穿模")]
    public float agentRadius = 0.32f;

    [Tooltip("判定为障碍的高度区间（米）——地板、天花板不算")]
    public float obstacleMinHeight = 0.15f;
    public float obstacleMaxHeight = 1.6f;

    [Tooltip("横向尺寸超过这个值的不算障碍（墙体的包围盒是整个房间，算进去会全封）")]
    public float maxObstacleSpan = 3f;

    int _w;
    int _h;
    bool[] _blocked;
    public bool Ready { get; private set; }

    public void Build()
    {
        _w = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cell));
        _h = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cell));
        _blocked = new bool[_w * _h];

        var obstacles = new List<Bounds>();
        foreach (var r in FindObjectsOfType<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r.GetComponentInParent<NpcAgent>() != null)
            {
                continue;
            }

            var b = r.bounds;
            var span = Mathf.Max(b.size.x, b.size.z);
            if (b.max.y < obstacleMinHeight || b.min.y > obstacleMaxHeight || span > maxObstacleSpan)
            {
                continue;
            }

            obstacles.Add(b);
        }

        // 膨胀：每个障碍按 agentRadius 外扩后栅格化
        foreach (var b in obstacles)
        {
            var x0 = b.min.x - agentRadius;
            var x1 = b.max.x + agentRadius;
            var z0 = b.min.z - agentRadius;
            var z1 = b.max.z + agentRadius;

            var i0 = Mathf.Clamp(Mathf.FloorToInt((x0 - min.x) / cell), 0, _w - 1);
            var i1 = Mathf.Clamp(Mathf.CeilToInt((x1 - min.x) / cell), 0, _w - 1);
            var j0 = Mathf.Clamp(Mathf.FloorToInt((z0 - min.y) / cell), 0, _h - 1);
            var j1 = Mathf.Clamp(Mathf.CeilToInt((z1 - min.y) / cell), 0, _h - 1);

            for (var i = i0; i <= i1; i++)
            {
                for (var j = j0; j <= j1; j++)
                {
                    _blocked[i + j * _w] = true;
                }
            }
        }

        Ready = true;
        var free = 0;
        foreach (var b in _blocked)
        {
            if (!b)
            {
                free++;
            }
        }

        GameLog.Event("grid_built", "w", _w.ToString(), "h", _h.ToString(), "free", free.ToString(), "obstacles", obstacles.Count.ToString());
        Debug.Log($"[Grid] {_w}x{_h} 空闲格 {free}/{_blocked.Length}，障碍 {obstacles.Count} 个");
    }

    public bool TryFindPath(Vector3 from, Vector3 to, List<Vector3> result)
    {
        result.Clear();
        if (!Ready)
        {
            Build();
        }

        var start = ToCell(from);
        var goal = ToCell(to);
        if (start.x < 0 || goal.x < 0)
        {
            return false;
        }

        // 起点/终点被占（比如贴着我方身体）时，就近找一个空格
        start = NearestFree(start);
        goal = NearestFree(goal);
        if (start.x < 0 || goal.x < 0)
        {
            return false;
        }

        var count = _w * _h;
        var g = new float[count];
        var f = new float[count];
        var came = new int[count];
        var closed = new bool[count];
        for (var i = 0; i < count; i++)
        {
            g[i] = float.MaxValue;
            came[i] = -1;
        }

        var startIndex = start.x + start.y * _w;
        var goalIndex = goal.x + goal.y * _w;
        g[startIndex] = 0f;
        f[startIndex] = Heuristic(start, goal);

        var open = new List<int> { startIndex };

        while (open.Count > 0)
        {
            var best = 0;
            for (var i = 1; i < open.Count; i++)
            {
                if (f[open[i]] < f[open[best]])
                {
                    best = i;
                }
            }

            var current = open[best];
            open.RemoveAt(best);
            if (current == goalIndex)
            {
                BuildResult(came, current, from, to, result);
                return true;
            }

            closed[current] = true;
            var cx = current % _w;
            var cy = current / _w;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h)
                    {
                        continue;
                    }

                    var index = nx + ny * _w;
                    if (_blocked[index] || closed[index])
                    {
                        continue;
                    }

                    // 斜向走时不能切角：两个正交邻居必须都通
                    if (dx != 0 && dy != 0)
                    {
                        if (_blocked[cx + dx + cy * _w] || _blocked[cx + (cy + dy) * _w])
                        {
                            continue;
                        }
                    }

                    var step = dx != 0 && dy != 0 ? 1.4142f : 1f;
                    var tentative = g[current] + step;
                    if (tentative >= g[index])
                    {
                        continue;
                    }

                    came[index] = current;
                    g[index] = tentative;
                    f[index] = tentative + Heuristic(new Vector2Int(nx, ny), goal);
                    if (!open.Contains(index))
                    {
                        open.Add(index);
                    }
                }
            }
        }

        return false;
    }

    List<Vector3> BuildResult(int[] came, int current, Vector3 from, Vector3 to, List<Vector3> result)
    {
        var cells = new List<int>();
        for (var index = current; index != -1; index = came[index])
        {
            cells.Add(index);
        }

        cells.Reverse();

        var points = new List<Vector3> { from };
        foreach (var index in cells)
        {
            points.Add(ToWorld(index % _w, index / _w));
        }

        points.Add(to);

        // 拉直：能直线到达的中间点全部跳过
        var simplified = new List<Vector3> { points[0] };
        var i2 = 1;
        while (i2 < points.Count)
        {
            var furthest = i2;
            for (var j = points.Count - 1; j > i2; j--)
            {
                if (LineFree(points[simplified.Count - 1], points[j]))
                {
                    furthest = j;
                    break;
                }
            }

            simplified.Add(points[furthest]);
            i2 = furthest + 1;
        }

        result.AddRange(simplified);
        return result;
    }

    bool LineFree(Vector3 a, Vector3 b)
    {
        var distance = Vector3.Distance(a, b);
        var steps = Mathf.CeilToInt(distance / (cell * 0.5f));
        for (var s = 1; s < steps; s++)
        {
            var p = Vector3.Lerp(a, b, s / (float)steps);
            var c = ToCell(p);
            if (c.x < 0 || _blocked[c.x + c.y * _w])
            {
                return false;
            }
        }

        return true;
    }

    Vector2Int ToCell(Vector3 world)
    {
        var i = Mathf.FloorToInt((world.x - min.x) / cell);
        var j = Mathf.FloorToInt((world.z - min.y) / cell);
        return i < 0 || j < 0 || i >= _w || j >= _h ? new Vector2Int(-1, -1) : new Vector2Int(i, j);
    }

    Vector3 ToWorld(int i, int j) => new Vector3(min.x + (i + 0.5f) * cell, 0f, min.y + (j + 0.5f) * cell);

    Vector2Int NearestFree(Vector2Int cell)
    {
        if (cell.x >= 0 && !_blocked[cell.x + cell.y * _w])
        {
            return cell;
        }

        for (var radius = 1; radius <= 6; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var x = cell.x + dx;
                    var y = cell.y + dy;
                    if (x < 0 || y < 0 || x >= _w || y >= _h)
                    {
                        continue;
                    }

                    if (!_blocked[x + y * _w])
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }
        }

        return new Vector2Int(-1, -1);
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        var dx = Mathf.Abs(a.x - b.x);
        var dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (1.4142f - 2f) * Mathf.Min(dx, dy);
    }
}
