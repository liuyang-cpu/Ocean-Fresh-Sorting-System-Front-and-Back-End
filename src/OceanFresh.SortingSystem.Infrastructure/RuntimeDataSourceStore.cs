using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class InMemoryRuntimeDataSourceStore : IRuntimeDataSourceStore
{
    private readonly object _syncRoot = new();
    private RuntimeDataSourceConfig _config = RuntimeDataSourceConfig.Default;
    private bool _isExternalLocalStreamActive;

    public bool IsExternalLocalStreamActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _isExternalLocalStreamActive;
            }
        }
    }

    public RuntimeDataSourceConfig Get()
    {
        lock (_syncRoot)
        {
            return _config;
        }
    }

    public void Update(RuntimeDataSourceConfig config)
    {
        lock (_syncRoot)
        {
            _config = config;
        }
    }

    public void ResetRunProgress()
    {
        lock (_syncRoot)
        {
            _config = _config with
            {
                CurrentIndex = 0,
                Status = _config.Mode == RuntimeDataSourceMode.LocalImageDirectory
                    ? "本地图片目录已准备，等待机器启动"
                    : "X 光相机"
            };
        }
    }

    public void UpdateProgress(int currentIndex, string status)
    {
        lock (_syncRoot)
        {
            _config = _config with
            {
                CurrentIndex = Math.Clamp(currentIndex, 0, Math.Max(_config.ImageCount, currentIndex)),
                Status = status
            };
        }
    }

    public void SetExternalLocalStreamActive(bool isActive)
    {
        lock (_syncRoot)
        {
            _isExternalLocalStreamActive = isActive;
        }
    }
}
