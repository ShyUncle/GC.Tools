using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nest;

namespace ElasticSearchDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DefaultController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public DefaultController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }
        private static ElasticClient client;
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<object> Get()
        {

            // var node = new Uri("http://172.24.91.110:9200");

            var node = new Uri("http://192.168.174.130:9200");
            var settings = new ConnectionSettings(node).DefaultIndex("goodsindex");
            settings.EnableHttpCompression(true);
            client = client ?? new ElasticClient(settings);
            var config = new ConnectionConfiguration(node);
            config.EnableHttpCompression(true);
            var lowclient = new Elasticsearch.Net.ElasticLowLevelClient(config);
            
            var http = _httpClientFactory.CreateClient("nest"); 
            
            http.BaseAddress = new Uri("http://192.168.174.130:9200");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.23.0");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Host", "192.168.174.130:9200");
            var ss = await http.GetAsync("/goodsindex/_doc/dcddadf503c24266a050f8e09771a112");
            var contentType = ss.Content.Headers.ContentType;
            if (string.IsNullOrEmpty(contentType.CharSet))
            {
                contentType.CharSet = "utf-8";
            }
            var sssd =await ss.Content.ReadAsStringAsync();
            var a = lowclient.Get<SearchResponse<Goods>>("goodsindex", "dcddadf503c24266a050f8e09771a112");
            var respon = client.Get<Goods>("dcddadf503c24266a050f8e09771a112").Source;
            var resp = client.LowLevel.Get<SearchResponse<Goods>>("goodsindex", "dcddadf503c24266a050f8e09771a112").Documents;
            resp = (await client.LowLevel.GetAsync<SearchResponse<Goods>>("goodsindex", "dcddadf503c24266a050f8e09771a112")).Documents;
            List<string> ids = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var goods = new Goods()
                {
                    CreateDate = DateTime.Now,
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "商品" + i,
                    Price = i,
                    Tags = i.ToString()
                };
                ids.Add(goods.Id);
                var response = client.IndexDocument(goods);
                if (response.Result != Result.Created)
                {

                }
            }
            var response1 = client.Get<Goods>("b395fd06c7e94a07bace0cb2dfd42eaa"); // returns an IGetResponse mapped 1-to-1 with the Elasticsearch JSON response
            var tweet = response1.Source; // the original document
            var list = await client.SearchAsync<Goods>(s => s.From(0).Size(5)
            .Query(q => q.Term(t => t.Name, "商品1")
            || q.Match(mq => mq.Field(f => f.Tags).Query("8"))
            || q.Match(mq => mq.Field(f => f.Tags).Query("6"))));
            if (list.Documents.Count > 0)
            {

            }
            return list.Documents;
        }
    }

    public class Goods
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime CreateDate { get; set; }
        public decimal Price { get; set; }
        public string Tags { get; set; }
    }
}