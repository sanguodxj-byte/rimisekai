namespace Rimisekai.Ink;

/// <summary>
/// 确定性伪随机。用来给手绘笔触和刻线提供稳定的抖动，
/// 同一 seed 每次渲染结果一致，避免画面逐帧“呼吸”。
/// </summary>
public sealed class InkRng
{
    private ulong _state;

    public InkRng(int seed)
    {
        _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xBF58476D1CE4E5B9UL;
        if (_state == 0)
            _state = 0x94D049BB133111EBUL;
    }

    public double Next()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return (_state * 2685821657736338717UL >> 11) * (1.0 / 9007199254740992.0);
    }

    public float Range(float lo, float hi) => lo + (hi - lo) * (float)Next();

    public int Int(int lo, int hi) => lo + (int)(Next() * (hi - lo));
}
