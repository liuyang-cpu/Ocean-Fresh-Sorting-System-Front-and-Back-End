export interface DashboardDto {
  snapshot: RuntimeSnapshot;
  currentSession: DetectionSession | null;
  summary: DashboardSummaryDto;
  channelKpis: DashboardChannelKpiDto[];
  defectStats: DashboardDefectStatDto[];
  latestAbnormal: DashboardLatestAbnormalDto | null;
  activeChannels: ChannelConfigDetailDto[];
  activeModels: ModelVersion[];
}

export interface RuntimeSnapshot {
  runtimeMode: number;
  deviceState: number;
  currentRecipe: string;
  currentModelVersion: string;
  currentBatch: string;
  totalInspected: number;
  totalRejected: number;
  yieldRate: number;
  updatedAt: string;
  machineStartedAt: string | null;
}

export interface DashboardSummaryDto {
  currentChannel: string;
  currentProduct: string;
  currentModel: string;
  machineWorkDuration: string;
  totalCount: number;
  normalCount: number;
  rejectCount: number;
  yieldRate: number;
}

export interface DashboardChannelKpiDto {
  channelId: string;
  channelName: string;
  status: string;
  productName: string;
  modelVersion: string;
  totalCount: number;
  normalCount: number;
  rejectCount: number;
  yieldRate: number;
  topDefectLabel: string;
}

export interface DashboardDefectStatDto {
  label: string;
  count: number;
  percent: number;
}

export interface DashboardLatestAbnormalDto {
  imagePath: string;
  summary: string;
  label: string;
  confidence: number;
  capturedAt: string;
}

export interface DetectionSession {
  id: string;
  sessionCode: string;
  channelId: string;
  productId: string;
  modelVersionId: string;
  startedAt: string;
  endedAt: string | null;
  status: number;
}

export interface ChannelConfigDetailDto {
  channel: ChannelConfig;
  product: SeafoodProduct | null;
  modelVersion: ModelVersion | null;
}

export interface ChannelConfig {
  id: string;
  channelNo: number;
  name: string;
  seafoodProductId: string;
  modelVersionId: string | null;
  modelPath: string;
  confidenceThreshold: number;
  isEnabled: boolean;
}

export interface SeafoodProduct {
  id: string;
  code: string;
  name: string;
  isEnabled: boolean;
}

export interface ModelVersion {
  id: string;
  seafoodCategoryId: string;
  version: string;
  sourceWeightPath: string;
  notes: string;
  status: number;
  createdAt: string;
}

export interface InspectionRecord {
  id: string;
  recipeId: string;
  modelVersionId: string;
  detectionSessionId: string | null;
  batchCode: string;
  imagePath: string;
  isRejected: boolean;
  isTimedOut: boolean;
  capturedAt: string;
  detections: DefectDetection[];
  ejectCommands: EjectCommand[];
}

export interface DefectDetection {
  id: string;
  label: string;
  confidence: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface EjectCommand {
  id: string;
  defectLabel: string;
  action: number;
  nozzleNumber: number;
  triggerEncoderPosition: number;
  triggerDelayMicroseconds: number;
  pulseWidthMicroseconds: number;
  createdAt: string;
}

export interface ManualInferenceResultDto {
  channelName: string;
  productName: string;
  modelVersion: string;
  modelPath: string;
  predictConfigPath: string;
  sourceImagePath: string;
  normalLabel: string;
  timedOut: boolean;
  detections: DefectDetection[];
  ejectCommands: EjectCommand[];
}
