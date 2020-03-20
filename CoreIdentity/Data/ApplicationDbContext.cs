using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace CoreIdentity.Data
{
    public class ApplicationDbContext : IdentityDbContext<CustomerUser,CustomerRole,int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<CustomerUser>().ToTable("Users");
            builder.Entity<CustomerRole>().ToTable("Roles");
        }
    }
}
