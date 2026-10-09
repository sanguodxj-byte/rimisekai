namespace Rimisekai.Voice;

/// <summary>
/// 台词所属的场景：领地、野外（大地图行进）、聚落（城镇村落等兴趣点）、地城（遗迹与委托地城）。
/// 每句台词、每段场景剧情都必须写明自己属于哪些场景，只在这些场景里出现，**禁止跨越**——
/// 领地里的家常话不会在地城里冒出来，地城里的话也不会回家说。没写场景的内容读表即报错。
/// </summary>
public enum VoiceSetting
{
    Territory,
    Wilds,
    Settlement,
    Dungeon,
}
