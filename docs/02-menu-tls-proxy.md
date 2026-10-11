# 02 · 卡在加载画面 / `Unable to complete SSL connection`

## 现象

游戏能启动、能看到左下角 `0.16.9.0.40087 SPT 4.0.13 - 2891fd` 水印，但画面一直停在昏暗的加载背景，
**没有主菜单**。客户端日志（`<SPT 根目录>\Logs\log_.../... errors.log`）里：

```
[Error|backend] HTTPS: https://127.0.0.1:6969/client/menu/locale/en,
    isNetworkError:True, isHttpError:False, responseCode:0
 error text: Unable to complete SSL connection
[Warn|backend] Request https://127.0.0.1:6969/client/menu/locale/en
    will be retried after 46.97 sec, retry:1 from retries:3
```

主菜单需要的那条数据请求拿不到 → 界面永远出不来。

## 定位

关键线索是**同一进程里两套网络栈表现不同**：

- SPT 的 BepInEx 插件（`spt-core.dll` 等）发往 `/singleplayer/*` 的请求 **全部成功**；
- 游戏自己的后端（`https://127.0.0.1:6969/client/...`）**TLS 握手失败**。

`BepInEx\LogOutput.log` 显示 `SslCertificatePatch`、`UnityWebRequestPatch` 都已启用，
所以**不是证书信任问题**。

用 libcurl（与 Unity 网络栈同族）做对照实验：

```
libcurl 直连 https://127.0.0.1:6969/...              → OK (http 200)
libcurl 走 Clash 代理 127.0.0.1:7890                 → exit=35
    "schannel: failed to receive handshake, SSL/TLS connection failed"
```

而当时 WinINET 设置是：

```
ProxyEnable = 1
ProxyServer = 127.0.0.1:7890      (Clash for Windows 在运行)
ProxyOverride = localhost;127.*;10.*;<local>;...
```

结论：**系统代理把 `127.0.0.1` 的请求也发给了代理**，代理再转发时 TLS 握手失败。
游戏自己的 HTTP 栈遵循系统代理且**没有正确应用 `127.*` 这段绕过列表**；
而 SPT 插件的请求被 `UnityWebRequestPatch` 显式绕开了代理，所以两者表现不一致。

> 注意：`ProxyOverride` 里有 `127.*`，`.NET Framework` 的 `HttpWebRequest` 确实会绕过它，
> 但 Unity/libcurl 这条路径并不会，所以"绕过列表里写了 127.*"并不足以解决问题。

## 规避方法

按推荐程度排序：

1. **玩 SPT 时关掉系统代理**（Clash for Windows: 关闭 "System Proxy"/"系统代理" 开关）。
   SPT 全程只连本机，不需要代理。玩完再开。
2. 在代理软件里加一条**直连规则**，例如 Clash 配置：

   ```yaml
   rules:
     - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve
     - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve
     - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve
     - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve
   ```

   注意：部分用户在**代理进程已接管流量**（TUN/增强模式）时，仅有绕过列表仍会失败，
   此时用第 1 条最稳。
3. 排查时先用 `curl -k https://127.0.0.1:6969/client/menu/locale/en` 直连测一次：
   直连 200、走代理失败 → 就是这个原因。

## 副作用说明

- 关掉系统代理不影响 SPT 服务端与客户端之间的通信（都是 `127.0.0.1`）。
- 若你的 SPT 服务端重启后客户端仍连不上，先确认 6969 端口在监听、且没有别的 SPT.Server 实例占用。
