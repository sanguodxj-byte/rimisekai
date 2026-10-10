# -*- coding: utf-8 -*-
"""密钥外置读取：同目录 api_keys.txt（不入库，本地维护）。

格式：一行一条「名字=值」，# 开头为注释。
脚本里 get("sakiko") 取用；文件缺失或没有这一条时抛错，
避免带着占位符去打接口、把认证失败当成别的毛病查。
"""
import os

_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "api_keys.txt")


def load():
    keys = {}
    with open(_PATH, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            name, value = line.split("=", 1)
            keys[name.strip()] = value.strip()
    return keys


def get(name):
    value = load().get(name, "")
    if not value:
        raise RuntimeError(f"api_keys.txt 里没有 {name}（该文件不入库，需本地维护）")
    return value
