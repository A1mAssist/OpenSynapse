# 发布资产命名

适用于所有 GitHub Release，包括正式版和预发布测试包。

## 文件名

| 用途 | 文件名 |
|---|---|
| 正式安装（推荐） | `OpenSynapse-<version>-win-x64.msi` |
| 自动更新用包（当前用户安装） | `OpenSynapse-<version>-win-Setup-AutoUpdate-PerUser.exe` |
| 便携版 | `OpenSynapse-<version>-win-Portable.zip` |
| Velopack 更新包 | 保持 `vpk` 生成的 `OpenSynapse-<version>-full.nupkg` |
| Velopack 清单 | 保持 `RELEASES`、`releases.win.json`、`assets.win.json` |

`<version>` 使用不带 `v` 的包版本，例如 `1.6.1`。改名后的安装包和原文件大小、SHA-256 必须一致。

## 为什么只改安装包名

`releases.win.json` 和 `RELEASES` 只引用 `*.nupkg`。MSI、Setup.exe、Portable.zip 不参与客户端自动更新，重命名这三个文件不会影响更新链路。不要改 `*.nupkg`、`RELEASES`、`releases.win.json`、`assets.win.json` 的文件名或内容。

## 发布步骤

1. 用同一次 `vpk pack` 生成全套资产。
2. 把三个安装包复制为上面的版本化名字，保留原文件用于校验。
3. 校验改名后的文件大小和 SHA-256 与原文件一致。
4. 上传版本化安装包，外加原样的 `*.nupkg`、`RELEASES`、`releases.win.json`、`assets.win.json`；不要再上传未改名的三个安装包。
5. Release 正文按下面的安装段开头，MSI 放第一位并标“推荐”。

## Release 正文安装段模板

中文：

```markdown
### 安装

| 文件 | 用途 |
|---|---|
| `OpenSynapse-<version>-win-x64.msi` | **推荐。** 安装到 `Program Files`，注册机器级 GPU 服务，之后切换显卡不再重复弹 UAC。 |
| `OpenSynapse-<version>-win-Setup-AutoUpdate-PerUser.exe` | 自动更新用包，仅安装到当前用户目录。只有需要当前用户安装或走自动更新链路时才用它；普通安装请用 MSI。 |
| `OpenSynapse-<version>-win-Portable.zip` | 便携版，解压即用；切换显卡时会请求 UAC。 |
```

English:

```markdown
### Install

| File | Purpose |
|---|---|
| `OpenSynapse-<version>-win-x64.msi` | **Recommended.** Installs under `Program Files` and registers the machine-wide GPU service, so GPU switches no longer prompt for UAC. |
| `OpenSynapse-<version>-win-Setup-AutoUpdate-PerUser.exe` | Auto-update/per-user package. Use it only for a per-user install or the auto-update path; normal installs should use the MSI. |
| `OpenSynapse-<version>-win-Portable.zip` | Portable copy. The GPU switch prompts for UAC. |
```

把这段放进传给 `vpk pack --releaseNotes` 的发布说明文件，再同步到 GitHub Release 正文。这样应用内更新提示和 Release 页面都会先引导用户使用 MSI。
