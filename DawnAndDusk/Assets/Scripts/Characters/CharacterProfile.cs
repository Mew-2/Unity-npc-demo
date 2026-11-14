using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个可替换角色的配置。v1 只填鲸鲸；以后加流萤/自捏角色就是多一条。
/// 放在 CharacterSwitcher 的列表里，不强制做成 ScriptableObject 资产。
/// </summary>
[Serializable]
public class CharacterProfile
{
    [Header("身份")]
    public string id = "whale";
    public string displayName = "鲸鲸";
    public string personaId = "whale";

    [Header("外观")]
    public GameObject visualPrefab;
    public Vector3 visualOffset = Vector3.zero;
    public float groundOffset = 1f;

    [Tooltip("模型整体缩放：VRM 出厂身高通常 1.8~1.9m，按场景需要缩到 1.55m 左右")]
    public float visualScale = 1f;

    [Header("交互骨骼（摸头 / 拍背用，可留空）")]
    public string headBone = "J_Bip_C_Head";
    public string backBone = "J_Bip_C_Spine";

    [Header("表情映射：语义 key → 模型 blendshape 名")]
    public List<ExpressionBinding> expressions = new List<ExpressionBinding>();
}

[Serializable]
public class ExpressionBinding
{
    public string key;
    public string blendshape;
}
