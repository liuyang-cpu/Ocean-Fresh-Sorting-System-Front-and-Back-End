# OceanFresh 人工复核工作台设计规格

## 目标

基于 Figma 文件 `OceanFresh 人工复核工作台 HMI 设计稿` 中的当前人工复核界面落地 ElectronHmi。`artifacts/design/manual-review-hmi-imagegen-reference.png` 只作为视觉探索参考，不作为本轮实现的主准绳。该界面面向产线操作员，优先保证高信息密度、状态可扫读、主操作不误触。

## 画布与结构

- Figma 源稿：`https://www.figma.com/design/9XusNrqH8L9WyZFKXCsAWb?node-id=2-3`
- 目标尺寸：`1920 x 1080`，最小工作尺寸 `1366 x 768`。
- 左侧导航：固定宽度 `112px`。
- 右侧工作区：顶部栏、任务摘要、三栏复核区、底部完成条。
- 三栏复核区：缺陷类型 `300px`，待复核对象 `500px`，单目标预览与判定占剩余空间。
- 单目标预览区按 Figma 当前稿实现为整块深色网格画布；有图片时显示真实 X 光图与检测框，无图片时显示空态说明。

## 视觉 Token

- 页面底色：`--hmi-bg #06131f`
- 导航底色：`--hmi-rail #08283c`
- 面板底色：`--hmi-surface #0b2438`
- 强面板底色：`--hmi-surface-strong #0d2a40`
- 输入底色：`--hmi-input #051625`
- 默认描边：`--hmi-border #245777`
- 强描边：`--hmi-border-strong #34759e`
- 主操作：`--hmi-primary #226fe5`
- 成功/已复核：`--hmi-success #20d991`
- 警告/剩余：`--hmi-warning #ffa722`
- 危险/误检：`--hmi-danger #ff6473`

## 组件拆分建议

- `SideNavigation`
- `TopReviewBar`
- `TaskSummaryBar`
- `DefectTypePanel`
- `PendingReviewList`
- `SingleTargetReviewPanel`
- `ReviewDecisionActions`
- `CompletionBar`

## 交互状态

- `确认异常`：主按钮，蓝色，高优先级。
- `正常误检`：同级危险判定，红色描边/填充。
- `保存类别修正`：次级操作，需选择类别或输入备注。
- `完成本次复核`：当剩余复核数为 0 时可用，否则禁用。

## 开发还原策略

1. Figma 当前人工复核界面为主准绳；PNG 仅作为辅助参考，不作为页面背景。
2. 使用 CSS token 固化颜色、间距、圆角和阴影。
3. 通过 Vue 组件和真实数据重建布局。
4. 开发完成后截图，与参考图对比布局、字号、颜色和状态层级。
