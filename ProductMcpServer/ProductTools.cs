using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ProductMcpServer;

[McpServerToolType]
public static class ProductTools
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(100) };

    [McpServerTool(Name = "search_products")]
    [Description("查询【天机阁】商品信息。当用户想找天机阁的商品、按名称/类别/价格筛选商品时调用。返回商品列表。")]
    public static async Task<string> QueryProducts(
        [Description("商品名称，例如：手机、耳机,不存在时传null或空字符串")] string? name,
        [Description("商品类别，例如：无线耳机、笔记本电脑,不存在时传null或空字符串")] string? category,
        [Description("最低价格，单位元,不存在时传null或空字符串")] decimal? minPrice)
    {
        try
        {
            var builder = new UriBuilder("https://localhost:7244/Product/query");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);

            if (!string.IsNullOrWhiteSpace(name))
                query["name"] = name;
            if (!string.IsNullOrWhiteSpace(category))
                query["category"] = category;
            if (minPrice.HasValue)
                query["minprice"] = minPrice.Value.ToString();

            builder.Query = query.ToString();

            var json = await _http.GetStringAsync(builder.Uri);

            // 尝试解析并精简返回
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sb = new System.Text.StringBuilder();

            if (root.TryGetProperty("total", out var total))
                sb.AppendLine($"共找到 {total.GetInt32()} 个商品：");

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var itemName = item.GetProperty("name").GetString() ?? "未知商品";
                    var category11 = item.TryGetProperty("category", out var c) ? c.GetString() : "未知类别";
                    var price = item.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                    sb.AppendLine($"- {itemName} | 类别：{category11} | 价格：{price:C}");
                }
            }

            return sb.Length > 0 ? sb.ToString() : "未找到符合条件的商品。";
        }
        catch (Exception ex)
        {
            return $"查询商品失败：{ex.Message}";
        }
    }
}