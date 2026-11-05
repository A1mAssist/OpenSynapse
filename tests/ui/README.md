# UI 自动化场景

`tests/ui` 不再保存一次性场景。运行 `scripts/run-ui-qa.ps1` 时，脚本会为本次执行生成临时 scenario 和截图目录，避免把本机绝对路径提交进仓库。

常用命令：

```powershell
pwsh -NoProfile -File scripts/run-ui-qa.ps1 -Configuration Release
```

需要验证某一页时，直接在脚本的 `steps` 中追加导航和截图步骤，或把生成的 scenario 保存到独立的 `artifacts/` 目录。不要把 `artifacts/`、`TestResults/` 或个人路径提交进仓库。
