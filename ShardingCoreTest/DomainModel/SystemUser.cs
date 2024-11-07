using System.ComponentModel.DataAnnotations;

namespace ShardingCoreTest.DomainModel
{
    public class SystemUser
    { 
        public string Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
    }
}
