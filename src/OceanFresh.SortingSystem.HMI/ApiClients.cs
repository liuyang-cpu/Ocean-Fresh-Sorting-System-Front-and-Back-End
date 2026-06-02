using System.Net.Http;
using System.Net.Http.Json;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.HMI;

internal sealed class OceanFreshLocalApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5188/")
    };

    public async Task<IReadOnlyList<ProductRecipe>> GetRecipesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/recipes", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<ProductRecipe>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<ProductRecipe> SaveRecipeAsync(UpsertRecipeRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/recipes", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var recipe = await response.Content.ReadFromJsonAsync<ProductRecipe>(cancellationToken: cancellationToken);
        if (recipe is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的产品配方。");
        }

        return recipe;
    }

    public async Task<IReadOnlyList<SeafoodProductProfileDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/products", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<SeafoodProductProfileDto>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<SeafoodProductProfileDto> SaveProductAsync(UpsertSeafoodProductRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/products", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var product = await response.Content.ReadFromJsonAsync<SeafoodProductProfileDto>(cancellationToken: cancellationToken);
        if (product is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的海鲜产品。");
        }

        return product;
    }

    public async Task DeleteProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/products/{productId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelConfigDetailDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/channels", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<ChannelConfigDetailDto>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<ChannelConfig> SaveChannelAsync(UpsertChannelConfigRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/channels", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var channel = await response.Content.ReadFromJsonAsync<ChannelConfig>(cancellationToken: cancellationToken);
        if (channel is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的通道配置。");
        }

        return channel;
    }

    public async Task DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/channels/{channelId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelVersion>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/models", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<ModelVersion>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    private static async Task EnsureSuccessWithMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"本地 API 请求失败: {(int)response.StatusCode} {response.ReasonPhrase}"
            : body.Trim().Trim('"');
        throw new InvalidOperationException(message);
    }
}
