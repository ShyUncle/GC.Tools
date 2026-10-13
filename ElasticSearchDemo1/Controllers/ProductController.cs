using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Nodes;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.AspNetCore.Mvc;

namespace ElasticSearchDemo1.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly ElasticsearchClient _elasticsearchClient;
        private const string IndexName = "products";
        public ProductController(ElasticsearchClient elasticsearchClient)
        {
            _elasticsearchClient = elasticsearchClient;
        }
        private async Task CreateChineseIndexAsync()
        {

            var response = await _elasticsearchClient.Indices.CreateAsync<Product>(IndexName, c => c
                .Settings(s => s.RefreshInterval(-1) // 在导入数据时，关闭刷新间隔以提高性能
                    .Analysis(a => a
                        // 定义自定义分析器
                        .Analyzers(an => an
                            .Custom("chinese_index", ca => ca
                                .Tokenizer("ik_max_word") // 索引时使用细粒度分词
                            )
                            .Custom("chinese_search", ca => ca
                                .Tokenizer("ik_smart")    // 搜索时使用智能分词
                            )
                        )
                    )
                )
                .Mappings(m => m
                    .Properties(p => p
                        .Text(t => t.Name, n => n.Analyzer("chinese_index")      // 指定索引分析器
                            .SearchAnalyzer("chinese_search")
                    ).Keyword(k => k.Category, s => s.IgnoreAbove(256))
                    )
                )
            );

            if (!response.IsValidResponse)
            {
                throw new Exception($"创建中文索引失败: {response.DebugInformation}");
            }
        }


        // 线程安全的随机数实例 (.NET 6+ 推荐 Random.Shared，旧版本用 static readonly)
        private static readonly Random _random = new Random();

        // 1. 中文品牌库
        private static readonly string[] Brands = {
        "华为", "小米", "苹果", "索尼", "三星", "联想", "戴尔", "惠普", "耐克", "阿迪达斯",
        "海尔", "美的", "格力", "九阳", "苏泊尔", "飞利浦", "松下", "西门子", "博世", "戴森"
    };

        // 2. 中文分类库
        private static readonly string[] Categories = {
        "智能手机", "笔记本电脑", "平板电脑", "智能手表", "无线耳机",
        "机械键盘", "游戏鼠标", "显示器", "空气净化器", "扫地机器人",
        "运动鞋", "休闲裤", "羽绒服", "蓝牙耳机", "充电宝"
    };

        // 3. 形容词库 (用于修饰产品)
        private static readonly string[] Adjectives = {
        "超薄", "静音", "高清", "智能", "便携", "专业", "家用", "商用",
        "旗舰", "入门", "豪华", "简约", "复古", "时尚", "高性能", "低功耗",
        "防水", "防摔", "快充", "长续航", "无线", "蓝牙", "触控", "语音"
    };

        // 4. 核心名词库 (与分类对应或通用)
        private static readonly string[] Nouns = {
        "手机", "笔记本", "平板", "手表", "耳机", "键盘", "鼠标", "显示器",
        "净化器", "机器人", "跑鞋", "长裤", "外套", "音箱", "电源"
    };

        // 5. 后缀/型号库
        private static readonly string[] Suffixes = {
        "Pro", "Max", "Plus", "Mini", "Lite", "Ultra", "SE", "Air",
        "2024款", "升级版", "标准版", "尊享版", "青春版", "电竞版"
    };

        /// <summary>
        /// 生成指定数量的中文产品数据
        /// </summary>
        public List<Product> GenerateChineseProducts(int count)
        {
            var products = new List<Product>(count);

            for (int i = 0; i < count; i++)
            {
                // 随机选择组件
                string brand = Brands[_random.Next(Brands.Length)];
                string category = Categories[_random.Next(Categories.Length)];
                string adj1 = Adjectives[_random.Next(Adjectives.Length)];
                string adj2 = Adjectives[_random.Next(Adjectives.Length)];
                string noun = Nouns[_random.Next(Nouns.Length)];
                string suffix = Suffixes[_random.Next(Suffixes.Length)];

                // 组合名称：品牌 + 形容词 + 名词 + 后缀 (例如：华为超薄静音手机Pro)
                // 为了避免过于冗长，有时只加一个形容词
                string name = _random.Next(2) == 0
                    ? $"{brand}{adj1}{noun}{suffix}"
                    : $"{brand}{adj1}{adj2}{noun}{suffix}";

                // 生成随机价格 (10.00 - 9999.00)
                decimal price = Convert.ToDecimal(Math.Round(_random.NextDouble() * 9989 + 10, 2));

                // 生成随机库存 (0 - 1000)
                int stock = _random.Next(0, 1001);

                products.Add(new Product
                {
                    Id = Guid.NewGuid().ToString(), // ES 文档 ID
                    Name = name,
                    Category = category,

                    Price = price,

                    CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 365))
                });
            }

            return products;
        }


        [HttpGet]
        public async Task<IEnumerable<Product>> GetProducts()
        {
            var list = await _elasticsearchClient.SearchAsync<Product>(s => s
                  .Indices(IndexName).From(0).Size(100)
                  .Query(q => q
                      .MatchAll()
                  )
              );
            if (list.IsValidResponse)
            {
                return list.Documents;
            }
            return new List<Product>();
        }

        [HttpGet]
        [Route("init")]
        public async Task InitAsync()
        {
            // 1. 创建索引并定义映射
            var existsResponse = await _elasticsearchClient.Indices.ExistsAsync(IndexName);
            if (!existsResponse.Exists)
            {
                await CreateChineseIndexAsync();
            }
            else
            {
                return;
            }

            //批量创建索引
            var list = GenerateChineseProducts(10000);
            var req = new BulkRequest(IndexName)
            {
                Operations = new Elastic.Clients.Elasticsearch.Core.Bulk.BulkOperationsCollection()
            };
            foreach (var product in list)
            {
                req.Operations.Add(new Elastic.Clients.Elasticsearch.Core.Bulk.BulkIndexOperation<Product>(product));
            }
            await _elasticsearchClient.BulkAsync(req);
            // 3. 导入完成后，恢复刷新间隔
            await _elasticsearchClient.Indices.PutSettingsAsync(IndexName, s => s
                .Settings(i => i.RefreshInterval("1s")));

            var response = await _elasticsearchClient.IndexAsync(new Product
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now,
                Name = "Iphone 14 Pro Max",
                Price = 10999,
                Category = "手机"

            }, idx => idx.Index(IndexName));
            if (!response.IsValidResponse)
            {
                throw new Exception($"Indexing failed: {response.DebugInformation}");
            }
        }

        [HttpGet]
        [Route("Query")]
        public async Task<object> SearchProductsAsync([FromQuery] ProductRequest request)
        {
            //根据查询条件拼接搜索语句，使用Match查询，支持中文分词 
            var mustQueries = new List<Query>();
            var filterQueries = new List<Query>();
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                mustQueries.Add(new MatchQuery()
                {
                    Field = Infer.Field<Product>(p => p.Name),
                    Query = request.Name
                });
            }

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                filterQueries.Add(new TermQuery()
                {
                    Field = Infer.Field<Product>(p => p.Category),
                    Value = request.Category,
                });

            }
            if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
            {
                filterQueries.Add(
                     new NumberRangeQuery()
                     {
                         Field = Infer.Field<Product>(p => p.Price),
                         Gte = request.MinPrice.HasValue
                             ? (double?)request.MinPrice.Value
                             : null,
                         Lte = request.MaxPrice.HasValue
                             ? (double?)request.MaxPrice.Value
                             : null
                     }
                 );
            }
            if (request.CreatedAt.HasValue)
            {
                filterQueries.Add(
                    new DateRangeQuery()
                    {
                        Field = Infer.Field<Product>(p => p.CreatedAt),
                        Lte = request.CreatedAt.Value
                    }
                );

            }
            var sortOptions = new List<SortOptions>();
            if (!string.IsNullOrWhiteSpace(request.SortBy))
            {
                switch (request.SortBy.ToLower())
                {
                    case "price":
                        sortOptions.Add(new SortOptions
                        {
                            Field = new FieldSort
                            {
                                Field = Infer.Field<Product>(p => p.Price),
                                Order = request.SortOrder == "asc"
                                        ? SortOrder.Asc
                                        : SortOrder.Desc
                            }
                        });
                        break;

                    case "createdat":
                        sortOptions.Add(new SortOptions
                        {
                            Field = new FieldSort
                            {
                                Field = Infer.Field<Product>(p => p.CreatedAt),
                                Order = request.SortOrder == "asc"
                                       ? SortOrder.Asc
                                       : SortOrder.Desc
                            }
                        });
                        break;

                    default:
                        break;
                }
            }
            else
            {
                sortOptions.Add(new SortOptions
                {
                    Score = new ScoreSort { Order = SortOrder.Desc }
                });
            }
            var response = await _elasticsearchClient.SearchAsync<Product>(s => s
                .Indices(IndexName)
                .From(0)
                .Size(10)
                .Query(q => q
                    .Bool(b => b.Must(mustQueries).Filter(filterQueries))
                ).Sort(sortOptions)
             );

            if (!response.IsValidResponse)
            {
                return new List<Product>();
            }
            return new { items = response.Documents.ToList(), total = response.Total };
        }
        [HttpDelete("{id}")]
        public async Task DeleteProductAsync(string id)
        {
            var response = await _elasticsearchClient.DeleteAsync(IndexName, id);
            if (!response.IsValidResponse)
            {
                throw new Exception($"Deletion failed: {response.DebugInformation}");
            }
        }

        [HttpGet("id")]
        public async Task<Product?> GetProductAsync(string id)
        {
            var response = await _elasticsearchClient.GetAsync<Product>(IndexName, id);
            if (response.IsValidResponse && response.Source != null)
            {
                return response.Source;
            }

            return null;
        }

        [HttpPut("{id}")]
        public async Task UpdateProductAsync(string id, [FromBody] Product product)
        {
            var response = await _elasticsearchClient.UpdateAsync<Product, object>(IndexName, id, u => u.Doc(new
            {
                product.Price,
                product.Name,
                product.Category,
            }));
            if (!response.IsValidResponse)
            {
                throw new Exception($"Update failed: {response.DebugInformation}");
            }
        }
    }
}
