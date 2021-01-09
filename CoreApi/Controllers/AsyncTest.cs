using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreApi.Controllers
{
    public class testDate
    {
        public DateTime date { get; set; }
        public testDate()
        {
            date = DateTime.Now;
        }
    }
    public class AsyncTest
    {
        private static readonly AsyncLocal<testDate> CurentScope=new AsyncLocal<testDate>();
        public AsyncTest()
        {
            
        }

        public testDate Current { get => CurentScope.Value; set => CurentScope.Value = value; }
    }
}
