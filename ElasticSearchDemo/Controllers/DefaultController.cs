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
        private readonly IElasticClientProvider _elasticClientProvider;
        public DefaultController(IHttpClientFactory httpClientFactory, IElasticClientProvider elasticClientProvider)
        {
            _elasticClientProvider = elasticClientProvider;
        }
        [HttpGet]
        /// <summary>
        /// 初始化数据
        /// </summary>
        /// <returns></returns>
        public async Task Get()
        {
            var clientPerson = _elasticClientProvider.GetElasticClient();
            var r = clientPerson.Indices.Delete("person");
            var bc = clientPerson.Indices.Create("person", x => x.Map<Person>((m) => m.AutoMap().Properties(p => p.Text(x => x.Name(x => x.FirstName).Fielddata(true)))));
            var list = new List<Person>() {
            new Person(){
             FirstName="张",
              LastName="三",
               Id=1,
               Age=25
            },
            new Person(){
             FirstName="李",
              LastName="四",
               Id=2,
               Age=35
            },
                new Person(){
             FirstName="王",
              LastName="五",
               Id=3,
               Age=26
            },new Person(){
             FirstName="赵",
              LastName="六",
               Id=4,
               Age=26
            },new Person(){
             FirstName="张",
              LastName="七",
               Id=5,
            },new Person(){
             FirstName="陆",
              LastName="七",
               Id=6,
               Age=18
            },new Person(){
             FirstName="老",
              LastName="七",
               Id=7,
               Age=43
            },new Person(){
             FirstName="张",
              LastName="五七",
               Id=8,
               Age=20
            },
            };

            // var aa = clientPerson.IndexMany(list, IndexName.From<Person>());
            List<string> l = new List<string>() { "赵", "钱", "宋", "李", "孙", "周", "吴", "郑", "王", "林" };
            List<string> le = new List<string>() { "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
            for (var i = 20; i < 1000; i++)
            {
                var perso = new Person()
                {
                    Age = i,
                    FirstName = l[i % 10],
                    LastName = le[i % 10],
                    Id = i

                };
                list.Add(perso);

            }
            var response = await clientPerson.IndexManyAsync<Person>(list);
            //    await clientPerson.IndexAsync(list[0], i => i.Index("person"));//自定义索引
            // await clientPerson.BulkAsync(b => b.Index("person").IndexMany(list));//自定义索引
            //   clientPerson.BulkAll(list, i => i.Index("person").BackOffTime("30s").BackOffRetries(10).RefreshOnCompleted().MaxDegreeOfParallelism(Environment.ProcessorCount).Size(1000)).Wait(;
        }

        [HttpGet("indexs")]
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<object> GetIndexs()
        {
            var clientPerson = _elasticClientProvider.GetElasticClient();
            var r = await clientPerson.Cat.IndicesAsync();
            return r.Records;
        }

        [HttpDelete("{index}")]
        public async Task<object> DeleteAsync(string index)
        {
            var response = _elasticClientProvider.GetElasticClient().Indices.Delete(index);
            return response.ServerError;
        }
        [HttpGet("ana")]
        public async Task<object> GetA(string words)
        {
            var response = await _elasticClientProvider.GetElasticClient().Indices.AnalyzeAsync(a => a.Analyzer("standard").Text(words));
            var response1 = await _elasticClientProvider.GetElasticClient().Indices.AnalyzeAsync(a => a.Analyzer("ik_max_word").Text(words));
            return new { response.Tokens, t1 = response1.Tokens };
        }
        /// <summary>
        /// 搜索
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("search")]
        public async Task<object> Search()
        {
            var clientPerson = _elasticClientProvider.GetElasticClient();
            var res = await clientPerson.SearchAsync<Person>(d => d.From(0).Size(10).Query(s => s.Match(q => q.Field(f => f.LastName).Query("七"))).Highlight(d => d.Fields(ss => ss.Field("LastName"))));
            return new { res.Hits, res.Documents };

        }
        /// <summary>
        /// 搜索
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("searchAge")]
        public async Task<object> SearchAge()
        {
            var clientPerson = _elasticClientProvider.GetElasticClient();

            var res = await clientPerson.SearchAsync<Person>(d => d.From(0).Size(10).Query(s => s.Range(r => r.Field(f => f.Age).GreaterThan(25))));
            return res.Documents;

        }

        ///// <summary>
        ///// 分析
        ///// </summary>
        ///// <returns></returns>
        //[HttpGet]
        //[Route("Analyze")]
        //public async Task<object> Analyze()
        //{
        //    var res = await clientPerson.Indices.AnalyzeAsync();
        //    return res.Detail;

        //}

        /// <summary>
        /// 聚合
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("Aggregations")]
        public async Task<object> Aggregations()
        {
            var clientPerson = _elasticClientProvider.GetElasticClient();

            var res = await clientPerson.SearchAsync<Person>(d => d.Query(s =>
            s.Match(q => q.Field(f => f.LastName).Query("七"))).Aggregations(a => a.Terms("xing", s => s.Field(f => f.FirstName))));
            return res.Aggregations.Terms("xing").Buckets.Select(x => new { x.Key, x.DocCount });
        }
    }
    public class Person
    {
        public int Id { get; set; }

        public int Age { get; set; }

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