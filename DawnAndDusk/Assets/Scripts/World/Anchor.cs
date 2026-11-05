using UnityEngine;

/// <summary>
/// 场景里的一个语义锚点。LLM 只能在这些命名点里选目的地，
/// 不做自由坐标导航。
/// </summary>
public class Anchor : MonoBehaviour
{
    [Tooltip("锚点 id，必须与 SPEC 一致：door / bed / window / screen / desk / lamp / player")]
    public string id;

    [Tooltip("给玩家看的名字，例如：床边、窗边")]
    public string displayName;

    [TextArea(2, 4)]
    [Tooltip("写进提示词的语义描述，例如：望海的大窗，夜里有月亮")]
    public string description;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.15f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.6f);
    }
}
