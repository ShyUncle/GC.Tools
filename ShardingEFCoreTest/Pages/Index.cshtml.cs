using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShardingEFCoreTest.DomainModel;
using System.Collections;
using System.Collections.Concurrent;

namespace ShardingEFCoreTest.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }
        public int GetStringHashCode(string value)
        {
            int num = 0;
            if (value.Length > 0)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    num = 31 * num + value[i];
                }
            }

            return num;
        }
        private string Mod(string id)
        {
            return Math.Abs(GetStringHashCode(id) % 5).ToString().PadLeft(2, '0');
        }

        public void OnGet(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var tail = Mod(id);
            using (var dbContext = new DbContextFactory().Creat(tail))
            {
                var dbEntity = dbContext.Set<SystemUser>().Find(id);
            }
        }
        public List<SystemUser> list { get; set; } = new List<SystemUser>();

        public void OnPostPaged()
        {
            var tails = new List<string> { "00", "01", "02", "03", "04" };
            var pageNo = 2;
            var pageSize = 10;
            var skipCount = 0;

            Dictionary<string, IEnumerator<SystemUser>> enumerableList = new Dictionary<string, IEnumerator<SystemUser>>();
            List<Task> tasks = new List<Task>();
            Dictionary<string, long> sortedDic = new Dictionary<string, long>();
            foreach (var item in tails)
            {

                var dbContext = new DbContextFactory().Creat(item);
                {
                    var tempList = dbContext.Set<SystemUser>().OrderByDescending(x => x.Id).Take(pageSize * pageNo).AsEnumerable().GetEnumerator();
                    tempList.MoveNext();
                    if (tempList.Current != null)
                    {
                        sortedDic.Add(item, Convert.ToInt64(tempList.Current.Id));
                        enumerableList.Add(item, tempList);
                    }
                }
            }

            list = new List<SystemUser>();

            if (sortedDic.Count == 0) return;

            while (skipCount < pageSize)
            {
                if (sortedDic.Count == 0) break;
                var max = sortedDic.MaxBy(x => x.Value);
                sortedDic.Remove(max.Key);
                skipCount++;
                bool s = enumerableList[max.Key].MoveNext();
                if (s)
                {
                    sortedDic.Add(max.Key, Convert.ToInt64(enumerableList[max.Key].Current.Id));
                }
                else
                {
                    continue;
                }
            }
            foreach (var item in sortedDic)
            {
                if (!list.Any(x => x.Id == item.Value.ToString()))
                    list.Add(new SystemUser()
                    {
                        Id = item.Value.ToString()
                    });
            }
            while (list.Count < pageSize)
            {

                if (enumerableList.Count == 0 || sortedDic.Count == 0) break;
                var max = sortedDic.MaxBy(x => x.Value);

                bool s = enumerableList[max.Key].MoveNext();
                if (s)
                {
                    list.Add(new SystemUser()
                    {
                        Id = enumerableList[max.Key].Current.Id
                    });
                    sortedDic[max.Key] = Convert.ToInt64(enumerableList[max.Key].Current.Id);
                }
                else
                {
                    sortedDic.Remove(max.Key);
                    enumerableList.Remove(max.Key);
                    continue;
                }
            }


        }

        //²éÑ¯ËùÓÐ
        public void OnPost()
        {
            var tails = new List<string> { "00", "01", "02", "03", "04" };
            foreach (var item in tails)
            {
                using (var dbContext = new DbContextFactory().Creat(item))
                {
                    list.AddRange(dbContext.Set<SystemUser>().ToList());
                }
            }
            list = list.OrderByDescending(x => x.Id).ToList();
        }
    }
}
