using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 运行时自动搭一个聊天 UI（Canvas + 输出区 + 输入框 + 发送按钮）。
/// 用系统雅黑做动态字体，避免中文变方块（TMP 字体资产还没做）。
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("依赖")]
    public DirectorClient client;
    public CommandExecutor executor;
    public NpcAgent npc;

    Text _output;
    Text _status;
    InputField _input;
    Font _font;
    bool _busy;

    void Start()
    {
        if (_output == null)
        {
            Build();
        }
    }

    void Update()
    {
        if (_busy || _input == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) && _input.isFocused)
        {
            Submit(_input.text);
        }
    }

    public void Submit(string text)
    {
        if (_busy || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        StartCoroutine(SendRoutine(text.Trim()));
    }

    IEnumerator SendRoutine(string text)
    {
        _busy = true;
        _input.text = string.Empty;
        Append("你：" + text);
        SetStatus("鲸鲸思考中…");

        DirectorClient.ResponseDto response = null;
        string error = null;
        var done = false;

        yield return client.Direct(
            text,
            npc != null ? npc.CurrentAnchorId : "bed",
            result =>
            {
                response = result;
                done = true;
            },
            message =>
            {
                error = message;
                done = true;
            });

        while (!done)
        {
            yield return null;
        }

        if (error != null)
        {
            Append("<color=#ff8080>（连接失败：" + error + "）</color>");
            SetStatus("连接失败，检查服务端是否在 8000 端口");
            _busy = false;
            yield break;
        }

        if (!string.IsNullOrEmpty(response.say))
        {
            Append("鲸鲸：" + response.say);
        }

        SetStatus("执行中…");
        yield return executor.Execute(response.commands);
        SetStatus(string.Empty);
        _busy = false;
    }

    void Append(string line)
    {
        var current = _output.text;
        var next = string.IsNullOrEmpty(current) ? line : current + "\n" + line;
        var lines = next.Split('\n');
        if (lines.Length > 14)
        {
            next = string.Join("\n", lines, lines.Length - 14, 14);
        }

        _output.text = next;
    }

    void SetStatus(string text)
    {
        if (_status != null)
        {
            _status.text = text;
        }
    }

    void Build()
    {
        _font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);

        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        var canvasGo = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = CreateRect("Panel", canvasGo.transform);
        SetAnchors(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(0, 0));
        panel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.92f);

        _output = CreateText("Output", panel, 20, TextAnchor.UpperLeft);
        SetAnchors(_output.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(24, 24));

        _status = CreateText("Status", panel, 16, TextAnchor.UpperRight);
        _status.color = new Color(1f, 0.8f, 0.4f);
        SetAnchors(_status.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(300, 24), new Vector2(-24, -6));

        var inputBg = CreateRect("Input", panel);
        SetAnchors(inputBg, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(-120, 40), new Vector2(24, 10));
        inputBg.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);

        _input = inputBg.gameObject.AddComponent<InputField>();
        _input.lineType = InputField.LineType.SingleLine;
        _input.contentType = InputField.ContentType.Standard;
        _input.targetGraphic = inputBg.GetComponent<Image>();
        _input.colors = new ColorBlock
        {
            normalColor = new Color(1f, 1f, 1f, 0.12f),
            highlightedColor = new Color(0.45f, 0.65f, 0.95f, 0.42f),
            pressedColor = new Color(0.35f, 0.55f, 0.9f, 0.52f),
            selectedColor = new Color(0.45f, 0.65f, 0.95f, 0.42f),
            disabledColor = new Color(1f, 1f, 1f, 0.05f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
        _input.customCaretColor = true;
        _input.caretColor = Color.white;
        _input.caretWidth = 2;
        _input.selectionColor = new Color(0.35f, 0.55f, 1f, 0.45f);

        var focusOutline = inputBg.gameObject.AddComponent<Outline>();
        focusOutline.effectColor = new Color(0.45f, 0.65f, 0.95f, 0.55f);
        focusOutline.effectDistance = new Vector2(1f, -1f);

        var inputText = CreateText("Text", inputBg, 20, TextAnchor.MiddleLeft);
        SetAnchors(inputText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-20, -8), new Vector2(12, 4));
        _input.textComponent = inputText;

        var placeholder = CreateText("Placeholder", inputBg, 20, TextAnchor.MiddleLeft);
        placeholder.text = "对鲸鲸说点什么…（回车发送）";
        placeholder.color = new Color(1, 1, 1, 0.35f);
        SetAnchors(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-20, -8), new Vector2(12, 4));
        _input.placeholder = placeholder;

        _input.onSubmit.AddListener(Submit);

        var sendBg = CreateRect("Send", panel);
        SetAnchors(sendBg, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(104, 40), new Vector2(-24, 10));
        var sendImage = sendBg.gameObject.AddComponent<Image>();
        sendImage.color = new Color(0.35f, 0.62f, 0.95f, 0.95f);

        var button = sendBg.gameObject.AddComponent<Button>();
        button.targetGraphic = sendImage;
        button.onClick.AddListener(() => Submit(_input.text));

        var sendLabel = CreateText("Label", sendBg, 20, TextAnchor.MiddleCenter);
        sendLabel.text = "发送";
        sendLabel.color = Color.white;
        SetAnchors(sendLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    Text CreateText(string name, Transform parent, int size, TextAnchor anchor)
    {
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.supportRichText = true;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return text;
    }

    static void SetAnchors(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Vector2 anchoredPosition)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;
    }
}
