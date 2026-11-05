# OpenSynapse 文档索引

根目录只保留架构与治理入口。按主题查下面的目录；历史交接和一次性测试记录在 `archive/`，不参与仓库发布。

## 架构与治理

- [架构边界与设备能力模型](ARCHITECTURE.md)
- [结构治理计划](MAINTAINABILITY-PLAN.md)

## Blade

- [Blade 2026 四显卡模式可行性](blade/BLADE_2026_GPU_FOUR_MODE_FEASIBILITY.zh-CN.md)
- [Blade Fn/M-key 映射](blade/BLADE_FN_MAPPINGS.md)
- [Blade 16 2025 M 键与 Fn 灯](blade/BLADE_MKEYS_AND_FN_LIGHTS.md)
- [CPU 功耗墙补丁分析](blade/CPU_POWER_PATCH_ANALYSIS.zh-CN.md)
- [Blade 功耗墙实现现状](blade/POWER_LIMIT_ALIGNMENT.md)
- [Blade 15 2019 灯光协议证据](blade/BLADE_023A_LIGHTING_EVIDENCE.md)
- [Blade 灯光方言](blade/BLADE_LIGHTING_DIALECTS.md)
- [GPU MUX HID 适配流程](blade/GPU_MUX_HID_ADAPTATION.zh-CN.md)

## 设备

- [支持设备（英文）](devices/SUPPORTED_DEVICES.md)
- [支持设备（简体中文）](devices/SUPPORTED_DEVICES.zh-CN.md)
- [设备适配指南（英文）](devices/DEVICE_ADAPTATION.md)
- [设备适配指南（简体中文）](devices/DEVICE_ADAPTATION.zh-CN.md)
- [中间件设备协议](devices/MIDDLEWARE_DEVICE_PROTOCOLS.md)
- [中间件鼠标审计](devices/MIDDLEWARE_MOUSE_AUDIT.md)
- [手柄协议可行性](devices/GAMEPAD_FEASIBILITY.md)
- [耳机音频对齐](devices/HEADSET_AUDIO_ALIGNMENT.md)
- [Leviathan V2 X 054A](devices/LEVIATHAN_V2X_054A.md)
- [Kiyo 相机 0E03](devices/KIYO_CAMERA_0E03.md)
- [飞利浦 Hue 发现与配对](devices/PHILIPS_HUE_DISCOVERY.zh-CN.md)
- [设备图片运行时解析](devices/RAZER_DEVICE_IMAGE_RUNTIME_RESOLUTION.zh-CN.md)
- [如何获取 Razer USB PID](devices/HOW_TO_GET_RAZER_USB_PID.zh-CN.md)

## 灯光

- [Chroma SDK 兼容层](lighting/CHROMA_SDK_SHIM.md)
- [Chroma 支持设备（英文）](lighting/CHROMA_SUPPORTED_DEVICES.md)
- [Chroma 支持设备（简体中文）](lighting/CHROMA_SUPPORTED_DEVICES.zh-CN.md)
- [灯光工作室](lighting/LIGHTING_STUDIO.zh-CN.md)
- [外设与底座灯光对齐](lighting/ACCESSORY_LIGHTING_ALIGNMENT.md)
- [Kiyo 灯环对齐](lighting/WEBCAM_LIGHTING_ALIGNMENT.md)

## 协议

- [BLE Protocol 30](protocols/PROTOCOL30_BLE.zh-CN.md)
- [BLE Protocol 30 覆盖情况](protocols/BLE_PROTOCOL30_COVERAGE.md)
- [Razer 0F1F 过滤驱动](protocols/RAZER_0F1F_FILTER_DRIVER.md)
- [Razer DKM IOCTL](protocols/RAZER_DKM_IOCTL.md)
- [Razer THX 原生通道](protocols/RAZER_THX_NATIVE.md)
- [Razer PID 到 Middleware JS](protocols/RAZER_PID_TO_MW_JS.md)
- [JS 协议对齐审计](protocols/JS_PROTOCOL_ALIGNMENT_AUDIT.md)

## 功能

- [通知与 Chroma 来源](features/NOTIFICATIONS_AND_CHROMA_SOURCES.md)
- [任务栏状态条](features/TASKBAR_STATUS_STRIP.zh-CN.md)

## 历史记录

`archive/` 只放本地交接、一次性测试指南和已完成的计划，不纳入 Git。要恢复历史内容，从 commit 历史或归档文件里取。
