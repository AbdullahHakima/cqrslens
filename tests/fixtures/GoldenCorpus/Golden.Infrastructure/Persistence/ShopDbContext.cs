using Golden.Domain;
using Microsoft.EntityFrameworkCore;

namespace Golden.Infrastructure.Persistence;

public sealed class ShopDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
}
