using Microsoft.EntityFrameworkCore;

namespace MyWorkFlow
{
    public class SchoolContext : DbContext
    {
        public SchoolContext(DbContextOptions<SchoolContext> options)
            : base(options)
        {
        }

   
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
           
        }
    }
}
