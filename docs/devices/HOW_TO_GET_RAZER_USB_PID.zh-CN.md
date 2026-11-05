# 如何获取 Razer 设备的 USB PID

Windows 10/11 · 无需安装雷云 · 普通权限即可

如果你希望 OpenSynapse 适配你的 Razer 设备，需要提供设备实际使用的 **USB Product ID**，格式为 `VID:PID`。下面两种方法任选一种，通常一分钟内可以完成。

> **先确认：** 这里的 PID 是 USB Product ID，不是任务管理器里的进程 PID，也不是产品型号或序列号。

## 开始前

- 用 USB 线或 2.4G 接收器连接设备。
- 蓝牙连接可能显示不同的 PID，甚至不暴露厂商 HID 接口；请优先使用有线或接收器。
- 不需要安装雷云或第三方 USB 工具。
- 读取设备信息不需要管理员权限。

## 方法一：使用 PowerShell（推荐）

### 1. 打开 PowerShell

按 `Win`，输入 `PowerShell`，打开“Windows PowerShell”或“终端”。

### 2. 粘贴下面的命令

```powershell
Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
  Where-Object { $_.InstanceId -match 'VID_1532&PID_' } |
  ForEach-Object {
    $m = [regex]::Match($_.InstanceId, 'PID_([0-9A-F]{4})')
    if ($m.Success) {
      [pscustomobject]@{
        Device = $_.FriendlyName
        PID    = $m.Groups[1].Value
      }
    }
  } |
  Sort-Object PID, Device -Unique |
  Format-Table -AutoSize
```

### 3. 找到你的设备

输出类似这样：

```text
Device                     PID
------                     ---
Razer Blade 16             02C6
Razer Viper V3 HyperSpeed  00B8
```

`PID` 这一列就是结果。报给支持人员时写成完整格式：

```text
1532:02C6
```

同一个设备可能出现多行，这是正常的：一个 Razer 设备通常会暴露键盘、鼠标、控制接口等多个 HID 接口，它们的 PID 相同。

## 方法二：使用设备管理器

如果你不想使用命令，可以按下面的步骤操作。

### 1. 打开设备管理器

右键点击“开始”按钮，选择“设备管理器”。

### 2. 找到设备

根据设备类型展开对应分类：

- 鼠标：**鼠标和其他指针设备**
- 键盘：**键盘**
- 笔记本内置设备：**人体学输入设备** 或 **系统设备**
- 找不到时：**通用串行总线控制器**

找到名称中带 `Razer`、并且与你的实际产品名称一致的条目。

不要选择这些辅助条目：

- `Razer Control Device`
- `Razer USB C Dock`
- 通用的 `HID-compliant device`
- 通用的 `USB Composite Device`

优先选择带具体产品名的条目，例如 `Razer Blade 16` 或 `Razer Viper V3 HyperSpeed`。

### 3. 查看硬件 ID

右键点击设备，选择：

```text
属性 → 详细信息 → 硬件 ID
```

你会看到类似内容：

```text
USB\VID_1532&PID_02C6&MI_01
HID\VID_1532&PID_02C6&MI_01&COL01
```

其中：

- `VID_1532` 表示 Razer 的厂商 ID。
- `PID_02C6` 中的 `02C6` 就是 USB PID。

### 4. 记录结果

把 `VID:PID` 写成：

```text
1532:02C6
```

如果同一设备有多个接口，只要 `PID` 相同，选任意一条都可以。

## 如何选对设备

一台电脑上可能同时出现多个 Razer 设备，例如：

```text
Razer Blade 16             02C6
Razer Control Device       02C6
Razer USB C Dock           0F4E
Razer Viper V3 HyperSpeed  00B8
```

选择规则：

1. 必须是 `VID_1532`。
2. 名称应与实际产品一致。
3. 同一个 PID 的多个条目是同一个设备的不同接口。
4. 如果无线鼠标同时有有线和接收器两种连接方式，分别记录两种状态下的 PID。
5. 如果蓝牙连接下找不到厂商 PID，改用 USB 线或 2.4G 接收器重新查询。

不要根据产品名称猜 PID。例如 `Razer Blade 16` 不同年份的 PID 不同，必须读取实际设备。

## 需要提供给支持人员的信息

直接复制下面的模板填写：

```text
设备完整名称：
连接方式：有线 / 2.4G 接收器 / 蓝牙
VID:PID：
设备管理器中的硬件 ID：
Windows 版本：
OpenSynapse 版本：
问题现象：
```

示例：

```text
设备完整名称：Razer Blade 16 2025
连接方式：内置设备
VID:PID：1532:02C6
设备管理器中的硬件 ID：HID\VID_1532&PID_02C6&MI_01&COL01
Windows 版本：Windows 11 24H2
OpenSynapse 版本：x.y.z
问题现象：设备识别不到，设置页没有显示
```

只需要提供 `VID:PID` 和产品名称，不需要提供序列号、实例路径中的设备唯一编号或其他个人信息。

## 常见问题

### 命令没有任何输出

按顺序检查：

1. 设备是否已经连接到电脑。
2. 是否使用了 USB 线或 2.4G 接收器，而不是蓝牙。
3. 换一个 USB 接口，尽量直接连接电脑，不要经过扩展坞。
4. 打开设备管理器，检查是否有带黄色感叹号的未知设备。

如果系统没有 `Get-PnpDevice` 命令，可以使用下面的替代命令：

```powershell
Get-CimInstance Win32_PnPEntity |
  Where-Object { $_.PNPDeviceID -match 'VID_1532&PID_' } |
  Select-Object Name, PNPDeviceID
```

### 看到多个不同的 PID

把所有 PID 和对应设备名称一起提供。一个产品可能因为以下原因暴露多个 PID：

- 有线连接和无线接收器不同。
- 键盘、鼠标、控制接口分别使用不同 PID。
- 笔记本内置设备与 USB C Dock 是不同产品。

### 只看到 Razer Control Device

先检查设备管理器中的“人体学输入设备”和“通用串行总线控制器”，寻找带具体产品名的条目。仍然找不到时，把完整的 Razer 设备列表提供给支持人员。

### 设备管理器里没有“硬件 ID”

在“详细信息”页面的属性下拉框中手动选择“硬件 ID”。如果仍然没有，可以改选“设备实例路径”，其中同样包含 `VID_1532&PID_XXXX`。

### 仍然无法获取

提供设备的完整型号、年份和连接方式，例如 `RZ09-0528`。这些信息只能帮助定位产品，不能代替实际的 USB PID。
