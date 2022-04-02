// See https://aka.ms/new-console-template for more information
using Rock.SqlParser.Irony;

Console.WriteLine("Hello, World!");
var sqlParser = new SqlServerParser();
sqlParser.Execute("SELECT Name FROM Product where A oR B and C OR D + X * 5; ");

Console.WriteLine($"获得的数据表: {sqlParser.ParsedTable}");
Console.WriteLine("获得的字段:");
foreach (var field in sqlParser.ParsedFields)
{
    Console.WriteLine(field);
}
Console.Read();
