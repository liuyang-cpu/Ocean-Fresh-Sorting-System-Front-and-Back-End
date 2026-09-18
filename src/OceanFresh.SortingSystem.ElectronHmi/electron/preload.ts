import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('oceanFresh', {
  get: <T>(endpoint: string) => ipcRenderer.invoke('local-api:get', endpoint) as Promise<T>,
  post: <T>(endpoint: string, body?: unknown) => ipcRenderer.invoke('local-api:post', endpoint, body) as Promise<T>,
  manualInfer: <T>(filePath: string) => ipcRenderer.invoke('local-api:manual-infer', filePath) as Promise<T>,
  selectImage: () => ipcRenderer.invoke('dialog:select-image') as Promise<string | null>,
  selectDirectory: () => ipcRenderer.invoke('dialog:select-directory') as Promise<{ directoryPath: string; imagePaths: string[] } | null>,
  fileToDataUrl: (filePath: string) => ipcRenderer.invoke('file:to-data-url', filePath) as Promise<string>,
  openFileLocation: (filePath: string) => ipcRenderer.invoke('file:open-location', filePath) as Promise<void>
});
