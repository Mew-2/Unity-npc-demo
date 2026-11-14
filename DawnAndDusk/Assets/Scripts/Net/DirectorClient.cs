using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 和服务端 /direct 通信。
/// 用 JsonUtility 而不是 Newtonsoft：工程没装 Newtonsoft 包，JsonUtility 够用。
/// </summary>
public class DirectorClient : MonoBehaviour
{
    [Header("服务地址")]
    public string baseUrl = "http://127.0.0.1:8000";
    public float timeoutSeconds = 8f;

    [Header("依赖")]
    public AnchorGraph graph;

    [Header("角色")]
    public string characterId = "whale";

    [Serializable]
    public class AnchorDto
    {
        public string id;
        public string desc;
    }

    [Serializable]
    public class EntityDto
    {
        public string id;
        public string kind;
        public string at;
    }

    [Serializable]
    public class WorldDto
    {
        public AnchorDto[] anchors;
        public EntityDto[] entities;
    }

    [Serializable]
    public class RequestDto
    {
        public string player_text;
        public string character_id;
        public WorldDto world;
    }

    [Serializable]
    public class CommandDto
    {
        public string op;
        public string entity;
        public string anchor;
        public string target;
        public string state;
        public string name;
    }

    [Serializable]
    public class ResponseDto
    {
        public string say;
        public CommandDto[] commands;
    }

    void Awake()
    {
        if (graph == null)
        {
            graph = FindObjectOfType<AnchorGraph>();
        }
    }

    public IEnumerator Direct(
        string playerText,
        string npcAnchor,
        Action<ResponseDto> onSuccess,
        Action<string> onError)
    {
        if (graph == null)
        {
            onError?.Invoke("场景里没有 AnchorGraph");
            yield break;
        }

        var world = BuildWorld(npcAnchor);
        var requestDto = new RequestDto
        {
            player_text = playerText,
            character_id = characterId,
            world = world
        };
        var json = JsonUtility.ToJson(requestDto);
        var url = baseUrl.TrimEnd('/') + "/direct";
        GameLog.Event("direct_req", "text", playerText, "char", characterId, "anchor", npcAnchor, "url", url);

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.timeout = Mathf.CeilToInt(timeoutSeconds);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                GameLog.Event("direct_err", "text", playerText, "err", request.error ?? "unknown");
                onError?.Invoke(request.error);
                yield break;
            }

            ResponseDto response = null;
            try
            {
                response = JsonUtility.FromJson<ResponseDto>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                GameLog.Event("direct_parse_err", "text", playerText, "msg", exception.Message);
                onError?.Invoke("JSON 解析失败：" + exception.Message);
                yield break;
            }

            if (response == null)
            {
                GameLog.Event("direct_empty", "text", playerText);
                onError?.Invoke("服务端返回为空");
                yield break;
            }

            var trace = request.GetResponseHeader("X-Trace-Id") ?? string.Empty;
            GameLog.Event(
                "direct_resp",
                "trace", trace,
                "say", response.say,
                "n", (response.commands == null ? 0 : response.commands.Length).ToString(),
                "body", (request.downloadHandler.text ?? string.Empty).Length > 400
                    ? request.downloadHandler.text.Substring(0, 400)
                    : request.downloadHandler.text);

            onSuccess?.Invoke(response);
        }
    }

    WorldDto BuildWorld(string npcAnchor)
    {
        var anchors = new AnchorDto[graph.anchors.Count];
        for (var i = 0; i < graph.anchors.Count; i++)
        {
            var anchor = graph.anchors[i];
            anchors[i] = new AnchorDto
            {
                id = anchor != null ? anchor.id : string.Empty,
                desc = anchor != null ? anchor.description : string.Empty
            };
        }

        return new WorldDto
        {
            anchors = anchors,
            entities = new[]
            {
                new EntityDto { id = "npc", kind = "npc", at = npcAnchor }
            }
        };
    }
}
