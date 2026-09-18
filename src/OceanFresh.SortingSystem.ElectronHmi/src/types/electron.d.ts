export {};

declare global {
  interface Window {
    oceanFresh: {
      get<T>(endpoint: string): Promise<T>;
      post<T>(endpoint: string, body?: unknown): Promise<T>;
      manualInfer<T>(filePath: string): Promise<T>;
      selectImage(): Promise<string | null>;
      selectDirectory(): Promise<{ directoryPath: string; imagePaths: string[] } | null>;
      fileToDataUrl(filePath: string): Promise<string>;
      openFileLocation(filePath: string): Promise<void>;
    };
  }
}
