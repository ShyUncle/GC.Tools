using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace CoreIdentity
{

    public class CustomerUser : IdentityUser<int>
    {
        [MaxLength(10)]
        public string OpenId { get; set; }
        public int Age { get; set; }
        public int Point { get; set; }
    }

    public class CustomerRole : IdentityRole<int>
    {
        public int ParentRoleId { get; set; }
    }
}
