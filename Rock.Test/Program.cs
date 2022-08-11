// See https://aka.ms/new-console-template for more information

using Rock.SqlParser.SqlServer;

var sqlParser = new SqlServerParser();
//sqlParser.Execute("SELECT Id,Name into dssd FROM Product join Order on Product.Id=Order.ProductId where A oR B and C OR D + X * 5;drop table a ");
sqlParser.Execute(@"SELECT [t].[Id], [t].[IsComplete], [t].[Name]
FROM [TodoItems] AS[t]".Replace("[","").Replace("]", ""));
Console.WriteLine($"获得的数据表: {sqlParser.ParsedTable}");
Console.WriteLine("获得的字段:");
foreach (var field in sqlParser.ParsedFields)
{
    Console.WriteLine(field);
}
Console.Read();