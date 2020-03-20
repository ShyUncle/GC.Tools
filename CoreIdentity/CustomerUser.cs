using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoreIdentity
{

    public class CustomerUser: IdentityUser<int>
    {
        public string OpenId { get; set; }
        public int Age { get; set; }
    }

    public class CustomerRole : IdentityRole<int>
    {
        public int ParentRoleId { get; set; }
    }
}
