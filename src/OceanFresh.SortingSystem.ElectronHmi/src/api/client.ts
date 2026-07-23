import type { DashboardDto, InspectionRecord, ManualInferenceResultDto } from './types';

export const api = {
  getDashboard: () => window.oceanFresh.get<DashboardDto>('api/runtime/dashboard'),
  getRecentRecords: (take = 1) => window.oceanFresh.get<InspectionRecord[]>(`api/records/recent?take=${take}`),
  startMachine: () => window.oceanFresh.post<void>('api/runtime/machine/start'),
  stopMachine: () => window.oceanFresh.post<void>('api/runtime/machine/stop'),
  startDetection: () => window.oceanFresh.post('api/runtime/detection/start'),
  stopDetection: () => window.oceanFresh.post('api/runtime/detection/stop'),
  manualInfer: (filePath: string) => window.oceanFresh.manualInfer<ManualInferenceResultDto>(filePath),
  selectImage: () => window.oceanFresh.selectImage(),
  selectDirectory: () => window.oceanFresh.selectDirectory(),
  fileToDataUrl: (filePath: string) => window.oceanFresh.fileToDataUrl(filePath)
};
