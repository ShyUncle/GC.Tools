using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIConsoleApp
{
    /// <summary>
    /// 阿里云百炼「知识检索（Knowledge Search）」请求体，此处用于检索财会课程资料库。
    /// POST https://{workspaceId}.cn-beijing.maas.aliyuncs.com/api/v1/indices/knowledge/search
    /// </summary>
    internal class CourseSearchRequest
    {
        [JsonPropertyName("agent_id")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("kb_search_configs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CourseSearchKbConfig>? KbSearchConfigs { get; set; }
    }

    /// <summary>
    /// 课程库过滤配置，id 为知识库 ID（pipeline_id），必须属于 agent 已绑定的知识库。
    /// </summary>
    internal class CourseSearchKbConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("search_filters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Dictionary<string, object?>>? SearchFilters { get; set; }
    }

    /// <summary>
    /// 统一响应结构，以 success 字段作为主判定。
    /// </summary>
    internal class CourseSearchResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("data")]
        public CourseSearchData? Data { get; set; }
    }

    internal class CourseSearchData
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("nodes")]
        public List<CourseSearchNode>? Nodes { get; set; }

        [JsonPropertyName("cost_time")]
        public int CostTime { get; set; }
    }

    /// <summary>
    /// 检索结果节点。metadata 用字典承载，保留课程库返回的全部原始字段。
    /// </summary>
    internal class CourseSearchNode
    {
        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, JsonElement>? Metadata { get; set; }
    }

    /// <summary>
    /// 面向「推荐课程」整形后的返回结构，字段抽不出时留空，原始节点保留在 raw / metadata 中兜底。
    /// </summary>
    internal class CourseSearchResult
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("cost_time")]
        public int CostTime { get; set; }

        [JsonPropertyName("courses")]
        public List<CourseItem> Courses { get; set; } = [];

        /// <summary>
        /// 模拟数据标识，仅 mock 模式输出；live 模式不序列化该字段，保持返回结构不变。
        /// </summary>
        [JsonPropertyName("is_mock")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsMock { get; set; }
    }

    internal class CourseItem
    {
        [JsonPropertyName("course_name")]
        public string? CourseName { get; set; }

        [JsonPropertyName("course_intro")]
        public string? CourseIntro { get; set; }

        [JsonPropertyName("chapter")]
        public string? Chapter { get; set; }

        [JsonPropertyName("knowledge_points")]
        public List<string>? KnowledgePoints { get; set; }

        [JsonPropertyName("video_url")]
        public string? VideoUrl { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("matched_text")]
        public string? MatchedText { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, JsonElement>? Metadata { get; set; }

        [JsonPropertyName("raw")]
        public CourseSearchNode? Raw { get; set; }
    }

    /// <summary>
    /// 财会课程资料库检索工具。
    /// 模式开关（每次调用实时读取，切换无需改代码、无需重新编译）：
    /// 1. 当前目录存在 course-search-mock.flag 标志文件 → 强制 mock；
    /// 2. 否则读环境变量 course-search-mode：live 走真实接口，其余（含未设置）→ mock（默认）。
    /// mock 模式不发任何网络请求，也不需要 api-key 等配置。
    /// </summary>
    public static class CourseSearchTools
    {
        private const string ApiKeyEnv = "api-key";
        private const string WorkspaceIdEnv = "workspace-id";
        private const string AgentIdEnv = "course-agent-id";
        private const string KbIdEnv = "course-kb-id";
        private const string EndpointEnv = "course-search-endpoint";
        private const string ModeEnv = "course-search-mode";
        private const string MockFlagFileName = "course-search-mock.flag";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// 当前生效的模式名称（mock / live），供启动日志展示。
        /// </summary>
        public static string ModeName => IsMockMode() ? "mock" : "live";

        private static bool IsMockMode()
        {
            // 优先级：标志文件（存在即 mock） > 环境变量 course-search-mode > 默认 mock
            if (File.Exists(MockFlagPath)) return true;
            var mode = Environment.GetEnvironmentVariable(ModeEnv)?.Trim();
            if (string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string MockFlagPath => Path.Combine(Environment.CurrentDirectory, MockFlagFileName);

        /// <summary>
        /// 在财会课程资料库中检索课程简介、章节、视频地址等课程资料，用于向学员推荐课程。
        /// </summary>
        [Description("查询会计教练的财会课程（课程简介、章节、视频地址等课程资料）。当学员的提问或正在学的知识点可能与某门财会课程相关时调用，用检索到的课程做推荐；它不用于查通用财会知识。")]
        public static async Task<string> SearchCourseMaterials(
            [Description("学员的问题或想学的知识点，例如：固定资产折旧年限怎么规定")] string query,
            [Description("可选：限定课程所属的知识库 ID（pipeline_id）。不传则取环境变量 course-kb-id；两者都没有时按服务已绑定的全部课程库检索")] string? kbId,

            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "课程检索参数错误：query 不能为空。";

            // 每次调用实时解析模式，进程内即可用外部开关切换 mock / live
            var mock = IsMockMode();
            Console.WriteLine($"[课程检索] mode={(mock ? "mock" : "live")}, query={query.ToString()}");
            if (mock)
                return SearchMockCourses(query, "");

            var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnv);
            var agentId = Environment.GetEnvironmentVariable(AgentIdEnv);
            var endpoint = Environment.GetEnvironmentVariable(EndpointEnv);
            var workspaceId = Environment.GetEnvironmentVariable(WorkspaceIdEnv);

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(apiKey)) missing.Add(ApiKeyEnv);
            if (string.IsNullOrWhiteSpace(agentId)) missing.Add(AgentIdEnv);
            // 未显式指定完整 endpoint 时，需要用业务空间 ID 拼出默认地址
            if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(workspaceId)) missing.Add(WorkspaceIdEnv);
            if (missing.Count > 0)
                return $"课程检索配置缺失：环境变量 {string.Join("、", missing)} 未设置。"
                     + $"{ApiKeyEnv}=百炼 API Key，{WorkspaceIdEnv}=业务空间 ID，{AgentIdEnv}=已发布的课程知识检索服务 ID（aid-xxx）。";

            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = $"https://{workspaceId}.cn-beijing.maas.aliyuncs.com/api/v1/indices/knowledge/search";

            var kb = !string.IsNullOrWhiteSpace(kbId) ? kbId : Environment.GetEnvironmentVariable(KbIdEnv);
            var payload = new CourseSearchRequest
            {
                AgentId = agentId!,
                Query = query,
            };
            if (!string.IsNullOrWhiteSpace(kb))
            {
                var config = new CourseSearchKbConfig { Id = kb };
                //var categoryList = courseCategory?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                //if (categoryList is { Length: > 0 })
                //{
                //    // 非结构化课程库的 search_filters 仅支持 tags / meta 两种 key
                //    config.SearchFilters = [new Dictionary<string, object?> { ["tags"] = categoryList }];
                //}
                payload.KbSearchConfigs = [config];
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return $"课程检索已触发限流（默认每 10 秒最多 10 次请求），请稍后重试。{Truncate(body)}";

                var result = TryParse(body);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return $"课程检索鉴权失败：API Key 无效或缺失，请检查环境变量 {ApiKeyEnv}。{Describe(result, body)}";
                    return $"课程检索失败：HTTP {(int)response.StatusCode}。{Describe(result, body)}";
                }

                // 文档要求始终以 success 字段判定，而非仅依赖 HTTP 状态码
                if (result is null)
                    return $"课程检索返回结构异常，无法解析响应内容。{Truncate(body)}";
                if (!result.Success)
                {
                    var hint = result.Code == "InvalidApiKey" ? $"请检查环境变量 {ApiKeyEnv}。" : string.Empty;
                    return $"课程检索失败：{result.Code}（status_code={result.StatusCode}）{result.Message}。{hint}request_id={result.RequestId}";
                }
                if (result.Data?.Nodes is not { Count: > 0 })
                    return $"未在课程库中找到与该问题匹配的课程（total={result.Data?.Total ?? 0}，cost_time={result.Data?.CostTime ?? 0}ms）。request_id={result.RequestId}";

                var shaped = new CourseSearchResult
                {
                    RequestId = result.RequestId,
                    Total = result.Data.Total,
                    CostTime = result.Data.CostTime,
                    Courses = result.Data.Nodes.Select(ToCourseItem).ToList(),
                };
                return JsonSerializer.Serialize(shaped, _jsonOptions);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return $"课程检索请求超时（{_http.Timeout.TotalSeconds:F0} 秒），请稍后重试。";
            }
            catch (HttpRequestException ex)
            {
                return $"课程检索请求失败：{ex.Message}";
            }
            catch (JsonException ex)
            {
                return $"课程检索返回结构异常：{ex.Message}";
            }
        }

        private sealed class MockCourse
        {
            public required string Id { get; init; }
            public required string Name { get; init; }
            public required string Intro { get; init; }
            public required string Chapter { get; init; }
            public required string Category { get; init; }
            public required string[] KnowledgePoints { get; init; }
            public required string[] Keywords { get; init; }
            public required string VideoUrl { get; init; }
        }

        /// <summary>
        /// 内置模拟课程数据（初级会计、财务管理、税法、成本会计），视频地址使用 mock:// 占位，不指向真实资源。
        /// </summary>
        private static readonly MockCourse[] MockCourses =
        [
            new()
            {
                Id = "course-101",
                Name = "初级会计实务精讲",
                Intro = "模拟数据：面向零基础学员的初级会计实务课程，讲练结合。",
                Chapter = "第二章 资产",
                Category = "初级会计",
                KnowledgePoints = ["固定资产", "折旧", "存货", "应交税费"],
                Keywords = ["会计", "分录", "资产", "折旧", "固定资产", "存货", "记账", "报表"],
                VideoUrl = "mock://course-video/course-101",
            },
            new()
            {
                Id = "course-201",
                Name = "财务管理基础",
                Intro = "模拟数据：讲解资金时间价值、投资决策与财务分析框架。",
                Chapter = "第四章 投资管理",
                Category = "财务管理",
                KnowledgePoints = ["资金时间价值", "净现值", "资本成本"],
                Keywords = ["财务管理", "折现", "净现值", "资本成本", "杠杆", "财务分析", "投资"],
                VideoUrl = "mock://course-video/course-201",
            },
            new()
            {
                Id = "course-301",
                Name = "税法实务",
                Intro = "模拟数据：按税种梳理增值税、企业所得税的计算与申报要点。",
                Chapter = "第三章 增值税",
                Category = "税法",
                KnowledgePoints = ["增值税", "企业所得税", "税率", "进项抵扣"],
                Keywords = ["税", "增值税", "所得税", "税率", "抵扣", "发票", "纳税"],
                VideoUrl = "mock://course-video/course-301",
            },
            new()
            {
                Id = "course-401",
                Name = "成本会计实务",
                Intro = "模拟数据：成本核算方法、费用归集与分配的系统讲解。",
                Chapter = "第五章 产品成本核算",
                Category = "成本会计",
                KnowledgePoints = ["制造费用", "品种法", "分批法", "成本结转"],
                Keywords = ["成本", "核算", "制造费用", "分配", "品种法", "分批法", "结转"],
                VideoUrl = "mock://course-video/course-401",
            },
        ];

        /// <summary>
        /// 模拟检索：完全不发起网络请求。按 query 关键词做简单相关性匹配，可用 courseCategory 限定分类；
        /// 无命中时返回 courses 为空的结构化结果，便于验证「不相关就不推荐」。
        /// </summary>
        private static string SearchMockCourses(string query, string? courseCategory)
        {
            var categories = courseCategory?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var normalizedQuery = query.Trim().ToLowerInvariant();
            var matched = new List<CourseItem>();

            foreach (var course in MockCourses)
            {
                //if (categories is { Length: > 0 }
                //    && !categories.Any(c => course.Category.Contains(c, StringComparison.OrdinalIgnoreCase)))
                //    continue;

                var hits = course.Keywords.Count(k => normalizedQuery.Contains(k.ToLowerInvariant(), StringComparison.Ordinal));
                //if (hits == 0) continue;

                var score = Math.Min(0.98, 0.55 + 0.15 * hits);
                matched.Add(BuildMockItem(course, score));
            }

            matched.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (matched.Count > 5)
                matched = matched.Take(5).ToList();

            var result = new CourseSearchResult
            {
                RequestId = "mock-" + Guid.NewGuid().ToString("N")[..8],
                Total = matched.Count,
                CostTime = 0,
                Courses = matched,
                IsMock = true,
            };
            Console.WriteLine($"[课程检索] mock 命中 {matched.Count} 门课程");
            return JsonSerializer.Serialize(result, _jsonOptions);
        }

        private static CourseItem BuildMockItem(MockCourse course, double score)
        {
            var metadata = new Dictionary<string, JsonElement>
            {
                ["course_name"] = JsonSerializer.SerializeToElement(course.Name),
                ["course_intro"] = JsonSerializer.SerializeToElement(course.Intro),
                ["chapter"] = JsonSerializer.SerializeToElement(course.Chapter),
                ["knowledge_points"] = JsonSerializer.SerializeToElement(course.KnowledgePoints),
                ["video_url"] = JsonSerializer.SerializeToElement(course.VideoUrl),
                ["doc_id"] = JsonSerializer.SerializeToElement(course.Id),
                ["doc_name"] = JsonSerializer.SerializeToElement($"{course.Id}.doc"),
                ["title"] = JsonSerializer.SerializeToElement(course.Chapter),
                ["content"] = JsonSerializer.SerializeToElement(course.Intro),
                ["pipeline_id"] = JsonSerializer.SerializeToElement("mock-course-kb"),
                ["_knowledge_type"] = JsonSerializer.SerializeToElement("document"),
            };
            var text = $"课程名称: {course.Name}\n章节: {course.Chapter}\n知识点: {string.Join('、', course.KnowledgePoints)}\n课程简介: {course.Intro}";
            var node = new CourseSearchNode { Score = score, Text = text, Metadata = metadata };

            return new CourseItem
            {
                CourseName = course.Name,
                CourseIntro = course.Intro,
                Chapter = course.Chapter,
                KnowledgePoints = course.KnowledgePoints.ToList(),
                VideoUrl = course.VideoUrl,
                Score = score,
                MatchedText = text,
                Metadata = metadata,
                Raw = node,
            };
        }

        /// <summary>
        /// 将检索节点整形为课程推荐条目，字段抽不出时留空，不臆造内容。
        /// </summary>
        private static CourseItem ToCourseItem(CourseSearchNode node)
        {
            var md = node.Metadata;
            return new CourseItem
            {
                CourseName = FirstString(md, "course_name", "course_title", "course", "name", "doc_name", "title"),
                CourseIntro = FirstString(md, "course_intro", "course_description", "course_desc", "intro", "description", "summary"),
                Chapter = FirstString(md, "chapter_name", "chapter", "hier_title", "section"),
                KnowledgePoints = StringList(md, "knowledge_points", "knowledge_point", "points", "keywords", "tags"),
                VideoUrl = FirstString(md, "video_url", "video_urls", "course_video_url", "video_src", "play_url", "video"),
                Score = node.Score,
                MatchedText = node.Text,
                Metadata = md,
                Raw = node,
            };
        }

        private static string? FirstString(Dictionary<string, JsonElement>? metadata, params string[] keys)
        {
            if (metadata is null) return null;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var value)) continue;
                var text = AsString(value);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return null;
        }

        private static List<string>? StringList(Dictionary<string, JsonElement>? metadata, params string[] keys)
        {
            if (metadata is null) return null;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var value)) continue;
                var items = value.ValueKind switch
                {
                    JsonValueKind.Array => value.EnumerateArray()
                        .Select(AsString)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .ToList(),
                    JsonValueKind.String => value.GetString()!
                        .Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                    _ => new List<string>(),
                };
                if (items.Count > 0) return items;
            }
            return null;
        }

        private static string? AsString(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.EnumerateArray().Select(AsString).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null,
        };

        private static CourseSearchResponse? TryParse(string body)
        {
            try
            {
                return JsonSerializer.Deserialize<CourseSearchResponse>(body, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Describe(CourseSearchResponse? result, string body)
        {
            if (result is null)
                return $"返回内容无法解析。{Truncate(body)}";

            var hint = result.Code == "InvalidApiKey" ? $"请检查环境变量 {ApiKeyEnv}。" : string.Empty;
            return $"{result.Code}：{result.Message}。{hint}request_id={result.RequestId}";
        }

        private static string Truncate(string text, int maxLength = 300)
            => text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
