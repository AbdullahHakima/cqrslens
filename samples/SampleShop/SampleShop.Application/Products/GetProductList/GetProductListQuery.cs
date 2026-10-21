using MediatR;

namespace SampleShop.Application.Products.GetProductList;

public sealed record ProductDto(int Id, string Name, decimal Price);

public sealed record GetProductListQuery(int Page = 1, int PageSize = 20) : IRequest<List<ProductDto>>;

public sealed class GetProductListQueryHandler : IRequestHandler<GetProductListQuery, List<ProductDto>>
{
    public Task<List<ProductDto>> Handle(GetProductListQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new List<ProductDto> { new(1, "Mechanical Keyboard", 79.99m) });
}
