namespace ElasticSearchDemo1
{
    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Category { get; set; }
    }

    public class ProductRequest
    {
        public string? Name { get; set; }
        public string? Category { get; set; }
        public DateTime? CreatedAt { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }

        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }  
    }
}
