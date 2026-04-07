using Microsoft.EntityFrameworkCore;

namespace BSEBResultBlockchainAPI.Helpers
{
    public class AppDBContext : DbContext
    {
        public AppDBContext(DbContextOptions<AppDBContext> options) : base(options) { }
    }
}
