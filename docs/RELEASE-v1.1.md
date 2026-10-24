# HaloVolt v1.1 — 多品牌兼容性更新

## 这次更新

- **Compx / Nordic 52840 不再限定 DAREU VID**：任何在诊断页出现 `FF02:0002` 的 2.4G 设备都会尝试 17 字节 Nordic 52 电量事务，覆盖更多国产品牌方案。
- **扩展 HID 集合扫描**：新增 Consumer Control、System Control、System Multi-Axis、HID Battery Strength、FF01 vendor collection，提升标准 HID 电量的发现率。
- **保留已验证设备**：DAREU EK75、DAREU 无线鼠标仍按原逻辑读取。
- **兼容性文档**：新增 `docs/COMPATIBILITY.md`，按协议列出常见品牌/型号、验证状态和无法支持的情况。
- **版本号**：1.1.0。

## 协议覆盖

| 协议 | 常见品牌 / 型号 | 状态 |
| --- | --- | --- |
| Bluetooth LE Battery Service | Logitech MX、Keychron K 系列、Microsoft、Rapoo、小米等 | 协议支持，按是否暴露 `0x180F` 判定 |
| Logitech HID++ 2.0 | MX Master 3S/3/2S、MX Keys S、MX Anywhere 3S、G Pro X Superlight 2、G502 X、G915/G913、G304 等 | 协议支持 |
| HID Battery Strength | 部分 HP / Dell / Lenovo / Microsoft 办公键鼠、公版 2.4G | 协议支持，按 feature 判定 |
| Compx / Nordic 52840 | DAREU A950 系列、ATK、VXE、VGN、MCHOSE、Darmoshark、Waizowl、Ninjutso 等 | 尝试读取，按 `FF02:0002` 与合法响应判定 |
| DAREU EK75 | EK75（2.4G） | 已实机验证 |

表中“常见型号”是协议覆盖示例，不代表每一台都已实机验证。完整列表和限制见 [docs/COMPATIBILITY.md](../docs/COMPATIBILITY.md)。

## 说明

- 没有电量通道的设备（input-only vendor collection、仅厂商私有驱动上报、有线无电池设备）无法通过软件读取。
- 电量数值随充电/放电变化，以实际诊断结果为准。
- 仍然不需要管理员权限；读取策略遵循“活动时读取、休眠时缓存”。
