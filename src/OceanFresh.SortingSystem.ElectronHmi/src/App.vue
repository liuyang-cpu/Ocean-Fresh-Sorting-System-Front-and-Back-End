<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { api } from './api/client';
import type { DashboardDto, DefectDetection, InspectionRecord } from './api/types';

const RuntimeMode = {
  Stopped: 0,
  Starting: 1,
  Running: 2,
  SafeStop: 3,
  Faulted: 4
} as const;

const traitPalette = {
  normal: { label: '正常', color: '#20d991', badge: '正常' },
  broken: { label: '疑似碎壳', color: '#ff6473', badge: '疑似碎壳' },
  muddy: { label: '泥包', color: '#ffa722', badge: '泥包' },
  empty: { label: '空壳', color: '#8db1cc', badge: '空壳' }
} as const;

type TraitKey = keyof typeof traitPalette;

const dashboard = ref<DashboardDto | null>(null);
const latestRecord = ref<InspectionRecord | null>(null);
const imageDataUrl = ref('');
const selectedImagePath = ref('');
const statusMessage = ref('正在连接本地 LocalApi...');
const selectedDirectoryImages = ref<string[]>([]);
const currentBatchIndex = ref(0);
const isBusy = ref(false);
const isBatchRunning = ref(false);
const batchStopRequested = ref(false);
const confirmedCount = ref(0);
const falsePositiveCount = ref(0);
const classFixCount = ref(0);
const reviewClass = ref('正常');
const reviewNote = ref('');
let refreshTimer: number | undefined;

const isMachineRunning = computed(() => dashboard.value?.snapshot.runtimeMode === RuntimeMode.Running);
const runtimeText = computed(() => {
  const mode = dashboard.value?.snapshot.runtimeMode;
  if (mode === RuntimeMode.Running) return '复核进行中';
  if (mode === RuntimeMode.Faulted) return '故障锁定';
  if (mode === RuntimeMode.SafeStop) return '安全停机';
  if (mode === RuntimeMode.Starting) return '启动中';
  return '待机';
});

const activeChannel = computed(() => dashboard.value?.summary.currentChannel || 'YG-MV-001');
const activeProduct = computed(() => dashboard.value?.summary.currentProduct || '油蛤');
const activeModel = computed(() => dashboard.value?.summary.currentModel || '生产检测');
const totalCount = computed(() => dashboard.value?.summary.totalCount ?? 0);
const rejectCount = computed(() => dashboard.value?.summary.rejectCount ?? 0);
const yieldRate = computed(() => Number(dashboard.value?.summary.yieldRate ?? 100));
const yieldRateText = computed(() => `${yieldRate.value.toFixed(0)}%`);
const taskCode = computed(() => latestRecord.value?.batchCode || dashboard.value?.currentSession?.sessionCode || 'DS-20260624-115232');
const imageName = computed(() => {
  const path = latestRecord.value?.imagePath || selectedImagePath.value;
  return path ? path.split(/[\\/]/).pop() : '0000001.jpg';
});

const abnormalObjectCount = computed(() => Math.max(rejectCount.value, latestRecord.value?.isRejected ? 1 : 0));
const reviewedCount = computed(() => confirmedCount.value + falsePositiveCount.value + classFixCount.value);
const remainingCount = computed(() => Math.max(0, abnormalObjectCount.value - reviewedCount.value));
const reviewProgress = computed(() => {
  if (abnormalObjectCount.value <= 0) return 0;
  return Math.min(100, Math.round((reviewedCount.value / abnormalObjectCount.value) * 100));
});
const completionReady = computed(() => abnormalObjectCount.value > 0 && remainingCount.value === 0);

function traitKey(label: string): TraitKey {
  const value = label.toLowerCase();
  if (value.includes('normal') || value.includes('正常')) return 'normal';
  if (value.includes('mud') || value.includes('泥')) return 'muddy';
  if (value.includes('empty') || value.includes('空')) return 'empty';
  return 'broken';
}

function boxStyle(detection: DefectDetection) {
  const color = traitPalette[traitKey(detection.label)].color;
  return {
    left: `${(detection.x / 1536) * 100}%`,
    top: `${(detection.y / 300) * 100}%`,
    width: `${(detection.width / 1536) * 100}%`,
    height: `${(detection.height / 300) * 100}%`,
    borderColor: color,
    color
  };
}

async function refreshDashboard() {
  try {
    dashboard.value = await api.getDashboard();
    const records = await api.getRecentRecords(1);
    const record = records[0] ?? null;

    if (record && record.id !== latestRecord.value?.id) {
      latestRecord.value = record;
      await loadImage(record.imagePath);
    }

    if (!latestRecord.value) {
      statusMessage.value = 'LocalApi 已连接，等待异常对象进入人工复核队列。';
    }
  } catch (error) {
    statusMessage.value = `本地服务未连接或正在启动: ${errorMessage(error)}`;
  }
}

async function loadImage(filePath: string) {
  try {
    imageDataUrl.value = await api.fileToDataUrl(filePath);
  } catch (error) {
    statusMessage.value = `图片读取失败: ${errorMessage(error)}`;
  }
}

