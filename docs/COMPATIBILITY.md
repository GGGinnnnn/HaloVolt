# HaloVolt 兼容性说明 / Compatibility

HaloVolt 不按品牌写死型号，而是按**协议**适配。设备只要暴露标准接口，或使用下面已实现的厂商协议，就可能读到电量；最终以「调试与诊断」的判定为准。

> 结论：覆盖主流四类协议，但不是“所有品牌所有型号”都能读。没有电量通道的设备，任何软件都无法凭空读到。

## 已验证（实机）

| 设备 | 连接 | 协议 | 实测 |
| --- | --- | --- | --- |
| DAREU EK75 | 2.4G 接收器 `260D:0042` | DAREU 65-byte HID feature（Power / Battery Status） | 54%（录制时） |
| DAREU 无线鼠标 / A950 系列接收器 | 2.4G 接收器 `260D:1175` | Compx / Nordic 52840 `FF02:02` | 25%（录制时） |

## 协议覆盖与常见型号

### 1. Bluetooth LE Battery Service

**协议**：GATT `0x180F` / `0x2A19`，优先读 Windows 缓存。

**常见设备**：

- Logitech MX Keys / MX Keys S、MX Master 3S 等蓝牙模式
- Keychron K / K Pro / K Max 系列蓝牙模式
- Microsoft Designer / Precision 蓝牙键鼠
- Rapoo、雷柏、小米、绿联等大多数蓝牙 LE 键鼠
- 其他在 Windows 中暴露 Battery Service 的 BLE HID 设备

### 2. Logitech HID++ 2.0

**协议**：HID++ 2.0 特性 `0x1004` Battery Unified / `0x1000` Battery Status / `0x1001` Battery Voltage；支持直连、Unifying、Bolt 和一拖多接收器。

**常见设备**：

- MX Master 3S / 3 / 2S、MX Anywhere 3S / 3
- MX Keys S / MX Keys、MX Mechanical
- G Pro X Superlight / Superlight 2、G502 X Lightspeed
- G915 / G915 X、G913、G703、G304、G705
- 其他支持 HID++ 2.0 的 Logitech 键鼠

### 3. 标准 HID Battery Strength

**协议**：HID Usage Page `0x06` / Usage `0x20` 的 feature report；设备需主动暴露 Battery Strength。

**常见设备**：

- 部分 HP / Dell / Lenovo / Microsoft 办公键鼠
- 部分公版 2.4G 键鼠
- 少数 USB-C / 2.4G 双模键盘

### 4. Compx / Nordic 52840（2.4G）

**协议**：`0xFF02:0x02` 集合，17 字节 Nordic 52 事务，电量在响应 byte 6。

**常见方案/品牌**：

- DAREU A950 / A950 Air / A950 Wing
- ATK / VXE / VGN / MCHOSE / Darmoshark / Waizowl / Ninjutso 等采用 Compx / Nordic 52840 的 2.4G 鼠标
- 其他在诊断页出现 `FF02:0002` 的 2.4G 设备

> 同一方案的不同固件可能使用不同握手；能出现 `FF02:0002` 且响应合法时，HaloVolt 会读取；否则显示“无电量通道”。

### 5. DAREU EK75

**专属适配**：65 字节 HID feature 的 `Power Management / Battery Status` 命令。官方 DAREU Driver 运行时优先读取驱动值，关闭驱动后直接查询 HID。

## 不能保证支持的情况

- 只提供 input-only vendor collection，没有 output/feature 查询通道（例如部分 `USB 2.4G Receiver 260D:0042` 的非 EK75 设备）
- 只通过 Razer Synapse / SteelSeries GG / Corsair iCUE / ASUS Armoury Crate 等厂商私有驱动上报，且没有公开 HID/BLE 电量接口
- 有线键鼠、无电池设备
- 厂商固件锁定、需要驱动握手或需要管理员权限的私有通道

## 如何判断你的设备

1. 打开「调试与诊断」。
2. 看原始 HID 通道是否有 `FF02:0002`、`0x06:0x20`，或看 `provider` 判定。
3. 如果显示“无电量通道”，说明设备没有可查询通道，不是 HaloVolt 的权限问题。
4. 导出诊断报告，附上品牌/型号，可继续适配。

## 测试环境

- Windows 11 26100 / x64
- HaloVolt v1.1
- DAREU EK75 + DAREU 2.4G 鼠标

> 表中的“常见设备”是协议覆盖示例，不代表每一台都已实机验证；购买前请先用诊断页确认。
