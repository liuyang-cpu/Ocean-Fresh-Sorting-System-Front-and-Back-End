import { app, BrowserWindow, dialog, ipcMain, shell } from 'electron';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import { readdir, readFile } from 'node:fs/promises';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const localApiBaseUrl = (process.env.OCEANFRESH_LOCAL_API_URL ?? 'http://127.0.0.1:5188').replace(/\/+$/, '');

let mainWindow: BrowserWindow | null = null;

async function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1680,
    height: 980,
    minWidth: 1280,
    minHeight: 780,
    backgroundColor: '#07111d',
    title: '海鲜 X 光分拣系统',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  mainWindow.removeMenu();

  if (process.env.VITE_DEV_SERVER_URL) {
    await mainWindow.loadURL(process.env.VITE_DEV_SERVER_URL);
    mainWindow.webContents.openDevTools({ mode: 'detach' });
  } else {
    await mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    void shell.openExternal(url);
    return { action: 'deny' };
  });
}

async function requestLocalApi(endpoint: string, init?: RequestInit) {
  const cleanEndpoint = endpoint.replace(/^\/+/, '');
  const response = await fetch(`${localApiBaseUrl}/${cleanEndpoint}`, init);
  const text = await response.text();

  if (!response.ok) {
    throw new Error(text.trim().replace(/^"|"$/g, '') || `本地 API 请求失败: ${response.status}`);
  }

  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function resolveMime(filePath: string) {
  const extension = path.extname(filePath).toLowerCase();
  if (extension === '.jpg' || extension === '.jpeg') {
    return 'image/jpeg';
  }

  if (extension === '.bmp') {
    return 'image/bmp';
  }

  return 'image/png';
}

function isSupportedImage(filePath: string) {
  return ['.png', '.jpg', '.jpeg', '.bmp'].includes(path.extname(filePath).toLowerCase());
}

async function collectImages(directoryPath: string): Promise<string[]> {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const images: string[] = [];

  for (const entry of entries) {
    const fullPath = path.join(directoryPath, entry.name);
    if (entry.isDirectory()) {
      images.push(...await collectImages(fullPath));
    } else if (entry.isFile() && isSupportedImage(fullPath)) {
      images.push(fullPath);
    }
  }

  return images.sort((a, b) => path.basename(a).localeCompare(path.basename(b), 'zh-CN', { numeric: true }));
}

ipcMain.handle('local-api:get', async (_event, endpoint: string) =>
  requestLocalApi(endpoint));

ipcMain.handle('local-api:post', async (_event, endpoint: string, body?: unknown) =>
  requestLocalApi(endpoint, {
    method: 'POST',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  }));

ipcMain.handle('local-api:manual-infer', async (_event, filePath: string) => {
  const bytes = await readFile(filePath);
  const blob = new Blob([bytes], { type: 'application/octet-stream' });
  const formData = new FormData();
  formData.append('image', blob, path.basename(filePath));
  return requestLocalApi('api/runtime/manual-infer', {
    method: 'POST',
    body: formData
  });
});

ipcMain.handle('dialog:select-image', async () => {
  const options = {
    title: '选择 X 光图片',
    properties: ['openFile'],
    filters: [
      { name: 'X 光图片', extensions: ['png', 'jpg', 'jpeg', 'bmp'] }
    ]
  } satisfies Electron.OpenDialogOptions;

  const result = mainWindow
    ? await dialog.showOpenDialog(mainWindow, options)
    : await dialog.showOpenDialog(options);

  return result.canceled ? null : result.filePaths[0];
});

ipcMain.handle('dialog:select-directory', async () => {
  const options = {
    title: '选择批量检测图片目录',
    properties: ['openDirectory']
  } satisfies Electron.OpenDialogOptions;

  const result = mainWindow
    ? await dialog.showOpenDialog(mainWindow, options)
    : await dialog.showOpenDialog(options);

  if (result.canceled || result.filePaths.length === 0) {
    return null;
  }

  const directoryPath = result.filePaths[0];
  return {
    directoryPath,
    imagePaths: await collectImages(directoryPath)
  };
});

ipcMain.handle('file:to-data-url', async (_event, filePath: string) => {
  const bytes = await readFile(filePath);
  return `data:${resolveMime(filePath)};base64,${bytes.toString('base64')}`;
});

ipcMain.handle('file:open-location', async (_event, filePath: string) => {
  await shell.openPath(path.dirname(filePath));
});

app.whenReady().then(async () => {
  await createWindow();

  app.on('activate', async () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      await createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
