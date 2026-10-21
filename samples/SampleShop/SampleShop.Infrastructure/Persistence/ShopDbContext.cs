using Microsoft.EntityFrameworkCore;
using SampleShop.Domain;

namespace SampleShop.Infrastructure.Persistence;

public sealed class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
}
