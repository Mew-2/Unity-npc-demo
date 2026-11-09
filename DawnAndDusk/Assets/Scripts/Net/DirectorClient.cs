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
        var requestDto = new RequestDto { player_text = playerText, world = world };
        var json = JsonUtility.ToJson(requestDto);
        var url = baseUrl.TrimEnd('/') + "/direct";

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.timeout = Mathf.CeilToInt(timeoutSeconds);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
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
                onError?.Invoke("JSON 解析失败：" + exception.Message);
                yield break;
            }

            if (response == null)
            {
                onError?.Invoke("服务端返回为空");
                yield break;
            }

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
