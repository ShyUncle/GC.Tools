using System.Text.Json;
public class ProductItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProductQueryResponse
{
    public int Total { get; set; }
    public List<ProductItem>? Items { get; set; }
}
public static class ProductClient
{
    private static readonly HttpClient _http = new();

    public static async Task<string> QueryProductsAsync(
        string? name,
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        CancellationToken cancellationToken = default)
    {
        var builder = new UriBuilder("https://localhost:7244/Product/query");

        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrWhiteSpace(name))
            query["name"] = name;

        if (!string.IsNullOrWhiteSpace(category))
            query["category"] = category;

        if (minPrice.HasValue)
            query["minprice"] = minPrice.Value.ToString();
        if (maxPrice.HasValue)
            query["maxPrice"] = maxPrice.Value.ToString();
        builder.Query = query.ToString();
        try
        {
            var json = await _http.GetStringAsync(builder.Uri, cancellationToken);

            // 如果接口返回的是复杂 JSON，建议反序列化后再精简返回
            var response = JsonSerializer.Deserialize<ProductQueryResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true, 
            });

            if (response?.Items == null || response.Items.Count == 0)
                return "未找到符合条件的商品。";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共找到 {response.Total} 个商品：");

            foreach (var item in response.Items)
            {
                sb.AppendLine(
                    $"- {item.Name} | 类别：{item.Category} | 价格：{item.Price:C}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"查询商品时发生错误：{ex.Message}";
        }
    }
}