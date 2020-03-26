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
            var node = new Uri("http://172.24.91.110:9200");

            // var node = new Uri("http://192.168.174.130:9200");
            var settings = new ConnectionSettings(node).DefaultIndex("goodsindex");
            settings.EnableHttpCompression(true);

            client = new ElasticClient(settings);
          
            // var node = new Uri("http://192.168.174.130:9200");
            var settings1 = new ConnectionSettings(node).DefaultIndex("person");
            settings1.EnableHttpCompression(true);

            clientPerson = new ElasticClient(settings1);

        }
        private ElasticClient client;

        private ElasticClient clientPerson;
        [HttpGet]
        /// <summary>
        /// 初始化数据
        /// </summary>
        /// <returns></returns>
        public async Task Get()
        {
            var list = new List<Person>() {
            new Person(){
             FirstName="张",
              LastName="三",
               Id=1,
            },
            new Person(){
             FirstName="李",
              LastName="四",
               Id=2,
            },
                new Person(){
             FirstName="王",
              LastName="五",
               Id=3,
            },new Person(){
             FirstName="赵",
              LastName="六",
               Id=4,
            },new Person(){
             FirstName="张",
              LastName="七",
               Id=5,
            },new Person(){
             FirstName="陆",
              LastName="七",
               Id=6,
            },new Person(){
             FirstName="老",
              LastName="七",
               Id=7,
            },new Person(){
             FirstName="张",
              LastName="五七",
               Id=8,
            },
            };
    
             var aa= clientPerson.IndexMany(list, IndexName.From<Person>());
    
            for (var i = 0; i < 10; i++)
            {
                var goods = new Goods()
                {
                    CreateDate = DateTime.Now,
                    Id = i.ToString(),
                    Name = "商品" + i,
                    Price = i,
                    Tags = i.ToString()
                };
                var response = await client.IndexDocumentAsync(goods);
                if (response.Result != Result.Created)
                {

                }
            }

        }

        /// <summary>
        /// 搜索
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("search")]
        public async Task<object> Search()
        {
           var res= await clientPerson.SearchAsync<Person>(d =>d.From(0).Size(10).Query(s=>s.Match(q=>q.Field(f=>f.LastName).Query("七"))) );
            return res.Documents;

        }

        /// <summary>
        /// 聚合
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("Aggregations")]
        public async Task<object> Aggregations()
        { 
            var res = await clientPerson.SearchAsync<Person>(d => d.Size(0).Query(s => 
            s.Match(q => q.Field(f => f.LastName).Query("七"))).Aggregations(a=>a.Terms("xing",s=>s.Field(f=>f.FirstName))));
            return res.Aggregations.Terms("xing").Buckets.Select(x=>new { x.Key,x.DocCount});
        }
    }
    public class Person
    {
        public int Id { get; set; }

       
        public string FirstName { get; set; }
        public string LastName { get; set; }
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