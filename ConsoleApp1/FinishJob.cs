using System;
using System.Collections.Generic;
using System.Text;

namespace ConsoleApp1
{
    public class FinishJob
    {
        private DependencyA _a;
        public FinishJob(DependencyA dependencyA)
        {
            this._a = dependencyA;
        }
        public void Test(string name)
        {
            _a.Name = name;
            _a.GetName();
        }
    }

    public class DependencyA
    {
        public string Name { get; set; }
        public void GetName()
        {
            Console.WriteLine(Name);
        }
    }
}
