using UnityEngine;

/// <summary>
/// 玩家靠近/点击 NPC 时的即时反应：表情 + 台词 + 转身。
/// 不经过 LLM，保证“摸头”这种互动零延迟。
/// </summary>
public class ReactionController : MonoBehaviour
{
    [Header("依赖")]
    public NpcAgent npc;
    public DialogueUI dialogue;

    [Header("节奏")]
    public float cooldown = 1.2f;

    float _nextTime;

    public void React(string kind)
    {
        if (Time.time < _nextTime)
        {
            return;
        }

        _nextTime = Time.time + cooldown;

        var expression = npc != null ? npc.GetComponentInChildren<ExpressionController>() : null;

        switch (kind)
        {
            case "pat_head":
                if (expression != null) expression.SetEmotion("happy");
                Say("……唔。摸头的话，鲸鲸会乖的。");
                break;

            case "tap_back":
                if (expression != null) expression.SetEmotion("surprised");
                if (npc != null) npc.TurnTo("player");
                Say("呀——！……吓、吓一跳。有什么吩咐吗？");
                break;

            case "touch":
                if (expression != null) expression.SetEmotion("relaxed");
                Say("……怎么了？");
                break;

            default:
                Debug.LogWarning($"[Reaction] 未知事件：{kind}");
                break;
        }
    }

    void Say(string text)
    {
        if (dialogue != null)
        {
            dialogue.ShowLine(text);
        }
        else
        {
            Debug.Log("[Reaction] " + text);
        }
    }
}
