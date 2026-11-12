using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 鼠标点击 NPC：按命中高度和玩家相对位置，分成摸头 / 拍背 / 轻触。
/// 只处理“点到 NPC 胶囊”的情况，点 UI 会被忽略。
/// </summary>
public class PlayerActionDetector : MonoBehaviour
{
    [Header("依赖")]
    public Camera playerCamera;
    public NpcAgent npc;
    public ReactionController reactions;

    [Header("参数")]
    public float maxDistance = 6f;
    public float headHeight = 0.6f;

    void Update()
    {
        if (playerCamera == null || npc == null || reactions == null)
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        var ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, maxDistance))
        {
            return;
        }

        if (hit.collider.GetComponentInParent<NpcAgent>() != npc)
        {
            return;
        }

        var localHeight = hit.point.y - npc.transform.position.y;
        var toPlayer = playerCamera.transform.position - npc.transform.position;
        toPlayer.y = 0f;

        var fromBehind = toPlayer.sqrMagnitude > 0.0001f
            && Vector3.Dot(npc.transform.forward, toPlayer.normalized) < -0.2f;

        if (fromBehind)
        {
            reactions.React("tap_back");
        }
        else if (localHeight > headHeight)
        {
            reactions.React("pat_head");
        }
        else
        {
            reactions.React("touch");
        }
    }
}
