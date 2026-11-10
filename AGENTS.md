## Razer 协议核对（强制）
- 任何 Razer 设备能力、报文、解析、宏/灯光/电量/映射改动前，先读当前项目本地解包/逆向产物中对应 PID 的 JS bundle：`artifacts/razer-mw-full-cdn/bundles/<PID>/`。
- 必须核对设备级 `main*.js` 的 `runtimeData`、`deviceUI`、`useFeature`、`onLoad*.js`，以及共享 chunk 的实际调用分支；共享 chunk 存在不等于该设备启用该能力。
- 回复里写明核对的 PID、文件、关键字段/分支，以及是否实机验证。没核对就直说没核对，禁止先改再猜。
