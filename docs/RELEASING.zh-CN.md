# 发布资产命名

适用于所有 GitHub Release，包括正式版和预发布测试包。

## 文件名

| 用途 | 文件名 |
|---|---|
| 正式安装（推荐） | `OpenSynapse-<version>-win-x64-Setup.msi` |
| 便携版 | `OpenSynapse-<version>-win-Portable.zip` |
| Velopack 更新包 | 保持 `vpk` 生成的 `OpenSynapse-<version>-full.nupkg` |
| Velopack 清单 | 保持 `RELEASES`、`releases.win.json`、`assets.win.json` |

当前用户安装包单独上传：`OpenSynapse-<version>-win-AutoUpdate-PerUser.exe`。它不放进上面的推荐安装列表；普通安装不要下载，只有明确需要当前用户安装时才用它。MSI 同样支持应用内更新，更新由安装时注册的 OpenSynapse 服务执行，不再请求管理员权限。

`<version>` 使用不带 `v` 的包版本，例如 `1.6.1`。改名后的安装包和原文件大小、SHA-256 必须一致。

## 为什么只改安装包名

`releases.win.json` 和 `RELEASES` 只引用 `*.nupkg`。MSI、AutoUpdate 包、Portable.zip 是初次安装资产；重命名它们不会影响客户端更新链路。MSI 安装也会下载同一个 `*.nupkg`，由安装时注册的 OpenSynapse 服务校验后通过任务计划执行 `Update.exe`，再以普通权限重启应用；服务不可用时才回退 UAC。不要改 `*.nupkg`、`RELEASES`、`releases.win.json`、`assets.win.json` 的文件名或内容。

发布资产和 Release 正文不要出现容易让人以为这是默认安装包的字样，避免用户误下当前用户包。普通安装只推荐 MSI，AutoUpdate 包放在最后。

## 发布步骤

1. 用同一次 `vpk pack` 生成全套资产。
2. 把三个安装包复制为本文列出的版本化名字，保留原文件用于校验。
3. 校验改名后的文件大小和 SHA-256 与原文件一致。
4. 上传版本化安装包，外加原样的 `*.nupkg`、`RELEASES`、`releases.win.json`、`assets.win.json`；不要再上传未改名的三个安装包。
5. Release 正文按下面的安装段开头，MSI 放第一位并标“推荐”，AutoUpdate 包不要放进推荐表。

## Release 正文安装段模板

中文：

```markdown
### 安装

| 文件 | 用途 |
|---|---|
| `OpenSynapse-<version>-win-x64-Setup.msi` | **推荐。** 安装到 `Program Files`，注册机器级 OpenSynapse 服务，之后切换显卡不再重复弹 UAC。 |
| `OpenSynapse-<version>-win-Portable.zip` | 便携版，解压即用；切换显卡时会请求 UAC。 |
```

当前用户安装包：`OpenSynapse-<version>-win-AutoUpdate-PerUser.exe`。**普通安装不要下载；只有明确需要当前用户安装时才用它。MSI 也支持应用内更新。**

English:

```markdown
### Install

| File | Purpose |
|---|---|
| `OpenSynapse-<version>-win-x64-Setup.msi` | **Recommended.** Installs under `Program Files` and registers the machine-wide OpenSynapse service, so GPU switches no longer prompt for UAC. |
| `OpenSynapse-<version>-win-Portable.zip` | Portable copy. The GPU switch prompts for UAC. |
```

Per-user package: `OpenSynapse-<version>-win-AutoUpdate-PerUser.exe`. **Do not download it for a normal install; use it only when you specifically need a per-user install. The MSI also supports in-app updates.**

把这段放进传给 `vpk pack --releaseNotes` 的发布说明文件，再同步到 GitHub Release 正文。这样应用内更新提示和 Release 页面都会先引导用户使用 MSI。