async function importSingleImage() {
  const filePath = await api.selectImage();
  if (!filePath) return;

  selectedImagePath.value = filePath;
  await runSingleImage(filePath);
}

async function runSingleImage(filePath: string) {
  try {
    isBusy.value = true;
    statusMessage.value = `正在按当前启用通道检测: ${filePath.split(/[\\/]/).pop()}`;
    await api.manualInfer(filePath);
    await refreshDashboard();
    statusMessage.value = '单张图片检测完成，已生成复核对象。';
  } catch (error) {
    statusMessage.value = `单张检测失败: ${errorMessage(error)}`;
  } finally {
    isBusy.value = false;
  }
}

async function chooseDirectory() {
  const result = await api.selectDirectory();
  if (!result) return;

  selectedDirectoryImages.value = result.imagePaths;
  currentBatchIndex.value = 0;
  statusMessage.value = `已选择批量检测目录，共 ${result.imagePaths.length} 张图片。`;
}

async function toggleBatchDetection() {
  if (isBatchRunning.value) {
    batchStopRequested.value = true;
    statusMessage.value = '正在停止离线批量检测...';
    return;
  }

  if (selectedDirectoryImages.value.length === 0) {
    await chooseDirectory();
    if (selectedDirectoryImages.value.length === 0) return;
  }

  batchStopRequested.value = false;
  isBatchRunning.value = true;
  currentBatchIndex.value = 0;

  try {
    try {
      await api.startDetection();
    } catch {
      // A running detection session is acceptable for batch inspection.
    }

    for (const [index, filePath] of selectedDirectoryImages.value.entries()) {
      if (batchStopRequested.value) break;
      currentBatchIndex.value = index + 1;
      statusMessage.value = `离线批量检测 ${index + 1}/${selectedDirectoryImages.value.length}: ${filePath.split(/[\\/]/).pop()}`;
      await api.manualInfer(filePath);
      await refreshDashboard();
      await delay(68);
    }

    statusMessage.value = batchStopRequested.value
      ? `离线批量检测已停止，已处理 ${currentBatchIndex.value} 张图片。`
      : `离线批量检测完成，共处理 ${selectedDirectoryImages.value.length} 张图片。`;
  } catch (error) {
    statusMessage.value = `离线批量检测失败: ${errorMessage(error)}`;
  } finally {
    isBatchRunning.value = false;
    batchStopRequested.value = false;
  }
}

function confirmAbnormal() {
  if (remainingCount.value > 0 || abnormalObjectCount.value === 0) confirmedCount.value += 1;
  statusMessage.value = '已保存“确认异常”判定，系统将进入下一条未复核对象。';
}

function markFalsePositive() {
  if (remainingCount.value > 0 || abnormalObjectCount.value === 0) falsePositiveCount.value += 1;
  statusMessage.value = '已标记为正常误检，请继续复核下一条对象。';
}

function saveClassFix() {
  if (remainingCount.value > 0 || abnormalObjectCount.value === 0) classFixCount.value += 1;
  statusMessage.value = `类别修正已保存为“${reviewClass.value}”。${reviewNote.value ? `备注: ${reviewNote.value}` : ''}`;
}

function delay(milliseconds: number) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

onMounted(() => {
  void refreshDashboard();
  refreshTimer = window.setInterval(() => void refreshDashboard(), 1000);
});

onBeforeUnmount(() => {
  if (refreshTimer) window.clearInterval(refreshTimer);
});
</script>

