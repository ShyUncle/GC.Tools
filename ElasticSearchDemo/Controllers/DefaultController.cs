using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nest;

namespace ElasticSearchDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DefaultController : ControllerBase
    {

        private static ElasticClient client;
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<object> Get()
        {

            var node = new Uri("http://172.24.91.110:9200");
            var settings = new ConnectionSettings(node).DefaultIndex("goodsindex");
              client =client?? new ElasticClient(settings);
            List<string> ids = new List<string>();
            //for (var i = 0; i < 10; i++)
            //{
            //    var goods = new Goods()
            //    {
            //        CreateDate = DateTime.Now,
            //        Id = Guid.NewGuid().ToString("N"),
            //        Name = "商品" + i,
            //        Price = i,
            //        Tags = i.ToString()
            //    };
            //    ids.Add(goods.Id);
            //    var response = await client.IndexDocumentAsync(goods);
            //    if(response.Result != Result.Created){
                
            //    }
            //}
            var response1 = client.Get<Goods>("b395fd06c7e94a07bace0cb2dfd42eaa"); // returns an IGetResponse mapped 1-to-1 with the Elasticsearch JSON response
            var tweet = response1.Source; // the original document
            var list = await client.SearchAsync<Goods>(s => s.From(0).Size(5)
            .Query(q=>q.Term(t=>t.Name,"商品1")
            ||q.Match(mq=>mq.Field(f=>f.Tags).Query("8"))
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