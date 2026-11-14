using UnityEngine;

/// <summary>
/// Kawaii 各状态的"片段自带步速"表（m/s），由 KawaiiAnimatorBuilder 从根位移曲线烘焙出来。
///
/// 为什么需要：Unity 里 applyRootMotion=false（我们靠代码走锚点图），根位移被忽略，
/// 但**脚在身体坐标系里的后退速度**仍然是片段烘焙的原始节奏。
/// 身体平移速度必须等于这个节奏，脚才不会打滑——所以把曲线采成表，运行时逐帧查。
/// </summary>
[CreateAssetMenu(fileName = "KawaiiMotionProfile", menuName = "DawnAndDusk/Kawaii Motion Profile")]
public class KawaiiMotionProfile : ScriptableObject
{
    [System.Serializable]
    public class Track
    {
        public string state;

        [Tooltip("按帧采样的瞬时步速（m/s），长度 = 帧数")]
        public float[] speeds;
    }

    [Tooltip("循环片段的平均步速，用来把各状态归一化到 moveSpeed")]
    public float referenceSpeed = 1.066f;

    public Track[] tracks;

    public float Speed(string state, float normalizedTime)
    {
        if (tracks == null)
        {
            return 1f;
        }

        foreach (var track in tracks)
        {
            if (track.state != state || track.speeds == null || track.speeds.Length == 0)
            {
                continue;
            }

            var t = Mathf.Clamp01(normalizedTime);
            var index = Mathf.Clamp(Mathf.RoundToInt(t * (track.speeds.Length - 1)), 0, track.speeds.Length - 1);
            return track.speeds[index];
        }

        // 表里没有的状态（Idle / Sit / Bow）当作原地：身体不该被动画带着走
        return 0f;
    }

    /// <summary>返回相对 moveSpeed 的比例。</summary>
    public float Scale(string state, float normalizedTime)
    {
        var speed = Speed(state, normalizedTime);
        return referenceSpeed > 0.0001f ? speed / referenceSpeed : 1f;
    }
}
