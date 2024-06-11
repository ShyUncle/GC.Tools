using Elasticsearch.Net;
using ElasticSearchDemo.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ElasticSearchDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PhoneNumberController : ControllerBase
    {

        private readonly IElasticClientProvider _elasticClientProvider;
        private string indexName = "phonenumber";
        public PhoneNumberController(IElasticClientProvider elasticClientProvider)
        {
            _elasticClientProvider = elasticClientProvider;
        }
        private string GeneralPhone(Random random)
        {
            var secondNumber = new List<int>() { 3, 5, 6, 7, 8, 9 };
            return $"1{secondNumber[random.Next(0, 6)]}{random.NextInt64(100000000, 1000000000)}";
        }

        [HttpPost("init")]
        public async Task<string> Init(PhoneNumberInitModel model)
        {
            var timer = new Stopwatch();
            timer.Start();
            var client = _elasticClientProvider.GetElasticClient(indexName);
            await client.Indices.DeleteAsync(indexName);
            client.Indices.Create(indexName, x => x.Map<PhoneNumberModel>((m) => m.AutoMap().Properties(p => p.Text(x => x.Name(x => x.PhoneNumber)))));
            var list = new List<PhoneNumberModel>();
            Random _random = new Random(DateTime.Now.Millisecond);
            for (long i = 0; i < model.Count; i++)
            {
                list.Add(new PhoneNumberModel()
                {
                    Id = i + 1,
                    PhoneNumber = GeneralPhone(_random)
                });
                if (i > 0 && i % 1000 == 0)
                {
                    var res = await client.BulkAsync(b => b.Index(indexName).IndexMany(list));

                    list.Clear();
                }
            }
            if (list.Count > 0)
            {
                await client.BulkAsync(b => b.Index(indexName).IndexMany(list));
            }
            timer.Stop();
            return $"初始化成功,生成{model.Count}条数据总用时{timer.ElapsedMilliseconds / 1000}秒";
        }

        [HttpGet("regquery")]
        public async Task<object> RegQueryPage(string regStr = "", int page = 1, int pageSize = 20)
        {
            var client = _elasticClientProvider.GetElasticClient(indexName);
            var res = await client.SearchAsync<PhoneNumberModel>(d => d.From(page * pageSize - pageSize).Size(pageSize).Query(s => s.Regexp(q => q.Field(s => s.PhoneNumber).Value(regStr))).Highlight(d => d.Fields(ss => ss.Field("PhoneNumber").PreTags("<em>").PostTags("</em>"))));
            return new { res.Documents, res.Hits, res.HitsMetadata, res.Total };
        }

        [HttpGet("query")]
        public async Task<object> QueryPage(string regStr = "", int page = 1, int pageSize = 20)
        {
            var client = _elasticClientProvider.GetElasticClient(indexName);
            ScrollRequest searchRequest = new ScrollRequest(string.Empty,new Nest.Time(TimeSpan.FromSeconds(100)));
            ScrollRequestParameters scrollRequestParameters = new ScrollRequestParameters();
            scrollRequestParameters.QueryString.Add("size", pageSize);
            searchRequest.SourceQueryString = scrollRequestParameters.;
            var res = await client.ScrollAsync<PhoneNumberModel>(searchRequest);
            return new { res.Documents, res.Hits, res.HitsMetadata, res.Total };
        }
    }
}
