using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 运行时结构化日志：一行一个 JSON，写工程的 Logs/game.jsonl。
///
/// 为什么不用 Debug.Log：Console 会滚掉、也没法按字段过滤。
/// 排查"走路打滑""对话丢失"这类问题，要的是"第几秒、什么状态、速度多少"的可对齐数据。
///
/// 用法：
///   GameLog.Event("anim", "state", "Walk", "speed", "1.02");
/// 数值请先过 GameLog.F(...) 转成 invariant 字符串，避免中文系统的逗号小数点。
/// </summary>
public static class GameLog
{
    public static bool Enabled = true;

    static StreamWriter _writer;
    static string _path;

    public static string FilePath => _path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        Init();
    }

    public static void Init()
    {
        if (_writer != null)
        {
            return;
        }

        try
        {
            // dataPath = <工程>/Assets，日志放到 <工程>/Logs/game.jsonl
            var dir = System.IO.Path.Combine(Application.dataPath, "../Logs");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "game.jsonl"));
            _writer = new StreamWriter(_path, false, new UTF8Encoding(false)) { AutoFlush = true };
            var header = $"{{\"t\":{F(Time.realtimeSinceStartup)},\"evt\":\"session\",\"unity\":\"{Application.unityVersion}\"}}";
            _writer.WriteLine(header);
            Debug.Log($"[GameLog] 写入 {_path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameLog] 初始化失败：{e.Message}");
            Enabled = false;
        }
    }

    public static void Event(string evt, params string[] keyValues)
    {
        if (!Enabled)
        {
            return;
        }

        if (_writer == null)
        {
            Init();
        }

        var sb = new StringBuilder(128);
        sb.Append("{\"t\":").Append(F(Time.realtimeSinceStartup));
        sb.Append(",\"evt\":\"").Append(evt).Append('"');

        for (var i = 0; i + 1 < keyValues.Length; i += 2)
        {
            sb.Append(",\"").Append(keyValues[i]).Append("\":\"").Append(Escape(keyValues[i + 1])).Append('"');
        }

        sb.Append('}');
        _writer?.WriteLine(sb.ToString());
    }

    public static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
