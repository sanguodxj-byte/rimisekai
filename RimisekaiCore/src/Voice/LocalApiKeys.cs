using System;
using System.IO;

namespace Rimisekai.Voice;

/// <summary>
/// 本地密钥表 tools/api_keys.txt（不入库，本地维护）的读取。
/// 一行一条「名字=值」，# 开头为注释；表里没有这一条就返回空串，
/// 生成器据此判为不可用（Available=false），不带占位符去打接口。
/// </summary>
public static class LocalApiKeys
{
    public static string Get(string name)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, "tools", "api_keys.txt");
                if (File.Exists(path))
                    return Lookup(path, name);
                dir = dir.Parent;
            }
        }
        return "";
    }

    private static string Lookup(string path, string name)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            if (line[..eq].Trim() == name)
                return line[(eq + 1)..].Trim();
        }
        return "";
    }
}
