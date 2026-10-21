namespace SampleShop.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
    public string Status { get; set; } = "Pending";
}

public sealed class OrderLine
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
