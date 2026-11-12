using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

/// <summary>
/// 鲸鲸的表情驱动：把 VRM 的预设表情封装成简单接口。
/// 用法：SetEmotion("happy") / SetEmotion("angry") / Clear()。
/// 调试：Play 时按住 Shift + 1~5 切表情，0 清空。
/// </summary>
public class ExpressionController : MonoBehaviour
{
    [Header("参数")]
    public float fadeSpeed = 6f;
    public bool debugKeys = true;

    Vrm10Instance _vrm;
    readonly Dictionary<ExpressionKey, float> _target = new Dictionary<ExpressionKey, float>();
    readonly Dictionary<ExpressionKey, float> _current = new Dictionary<ExpressionKey, float>();

    static readonly Dictionary<string, ExpressionKey> Presets = new Dictionary<string, ExpressionKey>
    {
        { "happy", ExpressionKey.Happy },
        { "angry", ExpressionKey.Angry },
        { "sad", ExpressionKey.Sad },
        { "relaxed", ExpressionKey.Relaxed },
        { "surprised", ExpressionKey.Surprised },
        { "neutral", ExpressionKey.Neutral },
    };

    void Awake()
    {
        _vrm = GetComponentInChildren<Vrm10Instance>();
        if (_vrm == null)
        {
            Debug.LogWarning("[ExpressionController] 没找到 Vrm10Instance，表情不可用。");
            enabled = false;
            return;
        }

        foreach (var pair in Presets)
        {
            _target[pair.Value] = 0f;
            _current[pair.Value] = 0f;
        }

        Clear();
    }

    void Update()
    {
        var t = 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime);

        foreach (var key in Presets.Values)
        {
            _current.TryGetValue(key, out var current);
            _target.TryGetValue(key, out var target);
            var next = Mathf.Lerp(current, target, t);
            _current[key] = next;
            _vrm.Runtime.Expression.SetWeight(key, next);
        }

        if (!debugKeys)
        {
            return;
        }

        if (!Input.GetKey(KeyCode.LeftShift))
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) SetEmotion("happy");
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetEmotion("angry");
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetEmotion("sad");
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetEmotion("surprised");
        if (Input.GetKeyDown(KeyCode.Alpha5)) SetEmotion("relaxed");
        if (Input.GetKeyDown(KeyCode.Alpha0)) Clear();
    }

    /// <summary>设置一个表情，其余归零；weight 0~1。</summary>
    public void SetEmotion(string name, float weight = 1f)
    {
        if (string.IsNullOrEmpty(name) || !Presets.TryGetValue(name.ToLowerInvariant(), out var key))
        {
            Debug.LogWarning($"[ExpressionController] 未知表情：{name}");
            return;
        }

        foreach (var pair in Presets)
        {
            _target[pair.Value] = pair.Value.Equals(key) ? Mathf.Clamp01(weight) : 0f;
        }

        Debug.Log($"[ExpressionController] 表情 -> {name} ({weight:F2})");
    }

    /// <summary>所有表情归零。</summary>
    public void Clear()
    {
        foreach (var key in Presets.Values)
        {
            _target[key] = 0f;
        }
    }

    void OnDisable()
    {
        if (_vrm == null)
        {
            return;
        }

        foreach (var key in Presets.Values)
        {
            _vrm.Runtime.Expression.SetWeight(key, 0f);
        }
    }
}
