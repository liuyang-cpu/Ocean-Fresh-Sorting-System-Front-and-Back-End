namespace OceanFresh.SortingSystem.HMI;

public static class ChannelRuntimeSelectionService
{
    public static Guid? ActiveChannelId { get; private set; }

    public static string ActiveChannelName { get; private set; } = "未启用";

    public static void EnsureDefault(Guid channelId, string channelName)
    {
        if (ActiveChannelId is not null)
        {
            return;
        }

        ActiveChannelId = channelId;
        ActiveChannelName = channelName;
    }

    public static void SetActive(Guid channelId, string channelName)
    {
        ActiveChannelId = channelId;
        ActiveChannelName = channelName;
    }

    public static void DisableIfActive(Guid channelId)
    {
        if (ActiveChannelId != channelId)
        {
            return;
        }

        ActiveChannelId = null;
        ActiveChannelName = "未启用";
    }

    public static void ResetForTests()
    {
        ActiveChannelId = null;
        ActiveChannelName = "未启用";
    }
}
