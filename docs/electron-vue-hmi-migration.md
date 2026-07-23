# Electron + Vue HMI 迁移方案

## 目标

将当前 WPF HMI 逐步替换为 `Electron + Vue 3 + TypeScript` 前端，同时保留现有后端能力：

- `.NET LocalApi`
- Runtime 运行协调
- DeviceHost 硬件状态与告警
- YOLO FastAPI 推理服务
- SQLite 本地持久化

## 第一阶段范围

第一阶段新增独立前端项目：

```text
src/OceanFresh.SortingSystem.ElectronHmi
```

已迁移的首个页面是生产常驻首页，包含：

- 当前通道、海鲜产品、模型版本
- X 光图片显示与检测框叠加
- 机器启动 / 机器停止单按钮
- 当前图片识别结果
- 分类统计、良率、异常剔除总数
- 单张图片检测
- 离线批量检测

## 为什么不直接删除 WPF

短期内 WPF 仍保留，原因是：

- 现有功能较多，直接删除风险高。
- 甲方交付前需要稳定可运行版本。
- 新前端需要逐页迁移和验收。

最终替换路径建议：

1. Electron 首页监控页稳定。
2. 迁移数据中心。
3. 迁移用户中心。
4. 迁移设置页：产品、模型、通道、硬件、剔除、安全、软件维护。
5. 确认新前端覆盖所有交付功能后，再下线 WPF HMI。

## 接入方式

Vue 页面不直接访问硬件，不直接访问数据库，不直接调用 YOLO 文件系统。

```text
Vue Renderer
    ↓ window.oceanFresh
Electron Preload
    ↓ IPC
Electron Main
    ↓ HTTP
.NET LocalApi
```

文件选择和本地图片显示由 Electron Main 处理，避免 Web 浏览器权限限制。

## 下一步建议

- 将数据中心迁移为 Vue + ECharts。
- 将设置页迁移为 Vue 表格和表单。
- 在 Electron Main 中加入 LocalApi 自动启动能力。
- 后续用 electron-builder 生成 Windows 安装包。