<template>
  <main class="review-shell">
    <aside class="review-rail">
      <div class="of-logo">
        <strong>OF</strong>
        <span>SORT</span>
      </div>
      <div class="rail-product">海鲜分拣</div>

      <nav class="rail-nav" aria-label="主导航">
        <button class="rail-link">
          <span class="rail-icon">⌂</span>
          首页
        </button>
        <button class="rail-link">
          <span class="rail-icon">♙</span>
          用户
        </button>
        <button class="rail-link active">
          <span class="rail-icon">▧</span>
          数据
        </button>
      </nav>

      <button class="rail-link rail-exit">
        <span class="rail-icon">↪</span>
        退出系统
      </button>
    </aside>

    <section class="review-workspace">
      <header class="review-topbar">
        <div class="top-title">人工复核</div>
        <div class="operator-text">操作员&nbsp; operator</div>
        <button class="mode-button">批注模式</button>
        <div class="top-spacer"></div>
        <div class="api-status">
          <i></i>
          LocalApi 已连接
        </div>
        <button class="ghost-button">返回数据中心</button>
        <button class="icon-button" aria-label="系统设置">⚙</button>
      </header>

      <section class="review-board">
        <div class="board-title-row">
          <h1>人工复核工作台</h1>
        </div>

        <section class="task-summary">
          <div class="task-main">
            <strong>{{ taskCode }} · {{ activeProduct }}</strong>
            <span>{{ activeModel }} / {{ activeChannel }} / 总量 {{ totalCount }} / 异常 {{ rejectCount }} / 良率 {{ yieldRateText }}</span>
          </div>
          <div class="task-metrics">
            <b class="danger">异常 {{ abnormalObjectCount }}</b>
            <b class="success">已复核 {{ reviewedCount }}</b>
            <b class="warning">剩余 {{ remainingCount }}</b>
          </div>
          <div class="task-progress">
            <span>复核进度</span>
            <div class="progress-track">
              <i :style="{ width: `${reviewProgress}%` }"></i>
            </div>
            <strong>{{ reviewProgress }}%</strong>
          </div>
          <div class="review-state" :class="{ running: isMachineRunning }">
            <i></i>
            {{ runtimeText }}
          </div>
        </section>

        <section class="review-grid">
          <article class="hmi-card defect-panel">
            <h2>缺陷类型</h2>
            <p>只显示对应异常对象</p>
            <button class="defect-card selected">
              <span>全部异常</span>
              <strong>{{ abnormalObjectCount }}</strong>
              <i></i>
              <small>已复核 {{ reviewedCount }} ({{ reviewProgress }}%)</small>
            </button>
          </article>

          <article class="hmi-card pending-panel">
            <div class="panel-heading">
              <h2>待复核对象</h2>
              <span>共 {{ remainingCount }} 项</span>
            </div>
            <label class="search-field">
              <span>⌕</span>
              <input placeholder="按编号、类别或图片名搜索" />
            </label>
            <div class="queue-tabs">
              <button class="active">未复核 {{ remainingCount }}</button>
              <button>已确认 {{ confirmedCount }}</button>
              <button>误检 {{ falsePositiveCount }}</button>
              <button>类别修正</button>
            </div>
            <div class="empty-queue">
              <span class="empty-icon">▱</span>
              <strong>{{ remainingCount > 0 ? '等待选择复核对象' : '暂无待复核对象' }}</strong>
              <small>当左侧选择缺陷类型后，这里会显示待复核对象列表</small>
            </div>
            <div class="queue-note">选择对象后，右侧才生成单目标复核图，避免列表加载卡顿。</div>
          </article>

          <article class="hmi-card judge-panel">
            <div class="panel-heading">
              <div>
                <h2>单目标预览与判定</h2>
                <p>请选择一张图片</p>
              </div>
              <span class="remain-badge">剩余 {{ remainingCount }} 个</span>
            </div>
            <div class="judge-meta">
              <span>异常集合 S {{ abnormalObjectCount }} 个 / 已复核 {{ reviewedCount }} 个 / 未完成，还剩 {{ remainingCount }} 个 / 误检 {{ falsePositiveCount }} 个 / 类别修正 {{ classFixCount }} 个</span>
              <strong>严格召回率不计算：当前只复核异常剔除集合，未覆盖未剔除集合抽检。</strong>
            </div>

            <div class="preview-canvas">
              <div class="xray-preview" :class="{ empty: !imageDataUrl }">
                <div v-if="imageDataUrl" class="xray-image-wrap">
                  <img :src="imageDataUrl" alt="当前复核图片" />
                  <div class="normal-frame">正常</div>
                  <div
                    v-for="detection in latestRecord?.detections ?? []"
                    :key="detection.id"
                    class="detect-box"
                    :style="boxStyle(detection)"
                  >
                    <span>{{ traitPalette[traitKey(detection.label)].badge }} {{ Number(detection.confidence).toFixed(2) }}</span>
                  </div>
                </div>
                <div v-else class="preview-empty-state">
                  <strong>当前只显示一个复核目标</strong>
                  <span>X 光目标图将在这里显示；检测框、置信度与原始类别同步呈现。</span>
                </div>
              </div>
            </div>

            <div class="decision-row">
              <button class="decision-button confirm" @click="confirmAbnormal">
                <span>✓</span>
                确认异常
              </button>
              <button class="decision-button false-positive" @click="markFalsePositive">
                <span>×</span>
                正常误检
              </button>
              <div class="next-hint">
                <span>保存后自动进入下一条未复核对象</span>
                <strong>{{ statusMessage }}</strong>
              </div>
            </div>

            <div class="class-fix">
              <h3>类别修正</h3>
              <button class="save-fix" @click="saveClassFix">保存类别修正</button>
              <select v-model="reviewClass">
                <option>正常</option>
                <option>疑似碎壳</option>
                <option>泥包</option>
                <option>空壳</option>
              </select>
              <input v-model="reviewNote" placeholder="可输入备注说明（选填）" />
              <button class="manual-save" @click="saveClassFix">按人工标签保存</button>
            </div>
          </article>
        </section>

        <footer class="completion-bar">
          <span>所有异常对象完成复核后任务才可完成 · 异常对象 {{ abnormalObjectCount }} 个</span>
          <strong class="success">已复核 {{ reviewedCount }} 个</strong>
          <strong class="warning">剩余 {{ remainingCount }} 个</strong>
          <strong class="danger">误检 {{ falsePositiveCount }} 个</strong>
          <strong class="info">类别修正 {{ classFixCount }} 个</strong>
          <button :disabled="!completionReady">完成本次复核</button>
        </footer>
      </section>
    </section>
  </main>
</template>
