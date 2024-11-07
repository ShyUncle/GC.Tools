using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShardingCoreTest.DomainModel;
using SnowFlakeTest;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ShardingCoreTest
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly MyDbContext _myDbContext;

        public ValuesController(MyDbContext myDbContext)
        {
            _myDbContext = myDbContext;
        }

        // GET: api/<ValuesController>
        [HttpGet]
        public async Task<IEnumerable<Order>> GetAsync(DateTime? startTime, DateTime? endTime)
        {
            var ds = _myDbContext.Set<SystemUser>();
            var query = from ord in _myDbContext.Order
                        join user in ds on ord.Money.ToString() equals user.Name
                        select ord;

            var orders = await query.Where(o => o.CreationTime >= startTime && o.CreationTime <= endTime).OrderByDescending(x => x.CreationTime).ToListAsync();
            return orders;
        }

        // GET api/<ValuesController>/5
        [HttpGet("{id}")]
        public async Task<Order> Get(int id)
        {
            var order = await _myDbContext.Set<Order>().FirstOrDefaultAsync(o => o.Id == id.ToString());
            return order;
        }

        // POST api/<ValuesController>
        [HttpPost]
        public async Task<string> Post([FromBody] Order model)
        {
            var order = new Order()
            {
                Area = model.Area,
                Id = model.Id,
                CreationTime = model.CreationTime,
                Money = model.Money,
                OrderStatus = model.OrderStatus,
                Payer = model.Payer
            };
            var result = await _myDbContext.Set<Order>().AddAsync(order);
            await _myDbContext.SaveChangesAsync();
            return result.Entity.Id;
        }

        // PUT api/<ValuesController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<ValuesController>/5
        [HttpDelete("{id}")]
        public async Task Delete(string id)
        {
            var order = await _myDbContext.Set<Order>().FindAsync(id);
            _myDbContext.Set<Order>().Remove(order);
            await _myDbContext.SaveChangesAsync();
        }


        // POST api/<ValuesController>
        [HttpPost]
        [Route("user")]
        public async Task<string> UserPost([FromBody] SystemUser model)
        {
            model.Id = IdWorker.Instance.NextId().ToString();
            var result = await _myDbContext.SystemUser.AddAsync(model);
            await _myDbContext.SaveChangesAsync();
            return result.Entity.Id;
        }
    }
}
